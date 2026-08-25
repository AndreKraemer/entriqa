using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using Entriqa.Application;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;

namespace Entriqa.Infrastructure.Storage;

/// <summary>Private container for lead magnets and generated PDFs. Delivery exclusively through time-limited SAS URLs.</summary>
public sealed class BlobArtifactAdapter : IStoreArtifactPort, ICreateDownloadLinkPort, IListArtifactsPort
{
    public async Task<IReadOnlyList<ArtifactInfo>> ListAsync(string prefix, CancellationToken ct = default)
    {
        await _ensure.Value;
        var result = new List<ArtifactInfo>();
        await foreach (var blob in _container.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
            result.Add(new ArtifactInfo(blob.Name, blob.Properties.ContentLength ?? 0));
        return result;
    }

    private readonly BlobContainerClient _container;
    private readonly Lazy<Task> _ensure;

    public BlobArtifactAdapter(IOptions<EntriqaOptions> options)
    {
        _container = new BlobContainerClient(options.Value.Storage.ConnectionString, options.Value.Storage.PrivateContainer);
        _ensure = new Lazy<Task>(() => _container.CreateIfNotExistsAsync(PublicAccessType.None));
    }

    public async Task<string> StoreAsync(string blobPath, byte[] content, string contentType, CancellationToken ct = default)
    {
        await _ensure.Value;
        var blob = _container.GetBlobClient(blobPath);
        await blob.UploadAsync(new BinaryData(content), new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
        return blobPath;
    }

    public async Task<byte[]> ReadAsync(string blobPath, CancellationToken ct = default)
    {
        await _ensure.Value;
        var result = await _container.GetBlobClient(blobPath).DownloadContentAsync(ct);
        return result.Value.Content.ToArray();
    }

    public async Task DeleteAsync(string blobPath, CancellationToken ct = default)
    {
        await _ensure.Value;
        await _container.GetBlobClient(blobPath).DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async Task<Uri> CreateAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default)
    {
        await _ensure.Value;
        var blob = _container.GetBlobClient(blobPath);
        if (!await blob.ExistsAsync(ct)) throw new InfrastructureException($"Datei '{blobPath}' fehlt im Container.");
        if (!blob.CanGenerateSasUri) throw new InfrastructureException("Storage-Verbindung erlaubt keine SAS-Erzeugung (Account-Key nötig).");
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(validFor))
        {
            BlobContainerName = _container.Name, BlobName = blobPath, ContentDisposition = $"attachment; filename=\"{Path.GetFileName(blobPath)}\"",
        };
        return blob.GenerateSasUri(sas);
    }
}
