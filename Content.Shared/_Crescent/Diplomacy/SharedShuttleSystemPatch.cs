using Content.Shared._Crescent.Diplomacy;
using Content.Shared.Shuttles.Components;
using JetBrains.Annotations;

namespace Content.Shared.Shuttles.Systems;

public abstract partial class SharedShuttleSystem
{
    [PublicAPI]
    public void SetIFFFaction(EntityUid gridUid, string faction, IFFComponent? component = null)
    {
        component ??= EnsureComp<IFFComponent>(gridUid);

        component.Faction = faction;

        var ev = new RequestFactionRelationsEvent(faction);
        RaiseLocalEvent(gridUid, ev);
    }

    public void UpdateFactionRelations(EntityUid uid, IFFComponent component, Dictionary<string, Relations> relations)
    {
        component.Relations = relations;
        Dirty(uid, component);
        UpdateIFFInterfaces(uid, component);
    }

    /// <summary>
    /// The faction a grid is advertising. Anything without a transponder counts as Neutral.
    /// </summary>
    [PublicAPI]
    public string GetIFFFaction(EntityUid gridUid, IFFComponent? component = null)
    {
        return Resolve(gridUid, ref component, false) ? component.Faction : "Neutral";
    }

    /// <summary>
    /// How the given grid feels about another faction. Unknown factions are Neutral.
    /// </summary>
    [PublicAPI]
    public Relations GetIFFRelation(EntityUid gridUid, string otherFaction, IFFComponent? component = null)
    {
        if (!Resolve(gridUid, ref component, false))
            return Relations.Neutral;

        if (component.Faction == otherFaction)
            return Relations.Ally;

        return component.Relations.TryGetValue(otherFaction, out var relation) ? relation : Relations.Neutral;
    }
}
