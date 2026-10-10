namespace Tropicast.Dashboard.Infrastructure.Provisioning;

/// <summary>What the control plane wants on a streaming node and what was last applied there.</summary>
public sealed class StreamingNodeState
{
    private StreamingNodeState()
    {
    }

    /// <summary>Node name, e.g. <c>tc-stream-1</c>.</summary>
    public string Node { get; private set; } = null!;
    public string? DesiredVersion { get; private set; }
    public string? AppliedVersion { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    /// <summary>Last failure, short and without secrets.</summary>
    public string? LastError { get; private set; }

    public bool InSync => DesiredVersion is not null && DesiredVersion == AppliedVersion;

    internal static StreamingNodeState For(string node) => new() { Node = node };

    internal void Want(string version) => DesiredVersion = version;

    internal bool Due(DateTimeOffset now) => NextAttemptAt is null || now >= NextAttemptAt;

    internal void Applied(string version, DateTimeOffset now)
    {
        AppliedVersion = version;
        AppliedAt = now;
        LastAttemptAt = now;
        FailedAttempts = 0;
        NextAttemptAt = null;
        LastError = null;
    }

    /// <summary>Exponential backoff from <paramref name="baseDelay"/>, capped at 10 minutes.</summary>
    internal void Failed(string error, DateTimeOffset now, TimeSpan baseDelay)
    {
        FailedAttempts++;
        LastAttemptAt = now;
        LastError = error.Length <= 500 ? error : error[..500];
        var delay = TimeSpan.FromTicks((long)Math.Min(TimeSpan.FromMinutes(10).Ticks, baseDelay.Ticks * Math.Pow(2, FailedAttempts - 1)));
        NextAttemptAt = now + delay;
    }

    /// <summary>The operator asked to retry now.</summary>
    internal void RetryNow() => NextAttemptAt = null;
}
