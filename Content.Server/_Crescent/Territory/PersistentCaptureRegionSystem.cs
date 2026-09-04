using System.Linq;
using System.Text.Json;
using Content.Server.Chat.Systems;
using Content.Server.DoAfter;
using Content.Server.PointCannons;
using Content.Server.Popups;
using Content.Shared._Crescent.Factions;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared._Crescent.Territory;
using Content.Shared.CaptureFlag;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.PointCannons;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Crescent.Territory;

/// <summary>
/// Applies a persistent territory's owner to its radar contact and mapped-in faction devices.
/// </summary>
public sealed class PersistentCaptureRegionSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly FactionMachineSystem _factionMachines = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly NpcFactionSystem _npcFactions = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedShuttleSystem _shuttles = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly ResPath SavePath = new("/capture_regions.json");
    private const string RegionIdPlaceholder = "REPLACE_WITH_UNIQUE_REGION_ID";
    private const int SaveVersion = 1;

    private readonly Dictionary<string, string> _owners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EntityUid> _liveRegions = new(StringComparer.OrdinalIgnoreCase);

    public override void Initialize()
    {
        base.Initialize();

        Load();
        SubscribeLocalEvent<PersistentCaptureRegionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PersistentCaptureRegionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PersistentCaptureRegionComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PersistentCaptureRegionComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<PersistentCaptureRegionComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<PersistentCaptureRegionComponent, PersistentCaptureRegionCaptureDoAfterEvent>(OnCaptureDoAfter);
        SubscribeLocalEvent<CaptureRegionDeviceComponent, ComponentStartup>(OnDeviceStartup);
        SubscribeLocalEvent<CaptureRegionDeviceComponent, EntParentChangedMessage>(OnDeviceParentChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // This query is the hard boundary between the ordinary capture mode and persistent freeplay territory.
        // CaptureFlag and ConquestFlag entities without PersistentCaptureRegion never enter this code path.
        var query = EntityQueryEnumerator<PersistentCaptureRegionComponent, CaptureFlagComponent>();
        while (query.MoveNext(out var uid, out var region, out var flag))
        {
            if (!region.ValidRegion)
                continue;

            if (string.IsNullOrWhiteSpace(flag.OwnerTeam))
            {
                if (region.AppliedOwner != null)
                    PersistNeutralOwner((uid, region));

                continue;
            }

            if (!PersistentTerritoryFactions.IsSupported(flag.OwnerTeam))
            {
                Log.Warning($"Persistent capture region {ToPrettyString(uid)} rejected unsupported owner '{flag.OwnerTeam}'.");
                flag.OwnerTeam = null;
                flag.ActiveTeam = null;
                flag.ProgressSeconds = 0f;
                flag.ProgressTeam = null;
                flag.Stage = CaptureFlagStage.Idle;
                Dirty(uid, flag);

                if (region.AppliedOwner != null)
                    PersistNeutralOwner((uid, region));

                continue;
            }

            if (!string.Equals(region.AppliedOwner, flag.OwnerTeam, StringComparison.Ordinal))
            {
                PersistCapturedOwner((uid, region), flag.OwnerTeam);
                continue;
            }

            TryRewardStock((uid, region), flag.OwnerTeam);
        }
    }

    private void OnMapInit(Entity<PersistentCaptureRegionComponent> ent, ref MapInitEvent args)
    {
        var regionId = ent.Comp.RegionId.Trim();
        if (regionId.Length == 0 || regionId.Equals(RegionIdPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            ent.Comp.ValidRegion = false;
            Log.Error($"Persistent capture region {ToPrettyString(ent)} must override its placeholder regionId.");
            return;
        }

        if (_liveRegions.TryGetValue(regionId, out var duplicate) && duplicate != ent.Owner && Exists(duplicate))
        {
            ent.Comp.ValidRegion = false;
            Log.Error($"Persistent capture region {ToPrettyString(ent)} duplicates regionId '{regionId}' already used by {ToPrettyString(duplicate)}.");
            return;
        }

        if (Transform(ent).GridUid is not { } regionGrid)
        {
            ent.Comp.ValidRegion = false;
            Log.Error($"Persistent capture region {ToPrettyString(ent)} must be placed on a grid.");
            return;
        }

        foreach (var (otherId, otherUid) in _liveRegions)
        {
            if (!Exists(otherUid) || Transform(otherUid).GridUid != regionGrid)
                continue;

            ent.Comp.ValidRegion = false;
            Log.Error($"Persistent capture region {ToPrettyString(ent)} shares grid {ToPrettyString(regionGrid)} " +
                      $"with region '{otherId}'. A grid may contain only one territory flag.");
            return;
        }

        ent.Comp.RegionId = regionId;
        _liveRegions[regionId] = ent;

        // Settle the faction-independent name up front. Working it out lazily inside the apply calls meant
        // anything that wanted to name the region before the first application - an announcement, an examine,
        // the admin console - had nothing to print but the save key.
        if (string.IsNullOrWhiteSpace(ent.Comp.BaseName))
            ent.Comp.BaseName = PersistentTerritoryFactions.StripOwnerPrefix(MetaData(regionGrid).EntityName);

        ent.Comp.StockRewardInterval = MathF.Max(15f, ent.Comp.StockRewardInterval);
        ent.Comp.StockRewardMagnitude = MathF.Max(0f, ent.Comp.StockRewardMagnitude);
        ent.Comp.StockRewardDuration = Math.Max(1, ent.Comp.StockRewardDuration);

        var hasSavedOwner = _owners.TryGetValue(regionId, out var owner);
        if (!hasSavedOwner)
        {
            // A mapper may give the region an initial owner. It becomes effective immediately but is not written to
            // disk until somebody captures it, keeping YAML as the source of truth for untouched territories.
            owner = CompOrNull<CaptureFlagComponent>(ent)?.OwnerTeam;
        }

        if (!string.IsNullOrWhiteSpace(owner) && !PersistentTerritoryFactions.IsSupported(owner))
        {
            Log.Warning($"Persistent capture region '{regionId}' discarded unsupported saved/mapped owner '{owner}'.");
            owner = string.Empty;

            if (hasSavedOwner)
            {
                _owners[regionId] = string.Empty;
                Save();
            }
        }

        if (string.IsNullOrWhiteSpace(owner))
        {
            // An empty value in the save is an explicit persisted neutral state, distinct from an untouched region
            // which falls back to its mapped owner above.
            ent.Comp.AppliedOwner = null;
            Timer.Spawn(TimeSpan.Zero, () => ApplyNeutral(ent));
            return;
        }

        // Mark it before the next-tick application so Update cannot mistake a mapped/saved starting owner for a
        // fresh player capture and rewrite the persistence file during map initialization.
        ent.Comp.AppliedOwner = owner;
        ent.Comp.NextStockReward = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.StockRewardInterval);

        // MapInit ordering does not guarantee that every marked console and turret on the grid has initialized yet.
        // Applying on the next tick makes the update independent of YAML entity order.
        Timer.Spawn(TimeSpan.Zero, () => ApplyOwner(ent, owner));
    }

    private void OnShutdown(Entity<PersistentCaptureRegionComponent> ent, ref ComponentShutdown args)
    {
        if (_liveRegions.TryGetValue(ent.Comp.RegionId, out var registered) && registered == ent.Owner)
            _liveRegions.Remove(ent.Comp.RegionId);
    }

    private void OnInteractHand(Entity<PersistentCaptureRegionComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartCapture(ent, args.User);
    }

    private void OnActivate(Entity<PersistentCaptureRegionComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartCapture(ent, args.User);
    }

    /// <summary>
    /// Starts one explicit capture stage. Held territory is first neutralized; the now-neutral standard must then be
    /// used again to raise the attacker's colours. This preserves the territory's two-stage timing while removing
    /// all capture-by-proximity behavior.
    /// </summary>
    private bool TryStartCapture(Entity<PersistentCaptureRegionComponent> ent, EntityUid user)
    {
        if (!ent.Comp.ValidRegion || !TryComp<CaptureFlagComponent>(ent, out var flag))
        {
            _popup.PopupEntity(Loc.GetString("territory-capture-misconfigured"), ent, user);
            return true;
        }

        var team = TryGetCaptureTeam(user);
        if (!PersistentTerritoryFactions.IsSupported(team) ||
            !TryComp<MobStateComponent>(user, out var mobState) ||
            mobState.CurrentState != MobState.Alive)
        {
            _popup.PopupEntity(Loc.GetString("territory-capture-no-faction"), ent, user);
            return true;
        }

        if (string.Equals(flag.OwnerTeam, team, StringComparison.Ordinal))
        {
            _popup.PopupEntity(Loc.GetString("territory-capture-already-yours"), ent, user);
            return true;
        }

        if (ent.Comp.Capturer != null)
        {
            _popup.PopupEntity(Loc.GetString("territory-capture-already-working"), ent, user);
            return true;
        }

        var expectedOwner = flag.OwnerTeam;
        var neutralizing = expectedOwner != null;
        var duration = neutralizing ? flag.NeutralizeTime : flag.CaptureTime;
        var doAfter = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(duration),
            new PersistentCaptureRegionCaptureDoAfterEvent(team!, expectedOwner), ent, ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            DistanceThreshold = flag.Radius,
            NeedHand = false,
            BlockDuplicate = true,
            CancelDuplicate = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            _popup.PopupEntity(Loc.GetString("territory-capture-already-working"), ent, user);
            return true;
        }

        ent.Comp.Capturer = user;
        ent.Comp.CaptureStartedAt = _timing.CurTime;
        ent.Comp.CaptureEndsAt = _timing.CurTime + TimeSpan.FromSeconds(duration);
        flag.ActiveTeam = team;
        flag.ProgressTeam = team;
        flag.ProgressSeconds = 0f;
        flag.Stage = neutralizing ? CaptureFlagStage.Neutralizing : CaptureFlagStage.Capturing;
        Dirty(ent, flag);

        _popup.PopupEntity(Loc.GetString(neutralizing
            ? "territory-neutralize-begin"
            : "territory-capture-begin"), ent, user);
        return true;
    }

    private void OnCaptureDoAfter(
        Entity<PersistentCaptureRegionComponent> ent,
        ref PersistentCaptureRegionCaptureDoAfterEvent args)
    {
        if (ent.Comp.Capturer != args.User || !TryComp<CaptureFlagComponent>(ent, out var flag))
            return;

        ResetCaptureAttempt(ent, flag);

        if (args.Handled || args.Cancelled || !ent.Comp.ValidRegion)
            return;

        var team = TryGetCaptureTeam(args.User);
        if (!string.Equals(team, args.Team, StringComparison.Ordinal) ||
            !PersistentTerritoryFactions.IsSupported(team) ||
            !TryComp<MobStateComponent>(args.User, out var mobState) ||
            mobState.CurrentState != MobState.Alive ||
            !string.Equals(flag.OwnerTeam, args.ExpectedOwner, StringComparison.Ordinal))
        {
            return;
        }

        args.Handled = true;
        if (args.ExpectedOwner != null)
        {
            PersistNeutralOwner(ent);
            return;
        }

        flag.OwnerTeam = args.Team;
        Dirty(ent, flag);
        RaiseLocalEvent(new CaptureFlagWonEvent(args.Team));
        PersistCapturedOwner(ent, args.Team);
    }

    private void ResetCaptureAttempt(Entity<PersistentCaptureRegionComponent> ent, CaptureFlagComponent flag)
    {
        ent.Comp.Capturer = null;
        ent.Comp.CaptureStartedAt = TimeSpan.Zero;
        ent.Comp.CaptureEndsAt = TimeSpan.Zero;
        flag.ActiveTeam = null;
        flag.ProgressTeam = null;
        flag.ProgressSeconds = 0f;
        flag.Stage = CaptureFlagStage.Idle;
        Dirty(ent, flag);
    }

    private string? TryGetCaptureTeam(EntityUid user)
    {
        if (TryComp<CaptureTeamComponent>(user, out var captureTeam) &&
            !string.IsNullOrWhiteSpace(captureTeam.Team))
        {
            return captureTeam.Team;
        }

        if (TryComp<HullrotFactionComponent>(user, out var faction) && !string.IsNullOrWhiteSpace(faction.Faction))
            return faction.Faction;

        return null;
    }

    private void OnDeviceStartup(Entity<CaptureRegionDeviceComponent> ent, ref ComponentStartup args)
    {
        // Map entity order is not stable. Resolve on the next tick so a flag serialized after this device has had
        // a chance to register itself first.
        Timer.Spawn(TimeSpan.Zero, () => RefreshDeviceOwner(ent));
    }

    private void OnDeviceParentChanged(Entity<CaptureRegionDeviceComponent> ent, ref EntParentChangedMessage args)
    {
        // An unanchorable console or turret may move between grids. Re-evaluate implicit grid binding and clear
        // ownership when it leaves a territory instead of carrying stale access and automatic fire control away.
        Timer.Spawn(TimeSpan.Zero, () => RefreshDeviceOwner(ent));
    }

    private void PersistCapturedOwner(Entity<PersistentCaptureRegionComponent> ent, string team)
    {
        var owner = team.Trim();
        if (owner.Length == 0 || !PersistentTerritoryFactions.IsSupported(owner))
            return;

        _owners[ent.Comp.RegionId] = owner;
        Save();
        ApplyOwner(ent, owner);

        // A mapped or saved starting owner is applied straight through ApplyOwner at map load and is not news.
        // Taking held ground always passes through the manual neutralization stage first, so the sector hears the
        // holder lose it and hears the attacker claim it only after the second interaction completes.
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("territory-captured-announcement", ("faction", owner), ("region", RegionName(ent))),
            Loc.GetString("territory-announcer-name"),
            colorOverride: Color.Goldenrod);
    }

    private void PersistNeutralOwner(Entity<PersistentCaptureRegionComponent> ent)
    {
        var previous = ent.Comp.AppliedOwner;
        _owners[ent.Comp.RegionId] = string.Empty;
        Save();
        ApplyNeutral(ent);

        if (previous != null)
        {
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString("territory-neutralised-announcement", ("faction", previous), ("region", RegionName(ent))),
                Loc.GetString("territory-announcer-name"),
                colorOverride: Color.Goldenrod);
        }
    }

    /// <summary>
    /// The flag is the one place the whole picture is legible: who holds the ground, who is manually working the
    /// standard, and how far along they are. The radar name only carries the first of those.
    /// </summary>
    private void OnExamined(Entity<PersistentCaptureRegionComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.ValidRegion)
        {
            args.PushMarkup(Loc.GetString("territory-examine-misconfigured"));
            return;
        }

        args.PushMarkup(ent.Comp.AppliedOwner is { } owner
            ? Loc.GetString("territory-examine-owner", ("faction", owner))
            : Loc.GetString("territory-examine-unclaimed"));

        if (!TryComp<CaptureFlagComponent>(ent, out var flag))
            return;

        switch (flag.Stage)
        {
            case CaptureFlagStage.Neutralizing when flag.ProgressTeam is { } neutralizer:
                args.PushMarkup(Loc.GetString(
                    "territory-examine-progress",
                    ("faction", neutralizer),
                    ("percent", CapturePercent(ent.Comp))));
                break;

            case CaptureFlagStage.Capturing when flag.ProgressTeam is { } capturer:
                args.PushMarkup(Loc.GetString(
                    "territory-examine-progress",
                    ("faction", capturer),
                    ("percent", CapturePercent(ent.Comp))));
                break;
        }
    }

    private int CapturePercent(PersistentCaptureRegionComponent region)
    {
        var total = region.CaptureEndsAt - region.CaptureStartedAt;
        if (total <= TimeSpan.Zero)
            return 0;

        var elapsed = _timing.CurTime - region.CaptureStartedAt;
        return (int) Math.Round(Math.Clamp(elapsed / total, 0d, 1d) * 100d);
    }

    private void TryRewardStock(Entity<PersistentCaptureRegionComponent> ent, string owner)
    {
        if (!ent.Comp.StockRewardEnabled || ent.Comp.StockRewardMagnitude <= 0f)
            return;

        var now = _timing.CurTime;
        if (now < ent.Comp.NextStockReward)
            return;

        // Never back-pay a long pause or lag spike. One interval produces at most one reward.
        ent.Comp.NextStockReward = now + TimeSpan.FromSeconds(ent.Comp.StockRewardInterval);

        var ev = new PersistentTerritoryStockRewardEvent(
            owner,
            ent.Comp.BaseName ?? ent.Comp.RegionId,
            ent.Comp.StockRewardMagnitude,
            ent.Comp.StockRewardDuration);
        RaiseLocalEvent(ref ev);
    }

    private void ApplyOwner(Entity<PersistentCaptureRegionComponent> ent, string owner)
    {
        if (TerminatingOrDeleted(ent) || !ent.Comp.ValidRegion || !PersistentTerritoryFactions.IsSupported(owner))
            return;

        ent.Comp.AppliedOwner = owner;
        ent.Comp.NextStockReward = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.StockRewardInterval);

        if (TryComp<CaptureFlagComponent>(ent, out var flag) &&
            (flag.OwnerTeam != owner || ent.Comp.Capturer != null))
        {
            flag.OwnerTeam = owner;
            ResetCaptureAttempt(ent, flag);
        }

        var regionGrid = Transform(ent).GridUid;
        ApplyDeviceOwner(ent, owner);

        if (regionGrid is { } namedGrid)
        {
            ApplyGridOwner(namedGrid, ent.Comp, owner);
            _metadata.SetEntityName(namedGrid, $"{owner} {RegionName(ent)}");
        }
    }

    private void ApplyNeutral(Entity<PersistentCaptureRegionComponent> ent)
    {
        if (TerminatingOrDeleted(ent) || !ent.Comp.ValidRegion)
            return;

        ent.Comp.AppliedOwner = null;
        ent.Comp.NextStockReward = TimeSpan.Zero;

        if (TryComp<CaptureFlagComponent>(ent, out var flag) &&
            (flag.OwnerTeam != null || ent.Comp.Capturer != null))
        {
            flag.OwnerTeam = null;
            ResetCaptureAttempt(ent, flag);
        }

        var regionGrid = Transform(ent).GridUid;
        ApplyDeviceOwner(ent, null);

        if (regionGrid is { } namedGrid)
        {
            ApplyGridOwner(namedGrid, ent.Comp, null);
            _metadata.SetEntityName(namedGrid, RegionName(ent));
        }
    }

    /// <summary>
    /// The territory's faction-independent name, as it reads on radar with no owner. Falls back through the grid
    /// name to the save key so a region whose grid was never named still has something to call itself.
    /// </summary>
    public string RegionName(Entity<PersistentCaptureRegionComponent> ent)
    {
        if (!string.IsNullOrWhiteSpace(ent.Comp.BaseName))
            return ent.Comp.BaseName;

        if (Transform(ent).GridUid is { } grid)
        {
            var gridName = PersistentTerritoryFactions.StripOwnerPrefix(MetaData(grid).EntityName);
            if (!string.IsNullOrWhiteSpace(gridName))
                return gridName;
        }

        return ent.Comp.RegionId;
    }

    private void ApplyDeviceOwner(Entity<PersistentCaptureRegionComponent> ent, string? owner)
    {
        var regionGrid = Transform(ent).GridUid;

        var devices = EntityQueryEnumerator<CaptureRegionDeviceComponent>();
        while (devices.MoveNext(out var deviceUid, out var device))
        {
            var explicitlyLinked = device.RegionId.Length != 0 &&
                                   string.Equals(device.RegionId.Trim(), ent.Comp.RegionId, StringComparison.OrdinalIgnoreCase);
            var linkedByGrid = device.RegionId.Length == 0 && regionGrid != null &&
                               Transform(deviceUid).GridUid == regionGrid;

            if (!explicitlyLinked && !linkedByGrid)
                continue;

            ApplySingleDeviceOwner((deviceUid, device), owner);
        }
    }

    private void RefreshDeviceOwner(Entity<CaptureRegionDeviceComponent> ent)
    {
        if (TerminatingOrDeleted(ent) || !HasComp<CaptureRegionDeviceComponent>(ent))
            return;

        PersistentCaptureRegionComponent? region = null;
        if (!string.IsNullOrWhiteSpace(ent.Comp.RegionId))
        {
            var regionId = ent.Comp.RegionId.Trim();
            if (_liveRegions.TryGetValue(regionId, out var regionUid) &&
                TryComp<PersistentCaptureRegionComponent>(regionUid, out var explicitRegion) &&
                explicitRegion.ValidRegion)
            {
                region = explicitRegion;
            }
        }
        else if (Transform(ent).GridUid is { } deviceGrid)
        {
            foreach (var regionUid in _liveRegions.Values)
            {
                if (!TryComp<PersistentCaptureRegionComponent>(regionUid, out var candidate) ||
                    !candidate.ValidRegion ||
                    Transform(regionUid).GridUid != deviceGrid)
                {
                    continue;
                }

                region = candidate;
                break;
            }
        }

        ApplySingleDeviceOwner(ent, region?.AppliedOwner);
    }

    private void ApplySingleDeviceOwner(Entity<CaptureRegionDeviceComponent> ent, string? owner)
    {
        // Ownership changes invalidate every existing operator, including an old owner who simply leaves the
        // window open. Closing the UI also clears the manual console's standing fire order.
        if (HasComp<TargetingConsoleComponent>(ent))
            _ui.CloseUi(ent.Owner, TargetingConsoleUiKey.Key);

        if (ent.Comp.UpdateName)
        {
            ent.Comp.BaseName ??= PersistentTerritoryFactions.StripOwnerPrefix(MetaData(ent).EntityName);
            _metadata.SetEntityName(ent, owner == null ? ent.Comp.BaseName : $"{owner} {ent.Comp.BaseName}");
        }

        if (ent.Comp.UpdateMachineFaction && HasComp<FactionMachineComponent>(ent))
            _factionMachines.SetFaction(ent, owner ?? string.Empty);

        if (!ent.Comp.UpdateNpcFaction || !TryComp<NpcFactionMemberComponent>(ent, out var npcFaction))
            return;

        // Refresh immediately even for the neutral state. When there is no new owner there is no following
        // AddFaction call to rebuild the cached friendly/hostile sets for us.
        _npcFactions.ClearFactions((ent.Owner, npcFaction));
        if (owner != null)
            _npcFactions.AddFaction((ent.Owner, npcFaction), owner);
    }

    private void ApplyGridOwner(EntityUid grid, PersistentCaptureRegionComponent region, string? owner)
    {
        if (owner == null)
        {
            _shuttles.SetIFFFaction(grid, "Neutral");
            _shuttles.SetIFFColor(grid, region.NeutralColor);
            return;
        }

        _shuttles.SetIFFFaction(grid, owner);

        if (region.FactionColors.TryGetValue(owner, out var color))
            _shuttles.SetIFFColor(grid, color);
        else if (_prototypes.TryIndex<FactionPrototype>(owner, out var faction))
            _shuttles.SetIFFColor(grid, faction.FactionButtonColor);
    }

    /// <summary>
    /// One row per territory the server knows about: every region present in the save plus every region loaded
    /// on the current map, since a freshly mapped one has no saved row until somebody takes it.
    /// </summary>
    public List<TerritoryStatus> GetRegions()
    {
        var rows = new Dictionary<string, TerritoryStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var (regionId, owner) in _owners)
        {
            rows[regionId] = new TerritoryStatus(
                regionId,
                string.IsNullOrWhiteSpace(owner) ? null : owner,
                regionId,
                Loaded: false);
        }

        foreach (var (regionId, uid) in _liveRegions)
        {
            if (!TryComp<PersistentCaptureRegionComponent>(uid, out var region))
                continue;

            rows[regionId] = new TerritoryStatus(
                regionId,
                region.AppliedOwner,
                RegionName((uid, region)),
                Loaded: true);
        }

        var list = rows.Values.ToList();
        list.Sort(static (a, b) => string.Compare(a.RegionId, b.RegionId, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>
    /// Forces a territory's owner and writes it to disk, whether or not its map is currently loaded. Pass null to
    /// hand the ground back to nobody. Returns false only for a faction that cannot hold territory at all.
    /// </summary>
    public bool SetOwner(string regionId, string? owner)
    {
        regionId = regionId.Trim();
        if (regionId.Length == 0)
            return false;

        if (owner != null)
        {
            owner = owner.Trim();
            if (!PersistentTerritoryFactions.IsSupported(owner))
                return false;
        }

        _owners[regionId] = owner ?? string.Empty;
        Save();

        // A region whose map is not loaded needs nothing else: the save is read back at its next MapInit. One
        // that is loaded has to have the change pushed through its flag, devices, radar identity and name now.
        if (!_liveRegions.TryGetValue(regionId, out var uid) ||
            !TryComp<PersistentCaptureRegionComponent>(uid, out var region))
        {
            return true;
        }

        if (owner == null)
            ApplyNeutral((uid, region));
        else
            ApplyOwner((uid, region), owner);

        return true;
    }

    /// <summary>
    /// Drops a region from the save entirely, so its next map load falls back to whatever owner the map itself
    /// specifies. Use this for a region ID that was renamed or removed from the maps.
    /// </summary>
    public bool ForgetRegion(string regionId)
    {
        if (!_owners.Remove(regionId.Trim()))
            return false;

        Save();
        return true;
    }

    private void Load()
    {
        try
        {
            if (!_resources.UserData.TryReadAllText(SavePath, out var json))
                return;

            var data = JsonSerializer.Deserialize<CaptureRegionSaveData>(json);
            if (data is null || data.Version != SaveVersion)
                return;

            _owners.Clear();
            foreach (var (region, owner) in data.Owners)
            {
                if (!string.IsNullOrWhiteSpace(region))
                    _owners[region.Trim()] = owner?.Trim() ?? string.Empty;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load persistent capture regions: {e}");
        }
    }

    private void Save()
    {
        try
        {
            var data = new CaptureRegionSaveData { Version = SaveVersion };
            foreach (var (region, owner) in _owners)
                data.Owners[region] = owner;

            _resources.UserData.WriteAllText(SavePath, JsonSerializer.Serialize(data));
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save persistent capture regions: {e}");
        }
    }
}

/// <summary>
/// Raised by the isolated territory system and consumed by the economy system, keeping stock knowledge out of the
/// capture mechanic itself.
/// </summary>
[ByRefEvent]
public readonly record struct PersistentTerritoryStockRewardEvent(
    string Faction,
    string RegionName,
    float Magnitude,
    int DurationTicks);

/// <summary>
/// One territory's state as the admin console sees it. <paramref name="Loaded"/> distinguishes a region whose map
/// is up right now from one that only exists as a row in the save.
/// </summary>
public readonly record struct TerritoryStatus(string RegionId, string? Owner, string Name, bool Loaded);

internal sealed class CaptureRegionSaveData
{
    public int Version { get; set; }
    public Dictionary<string, string> Owners { get; set; } = new();
}
