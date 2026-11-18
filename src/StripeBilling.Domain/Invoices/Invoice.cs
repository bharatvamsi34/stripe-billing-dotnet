using StripeBilling.Domain.Common;

namespace StripeBilling.Domain.Invoices;

public enum InvoiceStatus
{
    Open = 1,
    Paid = 2,
    PaymentFailed = 3,
    Void = 4,
}

/// <summary>Local mirror of a Stripe invoice, created and updated from webhooks.</summary>
public sealed class Invoice
{
    private Invoice() { }

    public Invoice(
        string stripeInvoiceId,
        Guid customerId,
        Guid? subscriptionId,
        long amountDueMinor,
        string currency,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(stripeInvoiceId))
        {
            throw new DomainException("Stripe invoice id is required.");
        }

        Id = Guid.NewGuid();
        StripeInvoiceId = stripeInvoiceId;
        CustomerId = customerId;
        SubscriptionId = subscriptionId;
        AmountDueMinor = Money.EnsureNonNegative(amountDueMinor, nameof(AmountDueMinor));
        Currency = Money.NormalizeCurrency(currency);
        Status = InvoiceStatus.Open;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string StripeInvoiceId { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
    public Guid? SubscriptionId { get; private set; }
    public long AmountDueMinor { get; private set; }
    public long AmountPaidMinor { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public InvoiceStatus Status { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>True when Stripe settled a different amount than was due (credits, partial payments).</summary>
    public bool HasAmountMismatch => Status == InvoiceStatus.Paid && AmountPaidMinor != AmountDueMinor;

    /// <summary>Marks the invoice paid. Returns false if it was already paid (duplicate delivery).</summary>
    public bool MarkPaid(long amountPaidMinor, string currency, DateTimeOffset paidAt)
    {
        Money.EnsureNonNegative(amountPaidMinor, nameof(AmountPaidMinor));

        if (Status == InvoiceStatus.Paid)
        {
            return false;
        }

        if (Status == InvoiceStatus.Void)
        {
            throw new DomainException("A void invoice cannot be paid.");
        }

        if (Money.NormalizeCurrency(currency) != Currency)
        {
            throw new DomainException($"Currency mismatch: invoice is {Currency}, payment is {currency}.");
        }

        AmountPaidMinor = amountPaidMinor;
        Status = InvoiceStatus.Paid;
        PaidAt = paidAt;
        return true;
    }

    public void RecordFailedAttempt(int attemptCount)
    {
        if (Status == InvoiceStatus.Paid)
        {
            // A late failure notice must never downgrade a paid invoice.
            return;
        }

        Status = InvoiceStatus.PaymentFailed;
        FailedAttempts = Math.Max(FailedAttempts, attemptCount);
    }

    public void MarkVoid()
    {
        if (Status == InvoiceStatus.Paid)
        {
            throw new DomainException("A paid invoice cannot be voided.");
        }

        Status = InvoiceStatus.Void;
    }
}
