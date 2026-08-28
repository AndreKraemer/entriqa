using System.Text.Json;

namespace Entriqa.Domain.Forms;

/// <summary>
/// Rules for visitor uploads: a deliberately narrow whitelist of "safe" document and image formats.
/// No SVG (scripts), no HTML, no archives, nothing executable. The files live in the private
/// container only and are never served directly - the admin gets time-limited SAS links.
/// </summary>
public static class UploadRules
{
    public const long MaxBytes = 10 * 1024 * 1024;                          // 10 MB

    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".png", ".jpg", ".jpeg", ".gif", ".webp" };

    public static bool IsAllowed(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>Defuse the file name for the blob path (letters, digits, .-_ only; the extension stays).</summary>
    public static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return safe.Length is > 0 and <= 100 ? safe : "datei" + Path.GetExtension(name);
    }
}

/// <summary>
/// The value of a file field in a submission: a small JSON handle issued by the upload
/// endpoint. <c>Path</c> points into the private container (uploads/… before submitting,
/// attachments/… afterwards - the submission takes the file over when it is saved).
/// </summary>
public sealed record UploadHandle(string Path, string Name, long Size)
{
    public string ToJson() => JsonSerializer.Serialize(new { upload = Path, name = Name, size = Size });

    public static UploadHandle? TryParse(string value)
    {
        if (!value.StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(value);
            var root = doc.RootElement;
            if (!root.TryGetProperty("upload", out var p) || p.ValueKind != JsonValueKind.String) return null;
            var path = p.GetString()!;
            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "datei";
            var size = root.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
            return new UploadHandle(path, name, size);
        }
        catch (JsonException) { return null; }
    }
}
