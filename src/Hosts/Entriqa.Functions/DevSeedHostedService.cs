using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Entriqa.Application;
using Entriqa.Application.Consent;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Functions;

/// <summary>
/// Development only: publishes the example definitions from seed/forms/*.json when the form does not exist yet,
/// uploads the lead magnet files next to them so a sample configuring a download can actually deliver it,
/// and fills the inbox with demo submissions on a store that has none.
/// In production everything comes from the admin. (No schema setup at startup in prod - Solution Standard §14.)
/// </summary>
public sealed class DevSeedHostedService(
    IHostEnvironment env,
    IConfiguration config,
    ITryGetPublishedFormQuery getPublished,
    IPublishFormVersionCommand publish,
    IStoreArtifactPort artifacts,
    IListRecentSubmissionsQuery recent,
    IRecordAdminSeenCommand recordAdminSeen,
    IStoreSubmissionCommand storeSubmission,
    SubmissionPipelineService pipeline,
    ConsentProofService consentProofs,
    IOptions<EntriqaOptions> options,
    TimeProvider time,
    ILogger<DevSeedHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var folder = config["Entriqa:SeedFolder"];
        if (!env.IsDevelopment() || string.IsNullOrEmpty(folder)) return;
        var path = ResolveSeedFolder(folder);
        if (path is null) { log.LogWarning("Seed-Ordner {Folder} nicht gefunden (gesucht ab {Base} aufwärts)", folder, AppContext.BaseDirectory); return; }

        await SeedLeadMagnetsAsync(path, cancellationToken);

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
        {
            try
            {
                var def = JsonSerializer.Deserialize<FormDefinition>(await File.ReadAllTextAsync(file, cancellationToken), json);
                if (def is null || await getPublished.ExecuteAsync(def.Slug, cancellationToken) is not null) continue;
                await publish.ExecuteAsync(def, "seed", cancellationToken);
                log.LogInformation("Seed: {Slug} veröffentlicht", def.Slug);
            }
            catch (Exception ex) { log.LogWarning(ex, "Seed {File} übersprungen", file); }
        }

        await SeedAdminsAsync(cancellationToken);
        await SeedSubmissionsAsync(cancellationToken);
    }

    /// <summary>
    /// The two admins an assignment can choose from (#13). Without them the picker is empty on a fresh
    /// environment: its choices are the admins the application has seen, and locally there is only ever
    /// one browser. Seeded outside SeedSubmissionsAsync on purpose - that one leaves a store with data
    /// alone, and an environment seeded before #13 would otherwise never get its rows.
    ///
    /// DevAdmin is what SwaPrincipalReader.UserName returns without a principal header, so it is the
    /// account the local admin actually acts as; DevColleague exists so that handing a submission over
    /// and "Meine offenen" can be told apart without a second real account.
    /// </summary>
    private async Task SeedAdminsAsync(CancellationToken ct)
    {
        foreach (var admin in new[] { DevAdmin, DevColleague })
        {
            try { await recordAdminSeen.ExecuteAsync(admin, time.GetUtcNow(), ct); }
            catch (Exception ex) { log.LogWarning(ex, "Demo-Admin {Admin} übersprungen", admin); }
        }
    }

    /// <summary>The account the local admin acts as - see SwaPrincipalReader.UserName.</summary>
    private const string DevAdmin = "admin";

    /// <summary>A second admin, so handing over is drivable without a second real account.</summary>
    private const string DevColleague = "martina.weiss@example.org";

    /// <summary>
    /// Demo submissions, so the inbox is not empty on a fresh environment. Without them the admin's list,
    /// its quick filters and the paging have nothing to show, and every criterion about them can only be
    /// exercised by filling in forms by hand twenty times.
    ///
    /// The shape is chosen so that each branch of the list exists: "kontakt" is long enough for a second
    /// page (18 &gt; 15 per page), every quick filter is non-empty, and exactly one submission has failed -
    /// so the "Fehler" selection holds a single entry and both walking directions are dead at once.
    ///
    /// The demo values below are German, like the content of seed/forms/*.json: they are read in the
    /// admin's German interface, next to Labels.SubmissionState, and a half-English inbox would be worse
    /// than either language. CLAUDE.md's exception list names this file for that reason (#11).
    /// </summary>
    private async Task SeedSubmissionsAsync(CancellationToken ct)
    {
        if ((await recent.ExecuteAsync(null, 1, ct)).Count > 0) return;   // never touch a store that has data

        var now = time.GetUtcNow();
        var index = 0;
        // "beratung" joins the two since #13: it is the only other seed form that tracks handling, and
        // "Meine offenen" across all forms needs open submissions in more than one of them to show
        // anything a single-form filter would not.
        foreach (var (slug, count) in new[] { ("kontakt", 18), ("whitepaper", 6), ("beratung", 4) })
        {
            var published = await getPublished.ExecuteAsync(slug, ct);
            if (published is null) continue;

            for (var i = 0; i < count; i++, index++)
            {
                try
                {
                    await storeSubmission.ExecuteAsync(Demo(published, i, index, now.AddHours(-index)), ct);
                }
                catch (Exception ex) { log.LogWarning(ex, "Demo-Einsendung {Slug} #{Index} übersprungen", slug, i); }
            }
        }

        // One submission already past RetentionDays, together with its consent proof (#1). A housekeeping
        // run deletes the submission and has to leave the proof standing - without this pair the storage
        // inspection the criterion is accepted on has nothing to inspect. The proof goes through
        // ConsentProofService, so the seed cannot drift away from the rule that decides when one exists.
        if (await getPublished.ExecuteAsync("kontakt", ct) is { } kontakt)
        {
            try
            {
                var expired = Demo(kontakt, 99, index++, now.AddDays(-(options.Value.RetentionDays + 5)));
                await storeSubmission.ExecuteAsync(expired, ct);
                await consentProofs.RecordAsync(kontakt.Definition.Localize("de"), expired, ct);
                log.LogInformation("Seed: abgelaufene Demo-Einsendung {Id} mit Einwilligungsnachweis angelegt", expired.Id);
            }
            catch (Exception ex) { log.LogWarning(ex, "Abgelaufene Demo-Einsendung übersprungen"); }
        }

        log.LogInformation("Seed: {Count} Demo-Einsendungen angelegt", index);
    }

    private Submission Demo(FormVersion published, int i, int index, DateTimeOffset createdAt)
    {
        var def = published.Definition;
        var values = DemoValues(def, index, longValue: def.Slug == "kontakt" && i == 2);
        var runs = pipeline.CreateRuns(def);
        Stage(runs, def.Slug, i);

        // Same row key shape as a real submission: descending by time, so the table sorts newest first.
        var rowKey = $"{DateTimeOffset.MaxValue.Ticks - createdAt.UtcTicks:D19}-seed{index:D4}";
        return new Submission
        {
            Id = $"{def.Slug}:{rowKey}",
            Slug = def.Slug,
            Version = published.Version,
            CreatedAt = createdAt,
            Locale = "de",
            Values = values,
            Email = def.EmailField is { } ef ? values.GetValueOrDefault(ef.Id) : null,
            FirstName = def.Fields.FirstOrDefault(f => f.Type == FieldTypes.Text) is { } nf
                ? values.GetValueOrDefault(nf.Id)?.Split(' ')[0] : null,
            Source = "linkedin",
            Quiz = null,
            ConsentText = def.ConsentField?.Text?.Resolve("de"),
            StepRuns = runs,
            Handling = def.Handling ? (i < 16 ? HandlingStates.Open : HandlingStates.Done) : HandlingStates.None,
            Assignee = def.Handling ? Assignee(def.Slug, i) : null,
        };
    }

    /// <summary>
    /// Who is on it (#13), so every branch of a personal filter exists without assigning by hand first:
    /// open ones of both admins in two different forms (AC4 cross-form, AC5), one that nobody has taken
    /// ("Niemand"), and one already done that must therefore stay out of "Meine offenen". Only where the
    /// form tracks handling - the application refuses an assignment anywhere else, and seed data must not
    /// hold a state it would reject.
    /// </summary>
    private static string? Assignee(string slug, int i) => (slug, i) switch
    {
        ("kontakt", 3) or ("beratung", 0) => DevAdmin,        // open, mine - in two forms
        ("kontakt", 4) or ("beratung", 1) => DevColleague,    // open, someone else's
        ("kontakt", 16) => DevAdmin,                          // done and mine: in no personal filter
        _ => null,                                            // the rest is unassigned - the "Niemand" filter
    };

    /// <summary>
    /// Puts the run into the state the list's quick filters distinguish. The state is derived from the step
    /// runs, so it is set here rather than assigned.
    /// </summary>
    private static void Stage(List<StepRun> runs, string slug, int i)
    {
        if (runs.Count == 0) return;
        var stage = (slug, i) switch
        {
            ("kontakt", 0) => StepRunStatus.Pending,                        // still processing
            ("kontakt", 1) or ("whitepaper", 1) => StepRunStatus.Waiting,   // waiting for the double opt-in
            ("whitepaper", 0) => StepRunStatus.Failed,                      // exactly one, see SeedSubmissionsAsync
            _ => StepRunStatus.Ok,
        };

        // Everything before the last run has finished; the last one carries the state to show.
        foreach (var run in runs) run.Status = stage == StepRunStatus.Pending ? StepRunStatus.Pending : StepRunStatus.Ok;
        if (stage is StepRunStatus.Waiting or StepRunStatus.Failed) runs[^1].Status = stage;
        if (stage == StepRunStatus.Failed) runs[^1].Error = "Brevo antwortete mit 502 (Demo-Daten).";
    }

    private static readonly string[] DemoNames =
    {
        "Martina Weiß", "Jonas Herrmann", "Sabine Behrens", "Ali Yilmaz", "Klara Nowak",
        "Tobias Ritter", "Ines Baumgartner", "Peer Johansson", "Rebecca Fuchs", "Lars Petersen",
    };

    /// <summary>
    /// The message of a demo submission. Two of the five name a company, because that is what the inbox
    /// search (#12) is there to find: a name that lives in a free-text answer and in no column of its own.
    /// Without one in the seed the feature has nothing to look for on a fresh environment.
    /// </summary>
    private static readonly string[] DemoMessages =
    {
        "Bitte um Rückruf zum Angebot.",
        "Wir sind die Nordlicht AG und bräuchten ein Angebot für 40 Arbeitsplätze.",
        "Bitte um Unterlagen per Post.",
        "Anfrage im Auftrag der Nordlicht AG – bitte an die Buchhaltung weiterleiten.",
        "Wann wäre ein Termin in der kommenden Woche möglich?",
    };

    /// <summary>One long unbroken value, so the detail page can be checked against horizontal page scroll.</summary>
    private const string LongValue =
        "Kontext: https://intranet.example.org/projekte/2026/formularmodernisierung/anforderungen/entwurf-v3-final-freigabe-langer-pfad-ohne-trennzeichen";

    private static Dictionary<string, string> DemoValues(FormDefinition def, int index, bool longValue)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in def.Fields.Where(f => !FieldTypes.IsLayout(f.Type)))
        {
            values[f.Id] = f.Type switch
            {
                FieldTypes.Email => $"person{index:D2}@example.org",
                FieldTypes.Text => DemoNames[index % DemoNames.Length],
                FieldTypes.Textarea => longValue ? LongValue : DemoMessages[index % DemoMessages.Length],
                FieldTypes.Select or FieldTypes.Multiselect =>
                    f.Options is { Count: > 0 } o ? o[index % o.Count].Resolve("de") : "",
                FieldTypes.Consent or FieldTypes.Checkbox => "true",
                FieldTypes.Hidden => "linkedin",
                FieldTypes.Number or FieldTypes.Rating => (index % 5 + 1).ToString(CultureInfo.InvariantCulture),
                FieldTypes.Tel => "+49 151 0000000",
                FieldTypes.Date => createdAtDate(index),
                _ => "–",
            };
        }
        return values;

        static string createdAtDate(int index) =>
            new DateOnly(2026, 8, 1).AddDays(index % 28).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Uploads seed/leadmagnets/* to leadmagnets/&lt;name&gt; in the private container. The forms
    /// reference those paths, and BlobArtifactAdapter fails the step when the blob is absent - so
    /// without this the shipped whitepaper example ends in a failed run on a fresh environment.
    /// </summary>
    private async Task SeedLeadMagnetsAsync(string seedPath, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetDirectoryName(seedPath.TrimEnd(Path.DirectorySeparatorChar))!, "leadmagnets");
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var blobPath = "leadmagnets/" + Path.GetFileName(file);
            try
            {
                await artifacts.StoreAsync(blobPath, await File.ReadAllBytesAsync(file, ct), ContentTypeOf(file), ct);
                log.LogInformation("Seed: {Blob} hochgeladen", blobPath);
            }
            catch (Exception ex) { log.LogWarning(ex, "Seed-Datei {File} übersprungen", file); }
        }
    }

    private static string ContentTypeOf(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };

    /// <summary>Search relative paths upwards from the output directory - works for func start, dotnet run and tests alike.</summary>
    private static string? ResolveSeedFolder(string folder)
    {
        if (Path.IsPathRooted(folder)) return Directory.Exists(folder) ? folder : null;
        var relative = folder.Replace("../", "", StringComparison.Ordinal).Replace("..\\", "", StringComparison.Ordinal);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
