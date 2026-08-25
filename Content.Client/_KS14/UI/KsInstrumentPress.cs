using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Client._KS14.UI;

/// <summary>
///     The instrument shell's click acknowledgement: a short bright flash on the button that was
///         just pressed.
/// </summary>
/// <remarks>
///     KS14: fork-only, not from upstream. The shell's buttons only changed appearance while the
///         mouse was physically held on them, and a real click is far shorter than that - so on a
///         quick press nothing on screen ever changed and the console read as unresponsive. The
///         flash outlives the click, which is the whole point of it.
///     <para>
///         Deliberately not gated on <c>accessibility.reduced_motion</c>, unlike the shell's boot
///         and tab blinks. Those are decoration; this is the only confirmation a momentary button
///         gives that it took the press at all, and it is a single step rather than a strobe.
///     </para>
/// </remarks>
public static class KsInstrumentPress
{
    /// <summary>How long the flash stays up, in milliseconds. Long enough to catch, short enough not to lag the button.</summary>
    private const int FlashMilliseconds = 110;

    /// <summary>
    ///     Marker class, matching no style rule. <see cref="Attach"/> runs from
    ///         <see cref="KsInstrumentSheetlet.MakeInstrument"/>, which a screen may re-dress more
    ///         than once, and this keeps a button from stacking up a handler per pass.
    /// </summary>
    private const string StyleClassFlashArmed = "KsInstrumentFlashArmed";

    /// <summary>
    ///     One flash box per chrome size. They exist separately only so the flash keeps the
    ///         button's content margins - a box with the other size's padding would resize the
    ///         button for the length of the flash and shove the panel around it.
    /// </summary>
    private static readonly StyleBoxFlat FlashBoxTab = FlashBox(14, 4);
    private static readonly StyleBoxFlat FlashBoxAction = FlashBox(8, 2);

    private static StyleBoxFlat FlashBox(int marginH, int marginV)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = KsInstrumentChrome.FlashBackground,
            BorderColor = KsInstrumentChrome.FlashBorder,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = marginH,
            ContentMarginRightOverride = marginH,
            ContentMarginTopOverride = marginV,
            ContentMarginBottomOverride = marginV,
        };
    }

    /// <summary>
    ///     Makes <paramref name="buttons"/> flash when pressed. Idempotent, and safe on a button
    ///         that already carries an <c>OnPressed</c> handler of its own - the flash is purely
    ///         additive and never consumes or reorders the press.
    /// </summary>
    public static void Attach(params ContainerButton[] buttons)
    {
        foreach (var button in buttons)
        {
            if (button.HasStyleClass(StyleClassFlashArmed))
                continue;

            button.AddStyleClass(StyleClassFlashArmed);
            button.OnPressed += _ => Flash(button);
        }
    }

    /// <summary>
    ///     Overrides the button's box for <see cref="FlashMilliseconds"/>, then hands it back to the
    ///         stylesheet.
    /// </summary>
    private static void Flash(ContainerButton button)
    {
        button.StyleBoxOverride = button.HasStyleClass(KsInstrumentSheetlet.StyleClassTab)
            ? FlashBoxTab
            : FlashBoxAction;

        Timer.Spawn(FlashMilliseconds, () =>
        {
            // The console can be closed - or a rebuilt list row disposed - inside the flash window.
            if (button.Disposed)
                return;

            button.StyleBoxOverride = null;
        });
    }
}
