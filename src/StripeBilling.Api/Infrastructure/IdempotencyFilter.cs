using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using StripeBilling.Application.Abstractions;
using StripeBilling.Domain.Operations;

namespace StripeBilling.Api.Infrastructure;

/// <summary>
/// Makes a POST endpoint safe to retry. The client sends an Idempotency-Key header:
/// the first successful response is stored, and any retry with the same key and body replays it.
/// Reusing a key with a different body is rejected with 422.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    public const string HeaderName = "Idempotency-Key";

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        ActivatorUtilities.CreateInstance<IdempotencyFilter>(serviceProvider);
}

public sealed class IdempotencyFilter : IAsyncActionFilter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IBillingDbContext _db;
    private readonly IClock _clock;

    public IdempotencyFilter(IBillingDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var ct = context.HttpContext.RequestAborted;
        var key = context.HttpContext.Request.Headers[IdempotentAttribute.HeaderName].ToString();
        if (key.Length is < 8 or > 128)
        {
            context.Result = Problem(StatusCodes.Status400BadRequest, "An Idempotency-Key header of 8 to 128 characters is required.");
            return;
        }

        var fingerprint = Fingerprint(context);
        var existing = await _db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (existing is not null)
        {
            if (existing.RequestFingerprint != fingerprint)
            {
                context.Result = Problem(StatusCodes.Status422UnprocessableEntity, "This Idempotency-Key was already used with a different request.");
                return;
            }

            context.HttpContext.Response.Headers["Idempotent-Replayed"] = "true";
            context.Result = new ContentResult
            {
                StatusCode = existing.StatusCode,
                Content = existing.ResponseBody,
                ContentType = "application/json",
            };
            return;
        }

        var executed = await next();
        if (executed.Exception is null && executed.Result is ObjectResult { StatusCode: >= 200 and < 300 } result)
        {
            _db.IdempotencyRecords.Add(new IdempotencyRecord(
                key, fingerprint, result.StatusCode ?? 200, JsonSerializer.Serialize(result.Value, Json), _clock.UtcNow));
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent request with the same key stored its response first; ours is equivalent.
                _db.DiscardPendingChanges();
            }
        }
    }

    private static string Fingerprint(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;
        var arguments = context.ActionArguments
            .Where(a => a.Value is not CancellationToken && a.Key != "idempotencyKey")
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(a => a.Key, a => a.Value);
        var payload = $"{request.Method} {request.Path}\n{JsonSerializer.Serialize(arguments, Json)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static ObjectResult Problem(int status, string title) =>
        new(new ProblemDetails { Status = status, Title = title }) { StatusCode = status };
}
