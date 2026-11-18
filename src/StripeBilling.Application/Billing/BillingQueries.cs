using Microsoft.EntityFrameworkCore;
using StripeBilling.Application.Abstractions;

namespace StripeBilling.Application.Billing;

/// <summary>Read-side queries. All reads are no-tracking, sorted and paged in the database.</summary>
public sealed class BillingQueries
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private readonly IBillingDbContext _db;

    public BillingQueries(IBillingDbContext db) => _db = db;

    public async Task<IReadOnlyList<CustomerDto>> ListCustomersAsync(int take, CancellationToken ct) =>
        (await _db.Customers.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Take(Clamp(take))
            .ToListAsync(ct))
        .Select(CustomerDto.From).ToList();

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct) =>
        (await _db.Plans.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct))
        .Select(PlanDto.From).ToList();

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(int take, CancellationToken ct)
    {
        var rows = await _db.Subscriptions.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Take(Clamp(take))
            .Join(_db.Plans.AsNoTracking(), s => s.PlanId, p => p.Id, (s, p) => new { Subscription = s, p.Code })
            .ToListAsync(ct);

        return rows.Select(r => SubscriptionDto.From(r.Subscription, r.Code)).ToList();
    }

    public async Task<IReadOnlyList<InvoiceDto>> ListInvoicesAsync(int take, CancellationToken ct) =>
        (await _db.Invoices.AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Take(Clamp(take))
            .ToListAsync(ct))
        .Select(InvoiceDto.From).ToList();

    public async Task<IReadOnlyList<LedgerEntryDto>> ListLedgerAsync(int take, CancellationToken ct) =>
        (await _db.LedgerEntries.AsNoTracking()
            .OrderByDescending(e => e.PostedAt).ThenBy(e => e.TransactionId).ThenBy(e => e.Account)
            .Take(Clamp(take))
            .ToListAsync(ct))
        .Select(LedgerEntryDto.From).ToList();

    /// <summary>Balances per account and currency, aggregated in the database.</summary>
    public async Task<LedgerSummaryDto> GetLedgerSummaryAsync(CancellationToken ct)
    {
        var rows = await _db.LedgerEntries.AsNoTracking()
            .GroupBy(e => new { e.Account, e.Currency, e.Direction })
            .Select(g => new { g.Key.Account, g.Key.Currency, g.Key.Direction, Total = g.Sum(e => e.AmountMinor) })
            .ToListAsync(ct);

        var balances = rows
            .GroupBy(r => new { r.Account, r.Currency })
            .Select(g => new AccountBalanceDto(
                g.Key.Account,
                g.Key.Currency,
                g.Sum(r => r.Direction == Domain.Ledger.EntryDirection.Debit ? r.Total : -r.Total)))
            .OrderBy(b => b.Currency).ThenBy(b => b.Account)
            .ToList();

        var isBalanced = balances.GroupBy(b => b.Currency).All(g => g.Sum(b => b.BalanceMinor) == 0);
        return new LedgerSummaryDto(balances, isBalanced);
    }

    private static int Clamp(int take) => take < 1 ? DefaultPageSize : Math.Min(take, MaxPageSize);
}
