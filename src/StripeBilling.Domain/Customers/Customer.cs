using StripeBilling.Domain.Common;

namespace StripeBilling.Domain.Customers;

public sealed class Customer
{
    private Customer() { }

    public Customer(string email, string name, string stripeCustomerId, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new DomainException("A valid email is required.");
        }

        if (string.IsNullOrWhiteSpace(stripeCustomerId))
        {
            throw new DomainException("Stripe customer id is required.");
        }

        Id = Guid.NewGuid();
        Email = email.Trim().ToLowerInvariant();
        Name = name.Trim();
        StripeCustomerId = stripeCustomerId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string StripeCustomerId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
