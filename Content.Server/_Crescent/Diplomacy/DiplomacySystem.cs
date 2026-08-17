using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._Crescent.Diplomacy;
using Content.Shared.GameTicking;
using Content.Shared.NPC.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Crescent.Diplomacy;

public sealed partial class DiplomacySystem : EntitySystem
{
    private const string DiplomacyEntityPrototype = "CrescentDiplomacy";

    [Dependency]
    private readonly IPrototypeManager _prototypeManager = default!;

    [Dependency]
    private readonly SharedShuttleSystem _shuttleSystem = default!;
    private EntityUid? _diplomacyEntity;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartedEvent>(InitializeDiplomacy);
        SubscribeLocalEvent<DiplomacyComponent, ComponentInit>(InitializeComponent);
        SubscribeLocalEvent<IFFComponent, RequestFactionRelationsEvent>(UpdateIFFRelations);

        InitializeCommands();

        LoadOverrides();
    }

    private void HandleDiplomacyChanged()
    {
        var iffs = EntityQueryEnumerator<IFFComponent>();

        while (iffs.MoveNext(out var iffGrid, out var iffComp))
        {
            _shuttleSystem.SetIFFFaction(iffGrid, iffComp.Faction, iffComp);
        }
    }

    private void InitializeDiplomacy(RoundStartedEvent ev)
    {
        _diplomacyEntity = Spawn(DiplomacyEntityPrototype, MapCoordinates.Nullspace);
    }

    private void InitializeComponent(EntityUid uid, DiplomacyComponent component, ComponentInit args)
    {
        // Claim the entity here rather than waiting for Spawn() to return. ComponentInit runs *inside* that
        // Spawn call, so HandleDiplomacyChanged below would otherwise push relations while _diplomacyEntity is
        // still null - GetRelationsForFaction hands back an empty dictionary and every IFF grid that already
        // exists at round start caches "neutral" toward its own allies until somebody changes a relation.
        _diplomacyEntity = uid;

        BuildDefaults(component);

        // Anything an admin changed in an earlier round goes on top of the prototype defaults.
        ApplyOverrides(component);

        HandleDiplomacyChanged();
    }

    /// <summary>Rebuilds the matrix from the <see cref="DiplomacyPrototype"/>s, ignoring anything set at runtime.</summary>
    private void BuildDefaults(DiplomacyComponent component)
    {
        // see how many different diplomacies we have
        var diplomacies = _prototypeManager.EnumeratePrototypes<DiplomacyPrototype>().ToArray();

        // create a grid sized just correctly to hold all of their opinions of each other
        component.DiplomaticSituation = new Relations[diplomacies.Length, diplomacies.Length];

        // track which factions are indexed where
        component.DiplomacyIndicies.Clear();
        int i = 0;
        foreach (var diplomacy in diplomacies)
        {
            component.DiplomacyIndicies.Add(diplomacy.ID, i);
            i++;
        }

        // fill in array with default relations
        int x = 0;
        int y = 0;
        while (y < diplomacies.Length)
        {
            if (x == y)
                component.DiplomaticSituation[x, y] = Relations.Ally;
            else
            {
                component.DiplomaticSituation[x, y] = Relations.Neutral;
            }

            x++;
            if (x == diplomacies.Length)
            {
                x = 0;
                y++;
            }
        }

        foreach (var diplomacy in diplomacies)
        {
            if (diplomacy.Relations == null)
                continue;

            foreach (var relation in diplomacy.Relations)
            {
                ChangeRelation(diplomacy.ID, relation.Key, relation.Value, component, true);
            }
        }
    }

    private void UpdateIFFRelations(EntityUid uid, IFFComponent component, RequestFactionRelationsEvent args)
    {
        _shuttleSystem.UpdateFactionRelations(uid, component, GetRelationsForFaction(args.Faction));
    }
    public void ChangeRelation(string faction1, string faction2, Relations newRelation, DiplomacyComponent? diplo = null, bool setup = false)
    {
        if (diplo == null && !TryComp<DiplomacyComponent>(_diplomacyEntity, out diplo))
            return;

        if (diplo.DiplomaticSituation == null)
            return;

        if (faction1 == faction2)
            return;

        if (!diplo.DiplomacyIndicies.ContainsKey(faction1) || !diplo.DiplomacyIndicies.ContainsKey(faction2))
            return;


        var previous = diplo.DiplomaticSituation[diplo.DiplomacyIndicies[faction1], diplo.DiplomacyIndicies[faction2]];

        diplo.DiplomaticSituation[diplo.DiplomacyIndicies[faction1], diplo.DiplomacyIndicies[faction2]] = newRelation;
        diplo.DiplomaticSituation[diplo.DiplomacyIndicies[faction2], diplo.DiplomacyIndicies[faction1]] = newRelation;

        if (setup)
            return;

        // Only the crossing into war, so replaying saved overrides at round start does not re-declare it.
        if (newRelation == Relations.War && previous != Relations.War)
        {
            var war = new FactionsWentToWarEvent(faction1, faction2);
            RaiseLocalEvent(ref war);
        }

        // Set at runtime rather than read out of a prototype, so it has to survive the round.
        _overrides[PairKey(faction1, faction2)] = newRelation;
        Save();

        HandleDiplomacyChanged();
    }

    public Relations GetRelations(string faction1, string faction2)
    {
        if (faction1 == faction2)
            return Relations.Ally;

        if (!TryComp<DiplomacyComponent>(_diplomacyEntity, out var diplo))
            return Relations.Neutral;

        if (diplo.DiplomaticSituation == null)
            return Relations.Neutral;

        if (!diplo.DiplomacyIndicies.ContainsKey(faction1) || !diplo.DiplomacyIndicies.ContainsKey(faction2))
            return Relations.Neutral;

        return diplo.DiplomaticSituation[diplo.DiplomacyIndicies[faction1], diplo.DiplomacyIndicies[faction2]];
    }

    /// <summary>
    ///     A grid's diplomatic faction, or null if it has none we recognise. Bare grids default their IFF
    ///     faction to the literal string "Neutral", which is not a diplomacy prototype - debris, asteroids and
    ///     unaligned hulls all land here, and must never be mistaken for a real faction in either direction.
    /// </summary>
    public string? GetGridFaction(EntityUid grid)
    {
        if (!TryComp<IFFComponent>(grid, out var iff) || string.IsNullOrEmpty(iff.Faction))
            return null;

        return _prototypeManager.HasIndex<DiplomacyPrototype>(iff.Faction) ? iff.Faction : null;
    }

    /// <summary>
    ///     Whether <paramref name="faction"/> should consider <paramref name="targetFaction"/> a valid thing to
    ///     shoot at. With <paramref name="warOnly"/> it takes a declared war; without it, anything that is not
    ///     an ally counts.
    /// </summary>
    public bool IsHostile(string faction, string targetFaction, bool warOnly = true)
    {
        var relation = GetRelations(faction, targetFaction);

        return warOnly ? relation == Relations.War : relation != Relations.Ally;
    }

    public Dictionary<string, Relations> GetRelationsForFaction(string faction)
    {
        var dict = new Dictionary<string, Relations>();

        if (!TryComp<DiplomacyComponent>(_diplomacyEntity, out var diplo))
            return dict;

        foreach (var index in diplo.DiplomacyIndicies)
        {
            dict.Add(index.Key, GetRelations(faction, index.Key));
        }

        return dict;
    }
}
