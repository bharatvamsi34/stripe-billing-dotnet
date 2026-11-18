using StripeBilling.Domain.Customers;
using StripeBilling.Domain.Invoices;
using StripeBilling.Domain.Ledger;
using StripeBilling.Domain.Plans;
using StripeBilling.Domain.Subscriptions;

namespace StripeBilling.Application.Billing;

public sealed record CustomerDto(Guid Id, string Email, string Name, string StripeCustomerId, DateTimeOffset CreatedAt)
{
    public static CustomerDto From(Customer c) => new(c.Id, c.Email, c.Name, c.StripeCustomerId, c.CreatedAt);
}

public sealed record PlanDto(
    Guid Id,
    string Code,
    string Name,
    string StripePriceId,
    long AmountMinor,
    string Currency,
    BillingInterval Interval,
    bool IsActive)
{
    public static PlanDto From(Plan p) =>
        new(p.Id, p.Code, p.Name, p.StripePriceId, p.AmountMinor, p.Currency, p.Interval, p.IsActive);
}

public sealed record SubscriptionDto(
    Guid Id,
    Guid CustomerId,
    string PlanCode,
    string StripeSubscriptionId,
    SubscriptionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CanceledAt)
{
    public static SubscriptionDto From(Subscription s, string planCode) =>
        new(s.Id, s.CustomerId, planCode, s.StripeSubscriptionId, s.Status, s.CreatedAt, s.CanceledAt);
}

public sealed record InvoiceDto(
    Guid Id,
    string StripeInvoiceId,
    Guid CustomerId,
    Guid? SubscriptionId,
    long AmountDueMinor,
    long AmountPaidMinor,
    string Currency,
    InvoiceStatus Status,
    int FailedAttempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt)
{
    public static InvoiceDto From(Invoice i) => new(
        i.Id, i.StripeInvoiceId, i.CustomerId, i.SubscriptionId, i.AmountDueMinor, i.AmountPaidMinor,
        i.Currency, i.Status, i.FailedAttempts, i.CreatedAt, i.PaidAt);
}

public sealed record LedgerEntryDto(
    Guid Id,
    Guid TransactionId,
    LedgerAccount Account,
    EntryDirection Direction,
    long AmountMinor,
    string Currency,
    string SourceReference,
    DateTimeOffset PostedAt)
{
    public static LedgerEntryDto From(LedgerEntry e) =>
        new(e.Id, e.TransactionId, e.Account, e.Direction, e.AmountMinor, e.Currency, e.SourceReference, e.PostedAt);
}

public sealed record AccountBalanceDto(LedgerAccount Account, string Currency, long BalanceMinor);

public sealed record LedgerSummaryDto(IReadOnlyList<AccountBalanceDto> Balances, bool IsBalanced);
