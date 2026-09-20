using System.Text.RegularExpressions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// AC 5 of #6: an appointment is never deleted without an explicit confirmation. Nothing renders this
/// page in the test host, so - like <see cref="FormsListActionsGuardTests"/> - this reads the source.
/// It proves the delete handler is gated by a confirm and cannot prove the dialog actually blocks the
/// call in the browser; that half is acceptance-only. Written after the code, so its bite is checked by
/// the mutation named below rather than by a red first run.
/// </summary>
public class AppointmentsPageGuardTests
{
    private static string Page() => AdminMarkup.Read("Pages", "Appointments.razor");

    // Mutation to re-run: drop the "if (!await …confirm…) return;" line, or delete unconditionally - the
    // confirm then no longer precedes the delete and this fails.
    [Fact]
    public void GivenTheDeleteHandler_WhenReadingIt_ThenAnUnconfirmedConfirmReturnsBeforeAnythingIsDeleted()
    {
        var body = SourceText.Block(Page(), "private async Task Delete(AppointmentView a)",
            "Appointments.razor no longer has Delete(AppointmentView a) - update this guard.");

        var guard = Regex.Match(body, @"if\s*\(\s*!\s*await\s+Js\.InvokeAsync<bool>\(""confirm"".*?\)\s*\)\s*return;",
            RegexOptions.Singleline);
        Assert.True(guard.Success, "the delete handler must return early when the confirm is declined");

        var deleteAt = body.IndexOf("DeleteAppointmentAsync", StringComparison.Ordinal);
        Assert.True(deleteAt >= 0, "the delete handler must call DeleteAppointmentAsync");
        Assert.True(guard.Index < deleteAt, "the confirm gate must come before the delete call");
    }
}
