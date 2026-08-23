using System.Text.Json;

namespace Entriqa.Domain.Forms;

/// <summary>
/// Regeln für Besucher-Uploads: bewusst enge Whitelist "sicherer" Dokument- und Bildformate.
/// Kein SVG (Skripte), kein HTML, keine Archive, nichts Ausführbares. Die Dateien liegen ausschließlich
/// im privaten Container und werden nie direkt ausgeliefert – der Admin bekommt zeitlich begrenzte SAS-Links.
/// </summary>
public static class UploadRules
{
    public const long MaxBytes = 10 * 1024 * 1024;                          // 10 MB

    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".png", ".jpg", ".jpeg", ".gif", ".webp" };

    public static bool IsAllowed(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>Dateiname für den Blob-Pfad entschärfen (nur Buchstaben/Ziffern/.-_, Erweiterung bleibt).</summary>
    public static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return safe.Length is > 0 and <= 100 ? safe : "datei" + Path.GetExtension(name);
    }
}

/// <summary>
/// Der Wert eines Datei-Feldes in der Einsendung: ein kleines JSON-Handle, das der Upload-Endpunkt
/// ausgestellt hat. <c>Path</c> zeigt in den privaten Container (uploads/… vor dem Absenden,
/// attachments/… danach – die Einsendung übernimmt die Datei beim Speichern).
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
