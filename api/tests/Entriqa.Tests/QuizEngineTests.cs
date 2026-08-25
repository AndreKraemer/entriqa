using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Quiz;
using Xunit;

namespace Entriqa.Tests;

public class QuizEngineTests
{
    [Fact]
    public void GivenAnsweredBranch_WhenEvaluating_ThenPointsAreNormalizedAgainstThePathMaximum()
    {
        // q1=a (0) -> q1b=c (2) -> q2=b (2): 4 out of max 2+2+2 = 6 -> 67 % -> "Mitte"
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
    public void GivenBranchSkipsAQuestion_WhenEvaluating_ThenTheSkippedQuestionDoesNotCountTowardsTheMaximum()
    {
        // q1=c (2) -> q2=b (2): 4 out of 4 = 100 % -> "Modern" (q1b never seen)
        var outcome = QuizEngine.Evaluate(TestData.Quiz(), new Dictionary<string, string> { ["q1"] = "c", ["q2"] = "b", ["q1b"] = "c" });

        Assert.Equal(100, outcome.Pct);
        Assert.Equal("modern", outcome.ResultId);
        Assert.Equal(2, outcome.Path.Count);
    }

    [Fact]
    public void GivenAnswerJumpingStraightToAResult_WhenEvaluating_ThenTheJumpWinsOverThePointScore()
    {
        var outcome = QuizEngine.Evaluate(TestData.Quiz(), new Dictionary<string, string> { ["q1"] = "a", ["q1b"] = "a" });

        Assert.Equal("legacy", outcome.ResultId);
        Assert.True(outcome.ReachedByJump);
        Assert.Equal(new[] { "q1", "q1b" }, outcome.Path);
    }

    [Fact]
    public void GivenAnswerMissingOnThePath_WhenEvaluating_ThenAValidationErrorNamesThatQuestion()
    {
        var ex = Assert.Throws<ValidationException>(() => QuizEngine.Evaluate(TestData.Quiz(), new Dictionary<string, string> { ["q1"] = "a" }));
        Assert.Equal("q1b", ex.Errors[0].Field);
    }

    [Fact]
    public void GivenQuizWithLoopUnreachableQuestionAndScoreGap_WhenChecking_ThenAllThreeAreReported()
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
    public void GivenValidQuiz_WhenChecking_ThenNoIssuesAreReported() => Assert.Empty(QuizEngine.Check(TestData.Quiz()));
}
