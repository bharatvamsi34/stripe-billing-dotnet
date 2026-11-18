namespace StripeBilling.Domain.Common;

/// <summary>
/// Money is always stored in the currency's minor unit (cents, paise) as a whole number.
/// Floating-point types are never used for amounts.
/// </summary>
public static class Money
{
    public static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new DomainException("Currency must be a 3-letter ISO 4217 code.");
        }

        return currency.Trim().ToLowerInvariant();
    }

    public static long EnsureNonNegative(long amountMinor, string field)
    {
        if (amountMinor < 0)
        {
            throw new DomainException($"{field} cannot be negative.");
        }

        return amountMinor;
    }
}
