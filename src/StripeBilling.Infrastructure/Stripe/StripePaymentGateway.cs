using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using StripeBilling.Application.Abstractions;
using StripeBilling.Application.Common;

namespace StripeBilling.Infrastructure.Stripe;

/// <summary>
/// Stripe implementation of <see cref="IPaymentGateway"/>. Every write passes an idempotency key,
/// so a network retry can never create a second customer or subscription.
/// </summary>
public sealed class StripePaymentGateway : IPaymentGateway
{
    private const string TestCardPaymentMethod = "pm_card_visa";

    private readonly IStripeClient _client;
    private readonly StripeOptions _options;
    private readonly ILogger<StripePaymentGateway> _logger;

    public StripePaymentGateway(IStripeClient client, IOptions<StripeOptions> options, ILogger<StripePaymentGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> CreateCustomerAsync(
        string email, string name, bool attachTestCard, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (attachTestCard && !_options.IsTestMode)
        {
            throw new PaymentGatewayException("Test cards can only be attached with a test-mode (sk_test_) key.");
        }

        var options = new CustomerCreateOptions
        {
            Email = email,
            Name = name,
            PaymentMethod = attachTestCard ? TestCardPaymentMethod : null,
            InvoiceSettings = attachTestCard
                ? new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = TestCardPaymentMethod }
                : null,
        };

        var customer = await ExecuteAsync(
            () => new CustomerService(_client).CreateAsync(options, Request(idempotencyKey), cancellationToken),
            "create customer");
        return customer.Id;
    }

    public async Task<GatewaySubscription> CreateSubscriptionAsync(
        string stripeCustomerId, string stripePriceId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var options = new SubscriptionCreateOptions
        {
            Customer = stripeCustomerId,
            Items = [new SubscriptionItemOptions { Price = stripePriceId }],

            // Without a saved card the subscription stays "incomplete" until the customer confirms payment,
            // instead of failing outright. State then advances through webhooks.
            PaymentBehavior = "default_incomplete",
            PaymentSettings = new SubscriptionPaymentSettingsOptions { SaveDefaultPaymentMethod = "on_subscription" },
        };

        var subscription = await ExecuteAsync(
            () => new SubscriptionService(_client).CreateAsync(options, Request(idempotencyKey), cancellationToken),
            "create subscription");
        return new GatewaySubscription(subscription.Id, subscription.Status);
    }

    public async Task<GatewaySubscription> CancelSubscriptionAsync(
        string stripeSubscriptionId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var subscription = await ExecuteAsync(
            () => new SubscriptionService(_client).CancelAsync(
                stripeSubscriptionId, new SubscriptionCancelOptions(), Request(idempotencyKey), cancellationToken),
            "cancel subscription");
        return new GatewaySubscription(subscription.Id, subscription.Status);
    }

    private static RequestOptions Request(string idempotencyKey) => new() { IdempotencyKey = idempotencyKey };

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> call, string operation)
    {
        try
        {
            return await call();
        }
        catch (StripeException ex)
        {
            // Log Stripe's request id for support tickets; never log card data or full payloads.
            _logger.LogError(
                ex,
                "Stripe {Operation} failed: {ErrorType} {ErrorCode} (request {RequestId})",
                operation, ex.StripeError?.Type, ex.StripeError?.Code, ex.StripeResponse?.RequestId);
            throw new PaymentGatewayException($"Payment provider could not {operation}: {ex.StripeError?.Message ?? ex.Message}", ex);
        }
    }
}
