using Content.Shared._Crescent.DegradeableArmor;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions.Events;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Abilities.Psionics;

/// <summary>
/// Winds worn armour back to the state it was in before it was ever shot, without a welder and
/// without taking it off.
///
/// The catch is that the plate does not always survive being asked to remember: a small fraction of
/// the time the weave unravels completely and what is left is the stock it was pressed from,
/// dropped at the wearer's feet.
/// </summary>
public sealed class PsionicArmorReweaveSystem : EntitySystem
{
    /// <summary>
    /// Per piece of armour, not per cast. Wearing a full rig is more to reweave and more to lose.
    /// </summary>
    private const float UnravelChance = 0.01f;

    /// <summary>
    /// How many plates a destroyed piece leaves behind.
    /// </summary>
    private const int SalvagePlates = 2;

    /// <summary>
    /// Sound-only effects. The audio for every psionic power lives in
    /// Prototypes/Entities/Effects/psionics.yml rather than in constants scattered across systems.
    /// </summary>
    private const string ReweaveEffect = "EffectPsionicReweave";

    private const string UnravelEffect = "EffectPsionicUnravel";

    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPsionicAbilitiesSystem _psionics = default!;

    /// <summary>
    /// Repair material to the plate entity that supplies it. Built from prototypes rather than
    /// hardcoded so a new plate is picked up without touching this file.
    /// </summary>
    private readonly Dictionary<ArmorRepairMaterial, EntProtoId> _salvage = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PsionicArmorReweaveActionEvent>(OnReweave);
    }

    private void BuildSalvageTable()
    {
        foreach (var proto in _prototypeManager.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract
                || !proto.TryGetComponent<ArmorRepairKitComponent>(out var kit, _componentFactory))
            {
                continue;
            }

            // First match wins, so an id collision cannot silently reshuffle the table between rounds.
            _salvage.TryAdd(kit.materialType, proto.ID);
        }
    }

    private void OnReweave(PsionicArmorReweaveActionEvent args)
    {
        if (args.Handled || !_psionics.OnAttemptPowerUse(args.Performer, "reweave", true))
            return;

        // Built on first use rather than at startup: prototypes are not loaded when systems
        // initialise. CanUnravel reads it, so it has to be standing before the walk below.
        if (_salvage.Count == 0)
            BuildSalvageTable();

        var repaired = 0;
        var destroyed = 0;

        // Materialised up front: unravelling deletes entities, which would otherwise mutate the
        // slots mid-walk.
        var worn = new List<EntityUid>();
        var slots = _inventory.GetSlotEnumerator(args.Performer);
        while (slots.NextItem(out var item))
        {
            AddPiece(worn, item);

            // A stowed hardsuit helmet degrades on its own but is not in a slot of its own, so
            // without this it is only ever repaired for free as a passenger of the suit - and the
            // same helmet worn on the head takes its own unravel risk. A deployed one is in both
            // places at once, which is what AddPiece is guarding against.
            if (TryComp<ToggleableClothingComponent>(item, out var toggleable))
            {
                foreach (var (attached, _) in toggleable.ClothingUids)
                    AddPiece(worn, attached);
            }
        }

        // Pieces whose stock has already been paid out by the suit they were locked to. They are
        // queued for deletion rather than gone, so they still answer to Deleted and TryComp for the
        // rest of this cast and would otherwise be salvaged a second time.
        var salvaged = new HashSet<EntityUid>();

        foreach (var armor in worn)
        {
            if (Deleted(armor)
                || salvaged.Contains(armor)
                || !TryComp<DegradeableArmorComponent>(armor, out var degradeable))
            {
                continue;
            }

            if (degradeable.armorHealth >= degradeable.armorMaxHealth)
                continue;

            // Rolled only for armour there is something to do to. Rolling first meant pressing the
            // power in undamaged kit could destroy it for nothing, and then bailing out on the
            // "nothing to repair" path without ever setting Handled - so the cooldown never
            // started and the risk could be taken again immediately.
            if (CanUnravel(degradeable) && _random.Prob(UnravelChance))
            {
                Unravel(args.Performer, armor, degradeable, salvaged);
                destroyed++;
                continue;
            }

            Restore(armor, degradeable);
            repaired++;
        }

        if (repaired == 0 && destroyed == 0)
        {
            _popup.PopupEntity(
                Loc.GetString("psionic-armor-reweave-nothing"),
                args.Performer,
                args.Performer,
                PopupType.SmallCaution);
            return;
        }

        if (repaired > 0)
        {
            Spawn(ReweaveEffect, Transform(args.Performer).Coordinates);
            _popup.PopupEntity(
                Loc.GetString("psionic-armor-reweave-restored", ("count", repaired)),
                args.Performer,
                args.Performer,
                PopupType.Medium);
        }

        _psionics.LogPowerUsed(args.Performer, "reweave", 4, 7);
        args.Handled = true;
    }

    /// <summary>
    /// Returns a piece to full. Anything it deploys is a piece in its own right and is walked
    /// separately, so a hardsuit and its helmet each take their own roll.
    /// </summary>
    private void Restore(EntityUid armor, DegradeableArmorComponent degradeable)
    {
        degradeable.armorHealth = degradeable.armorMaxHealth;
        Dirty(armor, degradeable);
    }

    /// <summary>
    /// Whether a piece can be unravelled at all. Some armour is made of stock no plate exists for -
    /// DuraThread has no <see cref="ArmorRepairKitComponent"/> anywhere in the prototypes - and
    /// unravelling that would delete the piece and leave nothing behind, which is a pure loss rather
    /// than the trade the power is meant to be.
    /// </summary>
    private bool CanUnravel(DegradeableArmorComponent degradeable)
    {
        return _salvage.ContainsKey(degradeable.armorRepair);
    }

    private void Unravel(
        EntityUid wearer,
        EntityUid armor,
        DegradeableArmorComponent degradeable,
        HashSet<EntityUid> salvaged)
    {
        var coordinates = Transform(wearer).Coordinates;

        SpawnPlates(degradeable, coordinates);
        salvaged.Add(armor);

        // Deleting a hardsuit deletes the helmet locked to it, so the helmet's own stock has to come
        // out here or it is destroyed for nothing.
        if (TryComp<ToggleableClothingComponent>(armor, out var toggleable))
        {
            foreach (var (attached, _) in toggleable.ClothingUids)
            {
                if (Deleted(attached) || !TryComp<DegradeableArmorComponent>(attached, out var attachedArmor))
                    continue;

                SpawnPlates(attachedArmor, coordinates);
                salvaged.Add(attached);
            }
        }

        var name = Name(armor);
        QueueDel(armor);

        Spawn(UnravelEffect, coordinates);
        _popup.PopupEntity(
            Loc.GetString("psionic-armor-reweave-unravelled", ("armor", name)),
            wearer,
            wearer,
            PopupType.LargeCaution);
    }

    /// <summary>
    /// Adds a piece of armour to the list to work through, once. A deployed hardsuit helmet is both
    /// an item in the head slot and an attachment of the suit, and being walked twice means being
    /// rolled twice and counted twice.
    /// </summary>
    private void AddPiece(List<EntityUid> worn, EntityUid piece)
    {
        if (!worn.Contains(piece) && HasComp<DegradeableArmorComponent>(piece))
            worn.Add(piece);
    }

    private void SpawnPlates(DegradeableArmorComponent degradeable, EntityCoordinates coordinates)
    {
        if (!_salvage.TryGetValue(degradeable.armorRepair, out var plate))
            return;

        for (var i = 0; i < SalvagePlates; i++)
            Spawn(plate, coordinates);
    }
}
