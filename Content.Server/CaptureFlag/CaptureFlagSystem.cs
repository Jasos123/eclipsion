using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared.CaptureFlag;
using Content.Shared._Crescent.Territory;
using Content.Shared.GameTicking;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.CaptureFlag;

public sealed class CaptureFlagSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    private readonly HashSet<EntityUid> _nearby = new();
    private readonly Dictionary<string, float> _majorityHoldSeconds = new();
    private bool _dominationEnded;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CaptureFlagComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnMapInit(EntityUid uid, CaptureFlagComponent comp, MapInitEvent args)
    {
        comp.Radius = MathF.Max(0.25f, comp.Radius);
        comp.CaptureTime = MathF.Max(1f, comp.CaptureTime);
        comp.NeutralizeTime = MathF.Max(1f, comp.NeutralizeTime);
        comp.DecayRate = MathF.Max(0f, comp.DecayRate);
        comp.DominationHoldTime = MathF.Max(30f, comp.DominationHoldTime);
        Dirty(uid, comp);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _majorityHoldSeconds.Clear();
        _dominationEnded = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CaptureFlagComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var flag, out var xform))
        {
            UpdateFlag(uid, flag, xform, frameTime);
        }

        UpdateDominationWin(frameTime);
    }

    private void UpdateFlag(EntityUid uid, CaptureFlagComponent flag, TransformComponent xform, float frameTime)
    {
        // Persistent territory is captured by explicitly interacting with its standard. It must not accumulate
        // progress merely because faction members happen to stand nearby.
        if (HasComp<PersistentCaptureRegionComponent>(uid))
            return;

        var mapPos = _xform.ToMapCoordinates(xform.Coordinates);
        _nearby.Clear();
        _lookup.GetEntitiesInRange(mapPos.MapId, mapPos.Position, flag.Radius, _nearby, LookupFlags.Dynamic | LookupFlags.Sundries);

        string? singleTeam = null;
        var contested = false;

        foreach (var ent in _nearby)
        {
            var team = TryGetTeam(ent);
            if (team is null)
                continue;

            if (singleTeam is null)
            {
                singleTeam = team;
                continue;
            }

            if (!string.Equals(singleTeam, team, StringComparison.Ordinal))
            {
                contested = true;
                break;
            }
        }

        if (singleTeam is null || contested)
        {
            var oldActiveTeam = flag.ActiveTeam;
            var oldStage = flag.Stage;
            var oldProgress = flag.ProgressSeconds;

            flag.ActiveTeam = null;
            flag.Stage = contested ? CaptureFlagStage.Contested : CaptureFlagStage.Idle;

            if (flag.ProgressSeconds > 0f)
            {
                if (flag.DecayWhenInactive)
                    flag.ProgressSeconds = MathF.Max(0f, flag.ProgressSeconds - frameTime * flag.DecayRate);
                else
                    flag.ProgressSeconds = 0f;
            }

            if (flag.ProgressSeconds <= 0f)
                flag.ProgressTeam = null;

            if (oldActiveTeam != flag.ActiveTeam || oldStage != flag.Stage || oldProgress != flag.ProgressSeconds)
                Dirty(uid, flag);

            return;
        }

        flag.ActiveTeam = singleTeam;

        // Partial progress belongs to the faction that earned it. Without this reset, another faction can walk in
        // after the first leaves and finish its capture or neutralization progress.
        if (flag.ProgressSeconds > 0f &&
            flag.ProgressTeam != null &&
            !string.Equals(flag.ProgressTeam, singleTeam, StringComparison.Ordinal))
        {
            flag.ProgressSeconds = 0f;
        }

        flag.ProgressTeam = singleTeam;

        var needNeutralize = flag.OwnerTeam != null &&
                             !string.Equals(flag.OwnerTeam, singleTeam, StringComparison.Ordinal);

        if (needNeutralize)
        {
            flag.Stage = CaptureFlagStage.Neutralizing;
            flag.ProgressSeconds += frameTime;

            if (flag.ProgressSeconds >= flag.NeutralizeTime)
            {
                flag.OwnerTeam = null;
                flag.ProgressSeconds = 0f;
                flag.ProgressTeam = singleTeam;
                flag.Stage = CaptureFlagStage.Capturing;
                Dirty(uid, flag);
                return;
            }

            Dirty(uid, flag);
            return;
        }

        if (flag.OwnerTeam != null && string.Equals(flag.OwnerTeam, singleTeam, StringComparison.Ordinal))
        {
            if (flag.ProgressSeconds != 0f || flag.Stage != CaptureFlagStage.Idle)
            {
                flag.ProgressSeconds = 0f;
                flag.ProgressTeam = null;
                flag.Stage = CaptureFlagStage.Idle;
                Dirty(uid, flag);
            }
            return;
        }

        flag.Stage = CaptureFlagStage.Capturing;
        flag.ProgressSeconds += frameTime;

        if (flag.ProgressSeconds >= flag.CaptureTime)
        {
            flag.OwnerTeam = singleTeam;
            flag.ProgressSeconds = 0f;
            flag.ProgressTeam = null;
            flag.Stage = CaptureFlagStage.Idle;
            RaiseLocalEvent(new CaptureFlagWonEvent(singleTeam));

            _popup.PopupEntity(Loc.GetString("capture-flag-captured", ("team", singleTeam)), uid, Filter.Broadcast(), true);
        }

        Dirty(uid, flag);
    }

    private string? TryGetTeam(EntityUid ent)
    {
        if (TryComp<CaptureTeamComponent>(ent, out var team) && !string.IsNullOrWhiteSpace(team.Team))
            return team.Team;

        if (TryComp<HullrotFactionComponent>(ent, out var hullrot) && !string.IsNullOrWhiteSpace(hullrot.Faction))
            return hullrot.Faction;

        return null;
    }

    private void UpdateDominationWin(float frameTime)
    {
        if (_dominationEnded)
            return;

        var total = 0;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        float? holdTime = null;
        var query = EntityQueryEnumerator<CaptureFlagComponent>();
        while (query.MoveNext(out _, out var flag))
        {
            if (!flag.DominationEnabled)
                continue;

            total++;

            holdTime ??= flag.DominationHoldTime;

            if (flag.OwnerTeam is null)
                continue;

            if (!counts.TryAdd(flag.OwnerTeam, 1))
                counts[flag.OwnerTeam]++;
        }

        if (total <= 0 || holdTime is null)
            return;

        var majorityThreshold = total / 2;
        var majorityTeam = counts.FirstOrDefault(kv => kv.Value > majorityThreshold).Key;

        if (majorityTeam is null)
        {
            _majorityHoldSeconds.Clear();
            return;
        }

        if (counts.Count(kv => kv.Value > majorityThreshold) != 1)
        {
            _majorityHoldSeconds.Clear();
            return;
        }

        foreach (var key in _majorityHoldSeconds.Keys.ToList())
        {
            if (!string.Equals(key, majorityTeam, StringComparison.Ordinal))
                _majorityHoldSeconds.Remove(key);
        }

        _majorityHoldSeconds.TryAdd(majorityTeam, 0f);
        _majorityHoldSeconds[majorityTeam] += frameTime;

        if (_majorityHoldSeconds[majorityTeam] < holdTime.Value)
            return;

        _dominationEnded = true;

        _ticker.EndRound(Loc.GetString("capture-flag-domination-win", ("team", majorityTeam), ("time", TimeSpan.FromSeconds(holdTime.Value).ToString(@"mm\:ss"))));
        Timer.Spawn(TimeSpan.FromMinutes(1), _ticker.RestartRound);
    }
}
