using System.Text.Json;
using Microsoft.Extensions.Options;
using Stripe;
using StripeBilling.Application.Webhooks;

namespace StripeBilling.Infrastructure.Stripe;

public sealed class InvalidWebhookSignatureException : Exception
{
    public InvalidWebhookSignatureException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Verifies the Stripe-Signature header (HMAC-SHA256 over timestamp and raw body, with a replay window)
/// before any JSON is trusted, then parses only the fields the processor needs.
/// </summary>
public sealed class StripeWebhookVerifier
{
    private readonly StripeOptions _options;

    public StripeWebhookVerifier(IOptions<StripeOptions> options) => _options = options.Value;

    public StripeWebhookEvent Verify(string payload, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            throw new InvalidWebhookSignatureException("Missing Stripe-Signature header.");
        }

        try
        {
            EventUtility.ValidateSignature(payload, signatureHeader, _options.WebhookSecret, _options.WebhookToleranceSeconds);
        }
        catch (StripeException ex)
        {
            throw new InvalidWebhookSignatureException("Invalid Stripe webhook signature.", ex);
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var dataObject = root.GetProperty("data").GetProperty("object").Clone();

        return new StripeWebhookEvent(
            root.GetProperty("id").GetString()!,
            root.GetProperty("type").GetString()!,
            DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("created").GetInt64()),
            dataObject);
    }
}
