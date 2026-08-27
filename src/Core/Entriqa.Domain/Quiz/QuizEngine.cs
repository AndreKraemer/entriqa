using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;

namespace Entriqa.Domain.Quiz;

/// <summary>
/// Walks the path through the branches, counts points, normalizes against the path maximum and picks the result.
/// Pure domain logic without dependencies - identical for scoring (server) and checking (publishing).
/// </summary>
public static class QuizEngine
{
    public static QuizOutcome Evaluate(QuizDefinition quiz, IReadOnlyDictionary<string, string> answers)
    {
        var byId = quiz.Questions.Select((q, i) => (q, i)).ToDictionary(x => x.q.Id, x => x.i);
        var path = new List<string>();
        var taken = new Dictionary<string, string>();
        int points = 0, max = 0, index = 0, guard = 0;
        QuizResult? jumped = null;

        while (index < quiz.Questions.Count)
        {
            if (++guard > 500) throw new AppException(ErrorCodes.QuizPathLoop, ErrorMessages.QuizPathLoop);
            var q = quiz.Questions[index];
            if (!answers.TryGetValue(q.Id, out var optionId))
                throw new ValidationException(new[] { new FieldError(q.Id, "Diese Frage wurde nicht beantwortet.") });
            var option = q.Options.FirstOrDefault(o => o.Id == optionId)
                ?? throw new ValidationException(new[] { new FieldError(q.Id, "Ungültige Antwort.") });

            path.Add(q.Id);
            taken[q.Id] = option.Id;
            points += option.Points;
            max += q.Options.Max(o => o.Points);

            if (option.Next is not null && option.Next.StartsWith(StepConditions.ResultPrefix, StringComparison.Ordinal))
            {
                var rid = option.Next[StepConditions.ResultPrefix.Length..];
                jumped = quiz.Results.FirstOrDefault(r => r.Id == rid)
                    ?? throw new AppException(ErrorCodes.QuizInvalidDefinition, ErrorMessages.QuizResultMissing, AppException.Args("id", rid));
                break;
            }
            index = option.Next is not null && byId.TryGetValue(option.Next, out var next) ? next : index + 1;
        }

        var pct = max == 0 ? 0 : (int)Math.Round(points * 100.0 / max);
        var result = jumped
            ?? quiz.Results.FirstOrDefault(r => pct >= r.MinPct && pct <= r.MaxPct)
            ?? quiz.Results.OrderBy(r => r.MinPct).Last();

        return new QuizOutcome(taken, path, points, max, pct, result.Id, jumped is not null, ComputeFindings(quiz, path, taken));
    }

    /// <summary>
    /// Findings following the self-assessment pattern: texts of the chosen options, prioritized, at most n of them;
    /// with fewer than two filled up from the one-point topics via the warning template, without any hit the empty text.
    /// Expects an already localized definition (texts resolve as plain strings).
    /// </summary>
    private static IReadOnlyList<string>? ComputeFindings(QuizDefinition quiz, IReadOnlyList<string> path, IReadOnlyDictionary<string, string> taken)
    {
        if (quiz.Findings is null) return null;
        var cfg = quiz.Findings;
        var byId = quiz.Questions.ToDictionary(q => q.Id);
        var rank = (cfg.Priority ?? Array.Empty<string>()).Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

        var pathIndex = path.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var chosen = path
            .Select(qid => (Question: byId[qid], Option: byId[qid].Options.First(o => o.Id == taken[qid])))
            .ToList();

        var findings = chosen
            .Where(x => x.Option.Finding is { IsEmpty: false })
            .OrderBy(x => rank.TryGetValue(x.Question.Id, out var r) ? r : int.MaxValue)
            .ThenBy(x => pathIndex[x.Question.Id])
            .Take(cfg.Max)
            .Select(x => x.Option.Finding!.ToString())
            .ToList();

        if (findings.Count < 2 && cfg.WarningTemplate is { IsEmpty: false } tpl)
        {
            var covered = chosen.Where(x => x.Option.Finding is { IsEmpty: false }).Select(x => x.Question.Id).ToHashSet();
            foreach (var x in chosen.Where(x => x.Option.Points == 1 && !covered.Contains(x.Question.Id)))
            {
                if (findings.Count >= cfg.Max) break;
                findings.Add(tpl.ToString().Replace("{topic}", x.Question.Topic?.ToString() ?? x.Question.Id, StringComparison.Ordinal));
            }
        }
        if (findings.Count == 0 && cfg.EmptyText is { IsEmpty: false } empty) findings.Add(empty.ToString());
        return findings;
    }

    /// <summary>Check when publishing: jump targets exist, no loops, everything reachable, results without gaps.</summary>
    public static IReadOnlyList<string> Check(QuizDefinition quiz)
    {
        var issues = new List<string>();
        if (quiz.Questions.Count == 0) issues.Add("Keine Fragen vorhanden.");
        if (quiz.Results.Count == 0) issues.Add("Kein Ergebnis definiert.");
        var byId = quiz.Questions.Select((q, i) => (q, i)).ToDictionary(x => x.q.Id, x => x.i);
        var resultIds = quiz.Results.Select(r => r.Id).ToHashSet();
        var seen = new HashSet<string>();

        void Visit(int i, ImmutableStack stack)
        {
            if (i >= quiz.Questions.Count) return;
            var q = quiz.Questions[i];
            if (stack.Contains(q.Id)) { issues.Add($"Schleife: Frage '{q.Id}' wird erneut erreicht."); return; }
            if (!seen.Add(q.Id)) return;
            foreach (var o in q.Options)
            {
                if (o.Next is null) Visit(i + 1, stack.Push(q.Id));
                else if (o.Next.StartsWith(StepConditions.ResultPrefix, StringComparison.Ordinal))
                {
                    if (!resultIds.Contains(o.Next[StepConditions.ResultPrefix.Length..]))
                        issues.Add($"Frage '{q.Id}': Sprungziel-Ergebnis '{o.Next}' fehlt.");
                }
                else if (!byId.TryGetValue(o.Next, out var n)) issues.Add($"Frage '{q.Id}': Sprungziel '{o.Next}' fehlt.");
                else Visit(n, stack.Push(q.Id));
            }
        }

        if (quiz.Questions.Count > 0) Visit(0, ImmutableStack.Empty);
        foreach (var q in quiz.Questions.Where(q => !seen.Contains(q.Id)))
            issues.Add($"Frage '{q.Id}' ist nicht erreichbar.");

        for (var pct = 0; pct <= 100; pct += 1)
            if (!quiz.Results.Any(r => pct >= r.MinPct && pct <= r.MaxPct)) { issues.Add($"Kein Ergebnis deckt {pct} % ab."); break; }

        foreach (var id in quiz.Findings?.Priority ?? Array.Empty<string>())
            if (!byId.ContainsKey(id)) issues.Add($"Findings-Priorität nennt unbekannte Frage '{id}'.");

        return issues.Distinct().ToList();
    }

    private sealed class ImmutableStack
    {
        public static readonly ImmutableStack Empty = new(null, null);
        private readonly string? _head; private readonly ImmutableStack? _tail;
        private ImmutableStack(string? head, ImmutableStack? tail) { _head = head; _tail = tail; }
        public ImmutableStack Push(string id) => new(id, this);
        public bool Contains(string id) { for (var s = this; s?._head is not null; s = s._tail) if (s._head == id) return true; return false; }
    }
}
