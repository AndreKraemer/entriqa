namespace Entriqa.Application.Ports;

// Integrations-Ports – implementiert in Entriqa.Infrastructure (Brevo, ReportingCloud, Blob, HTTP).

public sealed record MailAttachment(string FileName, byte[] Content);

public interface ISendTransactionalMailPort
{
    Task SendAsync(string toEmail, string? toName, int templateId, IReadOnlyDictionary<string, object?> parameters,
        MailAttachment? attachment = null, CancellationToken ct = default);
}

public interface IUpsertBrevoContactPort
{
    /// <summary>Creates or updates the contact (updateEnabled). Returns the Brevo contact id when known.</summary>
    Task<string?> UpsertAsync(string email, IReadOnlyList<int> listIds, IReadOnlyDictionary<string, object?> attributes, CancellationToken ct = default);
}

public interface IUpsertBrevoCompanyPort
{
    /// <summary>Looks the company up by name (exact, case-insensitive), creates it when needed and links the contact.</summary>
    Task UpsertCompanyAsync(string name, string contactEmail, CancellationToken ct = default);
}

public interface IMergeDocumentPort
{
    /// <summary>ReportingCloud: Vorlage + Merge-Daten → PDF.</summary>
    Task<byte[]> MergeToPdfAsync(string templateName, object mergeData, CancellationToken ct = default);
}

public interface IStoreArtifactPort
{
    /// <summary>Stores a file privately and returns the blob path.</summary>
    Task<string> StoreAsync(string blobPath, byte[] content, string contentType, CancellationToken ct = default);
    Task<byte[]> ReadAsync(string blobPath, CancellationToken ct = default);
    /// <summary>Deletes the file; a missing blob is not an error (retention runs idempotently).</summary>
    Task DeleteAsync(string blobPath, CancellationToken ct = default);
}

public interface ICreateDownloadLinkPort
{
    /// <summary>Time-limited read URL (SAS) for a private blob.</summary>
    Task<Uri> CreateAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default);
}

public interface IPostWebhookPort
{
    Task PostJsonAsync(Uri url, object payload, CancellationToken ct = default);
}

public sealed record BrevoListInfo(long Id, string Name);
public sealed record BrevoTemplateInfo(long Id, string Name);

public interface IBrevoDirectoryPort
{
    Task<IReadOnlyList<BrevoListInfo>> GetListsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BrevoTemplateInfo>> GetTemplatesAsync(CancellationToken ct = default);
}

public interface IListReportTemplatesPort
{
    Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct = default);
}

public sealed record ArtifactInfo(string Path, long Size);

public interface IListArtifactsPort
{
    Task<IReadOnlyList<ArtifactInfo>> ListAsync(string prefix, CancellationToken ct = default);
}
