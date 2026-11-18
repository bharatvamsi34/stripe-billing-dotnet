namespace StripeBilling.Application.Common;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>The payment provider rejected or failed a request.</summary>
public sealed class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, Exception? inner = null) : base(message, inner) { }
}
