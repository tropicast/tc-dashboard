namespace Tropicast.Dashboard.Infrastructure.Provisioning;

/// <summary>Provisioning settings (<c>Provisioning</c> section).</summary>
public sealed class ProvisioningOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Reconcile at least this often, even without events.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>First retry delay after a failed apply; doubles up to 10 minutes.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Node all stations are on (MVP: one node).</summary>
    public string Node { get; set; } = "tc-stream-1";
    public SshOptions Ssh { get; set; } = new();
}

/// <summary>
/// SSH to the node with a key that the node restricts to <c>deploy.sh apply-stations</c> (forced command).
/// </summary>
public sealed class SshOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "deploy";
    /// <summary>OpenSSH private key (PEM text). From a secret, never committed.</summary>
    public string? PrivateKey { get; set; }
    /// <summary>The node's host key, SHA256 fingerprint as OpenSSH prints it (with or without "SHA256:").</summary>
    public string? HostKeySha256 { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}
