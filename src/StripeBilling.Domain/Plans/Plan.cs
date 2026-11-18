using StripeBilling.Domain.Common;

namespace StripeBilling.Domain.Plans;

public enum BillingInterval
{
    Month = 1,
    Year = 2,
}

/// <summary>A sellable plan, mapped to a recurring Price in Stripe.</summary>
public sealed class Plan
{
    private Plan() { }

    public Plan(string code, string name, string stripePriceId, long amountMinor, string currency, BillingInterval interval)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("Plan code is required.");
        }

        if (string.IsNullOrWhiteSpace(stripePriceId) || !stripePriceId.StartsWith("price_", StringComparison.Ordinal))
        {
            throw new DomainException("A Stripe price id (price_...) is required.");
        }

        if (amountMinor <= 0)
        {
            throw new DomainException("Plan amount must be greater than zero.");
        }

        Id = Guid.NewGuid();
        Code = code.Trim().ToLowerInvariant();
        Name = name.Trim();
        StripePriceId = stripePriceId;
        AmountMinor = amountMinor;
        Currency = Money.NormalizeCurrency(currency);
        Interval = interval;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string StripePriceId { get; private set; } = string.Empty;
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public BillingInterval Interval { get; private set; }
    public bool IsActive { get; private set; }

    public void Deactivate() => IsActive = false;
}
