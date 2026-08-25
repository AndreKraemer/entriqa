namespace Entriqa.Domain.Forms;

/// <summary>A published, immutable snapshot. Submissions always reference a version.</summary>
public sealed record FormVersion(string Slug, int Version, FormDefinition Definition, DateTimeOffset PublishedAt, string PublishedBy);
