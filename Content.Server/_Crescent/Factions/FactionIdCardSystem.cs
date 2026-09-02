using Content.Server.Access.Systems;
using Content.Server.Jobs;
using Content.Shared.Access.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Roles;
using Content.Shared._Crescent.HullrotFaction;

namespace Content.Server._Crescent.Factions;

/// <summary>
///     Assigns faction identity to preset ID cards and resolves the credential worn in a mob's ID slot.
/// </summary>
public sealed partial class FactionIdCardSystem : EntitySystem
{
    [Dependency] private readonly IdCardSystem _idCards = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DidEquipEvent>(OnDidEquip);
    }

    private void OnDidEquip(DidEquipEvent args)
    {
        if (args.Slot != "id" ||
            !_idCards.TryGetIdCard(args.Equipment, out var idCard) ||
            !TryComp<FactionIdCardComponent>(idCard, out var factionId))
        {
            return;
        }

        TrackCredential(args.Equipee, idCard, factionId);
    }

    private void TrackCredential(
        EntityUid memberUid,
        Entity<IdCardComponent> idCard,
        FactionIdCardComponent factionId)
    {
        if (!TryComp<HullrotFactionComponent>(memberUid, out var member) ||
            factionId.Faction != member.Faction ||
            idCard.Comp.FullName != MetaData(memberUid).EntityName)
        {
            return;
        }

        var tracker = EnsureComp<FactionCredentialTrackerComponent>(memberUid);
        if (tracker.Faction != factionId.Faction)
        {
            foreach (var oldCard in tracker.Cards)
                ClearFaction(oldCard, tracker.Faction);

            tracker.Cards.Clear();
            tracker.Faction = factionId.Faction;
        }

        tracker.Cards.Add(idCard);
    }

    /// <summary>
    ///     Copies the faction granted by a job onto an ID. Returns false for jobs without a faction grant.
    /// </summary>
    public bool SetFactionFromJob(EntityUid id, JobPrototype job)
    {
        foreach (var special in job.Special)
        {
            if (special is not AddComponentSpecial add)
                continue;

            foreach (var entry in add.Components.Values)
            {
                if (entry.Component is not HullrotFactionComponent faction ||
                    string.IsNullOrWhiteSpace(faction.Faction))
                {
                    continue;
                }

                SetFaction(id, faction.Faction);
                return true;
            }
        }

        return false;
    }

    /// <summary>Sets the faction credential advertised by an ID card.</summary>
    public void SetFaction(EntityUid id, string faction)
    {
        var component = EnsureComp<FactionIdCardComponent>(id);
        component.Faction = faction.Trim();
    }

    /// <summary>
    /// Removes a faction credential from an ID. If <paramref name="expectedFaction"/> is supplied, a credential
    /// belonging to another faction is left untouched.
    /// </summary>
    public bool ClearFaction(EntityUid id, string? expectedFaction = null)
    {
        if (!TryComp<FactionIdCardComponent>(id, out var component) ||
            expectedFaction != null && component.Faction != expectedFaction)
        {
            return false;
        }

        RemComp<FactionIdCardComponent>(id);
        return true;
    }

    /// <summary>
    /// Gets the actual card contained by the item equipped in <paramref name="wearer"/>'s ID slot.
    /// </summary>
    public bool TryGetWornIdCard(EntityUid wearer, out Entity<IdCardComponent> idCard)
    {
        idCard = default;

        return _inventory.TryGetSlotEntity(wearer, "id", out var idItem) &&
               _idCards.TryGetIdCard(idItem.Value, out idCard);
    }

    /// <summary>
    ///     Gets the faction from the ID actually equipped in <paramref name="wearer"/>'s ID slot. A card merely
    ///     held in an active hand is intentionally not accepted.
    /// </summary>
    public bool TryGetWornFaction(EntityUid wearer, out string faction)
    {
        faction = string.Empty;

        if (!TryGetWornIdCard(wearer, out var id) ||
            !TryComp<FactionIdCardComponent>(id, out var factionId) ||
            string.IsNullOrWhiteSpace(factionId.Faction))
        {
            return false;
        }

        TrackCredential(wearer, id, factionId);
        faction = factionId.Faction;
        return true;
    }
}
