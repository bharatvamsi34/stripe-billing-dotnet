using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StripeBilling.Domain.Customers;
using StripeBilling.Domain.Invoices;
using StripeBilling.Domain.Ledger;
using StripeBilling.Domain.Operations;
using StripeBilling.Domain.Plans;
using StripeBilling.Domain.Subscriptions;

namespace StripeBilling.Infrastructure.Persistence;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.Email).IsUnique();
        b.HasIndex(x => x.StripeCustomerId).IsUnique();
        b.Property(x => x.StripeCustomerId).HasMaxLength(64);
    }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Code).HasMaxLength(64);
        b.Property(x => x.StripePriceId).HasMaxLength(64);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.StripeSubscriptionId).IsUnique();
        b.HasIndex(x => new { x.CustomerId, x.Status });
        b.Property(x => x.StripeSubscriptionId).HasMaxLength(64);
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.IsTerminal);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.StripeInvoiceId).IsUnique();
        b.HasIndex(x => x.CreatedAt);
        b.Property(x => x.StripeInvoiceId).HasMaxLength(64);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Subscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.HasAmountMismatch);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.TransactionId);
        b.HasIndex(x => x.PostedAt);

        // Defence in depth against double posting: one entry per source event and account.
        b.HasIndex(x => new { x.SourceReference, x.Account }).IsUnique();

        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.SourceReference).HasMaxLength(64);
        b.Ignore(x => x.SignedAmountMinor);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => x.OccurredAt);
        b.Property(x => x.Detail).HasMaxLength(1024);
    }
}

internal sealed class ProcessedWebhookEventConfiguration : IEntityTypeConfiguration<ProcessedWebhookEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookEvent> b)
    {
        b.HasKey(x => x.StripeEventId);
        b.Property(x => x.StripeEventId).HasMaxLength(64);
        b.Property(x => x.Type).HasMaxLength(128);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(128);
        b.Property(x => x.RequestFingerprint).HasMaxLength(64);
        b.Property(x => x.ResponseBody).HasMaxLength(int.MaxValue);
        b.HasIndex(x => x.CreatedAt);
    }
}
