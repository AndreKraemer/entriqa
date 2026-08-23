using Entriqa.Domain.UseCases;

namespace Entriqa.Application.Pipeline;

/// <summary>Katalog für den Admin: alles, was per DI als ISubmissionStep registriert ist.</summary>
public sealed class StepCatalogService(IEnumerable<ISubmissionStep> steps)
{
    public IReadOnlyList<StepDescriptor> Describe() => steps
        .OrderBy(s => s.Key)
        .Select(s => new StepDescriptor(s.Key, s.Name, s.Description, s.Mode.ToString().ToLowerInvariant(), s.SplitsPhase,
            s.Needs.Select(n => n.ToString()).ToList(), s.Produces, s.ConfigSchema, s.CriticalByDefault))
        .ToList();
}
