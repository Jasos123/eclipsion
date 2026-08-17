using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.UI;

/// <summary>
///     Applies the instrument look to an existing control tree.
/// </summary>
/// <remarks>
///     KS14: fork-only. Upstream's console screens were written against this look from the
///         start, so every label and button carried its style class in XAML. This fork kept
///         its own screens and only took the appearance, so the ramp is applied over the top
///         instead of being baked into each screen's markup - that way a screen's structure,
///         control names and behaviour stay exactly as they were.
///     <para>
///         Cosmetic by construction: it only adds style classes and fills an *unset* font
///         colour. Anything that paints itself at runtime (a status readout tinting on
///         alarm) still wins, and controls created after this runs simply stay vanilla.
///     </para>
/// </remarks>
public static class KsInstrumentDressing
{
    /// <summary>
    ///     Recursively dresses <paramref name="root"/> and everything under it: buttons lose
    ///         the vanilla "button" class so the flat chrome box wins, labels take the
    ///         readout face and the palette's text colour.
    /// </summary>
    public static void Apply(Control root, KsInstrumentPalette palette)
    {
        foreach (var child in root.Children)
        {
            switch (child)
            {
                case Button button:
                    KsInstrumentSheetlet.MakeInstrument(button);
                    button.AddStyleClass(KsInstrumentSheetlet.StyleClassAction);
                    break;

                case Label label:
                    label.AddStyleClass(KsInstrumentSheetlet.StyleClassText);
                    label.FontColorOverride ??= palette.Text;
                    break;
            }

            Apply(child, palette);
        }
    }
}
