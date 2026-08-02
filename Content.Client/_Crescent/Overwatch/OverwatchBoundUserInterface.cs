using Content.Client._Crescent.Mainframe;
using Content.Shared._Crescent.Mainframe;
using Content.Shared._Crescent.Overwatch;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._Crescent.Overwatch;

/// <summary>
/// BUI for the Overwatch console.
/// </summary>
public sealed class OverwatchBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;

    private OverwatchWindow? _window;
    private OverwatchPanel? _panel;
    private MainframeUiController? _mainframe;
    private OverwatchConsoleSystem? _consoleSystem;

    public OverwatchBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    /// <inheritdoc/>
    protected override void Open()
    {
        base.Open();

        _panel = new OverwatchPanel();
        _panel.Initialize(this);

        // On a mainframe this renders as a tab instead of its own window.
        if (UiSystem.HasUi(Owner, MainframeUiKey.Key))
        {
            _mainframe = _uiManager.GetUIController<MainframeUiController>();
            _mainframe.AttachPanel(Owner, MainframeTab.Overwatch, _panel, this);
        }
        else
        {
            _window = this.CreateWindow<OverwatchWindow>();
            _window.SetPanel(_panel);

            _window.OnClose += () =>
            {
                _window = null;
            };
        }

        // While spectating a camera the console window must get out of the way, or it just covers the
        // feed with itself — on smaller screens the mainframe shell fills the viewport entirely, which
        // reads as "clicking view does nothing". Restore it the moment spectating ends.
        _consoleSystem = EntMan.System<OverwatchConsoleSystem>();
        _consoleSystem.LocalWatchStateChanged += OnLocalWatchStateChanged;
    }

    private void OnLocalWatchStateChanged(bool watching)
    {
        SetHostVisible(!watching);
    }

    /// <summary>Shows or hides whichever surface hosts this console — its own window or the mainframe shell.</summary>
    private void SetHostVisible(bool visible)
    {
        if (_window != null)
            _window.Visible = visible;

        _mainframe?.SetShellVisible(Owner, visible);
    }

    /// <inheritdoc/>
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is OverwatchUpdateState updateState)
            _panel?.UpdateState(updateState);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_consoleSystem != null)
            {
                _consoleSystem.LocalWatchStateChanged -= OnLocalWatchStateChanged;
                _consoleSystem = null;
            }

            // Never leave the shared mainframe shell hidden behind us if we close mid-spectate.
            _mainframe?.SetShellVisible(Owner, true);
            _mainframe?.DetachPanel(Owner, MainframeTab.Overwatch);
            _window?.Dispose();
            _window = null;
        }

        base.Dispose(disposing);
    }

    public void ViewCamera(NetEntity target)
    {
        SendMessage(new OverwatchViewCameraMessage(target));
    }

    public void StopWatching()
    {
        SendMessage(new OverwatchStopWatchingMessage());
    }

    public void CreateSquad(string squadName)
    {
        SendMessage(new OverwatchCreateSquadMessage(squadName));
    }

    public void DeleteSquad(int squadId)
    {
        SendMessage(new OverwatchDeleteSquadMessage(squadId));
    }

    public void AssignSquad(NetEntity player, int squadId)
    {
        SendMessage(new OverwatchAssignSquadMessage(player, squadId));
    }

    public void RemoveSquadMember(NetEntity player)
    {
        SendMessage(new OverwatchRemoveSquadMemberMessage(player));
    }

    public void SendAnnouncement(string message, int? targetSquadId = null)
    {
        SendMessage(new OverwatchSendMessageAnnouncement(message, targetSquadId));
    }
}
