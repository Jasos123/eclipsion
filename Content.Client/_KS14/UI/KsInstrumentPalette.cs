using Robust.Shared.Maths;

namespace Content.Client._KS14.UI;

/// <summary>
///     Colour cast for one instrument screen: border/heading accent, background, and the
///         two text weights, plus the two state colours a readout can flip to.
/// </summary>
/// <remarks>
///     KS14: ported from Klovnstation14 (AGPL-3.0-or-later). Upstream drove these from a
///         <c>ksSensorHud</c> prototype so the sensor framework could re-cast each of its
///         tabs at runtime. This fork took the instrument *look* only, with none of the
///         sensor gameplay behind it, so the palettes are compiled-in constants instead of
///         a prototype - there is nothing left to hot-reload them for.
/// </remarks>
public sealed record KsInstrumentPalette(
    Color Accent,
    Color AccentDim,
    Color Background,
    Color Text,
    Color TextDim,
    Color Warning,
    Color Good)
{
    /// <summary>The window shell: bezel, title bar, status strip. Amber.</summary>
    public static readonly KsInstrumentPalette Shell = new(
        Accent: Color.FromHex("#C8A030"),
        AccentDim: Color.FromHex("#6A5518"),
        Background: Color.FromHex("#0C0A06"),
        Text: Color.FromHex("#E8C860"),
        TextDim: Color.FromHex("#8A7430"),
        Warning: Color.FromHex("#FF3030"),
        Good: Color.FromHex("#40FF80"));

    /// <summary>The nav/radar face. Green, so the plot reads as a separate instrument from the shell.</summary>
    public static readonly KsInstrumentPalette Nav = new(
        Accent: Color.FromHex("#38B048"),
        AccentDim: Color.FromHex("#1E5A28"),
        Background: Color.FromHex("#060A06"),
        Text: Color.FromHex("#70E080"),
        TextDim: Color.FromHex("#357040"),
        Warning: Color.FromHex("#FF3030"),
        Good: Color.FromHex("#40FF80"));

    /// <summary>The chart face. Amber, matching the shell.</summary>
    public static readonly KsInstrumentPalette Map = Shell;
}

/// <summary>
///     Window and button chrome, baked into the stylesheet at client start.
/// </summary>
/// <remarks>
///     KS14: separate from <see cref="KsInstrumentPalette"/> because these are consumed once,
///         by <see cref="KsInstrumentSheetlet.GetRules"/>, when StyleNano is built - editing
///         them needs a client restart, where a screen palette takes effect on the next draw.
/// </remarks>
public static class KsInstrumentChrome
{
    public static readonly Color WindowBackground = Color.FromHex("#0C0A06");
    public static readonly Color WindowBorder = Color.FromHex("#C8A030");

    public static readonly Color TitleBackground = Color.FromHex("#141008");
    public static readonly Color TitleText = Color.FromHex("#E8C860");

    // Four tiers per button, each a step brighter than the last: idle, hover, held down, and the
    // click flash. Hover used to borrow the held-down box, which left a click with nothing to
    // show for itself - the button looked identical the whole way through.
    public static readonly Color TabBackground = Color.FromHex("#141008");
    public static readonly Color TabBorder = Color.FromHex("#6A5518");
    public static readonly Color TabText = Color.FromHex("#8A7430");
    public static readonly Color TabHoverBackground = Color.FromHex("#1E1806");
    public static readonly Color TabHoverBorder = Color.FromHex("#8A7430");
    public static readonly Color TabHoverText = Color.FromHex("#C8A030");
    public static readonly Color TabPressedBackground = Color.FromHex("#2A2008");
    public static readonly Color TabPressedBorder = Color.FromHex("#C8A030");
    public static readonly Color TabPressedText = Color.FromHex("#FFD860");
    public static readonly Color TabDisabledBackground = Color.FromHex("#0E0B05");
    public static readonly Color TabDisabledBorder = Color.FromHex("#3A2E0C");
    public static readonly Color TabDisabledText = Color.FromHex("#4A3A10");

    public static readonly Color ActionBackground = Color.FromHex("#100D06");
    public static readonly Color ActionBorder = Color.FromHex("#6A5518");
    public static readonly Color ActionText = Color.FromHex("#C8A030");
    public static readonly Color ActionHoverBackground = Color.FromHex("#191305");
    public static readonly Color ActionHoverBorder = Color.FromHex("#8A7430");
    public static readonly Color ActionPressedBackground = Color.FromHex("#241C08");
    public static readonly Color ActionPressedBorder = Color.FromHex("#C8A030");
    public static readonly Color ActionPressedText = Color.FromHex("#FFD860");
    public static readonly Color ActionDisabledBackground = Color.FromHex("#0B0904");
    public static readonly Color ActionDisabledBorder = Color.FromHex("#352A0C");
    public static readonly Color ActionDisabledText = Color.FromHex("#4A3A10");

    /// <summary>
    ///     The click flash: brighter than any held-down state, so a click reads even on a button
    ///         that was already lit - a tab that is currently selected, or one being re-pressed.
    /// </summary>
    public static readonly Color FlashBackground = Color.FromHex("#5A4610");
    public static readonly Color FlashBorder = Color.FromHex("#FFD860");
}
