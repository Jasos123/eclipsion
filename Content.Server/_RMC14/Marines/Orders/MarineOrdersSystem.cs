using Content.Server.Actions;
using Content.Server.Chat.Systems;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared._RMC14.Marines.Orders;
using Content.Shared.Chat;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._RMC14.Marines.Orders;

public sealed class MarineOrdersSystem : SharedMarineOrdersSystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MarineOrdersComponent, MapInitEvent>(OnOrdersMapInit);
        SubscribeLocalEvent<MarineOrdersComponent, ComponentShutdown>(OnOrdersShutdown);
    }

    private void OnOrdersMapInit(Entity<MarineOrdersComponent> ent, ref MapInitEvent args)
    {
        var comp = ent.Comp;

        // The used action needs its own delay in addition to the shared cooldown.
        _actions.AddAction(ent, ref comp.MoveActionEntity, comp.MoveAction);
        _actions.SetUseDelay(comp.MoveActionEntity, comp.Cooldown);
        _actions.AddAction(ent, ref comp.HoldActionEntity, comp.HoldAction);
        _actions.SetUseDelay(comp.HoldActionEntity, comp.Cooldown);
        _actions.AddAction(ent, ref comp.FocusActionEntity, comp.FocusAction);
        _actions.SetUseDelay(comp.FocusActionEntity, comp.Cooldown);
    }

    private void OnOrdersShutdown(Entity<MarineOrdersComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.FocusActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.HoldActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.MoveActionEntity);
    }

    protected override void OnMoveAction(Entity<MarineOrdersComponent> ent, ref MoveActionEvent args)
    {
        var wasHandled = args.Handled;
        base.OnMoveAction(ent, ref args);

        if (!wasHandled && args.Handled)
            Callout(ent, MarineOrderType.Move);
    }

    protected override void OnHoldAction(Entity<MarineOrdersComponent> ent, ref HoldActionEvent args)
    {
        var wasHandled = args.Handled;
        base.OnHoldAction(ent, ref args);

        if (!wasHandled && args.Handled)
            Callout(ent, MarineOrderType.Hold);
    }

    protected override void OnFocusAction(Entity<MarineOrdersComponent> ent, ref FocusActionEvent args)
    {
        var wasHandled = args.Handled;
        base.OnFocusAction(ent, ref args);

        if (!wasHandled && args.Handled)
            Callout(ent, MarineOrderType.Focus);
    }

    private void Callout(Entity<MarineOrdersComponent> ent, MarineOrderType type)
    {
        var callouts = GetCallouts(ent, type);

        if (callouts.Count == 0)
            return;

        _chat.TrySendInGameICMessage(ent, Loc.GetString(_random.Pick(callouts)), InGameICChatType.Speak, false);
    }

    /// <summary>
    /// The issuer's faction callouts take priority, the component lists are the generic fallback.
    /// </summary>
    private List<LocId> GetCallouts(Entity<MarineOrdersComponent> ent, MarineOrderType type)
    {
        if (TryComp(ent, out HullrotFactionComponent? faction) &&
            !string.IsNullOrEmpty(faction.Faction) &&
            _prototypes.TryIndex<MarineOrderCalloutsPrototype>(faction.Faction, out var set))
        {
            var factionCallouts = type switch
            {
                MarineOrderType.Move => set.Move,
                MarineOrderType.Hold => set.Hold,
                _ => set.Focus,
            };

            if (factionCallouts.Count > 0)
                return factionCallouts;
        }

        return type switch
        {
            MarineOrderType.Move => ent.Comp.MoveCallouts,
            MarineOrderType.Hold => ent.Comp.HoldCallouts,
            _ => ent.Comp.FocusCallouts,
        };
    }
}
