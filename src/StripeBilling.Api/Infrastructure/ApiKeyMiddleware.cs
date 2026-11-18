using System.Security.Cryptography;
using System.Text;

namespace StripeBilling.Api.Infrastructure;

/// <summary>
/// Protects /api/* with a shared API key (X-Api-Key), compared in constant time.
/// Webhooks are authenticated by Stripe's signature instead; /health is public.
/// </summary>
public sealed class ApiKeyMiddleware
{
    public const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly byte[] _expectedKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var key = configuration["Security:ApiKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 24)
        {
            throw new InvalidOperationException("Security:ApiKey must be set to a random value of at least 24 characters.");
        }

        _expectedKey = Encoding.UTF8.GetBytes(key);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsOptions(context.Request.Method))
        {
            var provided = context.Request.Headers[HeaderName].ToString();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), _expectedKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { title = "Missing or invalid API key." });
                return;
            }
        }

        await _next(context);
    }
}
