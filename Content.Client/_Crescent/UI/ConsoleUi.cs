using Robust.Client.UserInterface.Controls;

namespace Content.Client._Crescent.UI;

/// <summary>
///     Shared semantic colours for console readouts, so "this is fine" and "this is a problem" look the same
///     on every machine instead of each console picking its own green.
/// </summary>
public static class ConsolePalette
{
    /// <summary>Working, funded, in range - the good case.</summary>
    public static readonly Color Good = Color.FromHex("#7DD87D");

    /// <summary>Needs attention but is not broken: unbound treasury, payroll over budget.</summary>
    public static readonly Color Warn = Color.FromHex("#E0A44C");

    /// <summary>Failed, denied or offline.</summary>
    public static readonly Color Bad = Color.FromHex("#D65C5C");

    /// <summary>Present but inactive - stale roster entries, disabled controls.</summary>
    public static readonly Color Muted = Color.FromHex("#7A7A85");

    /// <summary>An operator-set value overriding a default, so it reads as deliberate.</summary>
    public static readonly Color Override = Color.FromHex("#D9C05A");

    /// <summary>Ordinary readout text.</summary>
    public static readonly Color Normal = Color.FromHex("#DDDDE3");
}

public static class ConsoleUiExt
{
    /// <summary>
    ///     Sets a line edit's text unless the player is typing in it right now.
    /// </summary>
    /// <remarks>
    ///     Row reuse alone does not save an editable field: a console that refreshes every three seconds would
    ///     still overwrite a half-entered figure with the server's value. Skipping the write while the field
    ///     holds focus is what actually makes these boxes usable.
    /// </remarks>
    public static void SetTextIfIdle(this LineEdit edit, string text)
    {
        if (edit.HasKeyboardFocus() || edit.Text == text)
            return;

        edit.Text = text;
    }

    /// <summary>
    ///     Sets a label's text only when it actually changed - assigning it invalidates layout, which on a
    ///     shared window costs every other panel a relayout for nothing.
    /// </summary>
    public static void SetTextIfChanged(this Label label, string text)
    {
        if (label.Text == text)
            return;

        label.Text = text;
    }

    /// <summary>
    ///     Disables a button and says why. A greyed-out control with no explanation is a dead end - the player
    ///     cannot tell "you lack access" from "there is no money" from "the console is unpowered".
    /// </summary>
    /// <param name="reason">Localised explanation, shown as a tooltip. Cleared when enabled.</param>
    public static void SetDisabled(this Button button, bool disabled, string? reason = null)
    {
        button.Disabled = disabled;
        button.ToolTip = disabled ? reason : null;
    }

    /// <summary>
    ///     Case-insensitive "does this row survive the filter box" test. Empty filter matches everything.
    /// </summary>
    public static bool MatchesFilter(this string? haystack, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return haystack != null && haystack.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
