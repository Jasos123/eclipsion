using Content.Server.Access.Systems;
using Content.Server.Jobs;
using Content.Shared.Inventory;
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
    ///     Gets the faction from the ID actually equipped in <paramref name="wearer"/>'s ID slot. A card merely
    ///     held in an active hand is intentionally not accepted.
    /// </summary>
    public bool TryGetWornFaction(EntityUid wearer, out string faction)
    {
        faction = string.Empty;

        if (!_inventory.TryGetSlotEntity(wearer, "id", out var idItem) ||
            !_idCards.TryGetIdCard(idItem.Value, out var id) ||
            !TryComp<FactionIdCardComponent>(id, out var factionId) ||
            string.IsNullOrWhiteSpace(factionId.Faction))
        {
            return false;
        }

        faction = factionId.Faction;
        return true;
    }
}
