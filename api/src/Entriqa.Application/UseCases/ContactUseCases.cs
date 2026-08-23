using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Kontakt-Sicht: aggregiert die vorhandenen Einsendungen je E-Mail-Adresse. Brevo kennt den
/// Kontakt-ZUSTAND, das Formsystem die Vorgangshistorie – begrenzt durch die Aufbewahrungsfrist
/// (Standard 180 Tage), also bewusst "die letzten Monate", kein Ewigkeits-CRM.
/// </summary>
internal sealed class ListContactsUseCase(
    IListContactSubmissionsQuery query,
    ITryGetFormVersionQuery getVersion) : IListContactsUseCase
{
    public async Task<IReadOnlyList<ContactSummary>> ExecuteAsync(CancellationToken ct = default)
    {
        var all = await query.ListWithEmailAsync(10_000, ct);
        var versions = new Dictionary<(string, int), FormDefinition?>();

        async Task<FormDefinition?> DefOf(Submission s)
        {
            if (!versions.TryGetValue((s.Slug, s.Version), out var def))
                versions[(s.Slug, s.Version)] = def = (await getVersion.ExecuteAsync(s.Slug, s.Version, ct))?.Definition;
            return def;
        }

        var contacts = new Dictionary<string, List<(Submission S, string? Company)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in all)
        {
            var def = await DefOf(s);
            var company = def is null ? null : CompanyOf(def, s.Values);
            (contacts.TryGetValue(s.Email!, out var list) ? list : contacts[s.Email!] = new()).Add((s, company));
        }

        return contacts
            .Select(kv =>
            {
                var ordered = kv.Value.OrderByDescending(x => x.S.CreatedAt).ToList();
                return new ContactSummary(
                    ordered[0].S.Email!,
                    ordered.Select(x => x.S.FirstName).FirstOrDefault(n => n is { Length: > 0 }),
                    ordered.Select(x => x.Company).FirstOrDefault(c => c is { Length: > 0 }),
                    ordered.Count,
                    ordered[^1].S.CreatedAt,
                    ordered[0].S.CreatedAt,
                    ordered.Select(x => x.S.BrevoContactId).FirstOrDefault(b => b is { Length: > 0 }),
                    ordered.Select(x => x.S.Slug).Distinct().ToList());
            })
            .OrderByDescending(c => c.LastAt)
            .ToList();
    }

    /// <summary>Firmenfeld per Label-Heuristik – wie die Vornamen-Erkennung beim Absenden.</summary>
    private static string? CompanyOf(FormDefinition def, IReadOnlyDictionary<string, string> values)
    {
        var f = def.Fields.FirstOrDefault(x => x.Type == FieldTypes.Text
            && (x.Label.ToString().Contains("Unternehmen", StringComparison.OrdinalIgnoreCase)
                || x.Label.ToString().Contains("Firma", StringComparison.OrdinalIgnoreCase)
                || x.Label.ToString().Contains("Company", StringComparison.OrdinalIgnoreCase)));
        return f is not null && values.TryGetValue(f.Id, out var v) && v.Length > 0 ? v : null;
    }
}

internal sealed class ListContactSubmissionsUseCase(IListContactSubmissionsQuery query) : IListContactSubmissionsUseCase
{
    public Task<IReadOnlyList<SubmissionListItem>> ExecuteAsync(string email, CancellationToken ct = default)
        => query.ListByEmailAsync(email, ct);
}

internal sealed class DeleteContactUseCase(
    IListContactSubmissionsQuery query,
    IDeleteSubmissionAdminUseCase deleteSubmission) : IDeleteContactUseCase
{
    public async Task<int> ExecuteAsync(string email, CancellationToken ct = default)
    {
        var items = await query.ListByEmailAsync(email, ct);
        foreach (var s in items) await deleteSubmission.ExecuteAsync(s.Id, ct);
        return items.Count;
    }
}
