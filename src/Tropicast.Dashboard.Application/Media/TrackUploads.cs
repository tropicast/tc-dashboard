using FluentValidation;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Media;

/// <summary>Starts an upload: the server answers with a presigned URL the browser PUTs the file to.</summary>
/// <param name="FileName">The file's name, e.g. "jingle.mp3". Its extension picks the content type.</param>
/// <param name="Size">The file's size in bytes.</param>
public sealed record StartTrackUploadCommand(string FileName, long Size);

/// <summary>Confirms an upload once the browser has PUT the file: the server checks the stored object.</summary>
/// <param name="UploadId">The <c>trackId</c> the upload started with.</param>
/// <param name="Title">Shown in playlists; the file's tags (else its name) when omitted.</param>
/// <param name="Artist">The file's tags when omitted.</param>
public sealed record ConfirmTrackCommand(Guid UploadId, string? Title = null, string? Artist = null);

/// <summary>Rules for uploaded tracks (#104): accepted files, size, duration and the plan's storage quota.</summary>
public static class TrackRules
{
    /// <summary>Largest file accepted: a long FLAC mix fits, a whole album does not.</summary>
    public const long MaxBytes = 200L * 1024 * 1024;

    /// <summary>Longest track accepted.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(3);

    /// <summary>How long a presigned upload URL lives: the PUT must start by then.</summary>
    public static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long the pending track lives: longer than its URL, since a PUT started just in time can still be running.
    /// Its object is deleted with it, so a late upload is not left in the bucket.
    /// </summary>
    public static readonly TimeSpan PendingLifetime = UploadLifetime + TimeSpan.FromHours(1);

    public const string Accepted = "MP3, FLAC or Ogg (Opus or Vorbis)";

    /// <summary>The content type the upload must be sent with, from the file name; null when not accepted.</summary>
    public static string? ContentTypeOf(string? fileName) => Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".flac" => "audio/flac",
        ".ogg" or ".oga" or ".opus" => "audio/ogg",
        _ => null,
    };

    /// <summary>Whether <paramref name="adding"/> more bytes fit next to the <paramref name="used"/> ones.</summary>
    public static bool FitsQuota(Plan plan, long used, long adding)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return adding <= plan.StorageQuotaBytes - used;
    }

    public static string QuotaMessage(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return $"This file does not fit in the {plan.Name} plan's {Size(plan.StorageQuotaBytes)} of audio storage. Delete tracks or upgrade.";
    }

    public static string NoAutoDjMessage(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return $"AutoDJ is not in the {plan.Name} plan. Upgrade to Starter or higher to upload tracks.";
    }

    /// <summary>"1 GB", "500 MB": binary units, as quotas are set.</summary>
    public static string Size(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"
        : $"{bytes / (1024.0 * 1024):0.#} MB";
}

internal sealed class StartTrackUploadValidator : AbstractValidator<StartTrackUploadCommand>
{
    public StartTrackUploadValidator()
    {
        RuleFor(c => c.FileName).NotEmpty().MaximumLength(255)
            .Must(name => TrackRules.ContentTypeOf(name) is not null)
            .WithMessage($"Upload {TrackRules.Accepted} files (.mp3, .flac, .ogg, .opus).");
        RuleFor(c => c.Size).GreaterThan(0)
            .LessThanOrEqualTo(TrackRules.MaxBytes).WithMessage($"Files up to {TrackRules.Size(TrackRules.MaxBytes)} are accepted.");
    }
}

internal sealed class ConfirmTrackValidator : AbstractValidator<ConfirmTrackCommand>
{
    public ConfirmTrackValidator()
    {
        RuleFor(c => c.UploadId).NotEmpty();
        RuleFor(c => c.Title).MaximumLength(Domain.Media.Track.MaxTextLength);
        RuleFor(c => c.Artist).MaximumLength(Domain.Media.Track.MaxTextLength);
    }
}
