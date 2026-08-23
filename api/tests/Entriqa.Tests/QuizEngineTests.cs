using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Quiz;
using Xunit;

namespace Entriqa.Tests;

public class QuizEngineTests
{
    [Fact]
    public void Follows_branch_and_normalizes_on_path_maximum()
    {
        // q1=a (0) → q1b=c (2) → q2=b (2): 4 von max 2+2+2 = 6 → 67 % → Mitte
        var outcome = QuizEngine.Evaluate(TestData.Quiz(), new Dictionary<string, string> { ["q1"] = "a", ["q1b"] = "c", ["q2"] = "b", ["ignored"] = "x" });

        Assert.Equal(new[] { "q1", "q1b", "q2" }, outcome.Path);
        Assert.Equal(4, outcome.Points);
        Assert.Equal(6, outcome.MaxPoints);
        Assert.Equal(67, outcome.Pct);
        Assert.Equal("mitte", outcome.ResultId);
        Assert.False(outcome.ReachedByJump);
        Assert.DoesNotContain("ignored", outcome.Answers.Keys);
    }

    [Fact]
    public void Skipped_question_does_not_count_towards_maximum()
    {
        // q1=c (2) → q2=b (2): 4 von 4 = 100 % → Modern (q1b nie gesehen)
        var outcome = QuizEngine.Evaluate(TestData.Quiz(), new Dictionary<string, string> { ["q1"] = "c", ["q2"] = "b", ["q1b"] = "c" });

        Assert.Equal(100, outcome.Pct);
        Assert.Equal("modern", outcome.ResultId);
        Assert.Equal(2, outcome.Path.Count);
    }

    [Fact]
    public void Jump_to_result_wins_over_points()
    {
        var outcome = QuizEngine.Evaluate(TestData.Quiz(), new Dictionary<string, string> { ["q1"] = "a", ["q1b"] = "a" });

        Assert.Equal("legacy", outcome.ResultId);
        Assert.True(outcome.ReachedByJump);
        Assert.Equal(new[] { "q1", "q1b" }, outcome.Path);
    }

    [Fact]
    public void Missing_answer_on_path_is_validation_error()
    {
        var ex = Assert.Throws<ValidationException>(() => QuizEngine.Evaluate(TestData.Quiz(), new Dictionary<string, string> { ["q1"] = "a" }));
        Assert.Equal("q1b", ex.Errors[0].Field);
    }

    [Fact]
    public void Check_detects_loops_unreachable_and_gaps()
    {
        var quiz = TestData.Quiz() with
        {
            Questions = new[]
            {
                new QuizQuestion("q1", "1", new[] { new QuizOption("a", "A", 0, Next: "q2") }),
                new QuizQuestion("q2", "2", new[] { new QuizOption("a", "A", 0, Next: "q1") }),
                new QuizQuestion("q3", "3", new[] { new QuizOption("a", "A", 0, Next: "result:nope") }),
            },
            Results = new[] { new QuizResult("x", 0, 50, "X", "") },
        };

        var issues = QuizEngine.Check(quiz);

        Assert.Contains(issues, i => i.Contains("Schleife"));
        Assert.Contains(issues, i => i.Contains("q3") && i.Contains("nicht erreichbar"));
        Assert.Contains(issues, i => i.Contains("51 %"));
    }

    [Fact]
    public void Check_passes_for_valid_quiz() => Assert.Empty(QuizEngine.Check(TestData.Quiz()));
}
