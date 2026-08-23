using NetArchTest.Rules;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Schichtenregeln des Solution Standards als Test (§ ArchTests): Abhängigkeiten zeigen nur nach innen.
/// Functions ist hier bewusst nicht geladen (Worker-SDK im Testhost) – seine Regeln sichert der Build über Projektreferenzen.
/// </summary>
public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(Domain.Forms.FormDefinition).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(Application.ApplicationMarker).Assembly;
    private static readonly System.Reflection.Assembly Data = typeof(Data.DataServiceExtensions).Assembly;

    [Fact]
    public void Domain_haengt_von_nichts_ab()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot().HaveDependencyOnAny("Entriqa.Application", "Entriqa.Data", "Entriqa.Infrastructure", "Entriqa.Functions")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Application_kennt_weder_Data_noch_Infrastructure()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot().HaveDependencyOnAny("Entriqa.Data", "Entriqa.Infrastructure", "Entriqa.Functions")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Data_kennt_Infrastructure_nicht()
    {
        var result = Types.InAssembly(Data)
            .ShouldNot().HaveDependencyOnAny("Entriqa.Infrastructure", "Entriqa.Functions")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Entities_verlassen_die_Data_Schicht_nicht()
    {
        var result = Types.InAssembly(Data)
            .That().ResideInNamespace("Entriqa.Data.Entities")
            .Should().NotBePublic()
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }
}
