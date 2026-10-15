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
        // MPEG-1 Layer III, 128 kbps, 44.1 kHz, stereo: 417-byte frames, no Xing/Info frame.
        byte[] frame = [0xFF, 0xFB, 0x90, 0x00, .. new byte[413]];
        var file = Enumerable.Repeat(frame, 300).SelectMany(f => f).ToArray();

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
