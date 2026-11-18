using Microsoft.EntityFrameworkCore;
using StripeBilling.Domain.Customers;
using StripeBilling.Domain.Invoices;
using StripeBilling.Domain.Ledger;
using StripeBilling.Domain.Operations;
using StripeBilling.Domain.Plans;
using StripeBilling.Domain.Subscriptions;

namespace StripeBilling.Application.Abstractions;

public interface IBillingDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Plan> Plans { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops all tracked, unsaved changes (used after a failed save).</summary>
    void DiscardPendingChanges();
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Everything the application needs from the payment provider. Stripe is one implementation.</summary>
public interface IPaymentGateway
{
    Task<string> CreateCustomerAsync(
        string email,
        string name,
        bool attachTestCard,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<GatewaySubscription> CreateSubscriptionAsync(
        string stripeCustomerId,
        string stripePriceId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<GatewaySubscription> CancelSubscriptionAsync(
        string stripeSubscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed record GatewaySubscription(string Id, string Status);
