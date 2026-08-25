using Robust.Client.UserInterface.Controls;

namespace Content.Client._Crescent.UI;

/// <summary>
///     A console box that shows a value the server owns and that the operator also types into.
/// </summary>
/// <remarks>
///     <para>
///     Consoles push their state on a timer - three seconds for payroll and taxation - so anything
///     half-entered is racing the next refresh. Reusing the row rather than rebuilding it, and skipping
///     the write while the box holds keyboard focus, is not enough on its own: focus is dropped by
///     anything else the player touches, and by things they don't - switching mainframe tabs hides the
///     panel, and a hidden control is taken out of the tree, which releases focus with it. The refresh
///     that lands a moment later then wipes the figure back to the server's value. Typing a salary and
///     watching it snap to 0 two seconds later is that.
///     </para>
///     <para>
///     So a typed value is kept until it is dealt with: submitted with <see cref="SetSubmitted"/>,
///     dropped with <see cref="ClearEdited"/>, or abandoned by emptying the box. While it is pending the
///     box is tinted, because a figure that has not been sent yet looks exactly like one that has, and
///     an operator who walks away from a tinted box can at least see the console is not showing them the
///     live value. Boxes nobody has touched keep updating on every refresh as before.
///     </para>
/// </remarks>
public sealed class ConsoleValueEdit : LineEdit
{
    private bool _edited;

    /// <summary>Colour the box takes while it holds an unsubmitted figure.</summary>
    private static readonly Color PendingColor = ConsolePalette.Warn;

    private Color? _idleModulate;

    public ConsoleValueEdit()
    {
        // Only fires for edits the player makes; assigning Text does not raise it, so writing the
        // console's own value back in never marks the box as pending. Whitespace does not count as an
        // edit either, or a stray space would pin the box for the rest of the round.
        OnTextChanged += args => SetEdited(!string.IsNullOrWhiteSpace(args.Text));
    }

    /// <summary>Whether the box holds something the operator typed and has not submitted yet.</summary>
    public bool Edited => _edited;

    /// <summary>
    ///     Tint for the box when there is nothing pending in it - rows use it to mark a value as
    ///     deliberately overridden. Set this rather than <see cref="Control.ModulateSelfOverride"/>,
    ///     which the pending tint owns.
    /// </summary>
    public Color? IdleModulate
    {
        get => _idleModulate;
        set
        {
            _idleModulate = value;
            UpdateModulate();
        }
    }

    /// <summary>
    ///     Shows the value the console reported, unless the operator is typing in the box or has left an
    ///     unsubmitted figure in it.
    /// </summary>
    public void SetValue(string text)
    {
        if (_edited || HasKeyboardFocus() || Text == text)
            return;

        Text = text;
    }

    /// <summary>
    ///     Puts the box back in step with the console: shows <paramref name="text"/> and drops the pending
    ///     edit. Call it once what was typed has been sent.
    /// </summary>
    public void SetSubmitted(string text)
    {
        SetEdited(false);

        if (Text != text)
            Text = text;
    }

    /// <summary>
    ///     Stops treating what is in the box as a pending edit, without touching the text. The next
    ///     refresh takes the box back over.
    /// </summary>
    public void ClearEdited()
    {
        SetEdited(false);
    }

    private void SetEdited(bool edited)
    {
        if (_edited == edited)
            return;

        _edited = edited;
        UpdateModulate();
    }

    private void UpdateModulate()
    {
        ModulateSelfOverride = _edited ? PendingColor : _idleModulate;
    }
}
