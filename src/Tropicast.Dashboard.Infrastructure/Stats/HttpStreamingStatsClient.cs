using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Stats;

namespace Tropicast.Dashboard.Infrastructure.Stats;

/// <summary>Reads the tc-streaming exporter's <c>/metrics</c> over the private network.</summary>
internal sealed class HttpStreamingStatsClient(IOptions<StatsOptions> options, TimeProvider time) : IStreamingStatsClient, IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 4 * 1024 * 1024,
    };

    public async Task<NodeSample> GetAsync(string node, CancellationToken cancellationToken = default)
    {
        var url = options.Value.ExporterUrl ?? throw new InvalidOperationException("Set Stats:Url to the node's exporter.");
        var text = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return ExporterMetrics.Parse(node, text, time.GetUtcNow());
    }

    public void Dispose() => _http.Dispose();
}
