using Content.Shared._Crescent.HullrotFaction;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Marines.Orders;

/// <summary>
/// Applies timed squad orders to nearby allies.
/// </summary>
public abstract class SharedMarineOrdersSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly HashSet<Entity<MobStateComponent>> _receivers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoveOrderComponent, EntityUnpausedEvent>(OnUnpause);
        SubscribeLocalEvent<FocusOrderComponent, EntityUnpausedEvent>(OnUnpause);
        SubscribeLocalEvent<HoldOrderComponent, EntityUnpausedEvent>(OnUnpause);

        SubscribeLocalEvent<MarineOrdersComponent, FocusActionEvent>(OnFocusAction);
        SubscribeLocalEvent<MarineOrdersComponent, HoldActionEvent>(OnHoldAction);
        SubscribeLocalEvent<MarineOrdersComponent, MoveActionEvent>(OnMoveAction);

        SubscribeLocalEvent<MoveOrderComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
        SubscribeLocalEvent<MoveOrderComponent, ComponentStartup>(OnMoveStartup);
        SubscribeLocalEvent<MoveOrderComponent, ComponentShutdown>(OnMoveShutdown);
        SubscribeLocalEvent<MoveOrderComponent, AfterAutoHandleStateEvent>(OnMoveState);

        SubscribeLocalEvent<HoldOrderComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnUnpause<T>(Entity<T> order, ref EntityUnpausedEvent args) where T : IComponent, IOrderComponent
    {
        order.Comp.ExpiresAt += args.PausedTime;
    }

    private void OnDamageModify(Entity<HoldOrderComponent> order, ref DamageModifyEvent args)
    {
        var comp = order.Comp;
        var damage = args.Damage.DamageDict;
        var multiplier = Math.Max(0f, 1f - comp.DamageModifier * comp.Power);

        foreach (var type in comp.DamageTypes)
        {
            if (damage.TryGetValue(type, out var amount))
                damage[type] = amount * multiplier;
        }
    }

    private void OnRefreshMovement(Entity<MoveOrderComponent> order, ref RefreshMovementSpeedModifiersEvent args)
    {
        var speed = 1f + order.Comp.MoveSpeedModifier * order.Comp.Power;
        args.ModifySpeed(speed, speed);
    }

    private void OnMoveStartup(Entity<MoveOrderComponent> order, ref ComponentStartup args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(order);
    }

    private void OnMoveShutdown(Entity<MoveOrderComponent> order, ref ComponentShutdown args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(order);
    }

    private void OnMoveState(Entity<MoveOrderComponent> order, ref AfterAutoHandleStateEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(order);
    }

    protected virtual void OnFocusAction(Entity<MarineOrdersComponent> orders, ref FocusActionEvent args)
    {
        if (!args.Handled && HandleAction<FocusOrderComponent>(orders))
            args.Handled = true;
    }

    protected virtual void OnHoldAction(Entity<MarineOrdersComponent> orders, ref HoldActionEvent args)
    {
        if (!args.Handled && HandleAction<HoldOrderComponent>(orders))
            args.Handled = true;
    }

    protected virtual void OnMoveAction(Entity<MarineOrdersComponent> orders, ref MoveActionEvent args)
    {
        if (!args.Handled && HandleAction<MoveOrderComponent>(orders))
            args.Handled = true;
    }

    private bool HandleAction<T>(Entity<MarineOrdersComponent> orders) where T : IComponent, IOrderComponent, new()
    {
        if (!TryComp(orders, out TransformComponent? xform) || _mobState.IsDead(orders))
            return false;

        var comp = orders.Comp;
        var power = Math.Max(1, comp.Power);
        var expiresAt = _timing.CurTime + comp.Duration * (power + 1);

        string? faction = null;
        if (comp.RequireSameFaction)
        {
            if (!TryComp(orders, out HullrotFactionComponent? issuerFaction) ||
                string.IsNullOrEmpty(issuerFaction.Faction))
            {
                return false;
            }

            faction = issuerFaction.Faction;
        }

        // Start the shared cooldown only after the order passes validation.
        _actions.SetCooldown(comp.FocusActionEntity, comp.Cooldown);
        _actions.SetCooldown(comp.MoveActionEntity, comp.Cooldown);
        _actions.SetCooldown(comp.HoldActionEntity, comp.Cooldown);

        _receivers.Clear();
        _entityLookup.GetEntitiesInRange(xform.Coordinates, comp.OrderRange, _receivers);

        foreach (var receiver in _receivers)
        {
            if (_mobState.IsDead(receiver))
                continue;

            if (faction != null &&
                (!TryComp(receiver, out HullrotFactionComponent? receiverFaction) ||
                 receiverFaction.Faction != faction))
            {
                continue;
            }

            AddOrder<T>(receiver, power, expiresAt);
        }

        return true;
    }

    /// <summary>
    /// Applies an order without replacing a stronger active order.
    /// </summary>
    private void AddOrder<T>(EntityUid receiver, int power, TimeSpan expiresAt) where T : IComponent, IOrderComponent, new()
    {
        var comp = EnsureComp<T>(receiver);

        if (comp.Power > power && comp.ExpiresAt > _timing.CurTime)
            return;

        comp.Power = power;
        comp.ExpiresAt = expiresAt > comp.ExpiresAt ? expiresAt : comp.ExpiresAt;
        Dirty(receiver, comp);

        _movementSpeed.RefreshMovementSpeedModifiers(receiver);
    }

    private void RemoveExpired<T>() where T : IComponent, IOrderComponent
    {
        var time = _timing.CurTime;
        var query = EntityQueryEnumerator<T>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ExpiresAt <= time)
                RemCompDeferred<T>(uid);
        }
    }

    public void StartActionUseDelay(Entity<MarineOrdersComponent> orders)
    {
        _actions.StartUseDelay(orders.Comp.HoldActionEntity);
        _actions.StartUseDelay(orders.Comp.MoveActionEntity);
        _actions.StartUseDelay(orders.Comp.FocusActionEntity);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        RemoveExpired<MoveOrderComponent>();
        RemoveExpired<FocusOrderComponent>();
        RemoveExpired<HoldOrderComponent>();
    }
}
