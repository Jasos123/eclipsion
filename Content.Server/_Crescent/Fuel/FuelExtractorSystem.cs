using Content.Server.Power.Components;
using Content.Shared._Crescent.Fuel;
using Content.Shared.Audio;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Popups;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.Fuel;

/// <summary>
/// Handles fuel extraction from asteroid seeps.
/// </summary>
public sealed class FuelExtractorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPuddleSystem _puddle = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FuelExtractorComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<FuelExtractorComponent, ExaminedEvent>(OnExtractorExamined);
        SubscribeLocalEvent<FuelSeepComponent, MapInitEvent>(OnSeepMapInit);
        SubscribeLocalEvent<FuelSeepComponent, ExaminedEvent>(OnSeepExamined);
    }

    private void OnSeepMapInit(Entity<FuelSeepComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.MaxReserve < ent.Comp.Reserve)
            ent.Comp.MaxReserve = ent.Comp.Reserve;
        UpdateSeepAppearance(ent);
    }

    private void OnAnchorStateChanged(Entity<FuelExtractorComponent> ent, ref AnchorStateChangedEvent args)
    {
        ent.Comp.Seep = null;

        if (!args.Anchored || args.Detaching)
            return;

        ent.Comp.Seep = FindSeep(ent);
        _popup.PopupEntity(
            Loc.GetString(ent.Comp.Seep == null ? "fuel-extractor-popup-no-seep" : "fuel-extractor-popup-seated"),
            ent);
    }

    private void OnExtractorExamined(Entity<FuelExtractorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryComp<TransformComponent>(ent, out var xform) || !xform.Anchored)
        {
            args.PushMarkup(Loc.GetString("fuel-extractor-examine-unanchored"));
            return;
        }

        if (!TryComp<FuelSeepComponent>(ResolveSeep(ent), out var seep))
        {
            args.PushMarkup(Loc.GetString("fuel-extractor-examine-no-seep"));
            return;
        }

        if (seep.Reserve <= FixedPoint2.Zero)
        {
            args.PushMarkup(Loc.GetString("fuel-extractor-examine-dry"));
            return;
        }

        args.PushMarkup(Loc.GetString("fuel-extractor-examine-tapped", ("fuel", GetReagentName(seep.Reagent))));

        if (ent.Comp.BufferFull)
            args.PushMarkup(Loc.GetString("fuel-extractor-examine-buffer-full"));
    }

    private void OnSeepExamined(Entity<FuelSeepComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Reserve <= FixedPoint2.Zero)
        {
            args.PushMarkup(Loc.GetString("fuel-seep-examine-dry"));
            return;
        }

        var percent = ent.Comp.MaxReserve <= FixedPoint2.Zero
            ? 100
            : (ent.Comp.Reserve.Float() / ent.Comp.MaxReserve.Float() * 100f);

        args.PushMarkup(Loc.GetString("fuel-seep-examine-remaining",
            ("fuel", GetReagentName(ent.Comp.Reagent)),
            ("percent", (int) MathF.Ceiling(percent))));
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FuelExtractorComponent, PowerConsumerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var extractor, out var power, out var xform))
        {
            if (now < extractor.NextCycle)
                continue;

            extractor.NextCycle = now + extractor.CycleDelay;

            // Drain before pumping so a newly inserted container can catch the next cycle instead of losing it
            // to overflow. This also lets an idle drill empty its buffer.
            DrainBufferIntoContainer((uid, extractor));

            var powered = power.ReceivedPower >= power.DrawRate * extractor.RequiredPowerRatio;
            var running = xform.Anchored && powered && TryExtract((uid, extractor));

            _appearance.SetData(uid, FuelExtractorVisuals.Running, running);
            _ambient.SetAmbience(uid, running);
        }
    }

    private bool TryExtract(Entity<FuelExtractorComponent> ent)
    {
        if (!TryComp<FuelSeepComponent>(ResolveSeep(ent), out var seep) || seep.Reserve <= FixedPoint2.Zero)
        {
            ent.Comp.BufferFull = false;
            return false;
        }

        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out var soln, out var buffer))
            return false;

        var amount = FixedPoint2.Min(seep.YieldPerCycle, seep.Reserve);
        if (amount <= FixedPoint2.Zero)
            return false;

        seep.Reserve -= amount;
        if (seep.Reserve <= FixedPoint2.Zero)
        {
            seep.Reserve = FixedPoint2.Zero;
            UpdateSeepAppearance((ent.Comp.Seep!.Value, seep));
        }

        var stored = FixedPoint2.Min(amount, buffer.AvailableVolume);
        ent.Comp.BufferFull = buffer.AvailableVolume <= FixedPoint2.Zero;

        if (stored > FixedPoint2.Zero)
            _solution.TryAddReagent(soln.Value, seep.Reagent, stored, out stored);

        var overflow = amount - stored;
        if (overflow > FixedPoint2.Zero)
            SpillOverflow(ent, seep.Reagent, overflow);

        return true;
    }

    private void SpillOverflow(Entity<FuelExtractorComponent> ent, string reagent, FixedPoint2 quantity)
    {
        var spill = new Solution(reagent, quantity);
        _puddle.TrySpillAt(ent.Owner, spill, out _, sound: false);

        if (_timing.CurTime < ent.Comp.NextSpillWarning)
            return;

        ent.Comp.NextSpillWarning = _timing.CurTime + ent.Comp.SpillWarningDelay;
        _popup.PopupEntity(Loc.GetString("fuel-extractor-popup-overflowing"), ent, PopupType.MediumCaution);
    }

    private void DrainBufferIntoContainer(Entity<FuelExtractorComponent> ent)
    {
        if (!_itemSlots.TryGetSlot(ent, ent.Comp.ContainerSlotId, out var slot) || slot.Item is not { } target)
            return;

        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out var soln, out var buffer)
            || buffer.Volume <= FixedPoint2.Zero)
            return;

        if (!_solution.TryGetRefillableSolution((target, null, null), out var targetSoln, out var targetSolution))
            return;

        var amount = FixedPoint2.Min(buffer.Volume, targetSolution.AvailableVolume);
        if (amount <= FixedPoint2.Zero)
            return;

        _solution.TryAddSolution(targetSoln.Value, _solution.SplitSolution(soln.Value, amount));
    }

    private EntityUid? ResolveSeep(Entity<FuelExtractorComponent> ent)
    {
        if (HasComp<FuelSeepComponent>(ent.Comp.Seep))
            return ent.Comp.Seep;

        ent.Comp.Seep = FindSeep(ent);
        return ent.Comp.Seep;
    }

    private EntityUid? FindSeep(EntityUid uid, TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref xform) || !xform.Anchored)
            return null;

        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, indices);
        while (anchored.MoveNext(out var ent))
        {
            if (HasComp<FuelSeepComponent>(ent.Value))
                return ent.Value;
        }

        return null;
    }

    private void UpdateSeepAppearance(Entity<FuelSeepComponent> ent)
    {
        var depleted = ent.Comp.Reserve <= FixedPoint2.Zero;
        _appearance.SetData(ent, FuelSeepVisuals.Depleted, depleted);

        if (_light.TryGetLight(ent, out var light))
            _light.SetEnabled(ent, !depleted, light);
    }

    private string GetReagentName(string reagentId)
    {
        return _proto.TryIndex<ReagentPrototype>(reagentId, out var reagent)
            ? reagent.LocalizedName
            : reagentId;
    }
}
