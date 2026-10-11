using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Directories;

namespace Tropicast.Dashboard.Infrastructure.Directories;

/// <summary>
/// RadioBrowser's public API (<c>POST /json/add</c>). Servers are tried in random order, as RadioBrowser asks;
/// a refusal from one server is final, a network failure moves to the next.
/// </summary>
internal sealed class RadioBrowserDirectory : IRadioDirectory, IDisposable
{
    private const string DiscoveryHost = "all.api.radio-browser.info";

    private readonly IOptions<DirectoryOptions> options;
    private readonly HttpClient _http;

    public RadioBrowserDirectory(IOptions<DirectoryOptions> options) : this(options, null)
    {
    }

    /// <summary>For tests: another HTTP handler.</summary>
    internal RadioBrowserDirectory(IOptions<DirectoryOptions> options, HttpMessageHandler? handler)
    {
        this.options = options;
        _http = CreateClient(options.Value.UserAgent, handler);
    }

    public async Task<string> AddAsync(DirectoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var fields = new Dictionary<string, string> { ["name"] = entry.Name, ["url"] = entry.StreamUrl.AbsoluteUri };
        Add(fields, "homepage", entry.Homepage?.AbsoluteUri);
        Add(fields, "favicon", entry.Logo?.AbsoluteUri);
        Add(fields, "countrycode", entry.CountryCode);
        Add(fields, "languagecodes", entry.LanguageCodes);
        Add(fields, "tags", entry.Genre);

        var result = await PostAsync<AddResult>("/json/add", fields, cancellationToken);
        if (result is { Ok: true, Uuid: { Length: > 0 } uuid } && Guid.TryParse(uuid, out _))
        {
            return uuid;
        }
        // Never show server text beyond a short message.
        var message = result?.Message is { Length: > 0 } text ? text[..Math.Min(text.Length, 200)] : "no station ID in the answer";
        throw new DirectoryException($"RadioBrowser refused the station: {message}");
    }

    public async Task<string?> FindAsync(Uri streamUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamUrl);
        var stations = await PostAsync<List<FoundStation>>("/json/stations/byurl",
            new Dictionary<string, string> { ["url"] = streamUrl.AbsoluteUri }, cancellationToken);
        return stations?.FirstOrDefault(s => s.Url == streamUrl.AbsoluteUri && Guid.TryParse(s.StationUuid, out _))?.StationUuid;
    }

    /// <summary>
    /// Posts a form to the servers in turn: a network error, a 5xx or an unreadable answer moves to the next server;
    /// any other answer is returned (null for a 4xx).
    /// </summary>
    private async Task<T?> PostAsync<T>(string path, Dictionary<string, string> fields, CancellationToken cancellationToken) where T : class
    {
        var servers = await ServersAsync(cancellationToken);
        if (servers.Count == 0)
        {
            throw new DirectoryException("No RadioBrowser server found. The listing is retried later.");
        }
        foreach (var server in servers)
        {
            try
            {
                using var content = new FormUrlEncodedContent(fields);
                using var response = await _http.PostAsync(new Uri(server, path), content, cancellationToken);
                if (response.StatusCode >= HttpStatusCode.InternalServerError)
                {
                    continue;
                }
                return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>(cancellationToken) : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException
                || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Unreachable, or an answer that is not the expected JSON: try the next server.
            }
        }
        throw new DirectoryException("RadioBrowser is unreachable or answered unexpectedly. The listing is retried later.");
    }

    /// <summary>Configured servers, else the reverse DNS names of <c>all.api.radio-browser.info</c>, shuffled.</summary>
    private async Task<List<Uri>> ServersAsync(CancellationToken cancellationToken)
    {
        List<Uri> servers = [.. options.Value.Servers];
        if (servers.Count == 0)
        {
            try
            {
                foreach (var address in await Dns.GetHostAddressesAsync(DiscoveryHost, cancellationToken))
                {
                    try
                    {
                        var host = (await Dns.GetHostEntryAsync(address)).HostName;
                        if (host.EndsWith(".radio-browser.info", StringComparison.OrdinalIgnoreCase))
                        {
                            servers.Add(new Uri($"https://{host}"));
                        }
                    }
                    catch (System.Net.Sockets.SocketException)
                    {
                        // No reverse name: skip this address (HTTPS needs the name).
                    }
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                return [];
            }
        }
        return [.. servers.DistinctBy(s => s.Host).OrderBy(_ => Random.Shared.Next())];
    }

    private static void Add(Dictionary<string, string> fields, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            fields[name] = value;
        }
    }

    private static HttpClient CreateClient(string userAgent, HttpMessageHandler? handler)
    {
        var http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return http;
    }

    public void Dispose() => _http.Dispose();

    private sealed record FoundStation([property: JsonPropertyName("stationuuid")] string? StationUuid,
        [property: JsonPropertyName("url")] string? Url);

    private sealed record AddResult([property: JsonPropertyName("ok")] bool Ok, [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("uuid")] string? Uuid);
}
