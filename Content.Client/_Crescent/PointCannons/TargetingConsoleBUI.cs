using System.Threading;
using Content.Shared.PointCannons;
using Timer = Robust.Shared.Timing.Timer;
using JetBrains.Annotations;
using System.Numerics;
using Content.Client._Crescent.PointCannons;
using Robust.Client.GameObjects;
using Content.Shared.Weapons.Ranged.Events;
using Content.Client.Weapons.Ranged.Systems;

namespace Content.Client._Crescent.PointCannons;

[UsedImplicitly]
public sealed class TargetingConsoleBoundUserInterface : BoundUserInterface
{
    private IEntityManager _entMan;
    private TransformSystem _formSys;

    private TargetingConsoleWindow? _window;
    private bool _isFiring;
    private Vector2 _coords;
    private CancellationTokenSource _updTimerTok = new();
    private List<NetEntity>? _controlled;

    public TargetingConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _entMan = IoCManager.Resolve<IEntityManager>();
        _formSys = _entMan.System<TransformSystem>();
        Timer.SpawnRepeating(100, Update, _updTimerTok.Token);
    }

    private void Update()
    {
        if (_isFiring)
            SendMessage(new TargetingConsoleFireMessage(_coords));

        if (_controlled == null || _window == null)
            return;

        // Walk _controlled, not the entity query, or the bars end up in a different order than the server's list.
        var ammoValues = new List<(int, int)>(_controlled.Count);
        foreach (var netEntity in _controlled)
        {
            // Still push a placeholder so the remaining bars don't shift.
            if (!_entMan.TryGetEntity(netEntity, out var uid) ||
                !_entMan.HasComponent<PointCannonComponent>(uid.Value))
            {
                ammoValues.Add((0, 1));
                continue;
            }

            GetAmmoCountEvent ammoEv = new();
            _entMan.EventBus.RaiseLocalEvent(uid.Value, ref ammoEv);
            ammoValues.Add((ammoEv.Count, ammoEv.Capacity));
        }

        _window.UpdateAmmoStatus(ammoValues);
    }

    protected override void Open()
    {
        base.Open();

        _window = new TargetingConsoleWindow();
        _window.OpenCentered();
        _window.OnClose += Close;

        _window.OnServerRefresh += OnRefreshServer;

        _window.Radar.OnRadarClick += (coords) =>
        {
            _coords = _formSys.ToMapCoordinates(coords).Position;
            SendMessage(new TargetingConsoleFireMessage(_coords));
            _isFiring = true;
        };

        _window.Radar.OnRadarRelease += () =>
        {
            _isFiring = false;
        };

        _window.Radar.OnRadarMouseMove += (coords) =>
        {
            _coords = _formSys.ToMapCoordinates(coords).Position;
        };

        _window.OnCannonGroupChange += (groupName) =>
        {
            SendMessage(new TargetingConsoleGroupChangedMessage(groupName));
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _updTimerTok.Cancel();
            _window?.Dispose();
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not TargetingConsoleBoundUserInterfaceState consoleState)
            return;

        _controlled = consoleState.ControlledCannons;
        _window?.UpdateState(consoleState);
        if (_window != null) // Rat
            _window.Radar.ActiveCannons = _controlled; // Rat
    }

    private void OnRefreshServer()
    {
        SendMessage(new FireControlConsoleRefreshServerMessage());
    }
}
