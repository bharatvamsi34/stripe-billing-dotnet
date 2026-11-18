using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using StripeBilling.Api.Infrastructure;
using StripeBilling.Application.Billing;
using StripeBilling.Domain.Plans;

namespace StripeBilling.Api.Controllers;

public sealed record CreateCustomerRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(200)] string Name,
    bool AttachTestCard = false);

public sealed record CreatePlanRequest(
    [Required, RegularExpression("^[a-z0-9-]{2,64}$")] string Code,
    [Required, MaxLength(200)] string Name,
    [Required, RegularExpression("^price_[A-Za-z0-9]+$")] string StripePriceId,
    [Range(1, long.MaxValue)] long AmountMinor,
    [Required, StringLength(3, MinimumLength = 3)] string Currency,
    BillingInterval Interval);

public sealed record CreateSubscriptionRequest(
    [Required] Guid CustomerId,
    [Required, MaxLength(64)] string PlanCode);

[ApiController]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly BillingService _billing;
    private readonly BillingQueries _queries;

    public CustomersController(BillingService billing, BillingQueries queries)
    {
        _billing = billing;
        _queries = queries;
    }

    [HttpGet]
    public Task<IReadOnlyList<CustomerDto>> List([FromQuery] int take, CancellationToken ct) =>
        _queries.ListCustomersAsync(take, ct);

    [HttpPost]
    [Idempotent]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerDto>> Create(
        CreateCustomerRequest request,
        [FromHeader(Name = IdempotentAttribute.HeaderName)] string idempotencyKey,
        CancellationToken ct)
    {
        var customer = await _billing.CreateCustomerAsync(
            new CreateCustomerCommand(request.Email, request.Name, request.AttachTestCard), idempotencyKey, ct);
        return StatusCode(StatusCodes.Status201Created, customer);
    }
}

[ApiController]
[Route("api/plans")]
public sealed class PlansController : ControllerBase
{
    private readonly BillingService _billing;
    private readonly BillingQueries _queries;

    public PlansController(BillingService billing, BillingQueries queries)
    {
        _billing = billing;
        _queries = queries;
    }

    [HttpGet]
    public Task<IReadOnlyList<PlanDto>> List(CancellationToken ct) => _queries.ListPlansAsync(ct);

    [HttpPost]
    [ProducesResponseType<PlanDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlanDto>> Create(CreatePlanRequest request, CancellationToken ct)
    {
        var plan = await _billing.CreatePlanAsync(
            new CreatePlanCommand(request.Code, request.Name, request.StripePriceId, request.AmountMinor, request.Currency, request.Interval),
            ct);
        return StatusCode(StatusCodes.Status201Created, plan);
    }
}

[ApiController]
[Route("api/subscriptions")]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly BillingService _billing;
    private readonly BillingQueries _queries;

    public SubscriptionsController(BillingService billing, BillingQueries queries)
    {
        _billing = billing;
        _queries = queries;
    }

    [HttpGet]
    public Task<IReadOnlyList<SubscriptionDto>> List([FromQuery] int take, CancellationToken ct) =>
        _queries.ListSubscriptionsAsync(take, ct);

    [HttpPost]
    [Idempotent]
    [ProducesResponseType<SubscriptionDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SubscriptionDto>> Create(
        CreateSubscriptionRequest request,
        [FromHeader(Name = IdempotentAttribute.HeaderName)] string idempotencyKey,
        CancellationToken ct)
    {
        var subscription = await _billing.CreateSubscriptionAsync(
            new CreateSubscriptionCommand(request.CustomerId, request.PlanCode), idempotencyKey, ct);
        return StatusCode(StatusCodes.Status201Created, subscription);
    }

    [HttpPost("{id:guid}/cancel")]
    [Idempotent]
    public async Task<ActionResult<SubscriptionDto>> Cancel(
        Guid id,
        [FromHeader(Name = IdempotentAttribute.HeaderName)] string idempotencyKey,
        CancellationToken ct) =>
        Ok(await _billing.CancelSubscriptionAsync(id, idempotencyKey, ct));
}

[ApiController]
[Route("api")]
public sealed class ReportingController : ControllerBase
{
    private readonly BillingQueries _queries;

    public ReportingController(BillingQueries queries) => _queries = queries;

    [HttpGet("invoices")]
    public Task<IReadOnlyList<InvoiceDto>> Invoices([FromQuery] int take, CancellationToken ct) =>
        _queries.ListInvoicesAsync(take, ct);

    [HttpGet("ledger")]
    public Task<IReadOnlyList<LedgerEntryDto>> Ledger([FromQuery] int take, CancellationToken ct) =>
        _queries.ListLedgerAsync(take, ct);

    [HttpGet("ledger/summary")]
    public Task<LedgerSummaryDto> LedgerSummary(CancellationToken ct) => _queries.GetLedgerSummaryAsync(ct);
}
