using Content.Shared._Goobstation.Research;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Goobstation.Research.UI;

[UsedImplicitly]
public sealed class FancyResearchConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private FancyResearchConsoleMenu? _consoleMenu;  // Goobstation R&D Console rework - ResearchConsoleMenu -> FancyResearchConsoleMenu

    public FancyResearchConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        var owner = Owner;

        _consoleMenu = this.CreateWindow<FancyResearchConsoleMenu>();   // Goobstation R&D Console rework - ResearchConsoleMenu -> FancyResearchConsoleMenu
        _consoleMenu.SetEntity(owner);
        _consoleMenu.OnClose += () => _consoleMenu = null;

        _consoleMenu.OnTechnologyCardPressed += id =>
        {
            SendMessage(new ConsoleUnlockTechnologyMessage(id));
        };

        _consoleMenu.OnServerButtonPressed += () =>
        {
            SendMessage(new ConsoleServerSelectionMessage());
        };
    }

    public override void OnProtoReload(PrototypesReloadedEventArgs args)
    {
        base.OnProtoReload(args);

        if (!args.WasModified<TechnologyPrototype>())
            return;

        if (State is not ResearchConsoleBoundInterfaceState rState)
            return;

        if (_consoleMenu is not { } menu)
            return;

        menu.UpdateInformationPanel(menu.Points, menu.SoftCapMultiplier);
        menu.UpdatePanels(rState.Researches);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not ResearchConsoleBoundInterfaceState castState)
            return;

        if (_consoleMenu == null)
            return;

        // Complete states are only sent for tree/database changes, so refresh all header data.
        _consoleMenu.UpdateInformationPanel(castState.Points, castState.SoftCapMultiplier);
        if (!ResearchAvailabilityHelper.ResearchesEqual(_consoleMenu.List, castState.Researches))
            _consoleMenu.UpdatePanels(castState.Researches);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is ResearchConsolePointsChangedMessage pointsChanged)
            _consoleMenu?.UpdateResearchPoints(pointsChanged.Points);
    }
}
