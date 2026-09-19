using Entriqa.Admin.Services;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// #18, AC 4: an icon that stands alone must announce itself to a screen reader and show a tooltip;
/// an icon beside its own label must not, or the label is read twice. The decision is a plain
/// function of whether a label was given, so it lives outside the component and executes here.
/// </summary>
public class IconAttributesTests
{
    [Fact]
    public void GivenAStandaloneIconWithALabel_WhenComputingItsAccessibility_ThenItCarriesAScreenReaderLabelAndTooltip()
    {
        var a = IconAttributes.For("Entfernen");

        Assert.False(a.AriaHidden);
        Assert.Equal("Entfernen", a.AriaLabel);
        Assert.Equal("Entfernen", a.Title);
    }

    [Fact]
    public void GivenAnIconThatAccompaniesItsOwnText_WhenComputingItsAccessibility_ThenItIsDecorativeAndHiddenFromScreenReaders()
    {
        var a = IconAttributes.For(null);

        Assert.True(a.AriaHidden);
        Assert.Null(a.AriaLabel);
        Assert.Null(a.Title);
    }
}
