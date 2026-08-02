using Content.Shared._Crescent.DroneControl;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Map;

namespace Content.Client._Crescent.DroneControl;

[UsedImplicitly]
public sealed class DroneConsoleBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    private DroneConsoleWindow? _window;

    public DroneConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<DroneConsoleWindow>();

        _window.OnMoveOrder += OnMoveOrder;
        _window.OnAttackOrder += OnAttackOrder;

        _window.OnDeploy += () => SendMessage(new DroneConsoleDeployMessage());
        _window.OnSetStance += stance => SendMessage(new DroneConsoleSetStanceMessage(stance));
        _window.OnSetTargeting += targeting => SendMessage(new DroneConsoleSetTargetingMessage(targeting));
        _window.OnSetFormation += formation => SendMessage(new DroneConsoleSetFormationMessage(formation));
        _window.OnSpawn += vesselId => SendMessage(new DroneConsoleSpawnMessage(vesselId));
        _window.OnSelfDestruct += OnSelfDestruct;
    }

    private void OnSelfDestruct(bool arm)
    {
        if (_window == null)
            return;

        var selected = _window.SelectedDrones;
        if (selected.Count == 0)
            return;

        SendMessage(new DroneConsoleSelfDestructMessage(selected, !arm));
    }

    private void OnMoveOrder(EntityCoordinates coord)
    {
        if (_window == null)
            return;

        var selected = _window.SelectedDrones;
        if (selected.Count == 0)
            return;

        SendMessage(new DroneConsoleMoveMessage(selected, _entMan.GetNetCoordinates(coord)));
    }

    private void OnAttackOrder(EntityCoordinates coord)
    {
        if (_window == null)
            return;

        var selected = _window.SelectedDrones;
        if (selected.Count == 0)
            return;

        SendMessage(new DroneConsoleTargetMessage(selected, _entMan.GetNetCoordinates(coord)));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is DroneConsoleBoundUserInterfaceState cast)
        {
            _window?.UpdateState(cast);
        }
    }
}
