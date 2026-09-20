using System.Text.RegularExpressions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #17, the page side. Nothing renders <c>Stats.razor</c> in this test host, so this reads the
/// source, like <see cref="OverallStatsPageGuardTests"/>. It catches the regression - the period label
/// going back to a hardcoded "14 Tage", or the selector no longer feeding the chosen period into both
/// stats calls - but it cannot prove the period renders or that it survives the switch between the
/// overview and a single form (AC 3/5): only <c>/pairmode:acceptance</c> does, driving the running admin.
/// These guards are structural; per test-conventions their mutations are re-run once the markup exists.
/// </summary>
public class StatsPeriodPageGuardTests
{
    private static string Stats() => AdminMarkup.Read("Pages", "Stats.razor");

    [Fact]
    public void GivenTheStatsPage_WhenInspectingThePeriodLabels_ThenTheFixedFourteenDayTextIsGone()
    {
        var markup = Stats();

        // AC 3: the period is named from the chosen window, never hardcoded to fourteen days.
        Assert.DoesNotContain("Zeitraum Verlauf: letzte 14 Tage", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("(14 Tage)", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("der letzten 14 Tage", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenTheStatsPage_WhenInspectingTheFilterBar_ThenAPeriodSelectorOffersTheAvailablePeriods()
    {
        var markup = Stats();

        // AC 1/2: the filter bar offers the periods the server says are available for the current retention.
        Assert.Contains("AvailablePeriods", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenTheStatsPage_WhenInspectingTheStatsCalls_ThenBothPassTheChosenPeriod()
    {
        var markup = Stats();

        // AC 1/5: the chosen period reaches both the single-form and the cross-form call, so switching
        // form keeps the window. The field survives the switch because the page holds it in state.
        Assert.Matches(new Regex(@"GetStatsAsync\([^)]*_period", RegexOptions.Singleline), markup);
        Assert.Matches(new Regex(@"GetOverallStatsAsync\([^)]*_period", RegexOptions.Singleline), markup);
    }
}
