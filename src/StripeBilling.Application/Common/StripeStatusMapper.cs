using StripeBilling.Domain.Subscriptions;

namespace StripeBilling.Application.Common;

public static class StripeStatusMapper
{
    public static SubscriptionStatus ToSubscriptionStatus(string stripeStatus) => stripeStatus switch
    {
        "incomplete" => SubscriptionStatus.Incomplete,
        "incomplete_expired" => SubscriptionStatus.IncompleteExpired,
        "trialing" => SubscriptionStatus.Trialing,
        "active" => SubscriptionStatus.Active,
        "past_due" => SubscriptionStatus.PastDue,
        "unpaid" => SubscriptionStatus.Unpaid,
        "canceled" => SubscriptionStatus.Canceled,
        "paused" => SubscriptionStatus.Paused,
        _ => throw new ArgumentOutOfRangeException(nameof(stripeStatus), stripeStatus, "Unknown Stripe subscription status."),
    };
}
