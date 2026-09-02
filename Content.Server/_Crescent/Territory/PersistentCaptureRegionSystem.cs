using System.Text.Json;
using Content.Server.PointCannons;
using Content.Shared._Crescent.Factions;
using Content.Shared._Crescent.Territory;
using Content.Shared.CaptureFlag;
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
    [Dependency] private readonly FactionMachineSystem _factionMachines = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly NpcFactionSystem _npcFactions = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
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

        ent.Comp.RegionId = regionId;
        _liveRegions[regionId] = ent;
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

    private void PersistCapturedOwner(Entity<PersistentCaptureRegionComponent> ent, string team)
    {
        var owner = team.Trim();
        if (owner.Length == 0 || !PersistentTerritoryFactions.IsSupported(owner))
            return;

        _owners[ent.Comp.RegionId] = owner;
        Save();
        ApplyOwner(ent, owner);
    }

    private void PersistNeutralOwner(Entity<PersistentCaptureRegionComponent> ent)
    {
        _owners[ent.Comp.RegionId] = string.Empty;
        Save();
        ApplyNeutral(ent);
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

        if (TryComp<CaptureFlagComponent>(ent, out var flag) && flag.OwnerTeam != owner)
        {
            flag.OwnerTeam = owner;
            flag.ActiveTeam = null;
            flag.ProgressSeconds = 0f;
            flag.Stage = CaptureFlagStage.Idle;
            Dirty(ent, flag);
        }

        var regionGrid = Transform(ent).GridUid;
        var grids = ApplyDeviceOwner(ent, owner);
        foreach (var grid in grids)
            ApplyGridOwner(grid, ent.Comp, owner);

        if (regionGrid is { } namedGrid)
        {
            ent.Comp.BaseName ??= MetaData(namedGrid).EntityName;
            _metadata.SetEntityName(namedGrid, $"{owner} {ent.Comp.BaseName}");
        }
    }

    private void ApplyNeutral(Entity<PersistentCaptureRegionComponent> ent)
    {
        if (TerminatingOrDeleted(ent) || !ent.Comp.ValidRegion)
            return;

        ent.Comp.AppliedOwner = null;
        ent.Comp.NextStockReward = TimeSpan.Zero;

        if (TryComp<CaptureFlagComponent>(ent, out var flag) && flag.OwnerTeam != null)
        {
            flag.OwnerTeam = null;
            flag.ActiveTeam = null;
            flag.ProgressSeconds = 0f;
            flag.Stage = CaptureFlagStage.Idle;
            Dirty(ent, flag);
        }

        var regionGrid = Transform(ent).GridUid;
        var grids = ApplyDeviceOwner(ent, null);
        foreach (var grid in grids)
            ApplyGridOwner(grid, ent.Comp, null);

        if (regionGrid is { } namedGrid)
        {
            ent.Comp.BaseName ??= MetaData(namedGrid).EntityName;
            _metadata.SetEntityName(namedGrid, ent.Comp.BaseName);
        }
    }

    private HashSet<EntityUid> ApplyDeviceOwner(Entity<PersistentCaptureRegionComponent> ent, string? owner)
    {
        var regionGrid = Transform(ent).GridUid;
        var grids = new HashSet<EntityUid>();
        if (regionGrid is { } grid)
            grids.Add(grid);

        var devices = EntityQueryEnumerator<CaptureRegionDeviceComponent>();
        while (devices.MoveNext(out var deviceUid, out var device))
        {
            var explicitlyLinked = device.RegionId.Length != 0 &&
                                   string.Equals(device.RegionId.Trim(), ent.Comp.RegionId, StringComparison.OrdinalIgnoreCase);
            var linkedByGrid = device.RegionId.Length == 0 && regionGrid != null &&
                               Transform(deviceUid).GridUid == regionGrid;

            if (!explicitlyLinked && !linkedByGrid)
                continue;

            if (Transform(deviceUid).GridUid is { } deviceGrid)
                grids.Add(deviceGrid);

            // Ownership changes invalidate every existing operator, including an old owner who simply leaves the
            // window open. Closing the UI also clears the manual console's standing fire order.
            if (HasComp<TargetingConsoleComponent>(deviceUid))
                _ui.CloseUi(deviceUid, TargetingConsoleUiKey.Key);

            if (device.UpdateMachineFaction && HasComp<FactionMachineComponent>(deviceUid))
                _factionMachines.SetFaction(deviceUid, owner ?? string.Empty);

            if (device.UpdateNpcFaction && TryComp<NpcFactionMemberComponent>(deviceUid, out var npcFaction))
            {
                // Refresh immediately even for the neutral state. When there is no new owner there is no following
                // AddFaction call to rebuild the cached friendly/hostile sets for us.
                _npcFactions.ClearFactions((deviceUid, npcFaction));
                if (owner != null)
                    _npcFactions.AddFaction((deviceUid, npcFaction), owner);
            }
        }

        return grids;
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

internal sealed class CaptureRegionSaveData
{
    public int Version { get; set; }
    public Dictionary<string, string> Owners { get; set; } = new();
}
