using Azure;
using Azure.Data.Tables;

namespace Entriqa.Data.Entities;

// Entities are internal - they never leave the data layer (Solution Standard §10.5).

internal sealed class FormEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "form";
    public string RowKey { get; set; } = default!;          // slug
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "draft";           // draft | published
    public string DraftJson { get; set; } = "{}";
    public int PublishedVersion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

internal sealed class FormVersionEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;    // slug
    public string RowKey { get; set; } = default!;          // version, 4-stellig
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    public DateTimeOffset PublishedAt { get; set; }
    public string PublishedBy { get; set; } = "";
}

internal sealed class SubmissionEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;    // slug
    public string RowKey { get; set; } = default!;          // invertierte Ticks + GUID → neueste zuerst
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? Source { get; set; }
    public string? Locale { get; set; }
    public string? IpHash { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public string? QuizJson { get; set; }
    public string? ConsentText { get; set; }
    public string StepRunsJson { get; set; } = "[]";
    public string ArtifactsJson { get; set; } = "{}";
    public string Handling { get; set; } = "none";
    public string State { get; set; } = "processing";       // denormalized for filtering in the admin
    public string? QuizResultId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedIpHash { get; set; }
    public string? BrevoContactId { get; set; }
}

internal sealed class AdminStateEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "state";
    public string RowKey { get; set; } = default!;           // Admin-User (userDetails)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset LastVisitAt { get; set; }
    public string? Note { get; set; }                        // housekeeping row: summary of the last run
}

internal sealed class NonceEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "nonce";
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class FunnelEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;    // slug
    public string RowKey { get; set; } = default!;          // yyyyMMdd|type (view | start)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public int Count { get; set; }
}

internal sealed class RateLimitEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;    // ipHash
    public string RowKey { get; set; } = default!;          // Fensterbeginn (Ticks)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public int Count { get; set; }
}
