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
    // #14. Missing on every row written before this feature, which deserializes to the default "[]" -
    // an empty history, which is exactly what those submissions have. No migration.
    public string HistoryJson { get; set; } = "[]";
    public string ArtifactsJson { get; set; } = "{}";
    public string Handling { get; set; } = "none";
    public string State { get; set; } = "processing";       // denormalized for filtering in the admin
    public string? QuizResultId { get; set; }
    public string? Assignee { get; set; }                   // #13: the admin taking care of it, null = nobody
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedIpHash { get; set; }
    public string? BrevoContactId { get; set; }
    // #15. Missing on every row written before this feature, which deserializes to null - no override,
    // the regular deadline. No migration.
    public DateTimeOffset? RetainUntil { get; set; }
}

internal sealed class AdminStateEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "state";
    public string RowKey { get; set; } = default!;           // Admin-User (userDetails)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    // Nullable since #13: a state row is now also created by merely being in the admin, and such a
    // row has no last visit. Reporting 0001-01-01 instead would mark every submission as new.
    public DateTimeOffset? LastVisitAt { get; set; }
    public DateTimeOffset? SeenAt { get; set; }              // state row: when this admin was last seen at all (#13)
    public string? Note { get; set; }                        // housekeeping row: summary of the last run; consentdeletion row: the hashed address (#1)
    public int? Count { get; set; }                          // consentdeletion row: how many proofs the erasure removed (#1)
    public string? By { get; set; }                          // consentdeletion row: the admin account that triggered it (#2)
    public string? SubmissionId { get; set; }                // consentdeletion row: the single proof that was removed, null for a contact-wide erasure (#2)
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

internal sealed class ConsentProofEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;    // e-mail, lowercase - erasure of a contact is a point query
    public string RowKey { get; set; } = default!;          // submission id
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Email { get; set; } = "";                 // as submitted; the partition key carries the normalized form
    public string Slug { get; set; } = "";
    public int Version { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string ConsentText { get; set; } = "";
    public string? IpHash { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedIpHash { get; set; }
}
