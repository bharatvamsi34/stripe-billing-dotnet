using FluentAssertions;
using StripeBilling.Domain.Common;
using StripeBilling.Domain.Invoices;
using StripeBilling.Domain.Ledger;
using StripeBilling.Domain.Plans;
using StripeBilling.Domain.Subscriptions;

namespace StripeBilling.UnitTests;

public class SubscriptionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Subscription New(SubscriptionStatus status = SubscriptionStatus.Incomplete) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "sub_123", status, T0);

    [Fact]
    public void Newer_status_is_applied()
    {
        var sub = New();

        sub.ApplyStatus(SubscriptionStatus.Active, T0.AddMinutes(1)).Should().BeTrue();

        sub.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void Stale_event_is_ignored()
    {
        var sub = New();
        sub.ApplyStatus(SubscriptionStatus.PastDue, T0.AddMinutes(10));

        var changed = sub.ApplyStatus(SubscriptionStatus.Active, T0.AddMinutes(5));

        changed.Should().BeFalse();
        sub.Status.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public void Canceled_is_terminal()
    {
        var sub = New(SubscriptionStatus.Active);
        sub.ApplyStatus(SubscriptionStatus.Canceled, T0.AddMinutes(1));

        sub.ApplyStatus(SubscriptionStatus.Active, T0.AddMinutes(2)).Should().BeFalse();

        sub.Status.Should().Be(SubscriptionStatus.Canceled);
        sub.CanceledAt.Should().Be(T0.AddMinutes(1));
    }
}

public class InvoiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Invoice New(long due = 1999) => new("in_1", Guid.NewGuid(), null, due, "USD", T0);

    [Fact]
    public void Currency_is_normalized_to_lowercase()
    {
        New().Currency.Should().Be("usd");
    }

    [Fact]
    public void Paying_twice_is_a_no_op()
    {
        var invoice = New();

        invoice.MarkPaid(1999, "usd", T0).Should().BeTrue();
        invoice.MarkPaid(1999, "usd", T0.AddSeconds(5)).Should().BeFalse();

        invoice.PaidAt.Should().Be(T0);
    }

    [Fact]
    public void Currency_mismatch_is_rejected()
    {
        var act = () => New().MarkPaid(1999, "eur", T0);

        act.Should().Throw<DomainException>().WithMessage("*Currency mismatch*");
    }

    [Fact]
    public void Late_failure_never_downgrades_a_paid_invoice()
    {
        var invoice = New();
        invoice.MarkPaid(1999, "usd", T0);

        invoice.RecordFailedAttempt(2);

        invoice.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public void Partial_payment_is_flagged()
    {
        var invoice = New(2000);

        invoice.MarkPaid(1500, "usd", T0);

        invoice.HasAmountMismatch.Should().BeTrue();
    }

    [Fact]
    public void Paid_invoice_cannot_be_voided()
    {
        var invoice = New();
        invoice.MarkPaid(1999, "usd", T0);

        invoice.Invoking(i => i.MarkVoid()).Should().Throw<DomainException>();
    }
}

public class LedgerTests
{
    [Fact]
    public void Invoice_payment_creates_balanced_double_entry()
    {
        var entries = LedgerTransactions.InvoicePayment("in_1", 4900, "USD", DateTimeOffset.UtcNow);

        entries.Should().HaveCount(2);
        entries.Sum(e => e.SignedAmountMinor).Should().Be(0);
        entries.Select(e => e.TransactionId).Distinct().Should().ContainSingle();
        entries.Should().Contain(e => e.Account == LedgerAccount.StripeBalance && e.Direction == EntryDirection.Debit);
        entries.Should().Contain(e => e.Account == LedgerAccount.SubscriptionRevenue && e.Direction == EntryDirection.Credit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_amounts_are_rejected(long amount)
    {
        var act = () => LedgerTransactions.InvoicePayment("in_1", amount, "usd", DateTimeOffset.UtcNow);

        act.Should().Throw<DomainException>();
    }
}

public class PlanTests
{
    [Fact]
    public void Requires_a_stripe_price_id()
    {
        var act = () => new Plan("pro", "Pro", "prod_123", 4900, "usd", BillingInterval.Month);

        act.Should().Throw<DomainException>().WithMessage("*price_*");
    }
}

public class SubscriptionTimestampTests
{
    [Fact]
    public void Same_second_stripe_event_is_not_treated_as_stale()
    {
        var createdLocally = new DateTimeOffset(2026, 1, 1, 12, 0, 0, 750, TimeSpan.Zero);
        var sub = new Subscription(Guid.NewGuid(), Guid.NewGuid(), "sub_1", SubscriptionStatus.Incomplete, createdLocally);
        var stripeEventTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        sub.ApplyStatus(SubscriptionStatus.Active, stripeEventTime).Should().BeTrue();
    }
}
