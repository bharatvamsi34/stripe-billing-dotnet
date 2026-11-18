using StripeBilling.Domain.Common;

namespace StripeBilling.Domain.Subscriptions;

public enum SubscriptionStatus
{
    Incomplete = 1,
    IncompleteExpired = 2,
    Trialing = 3,
    Active = 4,
    PastDue = 5,
    Unpaid = 6,
    Canceled = 7,
    Paused = 8,
}

public sealed class Subscription
{
    private Subscription() { }

    public Subscription(
        Guid customerId,
        Guid planId,
        string stripeSubscriptionId,
        SubscriptionStatus status,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(stripeSubscriptionId))
        {
            throw new DomainException("Stripe subscription id is required.");
        }

        Id = Guid.NewGuid();
        CustomerId = customerId;
        PlanId = planId;
        StripeSubscriptionId = stripeSubscriptionId;
        Status = status;
        CreatedAt = createdAt;
        StatusChangedAt = ToStripeResolution(createdAt);
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid PlanId { get; private set; }
    public string StripeSubscriptionId { get; private set; } = string.Empty;
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Timestamp of the Stripe event (or API response) that last set <see cref="Status"/>.
    /// Used to ignore webhooks that arrive out of order.
    /// </summary>
    public DateTimeOffset StatusChangedAt { get; private set; }

    public DateTimeOffset? CanceledAt { get; private set; }

    public bool IsTerminal => Status is SubscriptionStatus.Canceled or SubscriptionStatus.IncompleteExpired;

    /// <summary>
    /// Applies a status reported by Stripe. Returns false when the update is stale
    /// (older than the last applied change) or the subscription is already in a terminal state.
    /// </summary>
    public bool ApplyStatus(SubscriptionStatus newStatus, DateTimeOffset reportedAt)
    {
        reportedAt = ToStripeResolution(reportedAt);
        if (reportedAt < StatusChangedAt)
        {
            return false;
        }

        if (IsTerminal && newStatus != Status)
        {
            return false;
        }

        if (newStatus == Status)
        {
            StatusChangedAt = reportedAt;
            return false;
        }

        Status = newStatus;
        StatusChangedAt = reportedAt;
        if (newStatus == SubscriptionStatus.Canceled)
        {
            CanceledAt = reportedAt;
        }

        return true;
    }

    /// <summary>
    /// Stripe event timestamps have one-second resolution. Comparing at that resolution prevents a
    /// sub-second local timestamp from making a same-second Stripe event look stale.
    /// </summary>
    private static DateTimeOffset ToStripeResolution(DateTimeOffset value) =>
        DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());
}
