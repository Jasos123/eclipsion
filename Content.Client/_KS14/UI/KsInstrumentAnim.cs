namespace Content.Client._KS14.UI;

/// <summary>
///     The instrument shell's two cosmetic flickers: the power-on blink when the console
///         opens, and the short interlace blink when the crew switches tabs.
/// </summary>
/// <remarks>
///     KS14: ported from Klovnstation14 (AGPL-3.0-or-later), visuals only. Upstream drove the
///         curves from a <c>ksSensorHud</c> prototype and gated them on its own
///         <c>klovn.hud.reduced_motion</c> cvar. Both are compiled in here instead, and the
///         gate is this fork's existing <c>accessibility.reduced_motion</c> so the console
///         obeys the accessibility setting the player already set rather than a second one.
/// </remarks>
public static class KsInstrumentAnim
{
    /// <summary>Seconds the power-on blink runs for when the window opens.</summary>
    public const double BootDuration = 0.7;

    /// <summary>Seconds the tab-switch blink runs for.</summary>
    public const double TabFlickerDuration = 0.12;

    private const float BootSpeed = 24f;
    private const float BootBase = 0.3f;
    private const float BootAmplitude = 0.7f;

    private const float TabSpeed = 30f;
    private const float TabBase = 0.4f;
    private const float TabAmplitude = 0.6f;

    /// <summary>
    ///     A square-wave blink: full for the first half of each cycle, base for the second.
    /// </summary>
    private static float Blink(double seconds, float speed, float baseValue, float amplitude)
    {
        var t = (float) seconds * speed;
        var frac = t - MathF.Floor(t);
        return baseValue + (frac < 0.5f ? amplitude : 0f);
    }

    /// <summary>
    ///     Brightness for the power-on blink at <paramref name="seconds"/> since the window
    ///         opened. The blink rides a rising envelope, so the shell strobes dark at first
    ///         and settles to full brightness as the boot completes.
    /// </summary>
    public static float BootBrightness(double seconds)
    {
        var progress = (float) (seconds / BootDuration);
        var flicker = Blink(seconds, BootSpeed, BootBase, BootAmplitude);
        return Math.Clamp(progress * float.Lerp(flicker, 1f, progress), 0f, 1f);
    }

    /// <summary>Brightness for the tab-switch blink at <paramref name="seconds"/> since the switch.</summary>
    public static float TabBrightness(double seconds)
        => Math.Clamp(Blink(seconds, TabSpeed, TabBase, TabAmplitude), 0f, 1f);
}
