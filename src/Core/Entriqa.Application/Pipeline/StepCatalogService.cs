using Entriqa.Domain.UseCases;

namespace Entriqa.Application.Pipeline;

/// <summary>Catalog for the admin: everything registered as an ISubmissionStep via DI.</summary>
public sealed class StepCatalogService(IEnumerable<ISubmissionStep> steps)
{
    public IReadOnlyList<StepDescriptor> Describe() => steps
        .OrderBy(s => s.Key)
        .Select(s => new StepDescriptor(s.Key, s.Name, s.Description, s.Mode.ToString().ToLowerInvariant(), s.SplitsPhase,
            s.Needs.Select(n => n.ToString()).ToList(), s.Produces, s.ConfigSchema, s.CriticalByDefault,
            s.MailParams.Count > 0 ? s.MailParams : null))
        .ToList();
}
