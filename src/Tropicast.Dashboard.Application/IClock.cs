namespace Tropicast.Dashboard.Application;

/// <summary>Current time, so use cases stay testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
