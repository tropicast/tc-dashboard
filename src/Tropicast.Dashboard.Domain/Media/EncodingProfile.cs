using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Domain.Media;

/// <summary>
/// How a station's live source encodes one format, pinned from what it declared at its last connect (#106), so
/// AutoDJ encodes the same and listeners hear no change of format when Icecast switches between them.
/// </summary>
public sealed class EncodingProfile
{
    /// <summary>What tc-station publishes for a Tropicast account, before any live source connected.</summary>
    public const int DefaultMp3Kbps = 128;
    public const int DefaultOpusKbps = 64;

    private static readonly int[] SampleRates = [22_050, 24_000, 32_000, 44_100, 48_000];

    private EncodingProfile()
    {
    }

    public Guid StationId { get; private set; }
    public AudioFormat Format { get; private set; }
    public int BitrateKbps { get; private set; }
    public int SampleRate { get; private set; }
    public int Channels { get; private set; }
    public DateTimeOffset PinnedAt { get; private set; }

    /// <summary>The desktop app's defaults, capped by the plan (not stored).</summary>
    public static EncodingProfile Default(Guid stationId, AudioFormat format, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new EncodingProfile
        {
            StationId = stationId,
            Format = format,
            BitrateKbps = Math.Min(format == AudioFormat.Mp3 ? DefaultMp3Kbps : DefaultOpusKbps, plan.MaxBitrateKbps),
            SampleRate = format == AudioFormat.Mp3 ? 44_100 : 48_000,
            Channels = 2,
        };
    }

    /// <summary>Whether a declared encoding is one AutoDJ can reproduce.</summary>
    public static bool IsValid(int bitrateKbps, int sampleRate, int channels)
        => bitrateKbps is >= 8 and <= 320 && SampleRates.Contains(sampleRate) && channels is 1 or 2;

    public static EncodingProfile Pin(Guid stationId, AudioFormat format, int bitrateKbps, int sampleRate, int channels, DateTimeOffset now)
    {
        var profile = new EncodingProfile { StationId = stationId, Format = format };
        profile.Update(bitrateKbps, sampleRate, channels, now);
        return profile;
    }

    /// <summary>Returns whether anything changed.</summary>
    public bool Update(int bitrateKbps, int sampleRate, int channels, DateTimeOffset now)
    {
        if (!IsValid(bitrateKbps, sampleRate, channels))
        {
            throw new ArgumentOutOfRangeException(nameof(bitrateKbps), "Not an encoding AutoDJ can reproduce.");
        }
        if ((BitrateKbps, SampleRate, Channels) == (bitrateKbps, sampleRate, channels))
        {
            return false;
        }
        (BitrateKbps, SampleRate, Channels, PinnedAt) = (bitrateKbps, sampleRate, channels, now);
        return true;
    }

    /// <summary>The bitrate the plan allows now (a downgrade may lower it below what was pinned).</summary>
    public int BitrateFor(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Math.Min(BitrateKbps, plan.MaxBitrateKbps);
    }
}
