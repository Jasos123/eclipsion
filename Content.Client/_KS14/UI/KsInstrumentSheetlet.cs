using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._KS14.UI;

/// <summary>
///     Type ramp and chrome for the instrument shell: the VT323 face, the four label
///         weights every screen uses, and the flat bordered tab/action buttons.
/// </summary>
/// <remarks>
///     KS14: ported from Klovnstation14 (AGPL-3.0-or-later), visuals only. Two changes from
///         upstream, both forced by this fork's older base:
///     <list type="bullet">
///         <item>Upstream shipped this as a <c>Sheetlet&lt;PalettedStylesheet&gt;</c> discovered by
///             the Redux stylesheet manager. This fork is still on the legacy
///             <see cref="Content.Client.Stylesheets.StyleNano"/> monolith, which has no sheetlet
///             discovery, so the rules are a plain static <see cref="GetRules"/> that StyleNano
///             concatenates. The rules themselves are a 1:1 translation into Robust's
///             <c>Element()</c> selector builder.</item>
///         <item>Chrome colours were read from a prototype; here they are the compiled-in
///             <see cref="KsInstrumentChrome"/>, since the prototype existed to serve sensor
///             gameplay this fork did not take.</item>
///     </list>
/// </remarks>
public static class KsInstrumentSheetlet
{
    /// <summary>
    ///     The instrument face: VT323 (OFL), a single-weight CRT-terminal pixel monospace.
    ///         Every manual-draw control in the shell resolves the same path/sizes so the
    ///         whole shell re-fonts from this one spot. VT323 has no bold cut: emphasis is
    ///         size and palette colour, never a weight change.
    /// </summary>
    public const string FontPath = "/Fonts/_KS14/VT323/VT323-Regular.ttf";

    /// <summary>Body size: readouts, panel titles, plot labels.</summary>
    public const int FontSizeBody = 13;

    /// <summary>Fine-print size: log rows, hints, the status strip.</summary>
    public const int FontSizeSmall = 10;

    /// <summary>Tab-strip and title-bar size.</summary>
    public const int FontSizeTab = 15;

    /// <summary>Standard readout text (VT323 13).</summary>
    public const string StyleClassText = "KsInstrumentText";

    /// <summary>Panel headings and emphasized values (VT323 13; emphasis is colour, not weight).</summary>
    public const string StyleClassStrong = "KsInstrumentStrong";

    /// <summary>Fine print: log rows, hints, status strip (VT323 10).</summary>
    public const string StyleClassSmall = "KsInstrumentSmall";

    /// <summary>The window title-bar text (VT323 15).</summary>
    public const string StyleClassTitle = "KsInstrumentTitle";

    /// <summary>Shell tab buttons (NAV // MAP // ...).</summary>
    public const string StyleClassTab = "KsInstrumentTab";

    /// <summary>Screen action buttons and roster rows.</summary>
    public const string StyleClassAction = "KsInstrumentAction";

    /// <summary>
    ///     Strips the vanilla "button" style class a stock <see cref="Button"/> adds
    ///         in its constructor. The NanoTrasen button rules (textured box,
    ///         per-state tints, faded disabled label) select on that class at the same
    ///         or higher specificity as the instrument rules, so a chrome button
    ///         wearing both classes renders vanilla. Must be called on every button
    ///         that wears <see cref="StyleClassTab"/> or <see cref="StyleClassAction"/>.
    /// </summary>
    /// <remarks>
    ///     Also arms the click flash (<see cref="KsInstrumentPress"/>). It rides along here
    ///         because this is the one call every instrument button already passes through -
    ///         XAML ones through their screen's setup, runtime rows through
    ///         <see cref="KsInstrumentDressing"/> - so no screen can add a button that silently
    ///         goes without acknowledgement.
    /// </remarks>
    public static void MakeInstrument(params ContainerButton[] buttons)
    {
        foreach (var button in buttons)
            button.RemoveStyleClass(ContainerButton.StyleClassButton);

        KsInstrumentPress.Attach(buttons);
    }

    private static StyleBoxFlat ChromeBox(Color background, Color border, int marginH, int marginV)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = marginH,
            ContentMarginRightOverride = marginH,
            ContentMarginTopOverride = marginV,
            ContentMarginBottomOverride = marginV,
        };
    }

    /// <summary>
    ///     Label-under-button rule. Robust's builder has no <c>ParentOf</c>, so the
    ///         <see cref="SelectorChild"/> is assembled by hand from a button selector
    ///         and a bare <see cref="Label"/> selector.
    /// </summary>
    private static MutableSelectorChild ButtonLabel(string styleClass, params string[] pseudo)
    {
        var button = Element<ContainerButton>().Class(styleClass);
        if (pseudo.Length > 0)
            button = button.Pseudo(pseudo);

        return Child().Parent(button).Child(Element<Label>());
    }

    public static StyleRule[] GetRules(IResourceCache resCache)
    {
        var small = resCache.GetFont(FontPath, size: FontSizeSmall);
        var body = resCache.GetFont(FontPath, size: FontSizeBody);
        var tab = resCache.GetFont(FontPath, size: FontSizeTab);

        var tabBox = ChromeBox(KsInstrumentChrome.TabBackground, KsInstrumentChrome.TabBorder, 14, 4);
        var tabBoxHover = ChromeBox(KsInstrumentChrome.TabHoverBackground, KsInstrumentChrome.TabHoverBorder, 14, 4);
        var tabBoxPressed = ChromeBox(KsInstrumentChrome.TabPressedBackground, KsInstrumentChrome.TabPressedBorder, 14, 4);
        var tabBoxDisabled = ChromeBox(KsInstrumentChrome.TabDisabledBackground, KsInstrumentChrome.TabDisabledBorder, 14, 4);

        var actionBox = ChromeBox(KsInstrumentChrome.ActionBackground, KsInstrumentChrome.ActionBorder, 8, 2);
        var actionBoxHover = ChromeBox(KsInstrumentChrome.ActionHoverBackground, KsInstrumentChrome.ActionHoverBorder, 8, 2);
        var actionBoxPressed = ChromeBox(KsInstrumentChrome.ActionPressedBackground, KsInstrumentChrome.ActionPressedBorder, 8, 2);
        var actionBoxDisabled = ChromeBox(KsInstrumentChrome.ActionDisabledBackground, KsInstrumentChrome.ActionDisabledBorder, 8, 2);

        return
        [
            Element<Label>().Class(StyleClassText).Prop(Label.StylePropertyFont, body),
            Element<Label>().Class(StyleClassStrong).Prop(Label.StylePropertyFont, body),
            Element<Label>().Class(StyleClassSmall).Prop(Label.StylePropertyFont, small),
            Element<Label>().Class(StyleClassTitle).Prop(Label.StylePropertyFont, tab),

            // Tab brightness tiers: pressed > hover > unpressed > disabled. The draw modes are
            // exclusive in ContainerButton, so each of these is the button's whole appearance in
            // that state - and hover has to stay dimmer than pressed, or holding the mouse down
            // changes nothing and the tab never looks clicked.
            Element<ContainerButton>().Class(StyleClassTab)
                .Prop(ContainerButton.StylePropertyStyleBox, tabBox),
            Element<ContainerButton>().Class(StyleClassTab).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(ContainerButton.StylePropertyStyleBox, tabBoxPressed),
            Element<ContainerButton>().Class(StyleClassTab).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(ContainerButton.StylePropertyStyleBox, tabBoxHover),
            Element<ContainerButton>().Class(StyleClassTab).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(ContainerButton.StylePropertyStyleBox, tabBoxDisabled),
            ButtonLabel(StyleClassTab).Prop(Label.StylePropertyFont, tab),
            ButtonLabel(StyleClassTab).Prop(Label.StylePropertyFontColor, KsInstrumentChrome.TabText),
            ButtonLabel(StyleClassTab, ContainerButton.StylePseudoClassHover)
                .Prop(Label.StylePropertyFontColor, KsInstrumentChrome.TabHoverText),
            ButtonLabel(StyleClassTab, ContainerButton.StylePseudoClassPressed)
                .Prop(Label.StylePropertyFontColor, KsInstrumentChrome.TabPressedText),
            ButtonLabel(StyleClassTab, ContainerButton.StylePseudoClassDisabled)
                .Prop(Label.StylePropertyFontColor, KsInstrumentChrome.TabDisabledText),

            Element<ContainerButton>().Class(StyleClassAction)
                .Prop(ContainerButton.StylePropertyStyleBox, actionBox),
            Element<ContainerButton>().Class(StyleClassAction).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(ContainerButton.StylePropertyStyleBox, actionBoxPressed),
            Element<ContainerButton>().Class(StyleClassAction).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(ContainerButton.StylePropertyStyleBox, actionBoxHover),
            Element<ContainerButton>().Class(StyleClassAction).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(ContainerButton.StylePropertyStyleBox, actionBoxDisabled),
            ButtonLabel(StyleClassAction).Prop(Label.StylePropertyFont, body),
            ButtonLabel(StyleClassAction).Prop(Label.StylePropertyFontColor, KsInstrumentChrome.ActionText),
            ButtonLabel(StyleClassAction, ContainerButton.StylePseudoClassPressed)
                .Prop(Label.StylePropertyFontColor, KsInstrumentChrome.ActionPressedText),
            ButtonLabel(StyleClassAction, ContainerButton.StylePseudoClassDisabled)
                .Prop(Label.StylePropertyFontColor, KsInstrumentChrome.ActionDisabledText),
        ];
    }
}
