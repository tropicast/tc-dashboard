using Tropicast.Dashboard.Domain.Stations;

namespace Tropicast.Dashboard.Domain.Media;

/// <summary>An ordered list of a station's tracks that AutoDJ plays (#105), in order or shuffled.</summary>
public sealed class Playlist
{
    /// <summary>Most entries in one playlist.</summary>
    public const int MaxItems = 2000;

    private readonly List<PlaylistItem> _items = [];

    private Playlist()
    {
    }

    public Guid Id { get; private set; }
    public Guid StationId { get; private set; }
    public Station Station { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    /// <summary>Play in random order instead of list order.</summary>
    public bool Shuffle { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>In play order.</summary>
    public IReadOnlyList<PlaylistItem> Items => _items;

    public static Playlist Create(Guid stationId, string name, bool shuffle, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        StationId = stationId,
        Name = Text.Required(name, 100, nameof(name)),
        Shuffle = shuffle,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Rename(string name, DateTimeOffset now)
    {
        Name = Text.Required(name, 100, nameof(name));
        UpdatedAt = now;
    }

    public void SetShuffle(bool shuffle, DateTimeOffset now)
    {
        Shuffle = shuffle;
        UpdatedAt = now;
    }

    /// <summary>Replaces the entries with <paramref name="trackIds"/>, in that order; a track may appear more than once.</summary>
    public void SetTracks(IReadOnlyList<Guid> trackIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        if (trackIds.Count > MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(trackIds), $"At most {MaxItems} entries.");
        }
        _items.Clear();
        for (var i = 0; i < trackIds.Count; i++)
        {
            _items.Add(new PlaylistItem(Guid.CreateVersion7(now), Id, trackIds[i], i));
        }
        UpdatedAt = now;
    }
}

/// <summary>One entry of a playlist: a track at a position.</summary>
public sealed class PlaylistItem
{
    private PlaylistItem()
    {
    }

    internal PlaylistItem(Guid id, Guid playlistId, Guid trackId, int position)
    {
        Id = id;
        PlaylistId = playlistId;
        TrackId = trackId;
        Position = position;
    }

    public Guid Id { get; private set; }
    public Guid PlaylistId { get; private set; }
    public Guid TrackId { get; private set; }
    public Track Track { get; private set; } = null!;
    /// <summary>0-based play order.</summary>
    public int Position { get; private set; }
}
