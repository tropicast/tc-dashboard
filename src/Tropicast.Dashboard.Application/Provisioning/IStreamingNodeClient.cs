namespace Tropicast.Dashboard.Application.Provisioning;

/// <summary>Applies a rendered <c>stations.json</c> to a streaming node (no Icecast restart).</summary>
public interface IStreamingNodeClient
{
    Task ApplyStationsAsync(string node, string stationsJson, CancellationToken cancellationToken);
}
