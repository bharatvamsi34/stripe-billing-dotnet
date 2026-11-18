using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stripe;
using StripeBilling.Application.Abstractions;
using StripeBilling.Infrastructure.Persistence;
using StripeBilling.Infrastructure.Stripe;

namespace StripeBilling.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Billing")
            ?? throw new InvalidOperationException("Connection string 'Billing' is not configured.");

        services.AddDbContext<BillingDbContext>(options =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });
        services.AddScoped<IBillingDbContext>(sp => sp.GetRequiredService<BillingDbContext>());

        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IStripeClient>(sp =>
            new StripeClient(sp.GetRequiredService<IOptions<StripeOptions>>().Value.SecretKey));
        services.AddScoped<IPaymentGateway, StripePaymentGateway>();
        services.AddSingleton<StripeWebhookVerifier>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
