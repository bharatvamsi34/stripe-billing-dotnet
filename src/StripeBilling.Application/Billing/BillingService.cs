using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StripeBilling.Application.Abstractions;
using StripeBilling.Application.Common;
using StripeBilling.Domain.Customers;
using StripeBilling.Domain.Operations;
using StripeBilling.Domain.Plans;
using StripeBilling.Domain.Subscriptions;

namespace StripeBilling.Application.Billing;

public sealed record CreateCustomerCommand(string Email, string Name, bool AttachTestCard);

public sealed record CreatePlanCommand(
    string Code,
    string Name,
    string StripePriceId,
    long AmountMinor,
    string Currency,
    BillingInterval Interval);

public sealed record CreateSubscriptionCommand(Guid CustomerId, string PlanCode);

/// <summary>Write-side use cases. Every call that reaches Stripe carries an idempotency key.</summary>
public sealed class BillingService
{
    private const string ApiActor = "api";

    private readonly IBillingDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly IClock _clock;
    private readonly ILogger<BillingService> _logger;

    public BillingService(IBillingDbContext db, IPaymentGateway gateway, IClock clock, ILogger<BillingService> logger)
    {
        _db = db;
        _gateway = gateway;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CustomerDto> CreateCustomerAsync(
        CreateCustomerCommand command,
        string idempotencyKey,
        CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        if (await _db.Customers.AnyAsync(c => c.Email == email, ct))
        {
            throw new ConflictException($"A customer with email {email} already exists.");
        }

        var stripeCustomerId = await _gateway.CreateCustomerAsync(
            email, command.Name, command.AttachTestCard, $"{idempotencyKey}:customer", ct);

        var customer = new Customer(email, command.Name, stripeCustomerId, _clock.UtcNow);
        _db.Customers.Add(customer);
        Audit("Customer", customer.Id.ToString(), "created", $"stripe={stripeCustomerId}");
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created customer {CustomerId} ({StripeCustomerId})", customer.Id, stripeCustomerId);
        return CustomerDto.From(customer);
    }

    public async Task<PlanDto> CreatePlanAsync(CreatePlanCommand command, CancellationToken ct)
    {
        var code = command.Code.Trim().ToLowerInvariant();
        if (await _db.Plans.AnyAsync(p => p.Code == code, ct))
        {
            throw new ConflictException($"Plan '{code}' already exists.");
        }

        var plan = new Plan(code, command.Name, command.StripePriceId, command.AmountMinor, command.Currency, command.Interval);
        _db.Plans.Add(plan);
        Audit("Plan", plan.Id.ToString(), "created", $"price={plan.StripePriceId}");
        await _db.SaveChangesAsync(ct);
        return PlanDto.From(plan);
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        CreateSubscriptionCommand command,
        string idempotencyKey,
        CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == command.CustomerId, ct)
            ?? throw new NotFoundException($"Customer {command.CustomerId} not found.");

        var planCode = command.PlanCode.Trim().ToLowerInvariant();
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Code == planCode && p.IsActive, ct)
            ?? throw new NotFoundException($"Active plan '{planCode}' not found.");

        var liveStatuses = new[]
        {
            SubscriptionStatus.Incomplete, SubscriptionStatus.Trialing, SubscriptionStatus.Active,
            SubscriptionStatus.PastDue, SubscriptionStatus.Unpaid, SubscriptionStatus.Paused,
        };
        var hasLive = await _db.Subscriptions.AnyAsync(
            s => s.CustomerId == customer.Id && s.PlanId == plan.Id && liveStatuses.Contains(s.Status), ct);
        if (hasLive)
        {
            throw new ConflictException($"Customer already has a live subscription to '{plan.Code}'.");
        }

        var created = await _gateway.CreateSubscriptionAsync(
            customer.StripeCustomerId, plan.StripePriceId, $"{idempotencyKey}:subscription", ct);

        var subscription = new Subscription(
            customer.Id,
            plan.Id,
            created.Id,
            StripeStatusMapper.ToSubscriptionStatus(created.Status),
            _clock.UtcNow);

        _db.Subscriptions.Add(subscription);
        Audit("Subscription", subscription.Id.ToString(), "created", $"plan={plan.Code};status={subscription.Status}");
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created subscription {SubscriptionId} ({StripeSubscriptionId}) with status {Status}",
            subscription.Id, created.Id, subscription.Status);
        return SubscriptionDto.From(subscription, plan.Code);
    }

    public async Task<SubscriptionDto> CancelSubscriptionAsync(Guid subscriptionId, string idempotencyKey, CancellationToken ct)
    {
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw new NotFoundException($"Subscription {subscriptionId} not found.");

        if (subscription.IsTerminal)
        {
            throw new ConflictException($"Subscription is already {subscription.Status}.");
        }

        var result = await _gateway.CancelSubscriptionAsync(
            subscription.StripeSubscriptionId, $"{idempotencyKey}:cancel", ct);

        var changed = subscription.ApplyStatus(StripeStatusMapper.ToSubscriptionStatus(result.Status), _clock.UtcNow);
        if (changed)
        {
            Audit("Subscription", subscription.Id.ToString(), "canceled", "requested via API");
        }

        await _db.SaveChangesAsync(ct);

        var planCode = await _db.Plans.Where(p => p.Id == subscription.PlanId).Select(p => p.Code).FirstAsync(ct);
        return SubscriptionDto.From(subscription, planCode);
    }

    private void Audit(string entityType, string entityId, string action, string? detail) =>
        _db.AuditLogs.Add(new AuditLog(entityType, entityId, action, ApiActor, detail, _clock.UtcNow));
}
