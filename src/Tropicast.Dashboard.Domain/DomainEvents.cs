namespace Tropicast.Dashboard.Domain;

/// <summary>Something that happened in the domain, published to other parts of the system through the outbox.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>An entity that records domain events until they are saved.</summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
