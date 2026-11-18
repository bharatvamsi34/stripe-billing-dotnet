using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StripeBilling.Application.Common;
using StripeBilling.Domain.Common;
using StripeBilling.Infrastructure.Stripe;

namespace StripeBilling.Api.Infrastructure;

/// <summary>Maps known exceptions to RFC 7807 problem responses. Unknown errors never leak details.</summary>
public sealed class ExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ExceptionHandler> _logger;

    public ExceptionHandler(ILogger<ExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, exception.Message),
            ConflictException => (StatusCodes.Status409Conflict, exception.Message),
            DomainException => (StatusCodes.Status422UnprocessableEntity, exception.Message),
            InvalidWebhookSignatureException => (StatusCodes.Status400BadRequest, exception.Message),
            PaymentGatewayException => (StatusCodes.Status502BadGateway, exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        };

        if (status >= 500)
        {
            _logger.LogError(exception, "Request {Method} {Path} failed", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier },
            },
            cancellationToken);
        return true;
    }
}
