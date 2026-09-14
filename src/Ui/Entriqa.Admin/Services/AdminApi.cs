using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Entriqa.Admin.Services;

/// <summary>
/// Thin client for /api/manage/*. The form definition deliberately stays raw JSON (JsonElement/string) -
/// the editor of the first stage works on the document directly, the visual builder comes later.
/// </summary>
public sealed class AdminApi(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<List<FormListItem>> ListFormsAsync() => GetAsync<List<FormListItem>>("api/manage/forms");

    public Task<List<StepDescriptorDto>> GetStepCatalogAsync() => GetAsync<List<StepDescriptorDto>>("api/manage/steps");

    public async Task<FormDraft?> TryGetDraftAsync(string slug)
    {
        var res = await http.GetAsync($"api/manage/forms/{Uri.EscapeDataString(slug)}");
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        await ThrowIfError(res);
        return (await res.Content.ReadFromJsonAsync<FormDraft>(Json))!;
    }

    public async Task SaveDraftAsync(string slug, string definitionJson)
    {
        var res = await http.PutAsync($"api/manage/forms/{Uri.EscapeDataString(slug)}",
            new StringContent(definitionJson, Encoding.UTF8, "application/json"));
        await ThrowIfError(res);
    }

    public async Task<CheckResult> CheckAsync(string definitionJson)
    {
        var res = await http.PostAsync("api/manage/forms/check", new StringContent(definitionJson, Encoding.UTF8, "application/json"));
        await ThrowIfError(res);
        return (await res.Content.ReadFromJsonAsync<CheckResult>(Json))!;
    }

    public async Task<PublishResult> PublishAsync(string slug)
    {
        var res = await http.PostAsync($"api/manage/forms/{Uri.EscapeDataString(slug)}/publish", null);
        await ThrowIfError(res);
        return (await res.Content.ReadFromJsonAsync<PublishResult>(Json))!;
    }

    public Task<IntegrationDirectory> GetDirectoryAsync() => GetAsync<IntegrationDirectory>("api/manage/directory");

    public Task<Dictionary<string, FormActivity>> GetActivityAsync() => GetAsync<Dictionary<string, FormActivity>>("api/manage/forms/activity");

    public Task<AdminStatus> GetStatusAsync() => GetAsync<AdminStatus>("api/manage/status");

    /// <summary>Who is signed in? SWA serves the principal under /.auth/me (locally emulated by the CLI).</summary>
    public async Task<AuthInfo?> GetMeAsync()
    {
        try
        {
            var doc = await http.GetFromJsonAsync<JsonElement>("/.auth/me", Json);
            if (doc.ValueKind != JsonValueKind.Object
                || !doc.TryGetProperty("clientPrincipal", out var p) || p.ValueKind != JsonValueKind.Object) return null;
            var roles = p.TryGetProperty("userRoles", out var r) && r.ValueKind == JsonValueKind.Array
                ? r.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                : new List<string>();
            return new AuthInfo(
                p.TryGetProperty("userDetails", out var d) ? d.GetString() : null,
                p.TryGetProperty("identityProvider", out var ip) ? ip.GetString() : null,
                roles);
        }
        catch { return null; }                       // no auth endpoint (dotnet run without SWA, say) -> anonymous
    }

    public async Task<LeadMagnetInfo> UploadLeadMagnetAsync(Microsoft.AspNetCore.Components.Forms.IBrowserFile file)
    {
        using var content = new MultipartFormDataContent();
        var stream = new StreamContent(file.OpenReadStream(25 * 1024 * 1024));
        stream.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(stream, "file", file.Name);
        var res = await http.PostAsync("api/manage/blobs/leadmagnets", content);
        await ThrowIfError(res);
        return (await res.Content.ReadFromJsonAsync<LeadMagnetInfo>(Json))!;
    }

    public Task<List<ContactSummary>> ListContactsAsync() => GetAsync<List<ContactSummary>>("api/manage/contacts");

    public Task<List<SubmissionListItem>> ListContactSubmissionsAsync(string email) =>
        GetAsync<List<SubmissionListItem>>($"api/manage/contacts/submissions?email={Uri.EscapeDataString(email)}");

    public async Task<int> DeleteContactAsync(string email)
    {
        var res = await http.DeleteAsync($"api/manage/contacts?email={Uri.EscapeDataString(email)}");
        await ThrowIfError(res);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        return doc.GetProperty("deleted").GetInt32();
    }

    public Task<List<ConsentProofView>> ListConsentProofsAsync(string email) =>
        GetAsync<List<ConsentProofView>>($"api/manage/consent?email={Uri.EscapeDataString(email)}");

    public async Task<bool> DeleteConsentProofAsync(string email, string submissionId)
    {
        var res = await http.DeleteAsync($"api/manage/consent?email={Uri.EscapeDataString(email)}" +
                                         $"&submission={Uri.EscapeDataString(submissionId)}");
        await ThrowIfError(res);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        return doc.GetProperty("deleted").GetBoolean();
    }

    public async Task<string> GetUploadLinkAsync(string path)
    {
        var doc = await GetAsync<JsonElement>($"api/manage/uploads/link?path={Uri.EscapeDataString(path)}");
        return doc.GetProperty("url").GetString() ?? "";
    }

    public async Task ResendDoiAsync(string id)
    {
        var res = await http.PostAsync($"api/manage/submissions/{Uri.EscapeDataString(id)}/resend-doi", null);
        await ThrowIfError(res);
    }

    public Task<FormStats> GetStatsAsync(string slug, int? version) =>
        GetAsync<FormStats>($"api/manage/forms/{Uri.EscapeDataString(slug)}/stats" + (version is { } v ? $"?version={v}" : ""));

    public Task<RecentSubmissions> ListRecentAsync(string? slug) =>
        GetAsync<RecentSubmissions>("api/manage/submissions" + (slug is null ? "" : $"?slug={Uri.EscapeDataString(slug)}"));

    public async Task<DateTimeOffset> MarkVisitedAsync()
    {
        var res = await http.PostAsync("api/manage/state/last-visit", null);
        await ThrowIfError(res);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        return doc.GetProperty("lastVisitAt").GetDateTimeOffset();
    }

    public Task<SubmissionPage> ListSubmissionsAsync(string slug, string? continuation) =>
        GetAsync<SubmissionPage>($"api/manage/forms/{Uri.EscapeDataString(slug)}/submissions"
            + (continuation is null ? "" : $"?continuation={Uri.EscapeDataString(continuation)}"));

    public Task<SubmissionDetail> GetSubmissionAsync(string id) =>
        GetAsync<SubmissionDetail>($"api/manage/submissions/{Uri.EscapeDataString(id)}");

    public async Task RetryStepAsync(string id, string stepId)
    {
        var res = await http.PostAsync($"api/manage/submissions/{Uri.EscapeDataString(id)}/steps/{Uri.EscapeDataString(stepId)}/retry", null);
        await ThrowIfError(res);
    }

    public async Task SetHandlingAsync(string id, string handling)
    {
        var res = await http.PostAsJsonAsync($"api/manage/submissions/{Uri.EscapeDataString(id)}/handling", new { handling }, Json);
        await ThrowIfError(res);
    }

    /// <summary>Hands a submission to an admin, or to nobody (null) - one operation for all three (#13).</summary>
    public async Task SetAssigneeAsync(string id, string? assignee)
    {
        var res = await http.PostAsJsonAsync($"api/manage/submissions/{Uri.EscapeDataString(id)}/assignee", new { assignee }, Json);
        await ThrowIfError(res);
    }

    /// <summary>The admins an assignment can choose from - those the application has seen at least once (#13).</summary>
    public Task<List<string>> ListAdminsAsync() => GetAsync<List<string>>("api/manage/admins");

    public async Task DeleteSubmissionAsync(string id)
    {
        var res = await http.DeleteAsync($"api/manage/submissions/{Uri.EscapeDataString(id)}");
        await ThrowIfError(res);
    }

    private async Task<T> GetAsync<T>(string url)
    {
        var res = await http.GetAsync(url);
        await ThrowIfError(res);
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task ThrowIfError(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode)
        {
            // Without a login the SWA redirects to the login page -> 200 with HTML instead of JSON.
            var mediaType = res.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.Contains("json")) throw new ApiException("Nicht angemeldet.", 401);
            return;
        }
        var title = $"HTTP {(int)res.StatusCode}";
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } text) title = text;
        }
        catch (JsonException) { /* no ProblemDetails */ }
        throw new ApiException(title, (int)res.StatusCode);
    }
}

public sealed class ApiException(string message, int status) : Exception(message)
{
    public int Status => status;
}

public sealed record AuthInfo(string? UserDetails, string? IdentityProvider, List<string> UserRoles);

public static class Errors
{
    /// <summary>401/403 almost always means: not signed in yet - then show instructions instead of the raw error.</summary>
    public static string Friendly(Exception ex, Ui t) => ex is ApiException { Status: 401 or 403 }
        ? t["Bitte zuerst anmelden: /.auth/login/aad öffnen, Benutzername wählen und im Rollen-Feld 'admin' eintragen (lokal simuliert die SWA-CLI den Login)."]
        : t[ex.Message];
}

public sealed record FormListItem(string Slug, string Name, string Type, string Status, int PublishedVersion, DateTimeOffset UpdatedAt);
public sealed record ContactSummary(string Email, string? Name, string? Company, int Count,
    DateTimeOffset FirstAt, DateTimeOffset LastAt, string? BrevoContactId, List<string> Slugs);
/// <summary>One consent proof as the admin reads it (#2) - the wording as it was ticked, never the form's current text.</summary>
public sealed record ConsentProofView(string Email, string SubmissionId, string Slug, int Version,
    DateTimeOffset SubmittedAt, DateTimeOffset? ConfirmedAt, string ConsentText,
    string? IpHash, string? ConfirmedIpHash);
public sealed record StepDescriptorDto(string Key, string Name, string Description, string Mode, bool SplitsPhase,
    List<string> Needs, string? Produces, string ConfigSchema, bool CriticalByDefault,
    List<MailParamDto>? MailParams = null);
public sealed record MailParamDto(string Name, string Description);
public sealed record FormDraft(string Slug, string Status, int PublishedVersion, DateTimeOffset UpdatedAt, string UpdatedBy, JsonElement Definition);
public sealed record CheckResult(List<string> Issues);
public sealed record PublishResult(int Version, List<string> Issues);

public sealed record SubmissionPage(List<SubmissionListItem> Items, string? ContinuationToken);
public sealed record RecentSubmissions(List<SubmissionListItem> Items, DateTimeOffset? LastVisitAt);

public sealed record FormStats(int Total, List<int> Daily, List<int> Versions, int WithEmail, int Confirmed,
    int AwaitingConfirmation, int Failed, List<StatsBar> Sources, QuizStats? Quiz, List<FieldStats> SelectFields,
    FunnelStats? Funnel);
public sealed record FunnelStats(int Views, int Starts);
public sealed record StatsBar(string Label, int Count);
public sealed record QuizStats(List<StatsBar> Results, int AvgPct, int EndedByJump, List<QuestionStats> Questions);
public sealed record QuestionStats(string Question, int Seen, List<StatsBar> Options);
public sealed record FieldStats(string Label, List<StatsBar> Options);
public sealed record SubmissionListItem(string Id, string Slug, int Version, DateTimeOffset CreatedAt, string? Email,
    string Summary, int State, string Handling, string? QuizResultId, string? Assignee = null);

public sealed record SubmissionDetail(string Id, string Slug, int Version, DateTimeOffset CreatedAt, string? Locale,
    string? Email, string? FirstName, string? Source, List<SubmissionValue> Values, QuizInfo? Quiz, string? QuizResultTitle,
    string? ConsentText, DateTimeOffset? ConfirmedAt, List<StepRunInfo> StepRuns,
    List<HistoryEntry>? History, string Handling, int State,
    string? BrevoContactId, bool CanResendDoi, List<QuizAnswer>? QuizAnswers, string? Assignee = null);

/// <summary>One entry of a submission's history (#14), newest first as the API delivers it.</summary>
public sealed record HistoryEntry(DateTimeOffset At, string Type, string Origin, string? By, string? Detail);

public sealed record IntegrationDirectory(bool BrevoConfigured, List<DirectoryEntry> BrevoLists, List<DirectoryEntry> BrevoTemplates,
    bool ReportingCloudConfigured, List<string> ReportTemplates, List<LeadMagnetInfo> LeadMagnets,
    bool BrevoListsComplete = true, bool BrevoTemplatesComplete = true);
public sealed record DirectoryEntry(long Id, string Name);
public sealed record LeadMagnetInfo(string Path, long Size);
public sealed record FormActivity(int Total, List<int> Daily);
public sealed record QuizAnswer(string Question, string Answer, int Points, bool Jumped);
public sealed record AdminStatus(string SiteName, string BaseUrl, bool BrevoConfigured, bool BrevoOk,
    bool ReportingCloudConfigured, int RetentionDays, int UnconfirmedRetentionDays, int SweepAfterMinutes,
    int AutoRetryMax, DateTimeOffset? HousekeepingLastRunAt, string? HousekeepingSummary, List<string>? Locales,
    LicenseView? License);
public sealed record LicenseView(bool Valid, string Status, string? Plan, DateOnly? ValidUntil);
public sealed record SubmissionValue(string FieldId, string Label, string Value);
public sealed record QuizInfo(Dictionary<string, string> Answers, List<string> Path, int Points, int MaxPoints, int Pct,
    string ResultId, bool ReachedByJump, List<string>? Findings)
{
    public bool Jump => ReachedByJump;
}
public sealed record StepRunInfo(string StepId, string StepKey, int Phase, int Status, string? Error, int Attempts, DateTimeOffset? FinishedAt);

/// <summary>Make the enum numbers of the API readable (order = server definition).</summary>
public static class Labels
{
    private static readonly string[] SubmissionStates = { "In Arbeit", "Wartet auf Bestätigung", "Fehler", "Fertig" };
    private static readonly string[] StepStatuses = { "Ausstehend", "OK", "Wartet", "Fehlgeschlagen", "Blockiert", "Übersprungen" };

    public static string SubmissionState(int s) => s >= 0 && s < SubmissionStates.Length ? SubmissionStates[s] : s.ToString(CultureInfo.InvariantCulture);
    public static string StepStatus(int s) => s >= 0 && s < StepStatuses.Length ? StepStatuses[s] : s.ToString(CultureInfo.InvariantCulture);
    public static string StateCss(int s) => s switch { 0 => "chip chip--busy", 1 => "chip chip--wait", 2 => "chip chip--err", 3 => "chip chip--ok", _ => "chip" };
    public static string StepCss(int s) => s switch { 1 => "chip chip--ok", 3 => "chip chip--err", 4 => "chip chip--err", 2 => "chip chip--wait", 5 => "chip", _ => "chip chip--busy" };

    /// <summary>
    /// The names an assignee picker offers (#13): the admins the application knows, plus the one the
    /// submission already carries when that is not among them.
    ///
    /// The second half is not a nicety. A select whose value matches no option falls back to the first
    /// one, so the picker would read "Niemand" for a submission that is assigned - and the next change
    /// would write that lie back. The case is the one the story accepted rather than a rarity: an admin
    /// who is renamed leaves their old name on every submission they still hold.
    /// </summary>
    public static List<string> AssigneeChoices(IEnumerable<string>? admins, string? current)
    {
        var choices = admins?.ToList() ?? [];
        if (current is { Length: > 0 } c && !choices.Contains(c, StringComparer.Ordinal)) choices.Insert(0, c);
        return choices;
    }

    /// <summary>
    /// The avatar a person is shown as - the signed-in admin in the header, and since #13 the assignee in
    /// the list and the detail view. It lives here rather than in MainLayout, which had it first, because
    /// a name has to read the same in every place that abbreviates it.
    ///
    /// The local part carries the name: an address abbreviated whole would read as its provider. Two
    /// initials where the name has parts, the first two letters where it has one, "?" for nobody.
    /// </summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var clean = name.Split('@')[0];
        var parts = clean.Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
            : clean[..Math.Min(2, clean.Length)].ToUpperInvariant();
    }

    /// <summary>
    /// Turns one history entry (#14) into a German sentence - the stable "handling"/"assignee"/… keys the
    /// server sends are never prose, or the wording would be frozen and untranslatable.
    /// </summary>
    public static string HistoryText(HistoryEntry e, Ui t)
    {
        var what = e.Type switch
        {
            "handling" => t.F("Status geändert zu {0}", e.Detail == "done" ? t["Erledigt"] : t["Offen"]),
            "assignee" => e.Detail is { Length: > 0 } target ? t.F("Zugewiesen an {0}", target) : t["Zuweisung entfernt"],
            "step.retry" => e.Detail is { Length: > 0 } step ? t.F("Schritt wiederholt: {0}", step) : t["Fehlgeschlagene Schritte wiederholt"],
            "doi.resend" => t["Bestätigungsmail erneut gesendet"],
            _ => e.Type,
        };
        var who = e.Origin == "system" ? t["automatisch"] : e.By is { Length: > 0 } by ? by : t["unbekannt"];
        return $"{what} – {who}";
    }
}
