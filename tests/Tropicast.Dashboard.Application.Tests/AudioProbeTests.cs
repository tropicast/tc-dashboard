using Tropicast.Dashboard.Application.Media;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Tests;

/// <summary>
/// The upload checks against real files: two-second 440 Hz tones made with ffmpeg, e.g.
/// <c>ffmpeg -f lavfi -i sine=frequency=440:duration=2 -ac 1 -c:a libmp3lame -b:a 32k cbr.mp3</c>.
/// </summary>
public sealed class AudioProbeTests
{
    private static AudioInfo? Probe(byte[] file, int headLimit = AudioProbe.HeadBytes)
    {
        var head = file.AsSpan(0, Math.Min(file.Length, headLimit));
        var tail = file.AsSpan(Math.Max(0, file.Length - AudioProbe.TailBytes));
        return AudioProbe.Read(head, tail, file.Length);
    }

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Theory]
    [InlineData("cbr.mp3", TrackFormat.Mp3, 44_100, "Salegy Morning", "Radio Mada")]
    [InlineData("vbr.mp3", TrackFormat.Mp3, 44_100, "Late Night", "")]
    [InlineData("tone.flac", TrackFormat.Flac, 22_050, "Jingle", "Studio")]
    [InlineData("tone.opus", TrackFormat.OggOpus, 48_000, "Opus Tone", "")]
    [InlineData("tone.ogg", TrackFormat.OggVorbis, 22_050, "Vorbis Tone", "")]
    public void Reads_format_duration_and_tags_of_real_files(string name, TrackFormat format, int sampleRate, string title, string artist)
    {
        var info = Probe(Fixture(name));

        Assert.NotNull(info);
        Assert.Equal((format, sampleRate, 1, title, artist), (info.Format, info.SampleRate, info.Channels, info.Title, info.Artist));
        // ffprobe says 2.000 s (2.0065 s for Opus); MP3 frames round up to 26 ms.
        Assert.InRange(info.Duration.TotalSeconds, 1.95, 2.1);
    }

    [Fact]
    public void A_constant_bitrate_mp3_without_a_header_frame_is_timed_by_its_size()
    {
        var file = Frames(300);

        var info = Probe(file);

        Assert.NotNull(info);
        Assert.Equal(TrackFormat.Mp3, info.Format);
        Assert.InRange(info.Duration.TotalSeconds, 7.8, 7.9); // 300 x 1152 / 44100 = 7.84 s.
    }

    [Theory]
    [InlineData("not audio at all, just text that someone renamed to .mp3")]
    [InlineData("")]
    public void Anything_else_is_not_a_track(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        Assert.Null(Probe(bytes));
    }

    [Fact]
    public void A_truncated_ogg_file_without_a_last_page_is_refused()
    {
        var opus = Fixture("tone.opus");
        // Only the first page: the end of the stream (the duration) is missing.
        var firstPage = opus.AsSpan(0, 64).ToArray();
        Assert.Null(AudioProbe.Read(firstPage, "no ogg page here"u8, firstPage.Length));
    }

    /// <summary>MPEG-1 Layer III, 128 kbps, 44.1 kHz, stereo: 417 bytes, no Xing/Info frame.</summary>
    private static readonly byte[] CbrFrame = [0xFF, 0xFB, 0x90, 0x00, .. new byte[413]];

    private static byte[] Frames(int count) => [.. Enumerable.Repeat(CbrFrame, count).SelectMany(f => f)];

    private static byte[] SyncSafe(int n) => [(byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F)];

    /// <summary>As the confirm reads the stored file: head, tail, and the frames past a long ID3v2 tag.</summary>
    private static AudioInfo? ProbeLikeTheServer(byte[] file)
    {
        var head = file.AsSpan(0, Math.Min(file.Length, AudioProbe.HeadBytes));
        var tail = file.AsSpan(Math.Max(0, file.Length - AudioProbe.TailBytes));
        var frames = AudioProbe.FramesOffset(head, file.Length) is { } offset
            ? file.AsSpan((int)offset, (int)Math.Min(file.Length - offset, AudioProbe.HeadBytes))
            : [];
        return AudioProbe.Read(head, tail, file.Length, frames);
    }

    [Fact]
    public void An_mp3_whose_cover_art_is_longer_than_the_head_is_read_past_its_tag()
    {
        const int tagSize = 500_000; // A large APIC picture: the frames start past the 256 KB head.
        byte[] file = [.. "ID3"u8, 4, 0, 0, .. SyncSafe(tagSize), .. new byte[tagSize], .. Frames(300)];

        Assert.Null(Probe(file)); // The head alone holds no frame.
        var info = ProbeLikeTheServer(file);

        Assert.NotNull(info);
        Assert.Equal(TrackFormat.Mp3, info.Format);
        Assert.InRange(info.Duration.TotalSeconds, 7.8, 7.9); // Timed by the audio's size, not the tag's.
    }

    [Fact]
    public void A_crafted_id3_extended_header_size_is_ignored_not_a_crash()
    {
        // ID3v2.3 with the extended header flag, and a size that turns negative as an int.
        byte[] tag = [0xFF, 0xFF, 0xFF, 0xF0, .. new byte[96]];
        byte[] file = [.. "ID3"u8, 3, 0, 0x40, .. SyncSafe(tag.Length), .. tag, .. Frames(300)];

        var info = Probe(file);

        Assert.NotNull(info);
        Assert.Equal(("", ""), (info.Title, info.Artist));
    }

    [Fact]
    public void Two_frame_headers_by_chance_in_other_data_are_not_an_mp3()
    {
        byte[] junk = [.. new byte[1000], .. CbrFrame, .. CbrFrame, .. new byte[300_000]];
        Assert.Null(Probe(junk));
    }

    [Theory]
    [InlineData("RIFF")]
    [InlineData("FORM")]
    public void Containers_holding_mp3_frames_are_not_an_mp3(string magic)
    {
        byte[] file = [.. System.Text.Encoding.ASCII.GetBytes(magic), .. new byte[40], .. Frames(300)];
        Assert.Null(Probe(file));
        byte[] mp4 = [0, 0, 0, 32, .. "ftypM4A "u8, .. new byte[20], .. Frames(300)];
        Assert.Null(Probe(mp4));
    }

    [Fact]
    public void A_png_is_not_a_track_even_with_an_audio_name()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[2048]];
        Assert.Null(Probe(png));
    }

    [Theory]
    [InlineData("jingle.mp3", "audio/mpeg")]
    [InlineData("Mix.FLAC", "audio/flac")]
    [InlineData("tone.opus", "audio/ogg")]
    [InlineData("tone.ogg", "audio/ogg")]
    [InlineData("tone.wav", null)]
    [InlineData("cover.jpg", null)]
    [InlineData("mp3", null)]
    public void Only_accepted_extensions_get_a_content_type(string name, string? contentType)
        => Assert.Equal(contentType, TrackRules.ContentTypeOf(name));

    [Fact]
    public void The_quota_counts_what_is_already_stored()
    {
        var quota = Plan.Starter.StorageQuotaBytes;
        Assert.True(TrackRules.FitsQuota(Plan.Starter, quota - 10, 10));
        Assert.False(TrackRules.FitsQuota(Plan.Starter, quota - 10, 11));
        Assert.False(TrackRules.FitsQuota(Plan.Free, 0, 1));
        Assert.Contains("1 GB", TrackRules.QuotaMessage(Plan.Starter), StringComparison.Ordinal);
    }
}
