using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Tropicast.Dashboard.Api.Auth;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>Runs every credential flow with Trace logging and checks no secret reached any log line.</summary>
public sealed class SecretLoggingTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    [Fact]
    public async Task No_password_token_or_code_appears_in_logs()
    {
        var token = TestContext.Current.CancellationToken;
        var secrets = new List<string>();
        var browser = new Browser(factory);
        var email = Browser.Unique("logs");
        const string password = "log check passphrase 1";
        secrets.Add(password);

        (await browser.Http.PostAsJsonAsync("/api/v1/auth/register", new { email, password }, token)).Status(HttpStatusCode.Accepted);
        var confirm = QueryHelpers.ParseQuery(Browser.LinkIn(factory.Emails.Last(email).TextBody).Query);
        secrets.Add(confirm["code"].ToString());
        (await browser.Http.PostAsJsonAsync("/api/v1/auth/confirm-email",
            new { userId = Guid.Parse(confirm["userId"]!), code = confirm["code"].ToString() }, token)).Status(HttpStatusCode.NoContent);
        (await browser.LoginAsync(email, password)).Status(HttpStatusCode.NoContent);
        (await browser.LoginAsync(email, "wrong passphrase for logs")).Status(HttpStatusCode.Unauthorized);
        secrets.Add("wrong passphrase for logs");

        var desktop = factory.CreateClient();
        var issued = await (await desktop.PostAsJsonAsync("/api/v1/auth/token", new { email, password, deviceName = "Studio PC" }, token))
            .Content.ReadFromJsonAsync<TokenResponse>(token);
        var refreshed = await (await desktop.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken = issued!.RefreshToken }, token))
            .Content.ReadFromJsonAsync<TokenResponse>(token);
        secrets.AddRange([issued.AccessToken, issued.RefreshToken, refreshed!.AccessToken, refreshed.RefreshToken]);
        (await desktop.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken = issued.RefreshToken }, token))
            .Status(HttpStatusCode.Unauthorized);

        (await browser.SendAsync(HttpMethod.Post, "/api/v1/auth/forgot-password", new { email })).Status(HttpStatusCode.Accepted);
        var reset = QueryHelpers.ParseQuery(Browser.LinkIn(factory.Emails.Last(email).TextBody).Query)["code"].ToString();
        const string newPassword = "another log check passphrase";
        secrets.AddRange([reset, newPassword]);
        (await browser.SendAsync(HttpMethod.Post, "/api/v1/auth/reset-password", new { email, code = reset, newPassword }))
            .Status(HttpStatusCode.NoContent);

        var lines = factory.Logs.Lines;
        Assert.NotEmpty(lines);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(lines, line => line.Contains(secret, StringComparison.Ordinal));
            // Also the raw Identity codes inside the URL-safe link codes.
            if (AuthEmails.Decode(secret) is { Length: > 8 } raw)
            {
                Assert.DoesNotContain(lines, line => line.Contains(raw, StringComparison.Ordinal));
            }
        }
    }
}
