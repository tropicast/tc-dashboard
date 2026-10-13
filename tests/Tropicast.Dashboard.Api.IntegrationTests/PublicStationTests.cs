using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Public;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Application.Stats;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;
using Tropicast.Dashboard.Infrastructure.Stats;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>Public station page, embed and their read endpoint (#12): anonymous, cacheable, no tenant data.</summary>
public sealed class PublicStationTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string Hostile = "</script><script>alert(1)</script><b onmouseover=alert(2)>";

    private async Task<(Guid TenantId, string TenantSlug, StationResponse Station)> StationAsync(string plan = "free",
        string description = "Community radio from Antananarivo.")
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        var tenantId = await owner.CreateTenantAsync();
        if (plan != "free")
        {
            await Browser.SetPlanAsync(factory, tenantId, plan);
        }
        var response = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new
        {
            name = "Radio Mada \"FM\" <b>",
            slug = "radio-mada",
            description,
            genre = "Salegy",
            country = "MG",
            language = "fr",
            logoUrl = "https://cdn.example.test/logo.png",
        });
        response.Status(HttpStatusCode.Created);
        var station = (await response.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slug = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.Slug).SingleAsync(Token);
        return (tenantId, slug, station);
    }

    [Fact]
    public async Task Anyone_reads_the_public_station_with_streams_and_cache_headers_but_no_tenant_data()
    {
        var (tenantId, tenantSlug, station) = await StationAsync();
        var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/public/stations/{tenantSlug}/radio-mada", Token);

        response.Status(HttpStatusCode.OK);
        Assert.Equal("public, max-age=15, stale-while-revalidate=30", response.Headers.CacheControl!.ToString());
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var body = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain(tenantId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(station.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plan", body, StringComparison.OrdinalIgnoreCase);
        var publicStation = JsonSerializer.Deserialize<PublicStationResponse>(body, Browser.Json)!;
        Assert.Equal("Radio Mada \"FM\" <b>", publicStation.Name);
        Assert.Equal([AudioFormat.Opus, AudioFormat.Mp3], publicStation.Streams.Select(s => s.Format));
        Assert.Equal(station.ListenerUrls.Opus, publicStation.Streams[0].Url);
        Assert.Equal(station.ListenerUrls.Mp3, publicStation.Streams[1].Url);
        Assert.All(publicStation.Streams, s => Assert.Equal("https", s.Url.Scheme));
        Assert.Equal(new Uri($"https://app.test/listen/{tenantSlug}/radio-mada"), publicStation.PageUrl);
        Assert.Null(publicStation.EmbedUrl);
    }

    [Fact]
    public async Task Unknown_deleted_suspended_or_malformed_addresses_are_not_found()
    {
        var (tenantId, tenantSlug, _) = await StationAsync();
        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/public/stations/{tenantSlug}/nope-station", Token)).Status(HttpStatusCode.NotFound);
        (await anonymous.GetAsync("/api/v1/public/stations/..%2F..%2Fadmin/x", Token)).Status(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/listen/{tenantSlug}/nope-station", Token)).Status(HttpStatusCode.NotFound);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, TenantStatus.Suspended), Token);
        }
        (await anonymous.GetAsync($"/api/v1/public/stations/{tenantSlug}/radio-mada", Token)).Status(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_page_is_small_server_rendered_escaped_and_cannot_be_framed()
    {
        var (_, tenantSlug, _) = await StationAsync(description: Hostile);
        var response = await factory.CreateClient().GetAsync($"/listen/{tenantSlug}/radio-mada", Token);

        response.Status(HttpStatusCode.OK);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        var html = await response.Content.ReadAsStringAsync(Token);
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(html), 1, 20_000);
        Assert.Contains("<html lang=\"fr\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:title\" content=\"Radio Mada &quot;FM&quot; &lt;b&gt;\">", html, StringComparison.Ordinal);
        Assert.Contains($"<link rel=\"canonical\" href=\"https://app.test/listen/{tenantSlug}/radio-mada\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:image\" content=\"https://cdn.example.test/logo.png\">", html, StringComparison.Ordinal);
        // Hostile station text never becomes markup or closes the data block.
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b onmouseover", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("</script>").Length - 1);

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        var nonce = System.Text.RegularExpressions.Regex.Match(csp, "script-src 'nonce-([^']+)'").Groups[1].Value;
        Assert.NotEmpty(nonce);
        Assert.Contains($"<script nonce=\"{nonce}\">", html, StringComparison.Ordinal);
        Assert.Contains("media-src https://listen.tropicastradio.com;", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("max-age=30", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Embedding_follows_the_plan()
    {
        var anonymous = factory.CreateClient();
        var (_, freeTenant, _) = await StationAsync();
        var free = await anonymous.GetAsync($"/embed/{freeTenant}/radio-mada", Token);
        free.Status(HttpStatusCode.Forbidden);
        Assert.Contains("embedding is not included", await free.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);

        var (_, growthTenant, _) = await StationAsync(plan: "growth");
        var embed = await anonymous.GetAsync($"/embed/{growthTenant}/radio-mada", Token);
        embed.Status(HttpStatusCode.OK);
        Assert.Contains("frame-ancestors *", embed.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.False(embed.Headers.Contains("X-Frame-Options"));
        var html = await embed.Content.ReadAsStringAsync(Token);
        Assert.Contains("<body class=\"embed\">", html, StringComparison.Ordinal);
        Assert.Contains("noindex", html, StringComparison.Ordinal);

        var info = await anonymous.GetFromJsonAsync<PublicStationResponse>($"/api/v1/public/stations/{growthTenant}/radio-mada", Browser.Json, Token);
        Assert.Equal(new Uri($"https://app.test/embed/{growthTenant}/radio-mada"), info!.EmbedUrl);

        var script = await anonymous.GetAsync("/embed.js", Token);
        script.Status(HttpStatusCode.OK);
        Assert.Equal("text/javascript; charset=utf-8", script.Content.Headers.ContentType!.ToString());
        Assert.Contains("/embed/", await script.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_and_endpoint_show_the_live_state_from_the_collector()
    {
        var (_, tenantSlug, station) = await StationAsync();
        factory.Stats.Next = new NodeSample("tc-stream-1", DateTimeOffset.UtcNow, true,
            [new MountSample(station.PublicId, AudioFormat.Mp3, 3, 128, 0, DateTimeOffset.UtcNow)], null, null);
        await ActivatorUtilities.CreateInstance<StatsCollector>(factory.Services).SampleOnceAsync(Token);

        var anonymous = factory.CreateClient();
        var info = await anonymous.GetFromJsonAsync<PublicStationResponse>($"/api/v1/public/stations/{tenantSlug}/radio-mada", Browser.Json, Token);
        Assert.True(info!.Live);
        var html = await anonymous.GetStringAsync($"/listen/{tenantSlug}/radio-mada", Token);
        Assert.Contains("\"live\":true", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_play_button_is_a_plain_button_whose_label_says_what_it_does()
    {
        // #74: Play/Stop text plus aria-pressed made screen readers say "Stop, pressed".
        var (_, tenantSlug, _) = await StationAsync();

        var html = await factory.CreateClient().GetStringAsync($"/listen/{tenantSlug}/radio-mada", Token);

        Assert.Contains("<button id=\"play\" class=\"play\" type=\"button\">", html, StringComparison.Ordinal);
        Assert.Contains("<span id=\"play-label\" class=\"sr\">Écouter</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-pressed", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_player_card_names_every_icon_control_and_links_the_streams()
    {
        // #86: the dashboard-styled card. Icon-only controls need an accessible name; nothing loads from elsewhere.
        var (_, tenantSlug, station) = await StationAsync(plan: "growth");
        var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/listen/{tenantSlug}/radio-mada", Token);

        Assert.Contains("<section id=\"card\" class=\"card\" aria-labelledby=\"name\">", html, StringComparison.Ordinal);
        Assert.Contains("id=\"share\" class=\"icon\" type=\"button\" aria-label=\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"mute\" class=\"icon\" type=\"button\" aria-label=\"", html, StringComparison.Ordinal);
        Assert.Contains("<input id=\"volume\" type=\"range\"", html, StringComparison.Ordinal);
        Assert.Contains("<summary aria-label=\"", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{station.ListenerUrls.Mp3.AbsoluteUri}\" target=\"_blank\" rel=\"noopener\">Ouvrir le flux MP3</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" style=\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html, StringComparison.Ordinal);
        Assert.True(html.Length < 30_000, $"page is {html.Length} characters");

        var embed = await client.GetStringAsync($"/embed/{tenantSlug}/radio-mada", Token);
        Assert.Contains($"/listen/{tenantSlug}/radio-mada\" target=\"_blank\" rel=\"noopener\">", embed, StringComparison.Ordinal);
    }
}
