using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StripeBilling.Application.Abstractions;
using StripeBilling.Infrastructure.Persistence;

namespace StripeBilling.IntegrationTests;

/// <summary>
/// Runs the real API against an isolated, file-backed SQLite database (one connection per request,
/// so concurrency behaves like production) and a fake Stripe gateway.
/// </summary>
public sealed class BillingApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-api-key-0123456789abcdef";
    public const string WebhookSecret = "whsec_test_secret_for_integration_tests";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"billing-tests-{Guid.NewGuid():N}.db");

    public FakePaymentGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Billing", $"Data Source={_databasePath};Pooling=False");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Stripe:SecretKey", "sk_test_fake");
        builder.UseSetting("Stripe:WebhookSecret", WebhookSecret);
        builder.UseSetting("Security:ApiKey", ApiKey);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Gateway);
        });
    }

    public async Task<T> WithDbAsync<T>(Func<BillingDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<BillingDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            File.Delete(_databasePath);
        }
    }
}

/// <summary>Records calls and simulates Stripe's idempotency: same key, same result.</summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, object> _byIdempotencyKey = new();
    private int _counter;

    private int _customerCalls;
    private int _subscriptionCalls;

    public int CustomerCalls => Volatile.Read(ref _customerCalls);
    public int SubscriptionCalls => Volatile.Read(ref _subscriptionCalls);

    public Task<string> CreateCustomerAsync(string email, string name, bool attachTestCard, string idempotencyKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _customerCalls);
        var id = (string)_byIdempotencyKey.GetOrAdd(idempotencyKey, _ => $"cus_{Interlocked.Increment(ref _counter)}");
        return Task.FromResult(id);
    }

    public Task<GatewaySubscription> CreateSubscriptionAsync(string stripeCustomerId, string stripePriceId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _subscriptionCalls);
        var result = (GatewaySubscription)_byIdempotencyKey.GetOrAdd(
            idempotencyKey, _ => new GatewaySubscription($"sub_{Interlocked.Increment(ref _counter)}", "incomplete"));
        return Task.FromResult(result);
    }

    public Task<GatewaySubscription> CancelSubscriptionAsync(string stripeSubscriptionId, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(new GatewaySubscription(stripeSubscriptionId, "canceled"));
}
