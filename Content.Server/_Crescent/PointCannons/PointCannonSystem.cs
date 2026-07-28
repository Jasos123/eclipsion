// =============================================================================
// OPTIMISED PointCannonSystem
// Path: Content.Server/_Crescent/PointCannons/PointCannonSystem.cs
//
// Changes based on Monolith FireControl:
// 1. Cached EntityQuery instead of TryComp<> (11 calls -> 0 on the hot paths)
// 2. GridCannonCacheComponent - caches the cannons on a grid (no EntityLookup scan every time)
// 3. CannonFireCooldownComponent - server-side fire cooldown (early exit before GunSystem)
// 4. Batch firing with an early exit for cannons that are not ready
// =============================================================================

using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Content.Server.Administration;
using Content.Server.Popups;
using Content.Server.Shuttles.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.PointCannons;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using System;
using Content.Server._Crescent.Hardpoint;
using Content.Server.Power.Components;
using Content.Shared._Crescent.CCvars;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Crescent.Radar;
using Robust.Shared.Timing;

namespace Content.Server.PointCannons;

public sealed class PointCannonSystem : EntitySystem
{
    [Dependency] private readonly ISharedPlayerManager _playerMan = default!;
    [Dependency] private readonly TransformSystem _formSys = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _uiSys = default!;
    [Dependency] private readonly PopupSystem _popSys = default!;
    [Dependency] private readonly QuickDialogSystem _dialogSys = default!;
    [Dependency] private readonly GunSystem _gunSys = default!;
    [Dependency] private readonly ShuttleConsoleSystem _shuttleConSys = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsSys = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly HardpointSystem _hardpoint = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!; // +NEW

    private float _accumulatedFrameTime;
    private float _uiTps;

    private readonly HashSet<EntityUid> _activeConsoles = new();

    private readonly Dictionary<EntityUid, float> _gridUpdateCooldown = new();
    private const float GridUpdateCooldownTime = 0.5f;
    private int CannonCheckRange = 25;
    private HashSet<EntityUid> QueuedGrids = new();

    // ===== OPTIMISATION 1: cached EntityQuery =====
    // Instead of TryComp<T>() every time, GetEntityQuery<T>() once in Initialize.
    // EntityQuery.TryGetComponent() is roughly 2-3x faster than TryComp<T>().
    private EntityQuery<PointCannonComponent> _cannonQuery;
    private EntityQuery<TargetingConsoleComponent> _consoleQuery;
    private EntityQuery<GunComponent> _gunQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<HardpointAnchorableOnlyComponent> _anchorQuery;
    private EntityQuery<ApcPowerReceiverComponent> _powerQuery;
    private EntityQuery<HardpointFixedMountComponent> _fixedMountQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<CannonFireCooldownComponent> _cooldownQuery;
    private EntityQuery<GridCannonCacheComponent> _gridCacheQuery;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CrescentCVars.PointCannonUiTps, (float val) => { _uiTps = val; }, true);

        SubscribeLocalEvent<TargetingConsoleComponent, ActivatableUIOpenAttemptEvent>(OnConsoleOpenAttempt);
        SubscribeLocalEvent<TargetingConsoleComponent, BoundUserInterfaceMessageAttempt>(BUIValidation);
        SubscribeLocalEvent<TargetingConsoleComponent, BoundUIOpenedEvent>(OnConsoleOpened);
        SubscribeLocalEvent<TargetingConsoleComponent, BoundUIClosedEvent>(OnConsoleClosed);
        SubscribeLocalEvent<TargetingConsoleComponent, TargetingConsoleFireMessage>(OnConsoleFire);
        SubscribeLocalEvent<TargetingConsoleComponent, TargetingConsoleGroupChangedMessage>(OnConsoleGroupChanged);
        SubscribeLocalEvent<TargetingConsoleComponent, FireControlConsoleRefreshServerMessage>(OnRefreshServer);
        SubscribeLocalEvent<TargetingConsoleComponent, ComponentRemove>(OnConsoleDelete);
        SubscribeLocalEvent<TargetingConsoleComponent, AnchorStateChangedEvent>(OnConsoleAnchor);

        SubscribeLocalEvent<PointCannonComponent, EntityTerminatingEvent>(OnCannonDetach);
        SubscribeLocalEvent<PointCannonComponent, EntParentChangedMessage>(OnCannonDetach);
        SubscribeLocalEvent<PointCannonComponent, ReAnchorEvent>(OnCannonDetach);

        SubscribeLocalEvent<PointCannonLinkToolComponent, UseInHandEvent>(OnLinkToolHandUse);

        // ===== OPTIMISATION 2: invalidate the grid cache when cannons change =====
        SubscribeLocalEvent<PointCannonComponent, AnchorStateChangedEvent>(OnCannonAnchorChanged);
        SubscribeLocalEvent<PointCannonComponent, ComponentInit>(OnCannonInit);
        SubscribeLocalEvent<PointCannonComponent, ComponentRemove>(OnCannonRemoved);

        // Cache the EntityQuery once
        _cannonQuery = GetEntityQuery<PointCannonComponent>();
        _consoleQuery = GetEntityQuery<TargetingConsoleComponent>();
        _gunQuery = GetEntityQuery<GunComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _anchorQuery = GetEntityQuery<HardpointAnchorableOnlyComponent>();
        _powerQuery = GetEntityQuery<ApcPowerReceiverComponent>();
        _fixedMountQuery = GetEntityQuery<HardpointFixedMountComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _cooldownQuery = GetEntityQuery<CannonFireCooldownComponent>();
        _gridCacheQuery = GetEntityQuery<GridCannonCacheComponent>();
    }

    // ===== OPTIMISATION 2: grid cache invalidation =====
    private void OnCannonAnchorChanged(EntityUid uid, PointCannonComponent comp, ref AnchorStateChangedEvent args)
    {
        InvalidateGridCache(uid);
    }

    private void OnCannonInit(EntityUid uid, PointCannonComponent comp, ComponentInit args)
    {
        InvalidateGridCache(uid);
    }

    private void OnCannonRemoved(EntityUid uid, PointCannonComponent comp, ComponentRemove args)
    {
        InvalidateGridCache(uid);
    }

    private void InvalidateGridCache(EntityUid cannonUid)
    {
        if (!_xformQuery.TryGetComponent(cannonUid, out var xform))
            return;
        if (xform.GridUid is not { } gridUid)
            return;
        if (_gridCacheQuery.TryGetComponent(gridUid, out var cache))
            cache.Dirty = true;
    }

    // ===== OPTIMISATION 3: getting cannons from the grid cache =====
    private HashSet<EntityUid> GetGridCannons(EntityUid gridUid)
    {
        var cache = EnsureComp<GridCannonCacheComponent>(gridUid);

        if (!cache.Dirty)
            return cache.CachedCannons;

        // Only rescan when Dirty=true
        cache.CachedCannons.Clear();
        var cannonList = new HashSet<Entity<PointCannonComponent>>();
        _lookup.GetGridEntities(gridUid, cannonList);

        foreach (var cannon in cannonList)
        {
            cache.CachedCannons.Add(cannon.Owner);
        }

        cache.Dirty = false;
        return cache.CachedCannons;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulatedFrameTime += frameTime;
        float targetTime = _uiTps > 0 ? 1.0f / _uiTps : 1.0f;
        if (_accumulatedFrameTime < targetTime)
            return;
        _accumulatedFrameTime -= targetTime;
        if (_accumulatedFrameTime > targetTime)
            _accumulatedFrameTime = 0;

        List<EntityUid>? inactiveConsoles = null;
        foreach (var uid in _activeConsoles)
        {
            // Use the cached query instead of TryComp
            if (!_consoleQuery.TryGetComponent(uid, out var console))
            {
                inactiveConsoles ??= new();
                inactiveConsoles.Add(uid);
                continue;
            }
            UpdateConsoleState(uid, console);
        }

        if (inactiveConsoles != null)
        {
            foreach (var uid in inactiveConsoles)
                _activeConsoles.Remove(uid);
        }

        var toRemove = new List<EntityUid>();
        foreach (var (uid, timer) in _gridUpdateCooldown)
        {
            if (timer <= 0)
            {
                if (!_consoleQuery.TryGetComponent(uid, out var consoleComp))
                    continue;
                ProcessGridShapeChange(uid, consoleComp);
                toRemove.Add(uid);
            }
            else
            {
                _gridUpdateCooldown[uid] = timer - frameTime;
            }
        }
        foreach (var id in toRemove)
        {
            _gridUpdateCooldown.Remove(id);
        }
    }

    private void ProcessGridShapeChange(EntityUid console, TargetingConsoleComponent component)
    {
        UnlinkAllCannonsFromConsole(console, component);
        LinkAllCannonsToConsole(console, component);
    }

    private void OnRefreshServer(EntityUid console, TargetingConsoleComponent component, FireControlConsoleRefreshServerMessage args)
    {
        // Invalidate the grid cache on a manual refresh
        var gridUid = Transform(console).GridUid;
        if (gridUid is not null && _gridCacheQuery.TryGetComponent(gridUid.Value, out var cache))
            cache.Dirty = true;

        if (_gridUpdateCooldown.ContainsKey(console))
            _gridUpdateCooldown[console] = GridUpdateCooldownTime;
        else
            _gridUpdateCooldown[console] = GridUpdateCooldownTime;
    }

    private void UnlinkAllCannonsFromConsole(EntityUid console, TargetingConsoleComponent comp)
    {
        var allLinks = new List<(string group, EntityUid cannon)>();

        foreach (var (group, cannons) in comp.CannonGroups)
        {
            foreach (var cannon in cannons)
            {
                allLinks.Add((group, cannon));
            }
        }

        foreach (var (group, cannon) in allLinks)
        {
            UnlinkConsole(cannon, console, comp);
        }
    }

    private void OnConsoleDelete<T>(EntityUid console, TargetingConsoleComponent comp, ref T args)
    {
        UnlinkAllCannonsFromConsole(console, comp);
        _activeConsoles.Remove(console);
    }

    private void OnConsoleAnchor(EntityUid console, TargetingConsoleComponent comp, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
        {
            OnConsoleDelete(console, comp, ref args);
            return;
        }
    }

    // ===== OPTIMISATION 3: LinkAllCannonsToConsole uses the grid cache =====
    public void LinkAllCannonsToConsole(EntityUid console, TargetingConsoleComponent comp)
    {
        var gridUid = Transform(console).GridUid;
        if (gridUid is null)
            return;

        // Use the cache instead of calling _lookup.GetGridEntities every time
        var cachedCannons = GetGridCannons(gridUid.Value);

        foreach (var cannonUid in cachedCannons)
        {
            if (!_xformQuery.TryGetComponent(cannonUid, out var xform) || !xform.Anchored)
                continue;
            if (!_anchorQuery.TryGetComponent(cannonUid, out var anchorComp) || anchorComp.anchoredTo is null)
                continue;
            LinkCannon(cannonUid, console, comp, MetaData(cannonUid).EntityName);
        }
    }

    private void OnConsoleOpenAttempt(EntityUid uid, TargetingConsoleComponent component, ActivatableUIOpenAttemptEvent args)
    {
        var uis = _uiSys.GetActorUis(args.User);
        var ourGridUid = Transform(uid).GridUid;
        if (ourGridUid is null)
        {
            args.Cancel();
            return;
        }

        foreach (var (_, key) in uis)
        {
            if (key is ShuttleConsoleUiKey)
            {
                args.Cancel();
                _popSys.PopupEntity(Loc.GetString("targeting-rejection-shuttle-console"), args.User, args.User, PopupType.LargeCaution);
                return;
            }
        }
    }

    private void BUIValidation(EntityUid uid, TargetingConsoleComponent component, BoundUserInterfaceMessageAttempt args)
    {
        var uis = _uiSys.GetActorUis(args.Actor);
        foreach (var (_, key) in uis)
        {
            if (key is ShuttleConsoleUiKey)
            {
                args.Cancel();
                return;
            }
        }
    }

    private void OnConsoleOpened(Entity<TargetingConsoleComponent> uid, ref BoundUIOpenedEvent args)
    {
        uid.Comp.RegenerateCannons = true;
        _activeConsoles.Add(uid.Owner);
    }

    private void OnConsoleClosed(Entity<TargetingConsoleComponent> uid, ref BoundUIClosedEvent args)
    {
        _activeConsoles.Remove(uid.Owner);
    }

    private void OnCannonDetach<T>(Entity<PointCannonComponent> uid, ref T args)
    {
        UnlinkCannon(uid);
    }

    private void OnLinkToolHandUse(Entity<PointCannonLinkToolComponent> uid, ref UseInHandEvent args)
    {
        if (!_playerMan.TryGetSessionByEntity(args.User, out var session))
            return;

        _dialogSys.OpenDialog(session, "Group name", "Name (case insensitive)", (string name) =>
        {
            // Invariant: the group is matched against keys that come from data, so lowering a typed "IFF"
            // with a Turkish locale ("ıff") would stop it ever matching.
            uid.Comp.GroupName = string.IsNullOrEmpty(name) ? "all" : name.ToLowerInvariant();
        });
    }

    public void LinkCannon(EntityUid cannonUid, EntityUid consoleUid, TargetingConsoleComponent console, string group)
    {
        // Use the cached query
        if (!_cannonQuery.TryGetComponent(cannonUid, out var cannonComponent))
            return;
        if (!console.CannonGroups.ContainsKey(group))
            console.CannonGroups[group] = new List<EntityUid>();

        if (console.CannonGroups[group].Contains(cannonUid))
        {
            if (cannonComponent.LinkedConsoleId is null)
                cannonComponent.LinkedConsoleId = consoleUid;
            return;
        }

        console.CannonGroups[group].Add(cannonUid);
        if (group != "all" && !console.CannonGroups["all"].Contains(cannonUid))
            console.CannonGroups["all"].Add(cannonUid);

        console.RegenerateCannons = true;
        cannonComponent.LinkedConsoleId = consoleUid;
        cannonComponent.LinkedConsoleIds.Add(consoleUid);

        RefreshFiringRanges(cannonUid, null, null, cannonComponent, CannonCheckRange);
    }

    public void UnlinkCannon(EntityUid cannonUid)
    {
        if (!_cannonQuery.TryGetComponent(cannonUid, out var cannonComp))
            return;

        var consoleIds = cannonComp.LinkedConsoleIds.ToList();

        foreach (var consoleUid in consoleIds)
        {
            if (!_consoleQuery.TryGetComponent(consoleUid, out var console))
                continue;

            var groups = console.CannonGroups.Keys.ToList();

            foreach (string group in groups)
            {
                if (console.CannonGroups.TryGetValue(group, out var cannons))
                {
                    cannons.Remove(cannonUid);

                    if (cannons.Count == 0 && group != "all")
                        console.CannonGroups.Remove(group);
                }
            }

            console.RegenerateCannons = true;
            TogglePvsOverride(new[] { cannonUid }, GetUiSessions(consoleUid), false);
        }

        cannonComp.LinkedConsoleIds.Clear();
    }

    public void UnlinkConsole(EntityUid cannonUid, EntityUid consoleUid, TargetingConsoleComponent comp)
    {
        if (!_cannonQuery.TryGetComponent(cannonUid, out var cannonComp))
            return;

        cannonComp.LinkedConsoleIds.Remove(consoleUid);

        if (!_consoleQuery.TryGetComponent(consoleUid, out var console))
            return;

        var groups = console.CannonGroups.Keys.ToList();

        foreach (string group in groups)
        {
            if (console.CannonGroups.TryGetValue(group, out var cannons))
            {
                cannons.Remove(cannonUid);

                if (cannons.Count == 0 && group != "all")
                    console.CannonGroups.Remove(group);
            }
        }

        console.RegenerateCannons = true;
        TogglePvsOverride(new[] { cannonUid }, GetUiSessions(consoleUid), false);
    }

    public void UpdateConsoleState(EntityUid uid, TargetingConsoleComponent console)
    {
        NavInterfaceState navState = _shuttleConSys.GetNavState(uid, _shuttleConSys.GetAllDocks());
        IFFInterfaceState iffState = _shuttleConSys.GetIFFState(uid,
            console.RegenerateCannons ? null : console.PrevState?.IFFState.Turrets);

        List<string>? groups = console.RegenerateCannons ? console.CannonGroups.Keys.ToList() : null;

        var consoleState = new TargetingConsoleBoundUserInterfaceState(
            navState,
            iffState,
            groups,
            GetNetEntityList(console.CurrentGroup));
        console.RegenerateCannons = false;
        console.PrevState = consoleState;
        _uiSys.SetUiState(uid, TargetingConsoleUiKey.Key, consoleState);
    }

    // ===== OPTIMISATION 4: OnConsoleFire with a server-side cooldown =====
    private void OnConsoleFire(EntityUid uid, TargetingConsoleComponent console, TargetingConsoleFireMessage ev)
    {
        var now = _timing.CurTime;

        for (int i = 0; i < console.CurrentGroup.Count;)
        {
            EntityUid cannonUid = console.CurrentGroup[i];
            if (Deleted(cannonUid))
            {
                console.CurrentGroup.RemoveAt(i);
                TogglePvsOverride(new[] { cannonUid }, GetUiSessions(uid), false);
                continue;
            }

            // ===== Server-side cooldown check - early exit before TryFireCannon =====
            if (_cooldownQuery.TryGetComponent(cannonUid, out var cooldown))
            {
                if (now < cooldown.NextFire)
                {
                    i++;
                    continue; // Cannon is on cooldown - skip it without calling GunSystem
                }
            }

            if (TryFireCannon(cannonUid, ev.Coordinates))
            {
                // Refresh the cooldown after a successful shot
                if (cooldown != null)
                    cooldown.NextFire = now + TimeSpan.FromSeconds(cooldown.FireCooldown);
            }

            i++;
        }
    }

    private void OnConsoleGroupChanged(Entity<TargetingConsoleComponent> uid, ref TargetingConsoleGroupChangedMessage args)
    {
        if (!uid.Comp.CannonGroups.TryGetValue(args.GroupName, out var cannons))
            return;

        var sessions = GetUiSessions(uid);

        if (uid.Comp.ActiveGroups.Contains(args.GroupName))
        {
            if (args.GroupName == "all")
                uid.Comp.ActiveGroups = new();
            else
                uid.Comp.ActiveGroups.Remove(args.GroupName);
            TogglePvsOverride(cannons, sessions, false);
        }
        else
        {
            if (args.GroupName == "all")
                uid.Comp.ActiveGroups = new() { "all" };
            else
                uid.Comp.ActiveGroups.Add(args.GroupName);
            TogglePvsOverride(cannons, sessions, true);
        }

        var totalLength = 0;

        foreach (var group in uid.Comp.ActiveGroups)
            totalLength += uid.Comp.CannonGroups[group].Count;

        var selected = new List<EntityUid>(totalLength);

        foreach (var group in uid.Comp.ActiveGroups)
            selected.AddRange(uid.Comp.CannonGroups[group]);

        uid.Comp.CurrentGroup = selected;
    }

    // ===== OPTIMISATION 1: TryFireCannon with cached EntityQuery =====
    public bool TryFireCannon(
        EntityUid uid,
        Vector2 pos,
        TransformComponent? form = null,
        GunComponent? gun = null,
        PointCannonComponent? cannon = null)
    {
        // Use the cached queries instead of Resolve -> TryComp
        if (form == null && !_xformQuery.TryGetComponent(uid, out form))
            return false;
        if (gun == null && !_gunQuery.TryGetComponent(uid, out gun))
            return false;
        if (cannon == null && !_cannonQuery.TryGetComponent(uid, out cannon))
            return false;

        if (form.MapUid == null || !_gunSys.CanShoot(gun))
            return false;

        // Cached queries for the hardpoint checks
        if (!_anchorQuery.TryGetComponent(uid, out var anchorComp) || anchorComp.anchoredTo is null)
            return false;
        if (!_powerQuery.TryGetComponent(anchorComp.anchoredTo.Value, out var powerComp) || !powerComp.Powered)
            return false;

        var entPos = new EntityCoordinates(uid, new Vector2(0, -1));
        if (!_fixedMountQuery.HasComponent(anchorComp.anchoredTo.Value))
        {
            Vector2 cannonPos = _formSys.GetWorldPosition(form);
            _formSys.SetWorldRotation(uid, Angle.FromWorldVec(pos - cannonPos));
            entPos = new EntityCoordinates(form.MapUid.Value, pos);
        }

        if (!SafetyCheck(form.LocalRotation - Math.PI / 2, cannon))
            return false;

        _gunSys.AttemptShoot(uid, uid, gun, entPos);
        return true;
    }

    public bool SafetyCheck(Angle ang, PointCannonComponent cannon)
    {
        foreach (var (start, width) in cannon.ObstructedRanges)
        {
            if (CrescentHelpers.AngInSector(ang, start, width))
                return false;
        }
        return true;
    }

    public void RefreshFiringRanges(EntityUid uid, TransformComponent? form = null, GunComponent? gun = null, PointCannonComponent? cannon = null, int? range = null)
    {
        if (form == null && !_xformQuery.TryGetComponent(uid, out form))
            return;
        if (gun == null && !_gunQuery.TryGetComponent(uid, out gun))
            return;
        if (cannon == null && !_cannonQuery.TryGetComponent(uid, out cannon))
            return;
        range ??= 10;

        cannon.ObstructedRanges = CalculateFiringRanges(uid, form, gun, cannon, range.Value);
        Dirty(uid, cannon);
    }

    private List<(Angle, Angle)> CalculateFiringRanges(EntityUid uid, TransformComponent form, GunComponent gun, PointCannonComponent cannon, int range)
    {
        if (form.GridUid == null)
            return new();

        var gridUid = form.GridUid.Value;
        var cannonPos = form.LocalPosition;

        var entities = _lookup.GetEntitiesInRange(uid, range, LookupFlags.Static);
        var sectors = new List<(Angle start, Angle width)>();

        foreach (var childUid in entities)
        {
            // Cached query instead of Transform()
            if (!_xformQuery.TryGetComponent(childUid, out var otherForm))
                continue;
            if (otherForm.GridUid != gridUid)
                continue;

            var dir = otherForm.LocalPosition - cannonPos;
            if (!otherForm.Anchored)
                continue;

            // Cached query instead of TryComp<PhysicsComponent>
            if (!_physicsQuery.TryGetComponent(childUid, out var body) || !body.Hard)
                continue;

            if ((body.CollisionLayer & (int)CollisionGroup.BulletImpassable) == 0)
                continue;

            var (start, width) = GetObstacleSector(dir);
            sectors.Add((start, width));
        }

        if (sectors.Count == 0)
            return new();

        sectors.Sort((a, b) => a.start.Theta.CompareTo(b.start.Theta));

        var merged = new List<(Angle start, Angle width)>();
        var current = sectors[0];

        for (int i = 1; i < sectors.Count; i++)
        {
            var next = sectors[i];
            if (CrescentHelpers.AngSectorsOverlap(current.start, current.width, next.start, next.width))
            {
                var (newStart, newWidth) = CrescentHelpers.AngCombinedSector(current.start, current.width, next.start, next.width);
                current = (newStart, newWidth);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);

        var maxSpread = gun.MaxAngle + Angle.FromDegrees(10);
        var clearance = maxSpread + cannon.ClearanceAngle;

        var result = new List<(Angle, Angle)>(merged.Count);
        foreach (var (start, width) in merged)
        {
            result.Add((CrescentHelpers.AngNormal(start - clearance / 2), width + clearance));
        }

        return result;
    }

    private (Angle, Angle) GetObstacleSector(Vector2 delta)
    {
        Angle dirAngle = CrescentHelpers.AngNormal(new Angle(delta));
        Vector2 a, b;

        if (dirAngle % (Math.PI * 0.5) == 0)
        {
            switch (dirAngle.Theta)
            {
                case 0:
                case Math.Tau:
                    a = delta - Vector2Helpers.Half;
                    b = new Vector2(delta.X - 0.5f, delta.Y + 0.5f);
                    break;
                case Math.PI * 0.5:
                    a = delta - Vector2Helpers.Half;
                    b = new Vector2(delta.X + 0.5f, delta.Y - 0.5f);
                    break;
                case Math.PI:
                    a = delta + Vector2Helpers.Half;
                    b = new Vector2(delta.X + 0.5f, delta.Y - 0.5f);
                    break;
                case Math.PI * 1.5:
                    a = delta + Vector2Helpers.Half;
                    b = new Vector2(delta.X - 0.5f, delta.Y + 0.5f);
                    break;
                default:
                    return (double.NaN, double.NaN);
            }
        }
        else if (dirAngle > 0 && dirAngle < Math.PI * 0.5 || dirAngle > Math.PI && dirAngle < Math.PI * 1.5)
        {
            a = new Vector2(delta.X - 0.5f, delta.Y + 0.5f);
            b = new Vector2(delta.X + 0.5f, delta.Y - 0.5f);
        }
        else
        {
            a = delta + Vector2Helpers.Half;
            b = delta - Vector2Helpers.Half;
        }

        Angle start = CrescentHelpers.AngNormal(new Angle(a));
        Angle end = CrescentHelpers.AngNormal(new Angle(b));
        Angle width = Angle.ShortestDistance(start, end);
        if (width < 0)
        {
            start = end;
            width = -width;
        }

        return (start, width);
    }

    private List<ICommonSession> GetUiSessions(EntityUid uid)
    {
        var sessions = new List<ICommonSession>();
        foreach (var actorUid in _uiSys.GetActors(uid, TargetingConsoleUiKey.Key))
        {
            if (_playerMan.TryGetSessionByEntity(actorUid, out var session))
                sessions.Add(session);
        }
        return sessions;
    }

    private void TogglePvsOverride(IEnumerable<EntityUid> uids, IEnumerable<ICommonSession> sessions, bool enable)
    {
        foreach (var session in sessions)
        {
            foreach (var uid in uids)
            {
                if (!Exists(uid))
                    continue;

                if (enable)
                    _pvsSys.AddSessionOverride(uid, session);
                else
                    _pvsSys.RemoveSessionOverride(uid, session);
            }
        }
    }
}
