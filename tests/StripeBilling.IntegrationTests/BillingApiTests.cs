using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StripeBilling.Application.Billing;
using StripeBilling.Domain.Invoices;
using StripeBilling.Domain.Subscriptions;

namespace StripeBilling.IntegrationTests;

public sealed class BillingApiTests : IClassFixture<BillingApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly BillingApiFactory _factory;
    private readonly HttpClient _client;

    public BillingApiTests(BillingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", BillingApiFactory.ApiKey);
    }

    // ---------- Security ----------

    [Fact]
    public async Task Api_rejects_requests_without_api_key()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/api/customers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await _factory.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webhook_with_invalid_signature_is_rejected()
    {
        var payload = StripeEvents.SubscriptionUpdated("evt_bad_sig", "sub_x", "active", DateTimeOffset.UtcNow);

        var response = await PostWebhookAsync(payload, StripeEvents.Sign(payload, "whsec_wrong_secret"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhook_with_expired_signature_is_rejected_to_block_replays()
    {
        var payload = StripeEvents.SubscriptionUpdated("evt_old_sig", "sub_x", "active", DateTimeOffset.UtcNow);
        var tenMinutesAgo = DateTimeOffset.UtcNow.AddMinutes(-10);

        var response = await PostWebhookAsync(payload, StripeEvents.Sign(payload, BillingApiFactory.WebhookSecret, tenMinutesAgo));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- Idempotency ----------

    [Fact]
    public async Task Retried_create_customer_with_same_key_replays_first_response()
    {
        var key = NewKey();
        var body = new { email = $"{Guid.NewGuid():N}@example.com", name = "Retry Test" };
        var callsBefore = _factory.Gateway.CustomerCalls;

        var first = await PostAsync("/api/customers", body, key);
        var second = await PostAsync("/api/customers", body, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotent-Replayed").Should().ContainSingle("true");
        (await second.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        _factory.Gateway.CustomerCalls.Should().Be(callsBefore + 1, "the retry must not reach Stripe again");
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_body_is_rejected()
    {
        var key = NewKey();
        await PostAsync("/api/customers", new { email = $"{Guid.NewGuid():N}@example.com", name = "A" }, key);

        var response = await PostAsync("/api/customers", new { email = $"{Guid.NewGuid():N}@example.com", name = "B" }, key);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Writes_without_idempotency_key_are_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/customers", new { email = "nokey@example.com", name = "No Key" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- Full lifecycle ----------

    [Fact]
    public async Task Subscription_lifecycle_posts_balanced_ledger_exactly_once()
    {
        var (customer, subscription) = await CreateSubscribedCustomerAsync();
        var invoiceId = $"in_{Guid.NewGuid():N}";

        // Stripe activates the subscription, then reports the paid invoice.
        await SendAsync(StripeEvents.SubscriptionUpdated(NewEventId(), subscription.StripeSubscriptionId, "active", DateTimeOffset.UtcNow));
        var paidEvent = StripeEvents.InvoicePaid(NewEventId(), invoiceId, customer.StripeCustomerId, subscription.StripeSubscriptionId, 4900);
        var first = await SendAsync(paidEvent);

        // Stripe redelivers the same event (it guarantees at-least-once delivery).
        var redelivery = await SendAsync(paidEvent);

        (await first.Content.ReadAsStringAsync()).Should().Contain("processed");
        (await redelivery.Content.ReadAsStringAsync()).Should().Contain("duplicate");

        var invoice = await _factory.WithDbAsync(db => db.Invoices.SingleAsync(i => i.StripeInvoiceId == invoiceId));
        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.SubscriptionId.Should().Be(subscription.Id);

        var entries = await _factory.WithDbAsync(db => db.LedgerEntries.Where(e => e.SourceReference == invoiceId).ToListAsync());
        entries.Should().HaveCount(2, "a redelivered event must not post twice");
        entries.Sum(e => e.SignedAmountMinor).Should().Be(0);

        var summary = await _client.GetFromJsonAsync<LedgerSummaryDto>("/api/ledger/summary", Json);
        summary!.IsBalanced.Should().BeTrue();

        var status = await _factory.WithDbAsync(db => db.Subscriptions.Where(s => s.Id == subscription.Id).Select(s => s.Status).SingleAsync());
        status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Concurrent_redeliveries_post_the_ledger_once()
    {
        var (customer, subscription) = await CreateSubscribedCustomerAsync();
        var invoiceId = $"in_{Guid.NewGuid():N}";
        var payload = StripeEvents.InvoicePaid(NewEventId(), invoiceId, customer.StripeCustomerId, subscription.StripeSubscriptionId, 4900);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            PostWebhookAsync(payload, StripeEvents.Sign(payload, BillingApiFactory.WebhookSecret))));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await _factory.WithDbAsync(db => db.LedgerEntries.CountAsync(e => e.SourceReference == invoiceId))).Should().Be(2);
    }

    [Fact]
    public async Task Out_of_order_subscription_events_keep_the_newest_status()
    {
        var (_, subscription) = await CreateSubscribedCustomerAsync();
        var now = DateTimeOffset.UtcNow;

        await SendAsync(StripeEvents.SubscriptionUpdated(NewEventId(), subscription.StripeSubscriptionId, "past_due", now.AddSeconds(30)));
        await SendAsync(StripeEvents.SubscriptionUpdated(NewEventId(), subscription.StripeSubscriptionId, "active", now.AddSeconds(10)));

        var status = await _factory.WithDbAsync(db => db.Subscriptions.Where(s => s.Id == subscription.Id).Select(s => s.Status).SingleAsync());
        status.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task Failed_payment_then_success_ends_paid_with_one_ledger_posting()
    {
        var (customer, subscription) = await CreateSubscribedCustomerAsync();
        var invoiceId = $"in_{Guid.NewGuid():N}";

        await SendAsync(StripeEvents.InvoicePaymentFailed(NewEventId(), invoiceId, customer.StripeCustomerId, 4900, attempt: 1));
        var afterFailure = await _factory.WithDbAsync(db => db.Invoices.SingleAsync(i => i.StripeInvoiceId == invoiceId));
        afterFailure.Status.Should().Be(InvoiceStatus.PaymentFailed);

        await SendAsync(StripeEvents.InvoicePaid(NewEventId(), invoiceId, customer.StripeCustomerId, subscription.StripeSubscriptionId, 4900));

        var invoice = await _factory.WithDbAsync(db => db.Invoices.SingleAsync(i => i.StripeInvoiceId == invoiceId));
        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.FailedAttempts.Should().Be(1);
        (await _factory.WithDbAsync(db => db.LedgerEntries.CountAsync(e => e.SourceReference == invoiceId))).Should().Be(2);
    }

    [Fact]
    public async Task Unknown_event_types_are_acknowledged_and_ignored()
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = NewEventId(),
            type = "charge.dispute.created",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new { @object = new { id = "dp_1" } },
        });

        var response = await SendAsync(payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ignored");
    }

    [Fact]
    public async Task Cannot_hold_two_live_subscriptions_to_the_same_plan()
    {
        var (customer, _) = await CreateSubscribedCustomerAsync();

        var response = await PostAsync("/api/subscriptions", new { customerId = customer.Id, planCode = "pro" }, NewKey());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancel_sets_terminal_status_and_is_audited()
    {
        var (_, subscription) = await CreateSubscribedCustomerAsync();

        var response = await PostAsync($"/api/subscriptions/{subscription.Id}/cancel", new { }, NewKey());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var canceled = await response.Content.ReadFromJsonAsync<SubscriptionDto>(Json);
        canceled!.Status.Should().Be(SubscriptionStatus.Canceled);

        var audited = await _factory.WithDbAsync(db =>
            db.AuditLogs.AnyAsync(a => a.EntityId == subscription.Id.ToString() && a.Action == "canceled"));
        audited.Should().BeTrue();
    }

    // ---------- Helpers ----------

    private async Task<(CustomerDto Customer, SubscriptionDto Subscription)> CreateSubscribedCustomerAsync()
    {
        await EnsureProPlanAsync();

        var customerResponse = await PostAsync(
            "/api/customers", new { email = $"{Guid.NewGuid():N}@example.com", name = "Test Customer" }, NewKey());
        customerResponse.EnsureSuccessStatusCode();
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDto>(Json))!;

        var subscriptionResponse = await PostAsync("/api/subscriptions", new { customerId = customer.Id, planCode = "pro" }, NewKey());
        subscriptionResponse.EnsureSuccessStatusCode();
        var subscription = (await subscriptionResponse.Content.ReadFromJsonAsync<SubscriptionDto>(Json))!;

        return (customer, subscription);
    }

    private async Task EnsureProPlanAsync()
    {
        var exists = await _factory.WithDbAsync(db => db.Plans.AnyAsync(p => p.Code == "pro"));
        if (!exists)
        {
            var response = await _client.PostAsJsonAsync("/api/plans", new
            {
                code = "pro",
                name = "Pro",
                stripePriceId = "price_test123",
                amountMinor = 4900,
                currency = "usd",
                interval = "Month",
            }, Json);
            response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
        }
    }

    private Task<HttpResponseMessage> PostAsync(string url, object body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(string payload)
    {
        var response = await PostWebhookAsync(payload, StripeEvents.Sign(payload, BillingApiFactory.WebhookSecret));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response;
    }

    private Task<HttpResponseMessage> PostWebhookAsync(string payload, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe") { Content = StripeEvents.AsContent(payload) };
        request.Headers.Add("Stripe-Signature", signature);
        return _client.SendAsync(request);
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static string NewEventId() => $"evt_{Guid.NewGuid():N}";
}
