namespace StripeBilling.Domain.Operations;

/// <summary>Who did what, when. Written for every state change.</summary>
public sealed class AuditLog
{
    private AuditLog() { }

    public AuditLog(string entityType, string entityId, string action, string actor, string? detail, DateTimeOffset occurredAt)
    {
        Id = Guid.NewGuid();
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        Actor = actor;
        Detail = detail;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>
/// Inbox record for a Stripe webhook event. The event id is the primary key, so a
/// redelivered event can never be processed twice.
/// </summary>
public sealed class ProcessedWebhookEvent
{
    private ProcessedWebhookEvent() { }

    public ProcessedWebhookEvent(string stripeEventId, string type, DateTimeOffset processedAt)
    {
        StripeEventId = stripeEventId;
        Type = type;
        ProcessedAt = processedAt;
    }

    public string StripeEventId { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }
}

/// <summary>Stored response for an Idempotency-Key, so client retries replay the first result.</summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord() { }

    public IdempotencyRecord(string key, string requestFingerprint, int statusCode, string responseBody, DateTimeOffset createdAt)
    {
        Key = key;
        RequestFingerprint = requestFingerprint;
        StatusCode = statusCode;
        ResponseBody = responseBody;
        CreatedAt = createdAt;
    }

    public string Key { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public int StatusCode { get; private set; }
    public string ResponseBody { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
