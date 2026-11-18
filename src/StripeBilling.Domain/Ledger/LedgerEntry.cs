using StripeBilling.Domain.Common;

namespace StripeBilling.Domain.Ledger;

public enum LedgerAccount
{
    /// <summary>Funds collected by Stripe on our behalf, not yet paid out.</summary>
    StripeBalance = 1,

    /// <summary>Subscription revenue earned.</summary>
    SubscriptionRevenue = 2,
}

public enum EntryDirection
{
    Debit = 1,
    Credit = 2,
}

/// <summary>
/// One line of a double-entry transaction. Entries are append-only: never updated or deleted.
/// Corrections are made by posting a new, reversing transaction.
/// </summary>
public sealed class LedgerEntry
{
    private LedgerEntry() { }

    internal LedgerEntry(
        Guid transactionId,
        LedgerAccount account,
        EntryDirection direction,
        long amountMinor,
        string currency,
        string sourceReference,
        DateTimeOffset postedAt)
    {
        Id = Guid.NewGuid();
        TransactionId = transactionId;
        Account = account;
        Direction = direction;
        AmountMinor = amountMinor;
        Currency = currency;
        SourceReference = sourceReference;
        PostedAt = postedAt;
    }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public LedgerAccount Account { get; private set; }
    public EntryDirection Direction { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    /// <summary>The business event this entry records, e.g. the Stripe invoice id.</summary>
    public string SourceReference { get; private set; } = string.Empty;

    public DateTimeOffset PostedAt { get; private set; }

    /// <summary>Signed amount: debits positive, credits negative. A balanced transaction sums to zero.</summary>
    public long SignedAmountMinor => Direction == EntryDirection.Debit ? AmountMinor : -AmountMinor;
}

public static class LedgerTransactions
{
    /// <summary>
    /// Records a settled invoice payment: debit Stripe balance, credit subscription revenue.
    /// </summary>
    public static IReadOnlyList<LedgerEntry> InvoicePayment(
        string stripeInvoiceId,
        long amountMinor,
        string currency,
        DateTimeOffset postedAt)
    {
        if (amountMinor <= 0)
        {
            throw new DomainException("Payment amount must be greater than zero.");
        }

        var normalized = Money.NormalizeCurrency(currency);
        var transactionId = Guid.NewGuid();
        var entries = new[]
        {
            new LedgerEntry(transactionId, LedgerAccount.StripeBalance, EntryDirection.Debit, amountMinor, normalized, stripeInvoiceId, postedAt),
            new LedgerEntry(transactionId, LedgerAccount.SubscriptionRevenue, EntryDirection.Credit, amountMinor, normalized, stripeInvoiceId, postedAt),
        };

        EnsureBalanced(entries);
        return entries;
    }

    public static void EnsureBalanced(IEnumerable<LedgerEntry> entries)
    {
        foreach (var byCurrency in entries.GroupBy(e => e.Currency))
        {
            if (byCurrency.Sum(e => e.SignedAmountMinor) != 0)
            {
                throw new DomainException($"Ledger transaction is not balanced in {byCurrency.Key}.");
            }
        }
    }
}
