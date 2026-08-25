using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Popups;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions.Events;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Robust.Shared.Map;
using System.Numerics;
using Content.Shared.NPC.Components;
using NpcFactionSystem = Content.Shared.NPC.Systems.NpcFactionSystem;


namespace Content.Server.Abilities.Psionics;

public sealed partial class PsionicFamiliarSystem : EntitySystem
{
    [Dependency] private readonly SharedPsionicAbilitiesSystem _psionics = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    /// How close to yourself you have to point the move order for it to mean "come back" rather than
    /// "go and stand there".
    /// </summary>
    private const float RecallRange = 1.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PsionicComponent, SummonPsionicFamiliarActionEvent>(OnSummon);
        SubscribeLocalEvent<PsionicFamiliarComponent, ComponentShutdown>(OnFamiliarShutdown);
        SubscribeLocalEvent<PsionicFamiliarComponent, AttackAttemptEvent>(OnFamiliarAttack);
        SubscribeLocalEvent<PsionicFamiliarComponent, MobStateChangedEvent>(OnFamiliarDeath);
        SubscribeLocalEvent<PsionicComponent, CommandPsionicFamiliarMoveActionEvent>(OnMoveCommand);
        SubscribeLocalEvent<PsionicComponent, CommandPsionicFamiliarAttackActionEvent>(OnAttackCommand);
    }

    private void OnSummon(EntityUid uid, PsionicComponent psionicComponent, SummonPsionicFamiliarActionEvent args)
    {
        if ((psionicComponent.Familiars.Count >= psionicComponent.FamiliarLimit && args.IgnoreFamiliarLimit == false)
            || !_psionics.OnAttemptPowerUse(args.Performer, args.PowerName, args.CheckInsulation)
            || args.Handled || args.FamiliarProto is null)
            return;

        args.Handled = true;
        var familiar = Spawn(args.FamiliarProto, Transform(uid).Coordinates);
        EnsureComp<PsionicFamiliarComponent>(familiar, out var familiarComponent);
        familiarComponent.Master = uid;
        psionicComponent.Familiars.Add(familiar);
        Dirty(familiar, familiarComponent);
        Dirty(uid, psionicComponent);

        InheritFactions(uid, familiar, familiarComponent);
        HandleBlackboards(uid, familiar, args);
        DoGlimmerEffects(uid, psionicComponent, args);
    }

    private void InheritFactions(EntityUid uid, EntityUid familiar, PsionicFamiliarComponent familiarComponent)
    {
        if (!familiarComponent.InheritMasterFactions
            || !TryComp<NpcFactionMemberComponent>(uid, out var masterFactions)
            || masterFactions.Factions.Count <= 0)
            return;

        EnsureComp<NpcFactionMemberComponent>(familiar, out var familiarFactions);
        foreach (var faction in masterFactions.Factions)
        {
            if (_factions.IsMember(familiar, faction))
                continue;

            _factions.AddFaction(familiar, faction, true);
        }
    }

    private void HandleBlackboards(EntityUid master, EntityUid familiar, SummonPsionicFamiliarActionEvent args)
    {
        if (!args.FollowMaster
            || !TryComp<HTNComponent>(familiar, out var htnComponent))
            return;

        _npc.SetBlackboard(familiar, NPCBlackboard.FollowTarget, new EntityCoordinates(master, Vector2.Zero), htnComponent);
        _htn.Replan(htnComponent);
    }

    private void DoGlimmerEffects(EntityUid uid, PsionicComponent component, SummonPsionicFamiliarActionEvent args)
    {
        if (!args.DoGlimmerEffects
            || args.MinGlimmer == 0 && args.MaxGlimmer == 0)
            return;

        var minGlimmer = (int) Math.Round(MathF.MinMagnitude(args.MinGlimmer, args.MaxGlimmer)
            * component.CurrentAmplification - component.CurrentDampening);
        var maxGlimmer = (int) Math.Round(MathF.MaxMagnitude(args.MinGlimmer, args.MaxGlimmer)
            * component.CurrentAmplification - component.CurrentDampening);

        _psionics.LogPowerUsed(uid, args.PowerName, minGlimmer, maxGlimmer);
    }

    private void OnFamiliarShutdown(EntityUid uid, PsionicFamiliarComponent component, ComponentShutdown args)
    {
        if (!Exists(component.Master)
            || !TryComp<PsionicComponent>(component.Master, out var psionicComponent)
            || !psionicComponent.Familiars.Contains(uid))
            return;

        psionicComponent.Familiars.Remove(uid);
    }

    private void OnFamiliarAttack(EntityUid uid, PsionicFamiliarComponent component, AttackAttemptEvent args)
    {
        if (component.CanAttackMaster || args.Target is null
            || args.Target != component.Master)
            return;

        args.Cancel();
        if (!Loc.TryGetString(component.AttackMasterText, out var attackFailMessage))
            return;

        _popup.PopupEntity(attackFailMessage, uid, uid, component.AttackPopupType);
    }

    private void OnFamiliarDeath(EntityUid uid, PsionicFamiliarComponent component, MobStateChangedEvent args)
    {
        if (!component.DespawnOnFamiliarDeath
            || args.NewMobState != MobState.Dead)
            return;

        DespawnFamiliar(uid, component);
    }

    private void OnMoveCommand(
        Entity<PsionicComponent> ent,
        ref CommandPsionicFamiliarMoveActionEvent args)
    {
        if (args.Handled)
            return;

        // Pointing at your own feet is how you call the familiar off a standing order and put it
        // back on your heel. Without it a move order could only ever be replaced by another one.
        var origin = _transform.GetMapCoordinates(ent.Owner);
        var destination = _transform.ToMapCoordinates(args.Target);
        var recall = destination.MapId == origin.MapId
            && (destination.Position - origin.Position).Length() <= RecallRange;

        foreach (var familiar in ent.Comp.Familiars)
        {
            if (!TryComp<PsionicFamiliarComponent>(familiar, out var familiarComp)
                || !familiarComp.Commandable
                || !TryComp<HTNComponent>(familiar, out var htn))
                continue;

            htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);

            if (recall)
                htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.OrderedMoveTarget);
            else
                _npc.SetBlackboard(familiar, NPCBlackboard.OrderedMoveTarget, args.Target, htn);

            ForceReplan(htn);
            args.Handled = true;
        }

        if (args.Handled)
        {
            _popup.PopupEntity(
                Loc.GetString(recall ? "psionic-familiar-order-recall" : "psionic-familiar-order-move"),
                ent.Owner,
                ent.Owner);
        }
    }

    private void OnAttackCommand(
        Entity<PsionicComponent> ent,
        ref CommandPsionicFamiliarAttackActionEvent args)
    {
        if (args.Handled || args.Target == ent.Owner)
            return;

        foreach (var familiar in ent.Comp.Familiars)
        {
            if (!TryComp<PsionicFamiliarComponent>(familiar, out var familiarComp)
                || !familiarComp.Commandable
                || args.Target == familiar
                || !TryComp<HTNComponent>(familiar, out var htn))
                continue;

            // An attack order replaces a standing move order rather than fighting it for priority.
            htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.OrderedMoveTarget);
            _npc.SetBlackboard(familiar, NPCBlackboard.CurrentOrderedTarget, args.Target, htn);
            ForceReplan(htn);
            args.Handled = true;
        }

        if (args.Handled)
        {
            _popup.PopupEntity(
                Loc.GetString("psionic-familiar-order-attack", ("target", args.Target)),
                ent.Owner,
                ent.Owner);
        }
    }

    /// <summary>
    /// Tears the running plan down before asking for a new one.
    /// </summary>
    /// <remarks>
    /// <see cref="HTNSystem.Replan"/> on its own only schedules a fresh planning pass, and the result
    /// is thrown away unless it comes from a lower-numbered branch than the plan already running. An
    /// order that moves the familiar <em>down</em> the tree - calling it off a target so it can go
    /// where it was pointed - would be silently discarded, which is exactly what "it does not listen"
    /// looks like from the other end. Dropping the current plan first means the next one always wins.
    /// </remarks>
    private void ForceReplan(HTNComponent htn)
    {
        if (htn.Plan != null)
        {
            _htn.ShutdownTask(htn.Plan.CurrentOperator, htn.Blackboard, HTNOperatorStatus.Failed);
            _htn.ShutdownPlan(htn);
        }

        _htn.Replan(htn);
    }

    public void DespawnFamiliar(EntityUid uid)
    {
        if (!TryComp<PsionicFamiliarComponent>(uid, out var familiarComponent))
            return;

        DespawnFamiliar(uid, familiarComponent);
    }

    public void DespawnFamiliar(EntityUid uid, PsionicFamiliarComponent component)
    {
        var popupText = Loc.GetString(component.DespawnText, ("entity", MetaData(uid).EntityName));
        _popup.PopupEntity(popupText, uid, component.DespawnPopopType);
        QueueDel(uid);
    }
}
