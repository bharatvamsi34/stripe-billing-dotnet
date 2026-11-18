using Microsoft.Extensions.DependencyInjection;
using StripeBilling.Application.Billing;
using StripeBilling.Application.Webhooks;

namespace StripeBilling.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<BillingService>();
        services.AddScoped<BillingQueries>();
        services.AddScoped<WebhookProcessor>();
        return services;
    }
}
