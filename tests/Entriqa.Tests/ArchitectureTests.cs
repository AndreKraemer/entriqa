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

    [Fact]
    public void GivenInfrastructureAssembly_WhenInspectingDependencies_ThenDataIsNotReferenced()
    {
        // The mirror of the rule above. Only the csproj held this direction; nothing asserted it.
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot().HaveDependencyOnAny("Entriqa.Data", "Entriqa.Functions")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    /// <summary>
    /// The admin is a UI project and may see the Domain only (Solution Standard section 8.1). It is
    /// asserted on the project file rather than the assembly: Entriqa.Admin is a Blazor WASM app and
    /// does not load in this test host. Before the projects moved it sat outside the solution and
    /// reached the domain through ..\..pi\src\...; now it is a sibling of Core under src/, so
    /// ..\..\Core\Entriqa.Application\... is one line away - and nothing enforced the boundary.
    /// </summary>
    [Fact]
    public void GivenTheAdminProject_WhenInspectingItsReferences_ThenOnlyTheDomainIsReferenced()
    {
        var csproj = Path.Combine(RepoRoot(), "src", "Ui", "Entriqa.Admin", "Entriqa.Admin.csproj");
        Assert.True(File.Exists(csproj), $"admin project not found at {csproj}");
        var referenced = System.Text.RegularExpressions.Regex
            .Matches(File.ReadAllText(csproj), @"<ProjectReference[^>]*Include=""[^""]*[\\/](Entriqa\.[A-Za-z]+)\.csproj""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        Assert.Equal(new[] { "Entriqa.Domain" }, referenced.OrderBy(x => x));
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
