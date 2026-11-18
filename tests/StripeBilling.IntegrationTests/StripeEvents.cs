using System.Text;
using System.Text.Json;
using Stripe;

namespace StripeBilling.IntegrationTests;

/// <summary>Builds Stripe-shaped webhook payloads and signs them exactly as Stripe does.</summary>
public static class StripeEvents
{
    public static string InvoicePaid(string eventId, string invoiceId, string customerId, string subscriptionId, long amount, DateTimeOffset? created = null) =>
        Event(eventId, "invoice.paid", created, new
        {
            id = invoiceId,
            @object = "invoice",
            customer = customerId,
            amount_due = amount,
            amount_paid = amount,
            currency = "usd",
            attempt_count = 1,
            status = "paid",
            parent = new { type = "subscription_details", subscription_details = new { subscription = subscriptionId } },
            status_transitions = new { paid_at = (created ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() },
        });

    public static string InvoicePaymentFailed(string eventId, string invoiceId, string customerId, long amount, int attempt) =>
        Event(eventId, "invoice.payment_failed", null, new
        {
            id = invoiceId,
            @object = "invoice",
            customer = customerId,
            amount_due = amount,
            amount_paid = 0,
            currency = "usd",
            attempt_count = attempt,
            status = "open",
        });

    public static string SubscriptionUpdated(string eventId, string subscriptionId, string status, DateTimeOffset created) =>
        Event(eventId, "customer.subscription.updated", created, new
        {
            id = subscriptionId,
            @object = "subscription",
            status,
        });

    public static string Sign(string payload, string secret, DateTimeOffset? at = null) =>
        EventUtility.GenerateSignatureHeader(payload, secret, (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds());

    public static StringContent AsContent(string payload) => new(payload, Encoding.UTF8, "application/json");

    private static string Event(string id, string type, DateTimeOffset? created, object dataObject) =>
        JsonSerializer.Serialize(new
        {
            id,
            @object = "event",
            type,
            api_version = "2025-03-31.basil",
            created = (created ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
            livemode = false,
            data = new { @object = dataObject },
        });
}
