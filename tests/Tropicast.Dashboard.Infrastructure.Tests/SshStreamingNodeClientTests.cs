using System.Diagnostics;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Infrastructure.Provisioning;

namespace Tropicast.Dashboard.Infrastructure.Tests;

/// <summary>
/// The SSH client against a real OpenSSH server whose key only runs a forced command, the way the streaming node
/// restricts the provisioning key to <c>deploy.sh apply-stations</c>.
/// </summary>
public sealed class SshStreamingNodeClientTests : IAsyncLifetime
{
    private readonly string _keys = Directory.CreateTempSubdirectory("tc-ssh-").FullName;
    private IContainer _server = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        using (var keygen = Process.Start("ssh-keygen", ["-t", "ed25519", "-N", "", "-q", "-f", Path.Combine(_keys, "id")])!)
        {
            await keygen.WaitForExitAsync(Token);
        }
        var publicKey = (await File.ReadAllTextAsync(Path.Combine(_keys, "id.pub"), Token)).Trim();
        _server = new ContainerBuilder("mirror.gcr.io/linuxserver/openssh-server:10.3_p1-r1-ls238")
            .WithEnvironment("USER_NAME", "deploy")
            .WithEnvironment("PUBLIC_KEY", $"command=\"cat > /tmp/applied.json\",restrict {publicKey}")
            .WithPortBinding(2222, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("done\\."))
            .Build();
        await _server.StartAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        Directory.Delete(_keys, recursive: true);
    }

    private async Task<string> HostKeyFingerprintAsync()
    {
        var pub = await _server.ExecAsync(["cat", "/config/ssh_host_keys/ssh_host_ed25519_key.pub"], Token);
        var blob = Convert.FromBase64String(pub.Stdout.Split(' ')[1]);
        return "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');
    }

    private SshStreamingNodeClient Client(string fingerprint) => new(Options.Create(new ProvisioningOptions
    {
        Ssh = new SshOptions
        {
            Host = _server.Hostname, Port = _server.GetMappedPublicPort(2222), Username = "deploy",
            PrivateKey = File.ReadAllText(Path.Combine(_keys, "id")), HostKeySha256 = fingerprint,
        },
    }));

    [Fact]
    public async Task Stations_json_reaches_the_forced_command_on_stdin()
    {
        const string json = "{\n  \"default\": {\"max_listeners\": 100},\n  \"stations\": {}\n}\n";
        await Client(await HostKeyFingerprintAsync()).ApplyStationsAsync("tc-stream-1", json, Token);
        var applied = await _server.ExecAsync(["cat", "/tmp/applied.json"], Token);
        Assert.Equal(json, applied.Stdout);
    }

    [Fact]
    public async Task An_unexpected_host_key_is_refused()
    {
        var wrong = "SHA256:" + Convert.ToBase64String(SHA256.HashData("not the node"u8.ToArray())).TrimEnd('=');
        await Assert.ThrowsAnyAsync<Exception>(() => Client(wrong).ApplyStationsAsync("tc-stream-1", "{}", Token));
        var applied = await _server.ExecAsync(["sh", "-c", "test -e /tmp/applied.json && echo present || echo absent"], Token);
        Assert.Equal("absent", applied.Stdout.Trim());
    }

    [Fact]
    public async Task Missing_configuration_fails_clearly()
    {
        var client = new SshStreamingNodeClient(Options.Create(new ProvisioningOptions()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ApplyStationsAsync("tc-stream-1", "{}", Token));
        Assert.Contains("not configured", error.Message, StringComparison.Ordinal);
    }
}
