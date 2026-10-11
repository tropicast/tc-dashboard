using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Directories;
using Tropicast.Dashboard.Infrastructure.Directories;

namespace Tropicast.Dashboard.Infrastructure.Tests;

public sealed class RadioBrowserDirectoryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly DirectoryEntry Entry = new(true, "Radio Mada", "Salegy all day.", "salegy", new Uri("https://radiomada.example/"),
        new Uri("https://cdn.example.test/logo.png"), "MG", "fr", new Uri("https://listen.tropicastradio.com/stations/k3m9x2p7qa/live.mp3"));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(Uri Url, string UserAgent, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.UserAgent.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private static RadioBrowserDirectory Directory(Handler handler, params string[] servers) => new(Options.Create(new DirectoryOptions
    {
        Servers = [.. servers.Select(s => new Uri(s))],
    }), handler);

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Adds_the_station_with_its_public_fields_and_returns_the_uuid()
    {
        var uuid = Guid.NewGuid().ToString();
        var handler = new Handler(_ => Json(HttpStatusCode.OK, $$"""{"ok":true,"message":"added station successfully","uuid":"{{uuid}}"}"""));
        using var directory = Directory(handler, "https://de1.api.radio-browser.info");

        Assert.Equal(uuid, await directory.AddAsync(Entry, Token));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://de1.api.radio-browser.info/json/add", request.Url.AbsoluteUri);
        Assert.StartsWith("Tropicast/", request.UserAgent, StringComparison.Ordinal);
        var fields = request.Body.Split('&').Select(f => f.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
        Assert.Equal("Radio Mada", fields["name"]);
        Assert.Equal("https://listen.tropicastradio.com/stations/k3m9x2p7qa/live.mp3", fields["url"]);
        Assert.Equal(("MG", "fr", "salegy"), (fields["countrycode"], fields["languagecodes"], fields["tags"]));
        Assert.Equal("https://cdn.example.test/logo.png", fields["favicon"]);
        Assert.Equal("https://radiomada.example/", fields["homepage"]);
    }

    [Fact]
    public async Task A_server_that_is_down_moves_to_the_next_one()
    {
        var uuid = Guid.NewGuid().ToString();
        var handler = new Handler(request => request.RequestUri!.Host == "down.radio-browser.info"
            ? throw new HttpRequestException("Connection refused")
            : Json(HttpStatusCode.OK, $$"""{"ok":true,"message":"ok","uuid":"{{uuid}}"}"""));
        using var directory = Directory(handler, "https://down.radio-browser.info", "https://up.radio-browser.info");

        Assert.Equal(uuid, await directory.AddAsync(Entry, Token));
    }

    [Fact]
    public async Task A_refusal_is_final_and_an_outage_everywhere_is_reported()
    {
        using var refusing = Directory(new Handler(_ => Json(HttpStatusCode.OK, """{"ok":false,"message":"countrycode is invalid"}""")),
            "https://a.radio-browser.info", "https://b.radio-browser.info");
        var refused = await Assert.ThrowsAsync<DirectoryException>(() => refusing.AddAsync(Entry, Token));
        Assert.Contains("countrycode is invalid", refused.Message, StringComparison.Ordinal);

        var down = new Handler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var unreachable = Directory(down, "https://a.radio-browser.info", "https://b.radio-browser.info");
        var outage = await Assert.ThrowsAsync<DirectoryException>(() => unreachable.AddAsync(Entry, Token));
        Assert.Contains("unreachable", outage.Message, StringComparison.Ordinal);
        Assert.Equal(2, down.Requests.Count);
    }

    [Fact]
    public async Task An_unreadable_answer_moves_on_and_ends_as_a_directory_failure()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>maintenance</html>") });
        using var directory = Directory(handler, "https://a.radio-browser.info", "https://b.radio-browser.info");

        var error = await Assert.ThrowsAsync<DirectoryException>(() => directory.AddAsync(Entry, Token));
        Assert.Contains("unexpectedly", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Finds_an_existing_entry_by_its_exact_stream_url()
    {
        var uuid = Guid.NewGuid().ToString();
        var handler = new Handler(_ => Json(HttpStatusCode.OK, $$"""
            [{"stationuuid":"{{Guid.NewGuid()}}","url":"https://listen.tropicastradio.com/stations/k3m9x2p7qa/live.mp3?x=1"},
             {"stationuuid":"{{uuid}}","url":"https://listen.tropicastradio.com/stations/k3m9x2p7qa/live.mp3"}]
            """));
        using var directory = Directory(handler, "https://de1.api.radio-browser.info");

        Assert.Equal(uuid, await directory.FindAsync(Entry.StreamUrl, Token));
        Assert.Equal("https://de1.api.radio-browser.info/json/stations/byurl", handler.Requests[0].Url.AbsoluteUri);

        using var empty = Directory(new Handler(_ => Json(HttpStatusCode.OK, "[]")), "https://de1.api.radio-browser.info");
        Assert.Null(await empty.FindAsync(Entry.StreamUrl, Token));
    }
}
