using System.Buffers.Binary;
using System.Text;
using Tropicast.Dashboard.Domain.Media;

namespace Tropicast.Dashboard.Application.Media;

/// <summary>What the server read from an uploaded file.</summary>
public sealed record AudioInfo(TrackFormat Format, TimeSpan Duration, int SampleRate, int Channels, string Title, string Artist);

/// <summary>
/// Reads an uploaded file's format, duration and title tags from its first and last bytes, without decoding audio:
/// MP3 (MPEG-1/2/2.5 Layer III, CBR or Xing/Info/VBRI VBR, ID3v2 tags), FLAC (STREAMINFO, Vorbis comments) and Ogg
/// Opus or Vorbis (last page granule position, comment header). Anything else is not a track.
/// </summary>
public static class AudioProbe
{
    /// <summary>Bytes read from the start of the file: tags and headers live there.</summary>
    public const int HeadBytes = 256 * 1024;

    /// <summary>Bytes read from the end: the last Ogg page and an ID3v1 tag live there.</summary>
    public const int TailBytes = 64 * 1024;

    /// <summary>
    /// <paramref name="head"/> is the start of the file; <paramref name="tail"/> its end (the same bytes when the file
    /// is short); <paramref name="size"/> the whole file's size. Null when the file is not a supported track.
    /// </summary>
    public static AudioInfo? Read(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, long size)
    {
        if (head.Length < 4 || size <= 0)
        {
            return null;
        }
        if (head.StartsWith("fLaC"u8))
        {
            return Flac(head);
        }
        if (head.StartsWith("OggS"u8))
        {
            return Ogg(head, tail);
        }
        return Mp3(head, tail, size);
    }

    private static AudioInfo? Flac(ReadOnlySpan<byte> head)
    {
        var offset = 4;
        int sampleRate = 0, channels = 0;
        long samples = 0;
        string title = "", artist = "";
        while (offset + 4 <= head.Length)
        {
            var last = (head[offset] & 0x80) != 0;
            var type = head[offset] & 0x7F;
            var length = (head[offset + 1] << 16) | (head[offset + 2] << 8) | head[offset + 3];
            var body = offset + 4;
            if (body + length > head.Length)
            {
                break;
            }
            if (type == 0 && length >= 18)
            {
                var b = head.Slice(body, 18);
                sampleRate = (b[10] << 12) | (b[11] << 4) | (b[12] >> 4);
                channels = ((b[12] >> 1) & 0x07) + 1;
                samples = ((long)(b[13] & 0x0F) << 32) | BinaryPrimitives.ReadUInt32BigEndian(b[14..18]);
            }
            else if (type == 4)
            {
                (title, artist) = VorbisComments(head.Slice(body, length));
            }
            offset = body + length;
            if (last)
            {
                break;
            }
        }
        return sampleRate > 0 && samples > 0
            ? new AudioInfo(TrackFormat.Flac, TimeSpan.FromSeconds((double)samples / sampleRate), sampleRate, channels, title, artist)
            : null;
    }

    private static AudioInfo? Ogg(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail)
    {
        // The first page carries the codec's identification header.
        if (head.Length < 28)
        {
            return null;
        }
        var segments = head[26];
        var payload = 27 + segments;
        if (payload + 19 > head.Length)
        {
            return null;
        }
        var id = head[payload..];
        TrackFormat format;
        int sampleRate, channels;
        long preSkip = 0;
        ReadOnlySpan<byte> commentMagic;
        if (id.StartsWith("OpusHead"u8))
        {
            format = TrackFormat.OggOpus;
            channels = id[9];
            preSkip = BinaryPrimitives.ReadUInt16LittleEndian(id[10..12]);
            // Opus granule positions always count 48 kHz samples.
            sampleRate = 48_000;
            commentMagic = "OpusTags"u8;
        }
        else if (id.Length >= 16 && id[0] == 1 && id[1..7].SequenceEqual("vorbis"u8))
        {
            format = TrackFormat.OggVorbis;
            channels = id[11];
            sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(id[12..16]);
            commentMagic = "\u0003vorbis"u8;
        }
        else
        {
            return null;
        }
        var lastPage = tail.LastIndexOf("OggS"u8);
        if (lastPage < 0 || lastPage + 14 > tail.Length || sampleRate <= 0)
        {
            return null;
        }
        var granule = BinaryPrimitives.ReadInt64LittleEndian(tail.Slice(lastPage + 6, 8));
        var samples = granule - preSkip;
        if (samples <= 0)
        {
            return null;
        }
        string title = "", artist = "";
        var comments = head.IndexOf(commentMagic);
        if (comments >= 0)
        {
            (title, artist) = VorbisComments(head[(comments + commentMagic.Length)..]);
        }
        return new AudioInfo(format, TimeSpan.FromSeconds((double)samples / sampleRate), sampleRate, channels, title, artist);
    }

    /// <summary>Vendor string, then <c>KEY=value</c> entries, all length-prefixed (little-endian).</summary>
    private static (string Title, string Artist) VorbisComments(ReadOnlySpan<byte> data)
    {
        string title = "", artist = "";
        if (data.Length < 8)
        {
            return (title, artist);
        }
        var offset = 4 + (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data), int.MaxValue - 8);
        if (offset + 4 > data.Length)
        {
            return (title, artist);
        }
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        for (var i = 0; i < count && offset + 4 <= data.Length; i++)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
            if (length > (uint)(data.Length - offset))
            {
                break;
            }
            var entry = Encoding.UTF8.GetString(data.Slice(offset, (int)length));
            offset += (int)length;
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }
            var key = entry[..equals];
            if (key.Equals("TITLE", StringComparison.OrdinalIgnoreCase) && title.Length == 0)
            {
                title = entry[(equals + 1)..];
            }
            else if (key.Equals("ARTIST", StringComparison.OrdinalIgnoreCase) && artist.Length == 0)
            {
                artist = entry[(equals + 1)..];
            }
        }
        return (Clean(title), Clean(artist));
    }

    private static readonly int[] Mpeg1Layer3Kbps = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
    private static readonly int[] Mpeg2Layer3Kbps = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];
    private static readonly int[] Mpeg1Rates = [44_100, 48_000, 32_000, 0];

    private readonly record struct Mp3Frame(int Version, int Kbps, int SampleRate, int Length, int SamplesPerFrame, int Channels);

    /// <summary>Version: 1 (MPEG-1), 2 (MPEG-2) or 25 (MPEG-2.5). Null when the four bytes are no Layer III header.</summary>
    private static Mp3Frame? Mp3Header(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4 || b[0] != 0xFF || (b[1] & 0xE0) != 0xE0)
        {
            return null;
        }
        var versionBits = (b[1] >> 3) & 0x03;
        var layerBits = (b[1] >> 1) & 0x03;
        if (versionBits == 1 || layerBits != 1)
        {
            return null; // Reserved version, or not Layer III.
        }
        var version = versionBits switch { 3 => 1, 2 => 2, _ => 25 };
        var kbps = (version == 1 ? Mpeg1Layer3Kbps : Mpeg2Layer3Kbps)[b[2] >> 4];
        var rate = Mpeg1Rates[(b[2] >> 2) & 0x03];
        if (kbps == 0 || rate == 0)
        {
            return null; // Free format or reserved values.
        }
        rate = version switch { 1 => rate, 2 => rate / 2, _ => rate / 4 };
        var padding = (b[2] >> 1) & 0x01;
        var samples = version == 1 ? 1152 : 576;
        var length = (samples / 8 * kbps * 1000 / rate) + padding;
        var channels = (b[3] >> 6) == 3 ? 1 : 2;
        return new Mp3Frame(version, kbps, rate, length, samples, channels);
    }

    private static AudioInfo? Mp3(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, long size)
    {
        var start = 0;
        string title = "", artist = "";
        if (head.Length >= 10 && head.StartsWith("ID3"u8))
        {
            var tagSize = (head[6] << 21) | (head[7] << 14) | (head[8] << 7) | head[9];
            (title, artist) = Id3v2(head, tagSize);
            start = 10 + tagSize + ((head[5] & 0x10) != 0 ? 10 : 0);
        }
        // The first frame whose successor is also a frame: a lone 0xFFE could be inside a picture or junk.
        Mp3Frame? frame = null;
        var at = start;
        for (; at + 4 <= head.Length && at < start + 64 * 1024; at++)
        {
            if (Mp3Header(head[at..]) is { } candidate
                && (at + candidate.Length + 4 > head.Length || Mp3Header(head[(at + candidate.Length)..]) is not null))
            {
                frame = candidate;
                break;
            }
        }
        if (frame is not { } first)
        {
            return null;
        }
        var trailing = tail.Length >= 128 && tail[^128..].StartsWith("TAG"u8) ? 128 : 0;
        // A Xing/Info or VBRI header counts the frames of a VBR file.
        var sideInfo = first.Version == 1 ? (first.Channels == 1 ? 17 : 32) : (first.Channels == 1 ? 9 : 17);
        long? frames = null;
        var xing = at + 4 + sideInfo;
        if (xing + 12 <= head.Length && (head[xing..].StartsWith("Xing"u8) || head[xing..].StartsWith("Info"u8))
            && (BinaryPrimitives.ReadUInt32BigEndian(head[(xing + 4)..]) & 1) != 0)
        {
            frames = BinaryPrimitives.ReadUInt32BigEndian(head[(xing + 8)..]);
        }
        else if (at + 36 + 18 <= head.Length && head[(at + 36)..].StartsWith("VBRI"u8))
        {
            frames = BinaryPrimitives.ReadUInt32BigEndian(head[(at + 36 + 14)..]);
        }
        var seconds = frames is > 0
            ? (double)frames.Value * first.SamplesPerFrame / first.SampleRate
            : (size - at - trailing) * 8.0 / (first.Kbps * 1000.0);
        if (seconds <= 0)
        {
            return null;
        }
        if (title.Length == 0 && artist.Length == 0 && trailing > 0)
        {
            var tag = tail[^128..];
            title = Clean(Encoding.Latin1.GetString(tag.Slice(3, 30)).TrimEnd('\0', ' '));
            artist = Clean(Encoding.Latin1.GetString(tag.Slice(33, 30)).TrimEnd('\0', ' '));
        }
        return new AudioInfo(TrackFormat.Mp3, TimeSpan.FromSeconds(seconds), first.SampleRate, first.Channels, title, artist);
    }

    /// <summary>TIT2 and TPE1 from an ID3v2.3 or v2.4 tag (frames past the bytes read are skipped).</summary>
    private static (string Title, string Artist) Id3v2(ReadOnlySpan<byte> head, int tagSize)
    {
        string title = "", artist = "";
        var major = head[3];
        if (major is not (3 or 4))
        {
            return (title, artist);
        }
        var end = Math.Min(head.Length, 10 + tagSize);
        var offset = 10;
        if ((head[5] & 0x40) != 0 && offset + 4 <= end)
        {
            // Extended header: skip it.
            var extended = major == 4 ? SyncSafe(head[offset..]) : (int)BinaryPrimitives.ReadUInt32BigEndian(head[offset..]) + 4;
            offset += extended;
        }
        while (offset + 10 <= end && head[offset] != 0)
        {
            var id = Encoding.ASCII.GetString(head.Slice(offset, 4));
            var length = major == 4 ? SyncSafe(head[(offset + 4)..]) : (int)BinaryPrimitives.ReadUInt32BigEndian(head[(offset + 4)..]);
            var body = offset + 10;
            if (length <= 0 || length > end - body)
            {
                break;
            }
            if (id is "TIT2" or "TPE1")
            {
                var text = Id3Text(head.Slice(body, length));
                if (id == "TIT2")
                {
                    title = text;
                }
                else
                {
                    artist = text;
                }
            }
            offset = body + length;
        }
        return (title, artist);
    }

    private static int SyncSafe(ReadOnlySpan<byte> b) => (b[0] << 21) | (b[1] << 14) | (b[2] << 7) | b[3];

    private static string Id3Text(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
        {
            return "";
        }
        var text = frame[1..];
        var value = frame[0] switch
        {
            0 => Encoding.Latin1.GetString(text),
            // UTF-16 with a byte order mark: big-endian (FE FF) or little-endian (FF FE).
            1 => text.Length >= 2 && text[0] == 0xFE && text[1] == 0xFF
                ? Encoding.BigEndianUnicode.GetString(text[2..])
                : Encoding.Unicode.GetString(SkipBom(text)),
            2 => Encoding.BigEndianUnicode.GetString(text),
            3 => Encoding.UTF8.GetString(text),
            _ => "",
        };
        return Clean(value.Split('\0')[0]);
    }

    private static ReadOnlySpan<byte> SkipBom(ReadOnlySpan<byte> text) => text.Length >= 2 && text[0] == 0xFF && text[1] == 0xFE ? text[2..] : text;

    /// <summary>No control characters, trimmed, at most <see cref="Track.MaxTextLength"/>.</summary>
    private static string Clean(string value)
    {
        var text = new string([.. value.Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        return text.Length <= Track.MaxTextLength ? text : text[..Track.MaxTextLength];
    }
}
