using System.Text;
using Microsoft.AspNetCore.Mvc;
using StripeBilling.Application.Webhooks;
using StripeBilling.Infrastructure.Stripe;

namespace StripeBilling.Api.Controllers;

/// <summary>
/// Receives Stripe webhooks. The raw body is verified against the Stripe-Signature header before
/// anything is parsed. Returns 2xx for processed, duplicate and ignored events so Stripe stops retrying;
/// a 5xx lets Stripe retry later with backoff.
/// </summary>
[ApiController]
[Route("webhooks/stripe")]
public sealed class StripeWebhooksController : ControllerBase
{
    private const int MaxPayloadBytes = 512 * 1024;

    private readonly StripeWebhookVerifier _verifier;
    private readonly WebhookProcessor _processor;

    public StripeWebhooksController(StripeWebhookVerifier verifier, WebhookProcessor processor)
    {
        _verifier = verifier;
        _processor = processor;
    }

    [HttpPost]
    [RequestSizeLimit(MaxPayloadBytes)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(ct);

        var evt = _verifier.Verify(payload, Request.Headers["Stripe-Signature"]);
        var outcome = await _processor.ProcessAsync(evt, ct);
        return Ok(new { received = true, outcome = outcome.ToString().ToLowerInvariant() });
    }
}
