using System.Windows;

namespace ScratchpadLite.App.Bridge;

/// <summary>
/// Attached properties that hold the IR facts WPF elements have nowhere to
/// put (Section 6.4): the paragraph style name, the font name a document
/// asked for when a substitute is being shown, super and subscript
/// alignment with the font size before shrinking, the IR underline kind,
/// and the properties kept only so a round trip does not lose them.
/// </summary>
/// <remarks>Phase 4.</remarks>
internal static class SpProps
{
    public static readonly DependencyProperty StyleNameProperty =
        DependencyProperty.RegisterAttached(
            "StyleName", typeof(string), typeof(SpProps), new PropertyMetadata(default(string)));

    public static void SetStyleName(DependencyObject element, string? value) =>
        element.SetValue(StyleNameProperty, value);

    public static string? GetStyleName(DependencyObject element) =>
        (string?)element.GetValue(StyleNameProperty);

    // RequestedFont, VAlign, BaseSizePt, Underline and Preserved follow the
    // same shape; they are registered in Phase 4 alongside the bridge.
}
