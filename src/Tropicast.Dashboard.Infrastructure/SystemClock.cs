using Tropicast.Dashboard.Application;

namespace Tropicast.Dashboard.Infrastructure;

internal sealed class SystemClock(TimeProvider time) : IClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow();
}
