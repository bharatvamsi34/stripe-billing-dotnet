using System.ComponentModel.DataAnnotations;

namespace StripeBilling.Infrastructure.Stripe;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Secret API key (sk_test_... or sk_live_...). Never commit a real key.</summary>
    [Required]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Webhook signing secret (whsec_...) from the Dashboard or `stripe listen`.</summary>
    [Required]
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Maximum age of a webhook signature, to block replayed requests.</summary>
    [Range(30, 3600)]
    public int WebhookToleranceSeconds { get; set; } = 300;

    public bool IsTestMode => SecretKey.StartsWith("sk_test_", StringComparison.Ordinal);
}
