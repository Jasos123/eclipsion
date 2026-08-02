using System.Linq;
using Content.Shared._Crescent.HeatSeeking;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.HeatSeeking;

/// <summary>
/// Tells a crew when something has locked onto their hull, so a missile isn't the first they hear of it.
/// </summary>
public sealed class HeatSeekingWarningSystem : EntitySystem
{
    private const float ScanInterval = 0.25f;

    // Per grid, otherwise a salvo of eight sets off eight buzzers.
    private static readonly TimeSpan WarningCooldown = TimeSpan.FromSeconds(6);

    private static readonly SoundPathSpecifier WarningSound =
        new("/Audio/Machines/warning_buzzer.ogg", AudioParams.Default.WithVolume(-4f));

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextWarning = new();

    private readonly HashSet<EntityUid> _lockedGrids = new();

    private readonly HashSet<EntityUid> _warningGrids = new();

    private float _scanAccumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _scanAccumulator += frameTime;
        if (_scanAccumulator < ScanInterval)
            return;

        _scanAccumulator = 0f;

        _lockedGrids.Clear();

        var query = EntityQueryEnumerator<HeatSeekingComponent>();
        while (query.MoveNext(out _, out var seeker))
        {
            if (seeker.TargetEntity is not { } target || !Exists(target))
                continue;

            if (Transform(target).GridUid is { } grid)
                _lockedGrids.Add(grid);
        }

        if (_lockedGrids.Count == 0)
        {
            _nextWarning.Clear();
            return;
        }

        var now = _timing.CurTime;
        _warningGrids.Clear();

        foreach (var grid in _lockedGrids)
        {
            if (_nextWarning.TryGetValue(grid, out var next) && now < next)
                continue;

            _nextWarning[grid] = now + WarningCooldown;
            _warningGrids.Add(grid);
        }

        if (_warningGrids.Count > 0)
            WarnGrids();

        if (_nextWarning.Count > _lockedGrids.Count)
        {
            foreach (var grid in _nextWarning.Keys.ToArray())
            {
                if (!_lockedGrids.Contains(grid))
                    _nextWarning.Remove(grid);
            }
        }
    }

    private void WarnGrids()
    {
        var message = Loc.GetString("heat-seeking-lock-warning");

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.GridUid is not { } grid || !_warningGrids.Contains(grid))
                continue;

            _popup.PopupEntity(message, uid, actor.PlayerSession, PopupType.LargeCaution);
            _audio.PlayGlobal(WarningSound, actor.PlayerSession);
        }
    }
}
