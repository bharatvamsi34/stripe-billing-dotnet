using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StripeBilling.Application.Abstractions;
using StripeBilling.Domain.Customers;
using StripeBilling.Domain.Invoices;
using StripeBilling.Domain.Ledger;
using StripeBilling.Domain.Operations;
using StripeBilling.Domain.Plans;
using StripeBilling.Domain.Subscriptions;

namespace StripeBilling.Infrastructure.Persistence;

public sealed class BillingDbContext : DbContext, IBillingDbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public void DiscardPendingChanges() => ChangeTracker.Clear();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // The ledger is append-only: refuse any update or delete of an existing entry.
        var tampered = ChangeTracker.Entries<LedgerEntry>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (tampered)
        {
            throw new InvalidOperationException("Ledger entries are append-only and cannot be modified or deleted.");
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no native DateTimeOffset type; storing ticks keeps ORDER BY and comparisons correct.
        if (Database.IsSqlite())
        {
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }

        configurationBuilder.Properties<string>().HaveMaxLength(256);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }
}
