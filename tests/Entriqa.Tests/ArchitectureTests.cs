using NetArchTest.Rules;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The Solution Standard's layering rules as a test (§ ArchTests): dependencies only ever point inwards.
/// Functions is deliberately not loaded here (worker SDK in the test host) - its rules are enforced by the build via project references.
/// </summary>
public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(Domain.Forms.FormDefinition).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(Application.ApplicationMarker).Assembly;
    private static readonly System.Reflection.Assembly Data = typeof(Data.DataServiceExtensions).Assembly;
    private static readonly System.Reflection.Assembly Infrastructure = typeof(Infrastructure.InfrastructureServiceExtensions).Assembly;

    [Fact]
    public void GivenDomainAssembly_WhenInspectingDependencies_ThenItDependsOnNoOtherLayer()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot().HaveDependencyOnAny("Entriqa.Application", "Entriqa.Data", "Entriqa.Infrastructure", "Entriqa.Functions")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void GivenApplicationAssembly_WhenInspectingDependencies_ThenNeitherDataNorInfrastructureIsReferenced()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot().HaveDependencyOnAny("Entriqa.Data", "Entriqa.Infrastructure", "Entriqa.Functions")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void GivenDataAssembly_WhenInspectingDependencies_ThenInfrastructureIsNotReferenced()
    {
        var result = Types.InAssembly(Data)
            .ShouldNot().HaveDependencyOnAny("Entriqa.Infrastructure", "Entriqa.Functions")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    /// <summary>
    /// IL-level, and that is the limit of it: NetArchTest sees a dependency only once a type is
    /// actually used, so an unused ProjectReference passes here. The project-file rules below are
    /// what catch that.
    /// </summary>
    [Fact]
    public void GivenInfrastructureAssembly_WhenInspectingDependencies_ThenNoDataTypeIsUsed()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot().HaveDependencyOnAny("Entriqa.Data", "Entriqa.Functions")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    /// <summary>
    /// The admin is a UI project and may see the Domain only (Solution Standard section 8.1).
    /// Before the projects moved it sat outside the solution; now it is a sibling of Core under
    /// src/, so a reference to Application is one line away - and nothing enforced the boundary.
    /// </summary>
    [Fact]
    public void GivenTheAdminProject_WhenInspectingItsReferences_ThenOnlyTheDomainIsReferenced() =>
        AssertProjectReferences(Path.Combine("src", "Ui", "Entriqa.Admin", "Entriqa.Admin.csproj"), "Entriqa.Domain");

    [Fact]
    public void GivenTheInfrastructureProject_WhenInspectingItsReferences_ThenOnlyApplicationIsReferenced() =>
        AssertProjectReferences(Path.Combine("src", "Core", "Entriqa.Infrastructure", "Entriqa.Infrastructure.csproj"), "Entriqa.Application");

    /// <summary>
    /// Reads the project file rather than the assembly. That catches a reference which has been
    /// added but not used yet - invisible to the IL rules above - and it works for Entriqa.Admin,
    /// whose WASM assembly does not load in this test host. The element count is asserted too, so
    /// a reference written in a shape the pattern does not match fails loudly instead of silently
    /// dropping out of the set.
    /// </summary>
    private static void AssertProjectReferences(string relativeCsproj, params string[] expected)
    {
        var csproj = Path.Combine(RepoRoot(), relativeCsproj);
        Assert.True(File.Exists(csproj), $"project not found at {csproj}");
        var text = File.ReadAllText(csproj);
        var elements = System.Text.RegularExpressions.Regex.Count(text, @"<(?:Project)?Reference\b");
        var referenced = System.Text.RegularExpressions.Regex
            .Matches(text, @"<(?:Project)?Reference[^>]*Include=""[^""]*[\\/](Entriqa\.[\w.]+)\.csproj""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        Assert.Equal(elements, referenced.Count);
        Assert.Equal(expected.OrderBy(x => x), referenced.OrderBy(x => x));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Entriqa.slnx"))) return dir.FullName;
        throw new DirectoryNotFoundException($"Entriqa.slnx not found above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void GivenEntityTypes_WhenCheckingTheirVisibility_ThenTheyStayInsideTheDataLayer()
    {
        var result = Types.InAssembly(Data)
            .That().ResideInNamespace("Entriqa.Data.Entities")
            .Should().NotBePublic()
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }
}
