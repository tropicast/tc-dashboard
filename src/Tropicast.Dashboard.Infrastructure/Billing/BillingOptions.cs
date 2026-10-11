namespace Tropicast.Dashboard.Infrastructure.Billing;

/// <summary>Stripe billing (<c>Billing</c> section). Without <see cref="SecretKey"/> billing is off.</summary>
public sealed class BillingOptions
{
    /// <summary>Stripe secret key (<c>sk_test_…</c> in test mode). A secret.</summary>
    public string? SecretKey { get; set; }
    /// <summary>Signing secret of the webhook endpoint (<c>whsec_…</c>). A secret.</summary>
    public string? WebhookSecret { get; set; }
    /// <summary>Stripe price ID per paid plan, e.g. <c>starter: price_…</c> (monthly, recurring).</summary>
    public Dictionary<string, string> Prices { get; set; } = [];
    /// <summary>Price shown in the app per plan, e.g. <c>starter: "€9 / month"</c> (Stripe stays the source of truth).</summary>
    public Dictionary<string, string> DisplayPrices { get; set; } = [];
    /// <summary>Free days before the first charge, once per tenant.</summary>
    public int TrialDays { get; set; } = 14;
    /// <summary>After a failed renewal, the paid plan stays this long before the tenant falls back to Free.</summary>
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromDays(7);
    /// <summary>How often to apply grace-period ends.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    public bool Enabled => !string.IsNullOrWhiteSpace(SecretKey);
}
