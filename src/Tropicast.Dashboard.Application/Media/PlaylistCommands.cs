using FluentValidation;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Media;

/// <summary>Creates a playlist on a station.</summary>
/// <param name="Name">e.g. "Night rotation".</param>
/// <param name="Shuffle">Play in random order.</param>
public sealed record CreatePlaylistCommand(string Name, bool Shuffle = false);

/// <summary>Renames a playlist or changes its order mode; omitted fields keep their value.</summary>
public sealed record UpdatePlaylistCommand(string? Name = null, bool? Shuffle = null);

/// <summary>Replaces a playlist's entries, in play order. A track may appear more than once.</summary>
/// <param name="TrackIds">Confirmed tracks of the same station.</param>
public sealed record SetPlaylistTracksCommand(IReadOnlyList<Guid> TrackIds);

/// <summary>Turns AutoDJ on or off and picks the playlist it plays.</summary>
/// <param name="Enabled">Play when no DJ is live.</param>
/// <param name="DefaultPlaylistId">The playlist to play; needed (and not empty) to turn AutoDJ on.</param>
public sealed record UpdateAutoDjCommand(bool Enabled, Guid? DefaultPlaylistId = null);

public static class AutoDjRules
{
    public static string NotInPlan(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return $"AutoDJ is not in the {plan.Name} plan. Upgrade to Starter or higher to use it.";
    }

    public const string NeedsPlaylist = "Choose a default playlist with at least one track before turning AutoDJ on.";
}

internal sealed class CreatePlaylistValidator : AbstractValidator<CreatePlaylistCommand>
{
    public CreatePlaylistValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
}

internal sealed class UpdatePlaylistValidator : AbstractValidator<UpdatePlaylistCommand>
{
    public UpdatePlaylistValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(100).When(c => c.Name is not null);
}

internal sealed class SetPlaylistTracksValidator : AbstractValidator<SetPlaylistTracksCommand>
{
    public SetPlaylistTracksValidator()
    {
        RuleFor(c => c.TrackIds).NotNull()
            .Must(ids => ids is null || ids.Count <= Playlist.MaxItems).WithMessage($"A playlist holds at most {Playlist.MaxItems} entries.");
        RuleForEach(c => c.TrackIds).NotEmpty();
    }
}
