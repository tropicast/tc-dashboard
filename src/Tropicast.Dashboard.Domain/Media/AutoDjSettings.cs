using Tropicast.Dashboard.Domain.Stations;

namespace Tropicast.Dashboard.Domain.Media;

/// <summary>AutoDJ was turned on or off, or its playlist changed: provisioning and the playout worker follow (#107).</summary>
public sealed record AutoDjChanged(Guid StationId, bool Enabled, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// A station's AutoDJ (#105): whether it plays when no DJ is live, and which playlist. It only plays while the plan
/// includes AutoDJ (<see cref="Plans.Plan.AutoDj"/>); a downgrade keeps the choice for a later upgrade.
/// </summary>
public sealed class AutoDjSettings : IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    private AutoDjSettings()
    {
    }

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    public void ClearDomainEvents() => _events.Clear();

    public Guid StationId { get; private set; }
    public Station Station { get; private set; } = null!;
    public bool Enabled { get; private set; }
    /// <summary>The playlist AutoDJ plays; null when none is chosen (or it was deleted).</summary>
    public Guid? DefaultPlaylistId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AutoDjSettings For(Guid stationId, DateTimeOffset now) => new() { StationId = stationId, UpdatedAt = now };

    /// <summary>Applies a change; records an event only when something changed.</summary>
    public void Set(bool enabled, Guid? defaultPlaylistId, DateTimeOffset now)
    {
        if (enabled && defaultPlaylistId is null)
        {
            throw new InvalidOperationException("AutoDJ needs a default playlist.");
        }
        if (enabled == Enabled && defaultPlaylistId == DefaultPlaylistId)
        {
            return;
        }
        Enabled = enabled;
        DefaultPlaylistId = defaultPlaylistId;
        UpdatedAt = now;
        _events.Add(new AutoDjChanged(StationId, enabled, now));
    }
}
