using System.Numerics;
using Content.Server.UserInterface;
using Content.Shared._Crescent.CCvars;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.PowerCell;
using Content.Shared.Movement.Components;
using Robust.Server.GameObjects;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Shuttles.Systems;


public sealed partial class RadarConsoleSystem : SharedRadarConsoleSystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private float _uiTps;
    private int _serverTickRate;

    private TimeSpan _updatePeriod = TimeSpan.Zero;

    private void InitializeCrescent()
    {
        Subs.CVar(_cfg, CrescentCVars.RadarConsoleUiTps, val => { _uiTps = val; RecalculatePeriod(); }, true);
        Subs.CVar(_cfg, CVars.NetTickrate, val => { _serverTickRate = val; RecalculatePeriod(); }, true);
    }

    private void RecalculatePeriod()
    {
        _updatePeriod = RadarUpdateScheduler.GetPeriod(_uiTps, _serverTickRate);
    }

    public void RefreshIFFState()
    {
        var query = AllEntityQuery<RadarConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (console.LastUpdatedState?.IFFState is null)
            {
                continue;
            }

            // Closed consoles rebuild their state when opened; there is no cache to refresh until then.
            if (!_uiSystem.IsUiOpen(uid, RadarConsoleUiKey.Key))
            {
                continue;
            }

            console.LastUpdatedState.IFFState.Turrets = _console.GetAllTurrets(uid);
            // Update() rebuilds the projectile list.
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;

        var query = EntityQueryEnumerator<RadarConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var console, out var transform))
        {
            if (!_uiSystem.IsUiOpen(uid, RadarConsoleUiKey.Key))
            {
                console.NextIffUpdate = TimeSpan.Zero;
                continue;
            }

            if (!RadarUpdateScheduler.TryConsume(ref console.NextIffUpdate, curTime, _updatePeriod, uid.Id))
            {
                continue;
            }

            if (console.LastUpdatedState is not null)
            {
                var turrets = console.LastUpdatedState.IFFState?.Turrets;
                var iffState = _console.GetIFFState(uid, turrets);
                var state = new NavBoundUserInterfaceState(console.LastUpdatedState);
                state.IFFState = iffState;
                state.DirtyFlags |= NavBoundUserInterfaceState.StateDirtyFlags.IFF;
                console.LastUpdatedState = state;
                _uiSystem.SetUiState(uid, RadarConsoleUiKey.Key, state);
            }

        }
    }
}
