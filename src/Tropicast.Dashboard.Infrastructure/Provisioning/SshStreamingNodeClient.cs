using System.Text;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Tropicast.Dashboard.Application.Provisioning;

namespace Tropicast.Dashboard.Infrastructure.Provisioning;

/// <summary>
/// Sends <c>stations.json</c> over SSH to the node's forced command (<c>deploy.sh apply-stations</c>). The host key
/// must match the pinned fingerprint; the key can run nothing else on the node.
/// </summary>
internal sealed class SshStreamingNodeClient(IOptions<ProvisioningOptions> options) : IStreamingNodeClient
{
    public async Task ApplyStationsAsync(string node, string stationsJson, CancellationToken cancellationToken)
    {
        var ssh = options.Value.Ssh;
        if (string.IsNullOrWhiteSpace(ssh.Host) || string.IsNullOrWhiteSpace(ssh.PrivateKey) || string.IsNullOrWhiteSpace(ssh.HostKeySha256))
        {
            throw new InvalidOperationException("Provisioning SSH is not configured (Provisioning:Ssh Host, PrivateKey, HostKeySha256).");
        }
        using var key = new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(ssh.PrivateKey)));
        var connection = new ConnectionInfo(ssh.Host, ssh.Port, ssh.Username, new PrivateKeyAuthenticationMethod(ssh.Username, key))
        {
            Timeout = ssh.Timeout,
        };
        using var client = new SshClient(connection);
        var expected = ssh.HostKeySha256.Replace("SHA256:", "", StringComparison.Ordinal).TrimEnd('=');
        client.HostKeyReceived += (_, e) => e.CanTrust = string.Equals(e.FingerPrintSHA256.TrimEnd('='), expected, StringComparison.Ordinal);
        await client.ConnectAsync(cancellationToken);
        try
        {
            // The node's forced command ignores this command line and runs apply-stations, reading stdin.
            using var command = client.CreateCommand("apply-stations");
            command.CommandTimeout = ssh.Timeout;
            var execution = command.ExecuteAsync(cancellationToken);
            await using (var input = command.CreateInputStream())
            {
                await input.WriteAsync(Encoding.UTF8.GetBytes(stationsJson), cancellationToken);
            }
            await execution;
            if (command.ExitStatus != 0)
            {
                throw new InvalidOperationException($"apply-stations on {node} exited with {command.ExitStatus}: {Trim(command.Error)}");
            }
        }
        finally
        {
            client.Disconnect();
        }
    }

    private static string Trim(string? text) => text is null ? "" : text.Length <= 200 ? text.Trim() : text[..200].Trim();
}
