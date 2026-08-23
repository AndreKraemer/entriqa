namespace Entriqa.Domain.Forms;

/// <summary>Ein veröffentlichter, unveränderlicher Snapshot. Einsendungen referenzieren immer eine Version.</summary>
public sealed record FormVersion(string Slug, int Version, FormDefinition Definition, DateTimeOffset PublishedAt, string PublishedBy);
