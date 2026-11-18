namespace StripeBilling.Domain.Common;

/// <summary>Raised when an operation would break a business rule.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
