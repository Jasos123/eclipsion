using Content.Client._Crescent.Mainframe;
using Content.Shared._Crescent.Diplomacy;
using Content.Shared._Crescent.Mainframe;
using Robust.Client.UserInterface;

namespace Content.Client._Crescent.Diplomacy;

public sealed class DiplomacyConsoleBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;

    private DiplomacyConsoleMenu? _menu;
    private DiplomacyPanel? _panel;
    private MainframeUiController? _mainframe;

    public DiplomacyConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _panel = new DiplomacyPanel();

        _panel.OnDeclareWar += targetFactionId =>
            SendMessage(new DiplomacyDeclareWarMessage(targetFactionId));

        _panel.OnProposePeace += targetFactionId =>
            SendMessage(new DiplomacyProposePeaceMessage(targetFactionId));

        _panel.OnProposeAlliance += targetFactionId =>
            SendMessage(new DiplomacyProposeAllianceMessage(targetFactionId));

        _panel.OnProposeTrade += targetFactionId =>
            SendMessage(new DiplomacyProposeTradeMessage(targetFactionId));

        _panel.OnBreakTrade += targetFactionId =>
            SendMessage(new DiplomacyBreakTradeMessage(targetFactionId));

        _panel.OnAcceptProposal += (fromFactionId, type) =>
            SendMessage(new DiplomacyAcceptProposalMessage(fromFactionId, type));

        _panel.OnRejectProposal += (fromFactionId, type) =>
            SendMessage(new DiplomacyRejectProposalMessage(fromFactionId, type));

        // On a mainframe this renders as a tab instead of its own window.
        if (UiSystem.HasUi(Owner, MainframeUiKey.Key))
        {
            _mainframe = _uiManager.GetUIController<MainframeUiController>();
            _mainframe.AttachPanel(Owner, MainframeTab.Diplomacy, _panel, this);
        }
        else
        {
            _menu = new DiplomacyConsoleMenu();
            _menu.OnClose += Close;
            _menu.SetPanel(_panel);
            _menu.OpenCentered();
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is DiplomacyConsoleBoundUserInterfaceState uiState)
            _panel?.UpdateState(uiState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _mainframe?.DetachPanel(Owner, MainframeTab.Diplomacy);
        _menu?.Dispose();
    }
}
