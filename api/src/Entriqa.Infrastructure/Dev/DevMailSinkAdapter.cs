using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Entriqa.Application.Ports;

namespace Entriqa.Infrastructure.Dev;

/// <summary>
/// Lokale Entwicklung ohne Brevo-Key: statt zu versenden landet jede Mail als HTML-Datei in
/// <c>%TEMP%\entriqa-devmails\</c> – mit klickbarem confirmUrl-Link, damit der komplette DOI-Flow
/// lokal durchspielbar ist. Kontakt-Upserts werden nur geloggt (contacts.log), damit die Pipeline
/// vollständig durchläuft. Aktiv nur, wenn <c>Entriqa__Brevo__ApiKey</c> leer ist.
/// </summary>
public sealed class DevMailSinkAdapter(ILogger<DevMailSinkAdapter> log) : ISendTransactionalMailPort, IUpsertBrevoContactPort, IUpsertBrevoCompanyPort
{
    public async Task<string?> UpsertAsync(string email, IReadOnlyList<int> listIds, IReadOnlyDictionary<string, object?> attributes, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}  {email}  Listen: [{string.Join(", ", listIds)}]  {JsonSerializer.Serialize(attributes)}";
        await File.AppendAllTextAsync(Path.Combine(Folder, "contacts.log"), line + Environment.NewLine, ct);
        log.LogInformation("Dev-Sink: Kontakt-Upsert {Email} → contacts.log", email);
        return null;
    }

    public async Task UpsertCompanyAsync(string name, string contactEmail, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}  Firma \"{name}\"  Kontakt: {contactEmail}";
        await File.AppendAllTextAsync(Path.Combine(Folder, "companies.log"), line + Environment.NewLine, ct);
        log.LogInformation("Dev-Sink: Firmen-Upsert {Name} → companies.log", name);
    }

    // NIE ins Function-Ausgabeverzeichnis schreiben: der Host überwacht es und würde bei jeder
    // Mail den Worker neu laden. Deshalb ein fester Ordner unterhalb von %TEMP%.
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "entriqa-devmails");

    public async Task SendAsync(string toEmail, string? toName, int templateId, IReadOnlyDictionary<string, object?> parameters,
        MailAttachment? attachment = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var path = Path.Combine(Folder, $"{stamp}-template{templateId}-{Sanitize(toEmail)}.html");

        var html = new StringBuilder()
            .Append("<!doctype html><meta charset=\"utf-8\"><title>Dev-Mail</title>")
            .Append("<body style=\"font-family:system-ui;max-width:640px;margin:2rem auto\">")
            .Append($"<h2>Dev-Mail (Brevo-Vorlage {templateId})</h2>")
            .Append($"<p><b>An:</b> {Html(toName)} &lt;{Html(toEmail)}&gt;</p><table border=\"1\" cellpadding=\"6\" style=\"border-collapse:collapse\">");
        foreach (var (key, value) in parameters)
        {
            var text = value is null ? "" : value as string ?? JsonSerializer.Serialize(value);
            var cell = key.Contains("url", StringComparison.OrdinalIgnoreCase) && text.StartsWith("http", StringComparison.Ordinal)
                ? $"<a href=\"{Html(text)}\">{Html(text)}</a>"
                : Html(text);
            html.Append($"<tr><td><b>{Html(key)}</b></td><td>{cell}</td></tr>");
        }
        html.Append("</table>");
        if (attachment is not null) html.Append($"<p><b>Anhang:</b> {Html(attachment.FileName)} ({attachment.Content.Length:N0} Bytes)</p>");
        html.Append("</body>");

        await File.WriteAllTextAsync(path, html.ToString(), ct);
        log.LogInformation("Dev-Mail-Sink: Mail an {To} (Vorlage {Template}) → {Path}", toEmail, templateId, path);
    }

    private static string Sanitize(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
    private static string Html(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}
