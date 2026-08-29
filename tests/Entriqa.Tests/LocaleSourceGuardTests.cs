using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Application.UseCases;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The two halves of #4 that no ordinary test can reach: the editor is a Razor component and the
/// startup warning lives in Entriqa.Functions, neither of which loads in the test host. The project
/// already guards such code by reading its sources (see <c>EditorChangedBindingTests</c>), which is
/// what these do - a weaker guard than executing the code, but far better than none, because both
/// defects are invisible from the outside: nothing crashes, the visitor just gets the wrong language.
/// </summary>
public class LocaleSourceGuardTests
{
    // AC4: the editor's language list must come from the API alone. A hardcoded locale or a union with
    // the form's own locales is exactly the pre-fix behaviour, and it also reintroduces AC5's second list.
    [Fact]
    public void GivenTheFormEditor_WhenReadingItsLanguageList_ThenItComesFromTheApiAlone()
    {
        var source = File.ReadAllText(Path.Combine(AdminDirectory(), "Pages", "FormEditor.razor"));
        var member = Regex.Match(source, @"IEnumerable<string>\s+SiteLocales\s*=>.*?;", RegexOptions.Singleline);
        Assert.True(member.Success, "FormEditor.razor no longer has a SiteLocales member - update this guard.");

        Assert.DoesNotMatch(@"""[a-z]{2}""", member.Value);       // no hardcoded locale
        Assert.DoesNotContain("Union", member.Value);              // not extended by the form's own locales
        Assert.Contains("_siteLocales", member.Value);             // the value the status API delivered
    }

    // AC2: the warning only reaches the operator if the host actually runs it. Anchored at the start of
    // a line, so commenting the registration out fails too - deletion is not the only way to lose it.
    [Fact]
    public void GivenTheFunctionsHost_WhenReadingItsComposition_ThenTheLocaleWarningIsRegistered()
    {
        var program = File.ReadAllText(Path.Combine(FunctionsDirectory(), "Program.cs"));
        Assert.Matches(new Regex(@"^\s*services\.AddHostedService<LocaleWarningHostedService>\(\);", RegexOptions.Multiline), program);
    }

    // AC2 in full: it asks for a *warning* that *names* the code. A debug line, or one that drops the
    // placeholder, would satisfy "something is logged" while failing the criterion as written.
    [Fact]
    public void GivenTheStartupWarning_WhenReadingIt_ThenItIsAWarningAndNamesTheCode()
    {
        var source = File.ReadAllText(Path.Combine(FunctionsDirectory(), "LocaleWarningHostedService.cs"));
        Assert.Contains("LogWarning", source);
        Assert.Contains("{Locale}", source);
    }

    // The reconciliation is what keeps the editor's four views of "which languages" in agreement;
    // dropping the call is invisible from the outside and from every executing test.
    [Fact]
    public void GivenTheFormEditor_WhenLoadingADefinition_ThenItReconcilesTheDeclaredLanguages()
    {
        var source = File.ReadAllText(Path.Combine(AdminDirectory(), "Pages", "FormEditor.razor"));
        var loadModel = Regex.Match(source, @"private void LoadModel\(string json\)\s*\{.*?\n    \}", RegexOptions.Singleline);
        Assert.True(loadModel.Success, "FormEditor.razor no longer has a LoadModel method - update this guard.");
        Assert.Contains("ReconcileLocales()", loadModel.Value);
    }

    // AC4, server half: the admin can only offer what the status hands it.
    [Fact]
    public async Task GivenAnUnsupportedCodeInTheConfiguration_WhenReadingTheAdminStatus_ThenItIsNotOffered()
    {
        var options = TestData.Options();
        options.Locales = "de,en,zz";
        var status = await new GetAdminStatusUseCase(
            Substitute.For<IBrevoDirectoryPort>(),
            Substitute.For<IGetHousekeepingRunQuery>(),
            new LicenseService(Options.Create(options), TestData.Time),
            Options.Create(options)).ExecuteAsync();

        Assert.Equal(new[] { "de", "en" }, status.Locales);
    }

    private static string RepositoryDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Ui", "Entriqa.Admin"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found above " + AppContext.BaseDirectory + ".");
    }

    private static string AdminDirectory() => Path.Combine(RepositoryDirectory(), "src", "Ui", "Entriqa.Admin");

    private static string FunctionsDirectory() => Path.Combine(RepositoryDirectory(), "src", "Hosts", "Entriqa.Functions");
}
