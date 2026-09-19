namespace Entriqa.Admin.Services;

/// <summary>
/// The accessibility attributes an icon carries. A standalone icon - one with no accompanying text -
/// always needs a screen-reader label and a tooltip (#18, AC 4); an icon that merely decorates a
/// labelled action is redundant to a screen reader and is hidden from it instead.
/// </summary>
public sealed record IconRendering(bool AriaHidden, string? AriaLabel, string? Title);

/// <summary>
/// Decides how an icon presents itself to assistive technology from the one thing the caller knows:
/// whether it passed a label. A label means the icon stands alone and speaks for the action; its
/// absence means text alongside already does, so the icon is decorative.
/// </summary>
public static class IconAttributes
{
    public static IconRendering For(string? label) => throw new NotImplementedException();
}
