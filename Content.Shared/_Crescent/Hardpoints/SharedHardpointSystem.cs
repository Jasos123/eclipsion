using System.Reflection.Metadata.Ecma335;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Destructible;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Shared._Crescent.Hardpoints;

/// <summary>
/// This handles...
/// </summary>
public class SharedHardpointSystem : EntitySystem
{
    [Dependency] public readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] public readonly EntityLookupSystem _lookupSystem = default!;
    [Dependency] public readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EntityManager _entMan = default!;

    //used for logging, don't touch this
    private ISawmill _sawmill = default!;
    private EntityQuery<MetaDataComponent> _metaQuery;

    /// <inheritdoc/>
    public override void Initialize()
    {
        _metaQuery = GetEntityQuery<MetaDataComponent>();

        SubscribeLocalEvent<HardpointAnchorableOnlyComponent, AnchorStateChangedEvent>(OnAnchorChange);
        SubscribeLocalEvent<HardpointAnchorableOnlyComponent, MapInitEvent>(OnMapLoad);
        SubscribeLocalEvent<HardpointComponent, AnchorStateChangedEvent>(OnHardpointAnchor);
        SubscribeLocalEvent<HardpointAnchorableOnlyComponent, ComponentRemove>(OnShipgunRemove);
        SubscribeLocalEvent<HardpointComponent, ComponentRemove>(OnHardpointRemove);
        SubscribeLocalEvent<HardpointAnchorableOnlyComponent, ShotAttemptedEvent>(OnShotAttempted);
        // TODO: ACCOUNT FOR REMOVING IT IN ADMIN MODE
        _sawmill = IoCManager.Resolve<ILogManager>().GetSawmill("crescent.hardpoints");
    }

    private void OnShotAttempted(Entity<HardpointAnchorableOnlyComponent> ent, ref ShotAttemptedEvent args)
    {
        if (!IsMounted(ent))
            args.Cancel();
    }

    /// <summary>
    /// Returns whether a hardpoint-only weapon is still physically mounted to the hardpoint that claims it.
    /// Checking both sides prevents stale component state from allowing a detached weapon to fire.
    /// </summary>
    public bool IsMounted(Entity<HardpointAnchorableOnlyComponent> weapon)
    {
        var weaponXform = Transform(weapon);

        if (!weaponXform.Anchored ||
            weapon.Comp.anchoredTo is not { } hardpointUid ||
            !TryComp<HardpointComponent>(hardpointUid, out var hardpoint) ||
            hardpoint.anchoring != weapon.Owner ||
            !TryComp<TransformComponent>(hardpointUid, out var hardpointXform) ||
            !hardpointXform.Anchored ||
            hardpointXform.GridUid != weaponXform.GridUid)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Severs a weapon/hardpoint pair from whichever half of it still exists, and tells the rest of the game the
    /// gun has come off its mount.
    /// </summary>
    /// <remarks>
    /// Destroying the tile under a mount seldom takes both halves with it and never guarantees an order: the
    /// hardpoint can be gone before the gun hears about it or the other way round. So every step here resolves
    /// the other half with <see cref="TryComp{T}(EntityUid?, out T)"/> and carries on without it, rather than
    /// reading through an <see cref="EntityUid"/> that may already be deleted. The deanchor event is raised on
    /// the CANNON rather than the hardpoint for the same reason - everything it drives (stopping continuous
    /// fire, unlinking the targeting console) acts on the gun, and raising it on the hardpoint meant a destroyed
    /// hardpoint left its gun linked and still burst-firing, which is the state this path exists to prevent.
    /// </remarks>
    public void BreakMount(EntityUid weaponUid, HardpointAnchorableOnlyComponent weaponComp)
    {
        var hardpointUid = weaponComp.anchoredTo;
        weaponComp.anchoredTo = null;

        var gridUid = EntityUid.Invalid;
        if (TryComp<TransformComponent>(weaponUid, out var weaponXform) && weaponXform.GridUid is { } weaponGrid)
            gridUid = weaponGrid;

        // rat-change: the defensive resolution of the other half originated as the TryComp guards Ratgore added
        // to the removal handler; it lives here now so every path through the link gets it.
        if (TryComp<HardpointComponent>(hardpointUid, out var hardpointComp))
        {
            // Only ever release a hardpoint that still claims this weapon. Something else may have taken the
            // mount over in the meantime, and clearing it then would leave that gun mounted on nothing.
            if (hardpointComp.anchoring == weaponUid)
                hardpointComp.anchoring = null;

            if (gridUid == EntityUid.Invalid &&
                TryComp<TransformComponent>(hardpointUid, out var hardpointXform) &&
                hardpointXform.GridUid is { } hardpointGrid)
            {
                gridUid = hardpointGrid;
            }

            DirtyEntity(hardpointUid.Value);
        }

        var arg = new HardpointCannonDeanchoredEvent
        {
            CannonUid = weaponUid,
            gridUid = gridUid,
        };
        RaiseLocalEvent(weaponUid, arg);

        DirtyEntity(weaponUid);
    }

    public void OnMapLoad(EntityUid uid, HardpointAnchorableOnlyComponent comp, ref MapInitEvent args)
    {
        if (Transform(uid).MapUid == null)
            return;
        if (TryAnchorToHardpoint(uid, comp))
            return;
        _sawmill.Debug(
            $"Hardpoint-only weapon had no hardpoint under itself at mapInit. {uid} , {MetaData(uid).EntityName}");
    }
    public void OnAnchorChange(EntityUid uid, HardpointAnchorableOnlyComponent component, ref AnchorStateChangedEvent args)
    {
        //this is here to prevent this code from running at the start of the round OR when you spawn a new ship.
        //otherwise, targeting computers do not see turrets. only sometimes.
        if (_entMan.GetComponent<MetaDataComponent>(uid).EntityLifeStage != EntityLifeStage.MapInitialized)
            return;
        //_sawmill.Debug("ON ANCHOR CHANGE RAN" + args.Anchored.ToString());
        //LOGIC:
        /*
        "im a shipgun"
        "i just got anchored or deanchored!"
        "if i got anchored, let me check if I've got a valid hardpoint under me."
            "if yes, then set the values properly and stay anchored."
            "if no, then and send a popup. the values were never set, so the gun can't fire anyway."
        "if i just got deanchored,"
            "de-set all the values, then de-anchor the gun too."
        */
        if (args.Anchored)
        {
            if (TryAnchorToHardpoint(uid, component)) //if it's a valid hardpoint, then we're good. this function also sets the values properly.
            {
                return;
            }
            else
            {
                //_transformSystem.Unanchor(uid); //if it's not / we dont have a hardpoint under it, kick that shit out
                //play sound effect
                _popup.PopupPredicted(Loc.GetString("WARNING! This weapon is not mounted on a compatible hardpoint and will not function!"), uid, null);
                return;
            }
        }

        //else, if we UNanchored
        if (component.anchoredTo == null) //this should literally never happen
        {
            return;
        }

        // Deliberately not resolved through the hardpoint's transform any more. That read threw outright once the
        // hardpoint had been deleted - which is precisely what happens when the tile under the pair is destroyed
        // and the gun outlives its mount - and its "no grid, do nothing" branch left the gun holding a dangling
        // mount it could never shed.
        BreakMount(uid, component);
        _transformSystem.Unanchor(uid);
    }

    public void OnHardpointAnchor(EntityUid target, HardpointComponent comp, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            return;
        if (comp.anchoring is not { } weaponUid)
            return;

        // Same guard the weapon side has carried all along. Without it this ran on the client, where a networked
        // EntityUid whose entity has not arrived (or has already gone) resolves to EntityUid.Invalid, and
        // Unanchor threw on it. It also runs mid-deletion on the server, where ComponentRemove is the correct
        // place to unpick the link.
        if (!_metaQuery.TryGetComponent(target, out var meta) ||
            meta.EntityLifeStage != EntityLifeStage.MapInitialized)
        {
            return;
        }

        if (!TryComp<HardpointAnchorableOnlyComponent>(weaponUid, out var weaponComp))
        {
            comp.anchoring = null;
            DirtyEntity(target);
            return;
        }

        _transformSystem.Unanchor(weaponUid);

        // The weapon's own AnchorStateChanged normally severs the link. It does not when the weapon was already
        // unanchored, so finish the job here rather than leaving the hardpoint claiming a gun it no longer holds.
        if (weaponComp.anchoredTo == target)
            BreakMount(weaponUid, weaponComp);
    }

    /// <summary>
    /// this is used for when shipguns are destroyed, but this ALSO runs when the grid is deleted.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <param name="args"></param>
    public void OnShipgunRemove(EntityUid uid, HardpointAnchorableOnlyComponent component, ComponentRemove args)
    {
        if (component.anchoredTo is null)
            return;

        // Every one of the old guards here bailed out early and left the hardpoint still claiming a gun that was
        // being deleted. On a grid teardown the hardpoint is often detached to nullspace before the gun's turn
        // comes, so "the hardpoint has no grid" was routine rather than exceptional.
        BreakMount(uid, component);
    }

    /// <summary>
    /// The mirror of <see cref="OnShipgunRemove"/>. Nothing used to run when a hardpoint was destroyed under a
    /// surviving gun, so the gun kept an <c>anchoredTo</c> pointing at a deleted entity - which every later read
    /// of it then threw on, and which made the gun's mount look like a traversing turret to anything that only
    /// asked whether the mount was fixed.
    /// </summary>
    private void OnHardpointRemove(EntityUid uid, HardpointComponent component, ComponentRemove args)
    {
        if (component.anchoring is not { } weaponUid)
            return;

        component.anchoring = null;

        if (!TryComp<HardpointAnchorableOnlyComponent>(weaponUid, out var weaponComp) ||
            weaponComp.anchoredTo != uid)
        {
            return;
        }

        BreakMount(weaponUid, weaponComp);

        // Sever the link first, then drop the gun loose the way unanchoring the hardpoint does. Doing it in this
        // order means the gun's own AnchorStateChanged finds nothing left to unpick and cannot re-enter here.
        if (!TerminatingOrDeleted(weaponUid))
            _transformSystem.Unanchor(weaponUid);
    }


    /// <summary>
    /// Returns true/false based on if we are able to anchor something here or not.
    /// TRUE: we're good to anchor here, hardpoint fits/exists.
    /// FALSE: there is no hardpoint/it's of the wrong type
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <returns></returns>
    public bool TryAnchorToHardpoint(EntityUid uid, HardpointAnchorableOnlyComponent component)
    {
        //_sawmill.Debug("TRY ANCHOR TO HARDPOINT RAN");
        var gridUid = Transform(uid).GridUid;
        if (gridUid is null)
            return false;
        if (!TryComp<MapGridComponent>(gridUid, out var gridComp))
            return false;
        if (!_transformSystem.TryGetGridTilePosition(uid, out var indice, gridComp))
        {
            return false;
        }

        foreach (var entity in _mapSystem.GetAnchoredEntities(new Entity<MapGridComponent>(gridUid.Value, gridComp), indice))
        {
            if (!TryComp<HardpointComponent>(entity, out var hardComp))
                continue;
            if (hardComp.anchoring is not null)
                continue;
            if ((hardComp.CompatibleTypes & component.CompatibleTypes) == 0)
                continue;
            if (hardComp.CompatibleSizes < component.CompatibleSizes)
                continue;
            AnchorEntityToHardpoint(uid, entity, component, hardComp, gridUid.Value);
            return true;
        }

        return false;
    }

    public void AnchorEntityToHardpoint(EntityUid target, EntityUid anchor, HardpointAnchorableOnlyComponent targetComp, HardpointComponent hardpoint, EntityUid grid)
    {
        //_sawmill.Debug("ANCHOR ENTITY TO HARDPOINT RAN");
        hardpoint.anchoring = target;
        targetComp.anchoredTo = anchor;
        _transformSystem.SetLocalRotation(target, Transform(anchor).LocalRotation);
        DirtyEntity(target);
        DirtyEntity(anchor);
        //Dirty(target, targetComp);
    }
}
