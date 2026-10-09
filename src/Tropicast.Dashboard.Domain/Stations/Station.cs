using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Domain.Stations;

/// <summary>A radio station. Deleting it is soft: its history (sessions, stats) stays.</summary>
public sealed class Station : ITenantOwned, IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    private Station()
    {
    }

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    public void ClearDomainEvents() => _events.Clear();

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    /// <summary>Immutable; see <see cref="StationPublicId"/>.</summary>
    public string PublicId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string Description { get; private set; } = "";
    public string Genre { get; private set; } = "";
    /// <summary>ISO 3166-1 alpha-2 country code, e.g. <c>MG</c>.</summary>
    public string? Country { get; private set; }
    /// <summary>BCP 47 language tag, e.g. <c>mg</c> or <c>fr</c>.</summary>
    public string? Language { get; private set; }
    public Uri? LogoUrl { get; private set; }
    public Uri? Website { get; private set; }
    /// <summary>Opt-in listing on RadioBrowser (plan-gated).</summary>
    public bool ListInDirectory { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static Station Create(Guid tenantId, string name, string slug, DateTimeOffset now)
    {
        var station = new Station
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            PublicId = StationPublicId.New(),
            Name = Text.Required(name, 100, nameof(name)),
            Slug = Text.Slug(slug, nameof(slug)),
            CreatedAt = now,
        };
        station._events.Add(new StationCreated(station.Id, tenantId, station.PublicId, now));
        return station;
    }

    public void Rename(string name, string slug, DateTimeOffset now)
    {
        Name = Text.Required(name, 100, nameof(name));
        Slug = Text.Slug(slug, nameof(slug));
        Changed(now);
    }

    public void Describe(string? description, string? genre, string? country, string? language, Uri? logoUrl, Uri? website,
        DateTimeOffset now)
    {
        Description = Text.Optional(description, 2000, nameof(description));
        Genre = Text.Optional(genre, 64, nameof(genre));
        Country = string.IsNullOrWhiteSpace(country) ? null : Text.Required(country, 2, nameof(country)).ToUpperInvariant();
        Language = string.IsNullOrWhiteSpace(language) ? null : Text.Required(language, 35, nameof(language));
        LogoUrl = logoUrl;
        Website = website;
        Changed(now);
    }

    /// <summary>Directory listing is allowed only when the plan includes it.</summary>
    public void SetDirectoryListing(bool listed, Plan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ListInDirectory = listed && plan.DirectoryListing;
        Changed(now);
    }

    /// <summary>Soft delete; idempotent. The public ID is never reused.</summary>
    public void Delete(DateTimeOffset now)
    {
        if (DeletedAt is null)
        {
            DeletedAt = now;
            _events.Add(new StationDeleted(Id, TenantId, PublicId, now));
        }
    }

    /// <summary>One change event per save is enough for consumers, who reload the station.</summary>
    private void Changed(DateTimeOffset now)
    {
        if (!_events.Any(e => e is StationChanged or StationCreated))
        {
            _events.Add(new StationChanged(Id, TenantId, PublicId, now));
        }
    }
}
