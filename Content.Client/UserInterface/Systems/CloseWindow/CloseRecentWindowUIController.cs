using Content.Client.Gameplay;
using Content.Client.Info;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;

namespace Content.Client.UserInterface.Systems.Info;

public sealed class CloseRecentWindowUIController : UIController
{
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;

    /// <summary>
    /// A list of windows that have been interacted with recently.  Windows should only
    /// be in this list once, with the most recent window at the end, and the oldest
    /// window at the start.
    /// </summary>
    List<BaseWindow> recentlyInteractedWindows = new List<BaseWindow>();

    public override void Initialize()
    {
        // Add listeners to be able to know when windows are opened.
        // (Does not need to be unlistened since UIControllers live forever)
        _uiManager.OnKeyBindDown += OnKeyBindDown;
        _uiManager.WindowRoot.OnChildAdded += OnRootChildAdded;

        _inputManager.SetInputCommand(EngineKeyFunctions.WindowCloseRecent,
            InputCmdHandler.FromDelegate(session => CloseMostRecentWindow()));
    }

    /// <summary>
    /// Closes the most recently focused window.
    /// </summary>
    public void CloseMostRecentWindow()
    {
        if (GetClosableWindow() is not { } window)
            return;

        recentlyInteractedWindows.Remove(window);
        window.Close();
    }

    private void OnKeyBindDown(Control control)
    {
        // On click, we should set the window that owns this control (if any) to the most recently
        // clicked window.  By doing this, we can create an ordering of what windows have been
        // interacted with.

        // Something was clicked, so find the window corresponding to what was clicked
        var window = GetWindowForControl(control);

        // Find the window owning the control
        if (window != null)
        {
            // And move to top of recent stack
            //Logger.Debug("Most recent window is " + window.Name);
            SetMostRecentlyInteractedWindow(window);
        }
    }

    /// <summary>
    /// Sets the window as the one most recently interacted with.  This function will update the
    /// internal recentlyInteractedWindows tracking.
    /// </summary>
    /// <param name="window"></param>
    public void SetMostRecentlyInteractedWindow(BaseWindow window)
    {
        // Search through the list and see if already added.
        // (This search is backwards since it's fairly common that the user is clicking the same
        // window multiple times in a row, and so that saves a tiny bit of perf doing it this way)
        for (int i=recentlyInteractedWindows.Count-1; i>=0; i--)
        {
            if (recentlyInteractedWindows[i] == window)
            {
                // Window already in the list

                // Is window the top most recent entry?
                if (i == recentlyInteractedWindows.Count-1)
                    return; // Then there's nothing to do, it's already in the right spot
                else
                {
                    // Need to remove the old entry so it can be readded (no duplicates in list allowed)
                    recentlyInteractedWindows.RemoveAt(i);
                    break;
                }
            }
        }

        // Now that the list has been checked for duplicates, okay to add new window at end of tracking
        recentlyInteractedWindows.Add(window);
    }

    private BaseWindow? GetWindowForControl(Control? control)
    {
        if (control == null)
            return null;

        if (control is BaseWindow)
            return (BaseWindow) control;

        // Go up the hierarchy until we find a window (or don't)
        return GetWindowForControl(control.Parent);
    }

    private void OnRootChildAdded(Control control)
    {
        if (control is BaseWindow)
        {
            // On new window open, add to tracking
            SetMostRecentlyInteractedWindow((BaseWindow) control);
        }
    }

    /// <summary>
    /// Checks whether there are any windows that can be closed.
    /// </summary>
    /// <returns></returns>
    public bool HasClosableWindow()
    {
        return GetClosableWindow() != null;
    }

    /// <summary>
    /// Picks the window Escape should close: the frontmost one the player can actually see.
    /// </summary>
    /// <remarks>
    /// The recency list on its own is not trustworthy for this. It is fed by
    /// <see cref="IUserInterfaceManager.OnKeyBindDown"/>, which fires for every bound key routed to
    /// the UI, and the engine routes keys nothing else claimed to whatever sits under the mouse. So
    /// resting the cursor over a window and pressing any key - Escape included - promoted that
    /// window, and Escape then closed something behind the window in view instead of the one on top.
    /// The window root draws its children back to front and both opening a window and clicking its
    /// frame move it last, so its child order is the ordering the player is looking at.
    /// </remarks>
    private BaseWindow? GetClosableWindow()
    {
        var root = _uiManager.WindowRoot;
        for (var i = root.ChildCount - 1; i >= 0; i--)
        {
            if (root.GetChild(i) is BaseWindow window && window.IsOpen && IsOnScreen(window))
                return window;
        }

        // Windows parented somewhere other than the window root are only known through interaction,
        // so fall back to the recency list for those.
        for (var i = recentlyInteractedWindows.Count - 1; i >= 0; i--)
        {
            var window = recentlyInteractedWindows[i];

            if (!window.IsOpen)
            {
                // Stale reference, drop it and keep looking.
                recentlyInteractedWindows.RemoveAt(i);
                continue;
            }

            // Still parented, so IsOpen is true, but the player cannot see it: nested storage hides
            // the outer bag, spectating a camera hides the console behind it. Closing one of these
            // looks exactly like Escape doing nothing, so skip it and leave it tracked for later.
            if (IsOnScreen(window))
                return window;
        }

        return null;
    }

    /// <summary>
    /// Whether the window is actually on screen for the player, and not merely parented.
    /// <see cref="BaseWindow.IsOpen"/> only checks parenting, so a window that was hidden rather
    /// than closed still counts as open.
    /// </summary>
    private static bool IsOnScreen(BaseWindow window)
    {
        return window.VisibleInTree;
    }
}
