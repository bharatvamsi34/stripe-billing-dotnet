using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StripeBilling.Application.Abstractions;
using StripeBilling.Application.Common;
using StripeBilling.Domain.Invoices;
using StripeBilling.Domain.Ledger;
using StripeBilling.Domain.Operations;

namespace StripeBilling.Application.Webhooks;

/// <summary>A Stripe event whose signature has already been verified.</summary>
public sealed record StripeWebhookEvent(string Id, string Type, DateTimeOffset Created, JsonElement DataObject);

public enum WebhookOutcome
{
    Processed,
    Duplicate,
    Ignored,
}

/// <summary>
/// Applies Stripe events to local state.
/// Guarantees:
/// 1. Exactly-once effect: the event id is stored in the same database transaction as the changes it causes.
/// 2. Out-of-order safety: subscription updates older than the last applied change are skipped.
/// 3. No double posting: the ledger has a unique index per invoice and account as a second line of defence.
/// </summary>
public sealed class WebhookProcessor
{
    private const string Actor = "stripe-webhook";

    private readonly IBillingDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<WebhookProcessor> _logger;

    public WebhookProcessor(IBillingDbContext db, IClock clock, ILogger<WebhookProcessor> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<WebhookOutcome> ProcessAsync(StripeWebhookEvent evt, CancellationToken ct)
    {
        if (await _db.ProcessedWebhookEvents.AnyAsync(e => e.StripeEventId == evt.Id, ct))
        {
            _logger.LogInformation("Skipping duplicate Stripe event {EventId} ({Type})", evt.Id, evt.Type);
            return WebhookOutcome.Duplicate;
        }

        var handled = evt.Type switch
        {
            "invoice.paid" => await HandleInvoicePaidAsync(evt, ct),
            "invoice.payment_failed" => await HandleInvoicePaymentFailedAsync(evt, ct),
            "invoice.voided" => await HandleInvoiceVoidedAsync(evt, ct),
            "customer.subscription.created"
                or "customer.subscription.updated"
                or "customer.subscription.deleted" => await HandleSubscriptionChangedAsync(evt, ct),
            _ => false,
        };

        // The inbox row is saved even for ignored events, so Stripe retries never do repeat work.
        _db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent(evt.Id, evt.Type, _clock.UtcNow));

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (!await IsAlreadyProcessedAsync(evt.Id, ct))
            {
                throw;
            }

            // A concurrent delivery of the same event won the race and committed first.
            _logger.LogInformation("Concurrent duplicate of Stripe event {EventId} detected", evt.Id);
            return WebhookOutcome.Duplicate;
        }

        _logger.LogInformation(
            "Stripe event {EventId} ({Type}) {Outcome}", evt.Id, evt.Type, handled ? "processed" : "ignored");
        return handled ? WebhookOutcome.Processed : WebhookOutcome.Ignored;
    }

    private async Task<bool> HandleInvoicePaidAsync(StripeWebhookEvent evt, CancellationToken ct)
    {
        var invoice = await GetOrCreateInvoiceAsync(evt, ct);
        if (invoice is null)
        {
            return false;
        }

        var data = evt.DataObject;
        var amountPaid = data.GetProperty("amount_paid").GetInt64();
        var currency = data.GetProperty("currency").GetString()!;
        var paidAt = ReadPaidAt(data) ?? evt.Created;

        if (!invoice.MarkPaid(amountPaid, currency, paidAt))
        {
            return false;
        }

        // Zero-amount invoices (e.g. trials) change status but move no money.
        if (amountPaid > 0)
        {
            _db.LedgerEntries.AddRange(LedgerTransactions.InvoicePayment(invoice.StripeInvoiceId, amountPaid, currency, paidAt));
        }

        Audit("Invoice", invoice.Id.ToString(), "paid", $"stripe={invoice.StripeInvoiceId};amount={amountPaid} {currency}");

        if (invoice.HasAmountMismatch)
        {
            // Settled amount is the source of truth for the ledger; the mismatch is flagged for review.
            _logger.LogWarning(
                "Invoice {StripeInvoiceId} paid {AmountPaid} but {AmountDue} was due",
                invoice.StripeInvoiceId, amountPaid, invoice.AmountDueMinor);
            Audit("Invoice", invoice.Id.ToString(), "amount_mismatch", $"due={invoice.AmountDueMinor};paid={amountPaid}");
        }

        return true;
    }

    private async Task<bool> HandleInvoicePaymentFailedAsync(StripeWebhookEvent evt, CancellationToken ct)
    {
        var invoice = await GetOrCreateInvoiceAsync(evt, ct);
        if (invoice is null)
        {
            return false;
        }

        var attempts = evt.DataObject.TryGetProperty("attempt_count", out var a) && a.ValueKind == JsonValueKind.Number
            ? a.GetInt32()
            : invoice.FailedAttempts + 1;

        invoice.RecordFailedAttempt(attempts);
        Audit("Invoice", invoice.Id.ToString(), "payment_failed", $"attempt={attempts}");
        return true;
    }

    private async Task<bool> HandleInvoiceVoidedAsync(StripeWebhookEvent evt, CancellationToken ct)
    {
        var stripeInvoiceId = evt.DataObject.GetProperty("id").GetString()!;
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.StripeInvoiceId == stripeInvoiceId, ct);
        if (invoice is null || invoice.Status == InvoiceStatus.Paid)
        {
            return false;
        }

        invoice.MarkVoid();
        Audit("Invoice", invoice.Id.ToString(), "voided", null);
        return true;
    }

    private async Task<bool> HandleSubscriptionChangedAsync(StripeWebhookEvent evt, CancellationToken ct)
    {
        var data = evt.DataObject;
        var stripeSubscriptionId = data.GetProperty("id").GetString()!;
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);
        if (subscription is null)
        {
            _logger.LogWarning("Stripe subscription {StripeSubscriptionId} is not known locally", stripeSubscriptionId);
            return false;
        }

        var newStatus = StripeStatusMapper.ToSubscriptionStatus(data.GetProperty("status").GetString()!);
        var previous = subscription.Status;
        if (!subscription.ApplyStatus(newStatus, evt.Created))
        {
            return false;
        }

        Audit("Subscription", subscription.Id.ToString(), "status_changed", $"{previous} -> {newStatus}");
        return true;
    }

    private async Task<Invoice?> GetOrCreateInvoiceAsync(StripeWebhookEvent evt, CancellationToken ct)
    {
        var data = evt.DataObject;
        var stripeInvoiceId = data.GetProperty("id").GetString()!;

        var existing = await _db.Invoices.FirstOrDefaultAsync(i => i.StripeInvoiceId == stripeInvoiceId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var stripeCustomerId = data.GetProperty("customer").GetString()!;
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.StripeCustomerId == stripeCustomerId, ct);
        if (customer is null)
        {
            _logger.LogWarning("Invoice {StripeInvoiceId} belongs to unknown customer {StripeCustomerId}", stripeInvoiceId, stripeCustomerId);
            return null;
        }

        Guid? subscriptionId = null;
        var stripeSubscriptionId = ReadSubscriptionId(data);
        if (stripeSubscriptionId is not null)
        {
            subscriptionId = await _db.Subscriptions
                .Where(s => s.StripeSubscriptionId == stripeSubscriptionId)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(ct);
        }

        var invoice = new Invoice(
            stripeInvoiceId,
            customer.Id,
            subscriptionId,
            data.GetProperty("amount_due").GetInt64(),
            data.GetProperty("currency").GetString()!,
            evt.Created);

        _db.Invoices.Add(invoice);
        return invoice;
    }

    /// <summary>Supports both the current API shape (parent.subscription_details) and the legacy top-level field.</summary>
    private static string? ReadSubscriptionId(JsonElement invoice)
    {
        if (invoice.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty("subscription_details", out var details) && details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("subscription", out var sub) && sub.ValueKind == JsonValueKind.String)
        {
            return sub.GetString();
        }

        return invoice.TryGetProperty("subscription", out var legacy) && legacy.ValueKind == JsonValueKind.String
            ? legacy.GetString()
            : null;
    }

    private static DateTimeOffset? ReadPaidAt(JsonElement invoice) =>
        invoice.TryGetProperty("status_transitions", out var st) && st.ValueKind == JsonValueKind.Object
        && st.TryGetProperty("paid_at", out var paidAt) && paidAt.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(paidAt.GetInt64())
            : null;

    private async Task<bool> IsAlreadyProcessedAsync(string eventId, CancellationToken ct)
    {
        _db.DiscardPendingChanges();
        return await _db.ProcessedWebhookEvents.AsNoTracking().AnyAsync(e => e.StripeEventId == eventId, ct);
    }

    private void Audit(string entityType, string entityId, string action, string? detail) =>
        _db.AuditLogs.Add(new AuditLog(entityType, entityId, action, Actor, detail, _clock.UtcNow));
}
