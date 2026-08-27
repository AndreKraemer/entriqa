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
    public void GivenEntityTypes_WhenCheckingTheirVisibility_ThenTheyStayInsideTheDataLayer()
    {
        var result = Types.InAssembly(Data)
            .That().ResideInNamespace("Entriqa.Data.Entities")
            .Should().NotBePublic()
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }
}
