using Content.Shared.Abilities.Psionics;
using Content.Shared.StatusEffect;
using Content.Shared.Psionics;
using Content.Shared.Psionics.Glimmer;
using Content.Shared.Random;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Damage.Events;
using Content.Shared.CCVar;
using Content.Server.Abilities.Psionics;
using Content.Server.Electrocution;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Random;
using Content.Shared.Popups;
using Content.Shared.Chat;
using Robust.Server.Player;
using Content.Server.Chat.Managers;
using Robust.Shared.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Damage;
using Content.Shared.Interaction.Events;
using Timer = Robust.Shared.Timing.Timer;
using Content.Shared.Alert;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Rounding;

namespace Content.Server.Psionics;

public sealed class PsionicsSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly ElectrocutionSystem _electrocutionSystem = default!;
    [Dependency] private readonly MindSwapPowerSystem _mindSwapPowerSystem = default!;
    [Dependency] private readonly GlimmerSystem _glimmerSystem = default!;
    [Dependency] private readonly NpcFactionSystem _npcFactonSystem = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly PsionicFamiliarSystem _psionicFamiliar = default!;
    [Dependency] private readonly NPCRetaliationSystem _retaliationSystem = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly PsionicSkillTreeSystem _skillTreeSystem = default!;

    private const string BaselineAmplification = "Baseline Amplification";
    private const string BaselineDampening = "Baseline Dampening";

    // Baseline values used for Potentia-based level progression.
    // We haven't generated a prototype yet, and I'm not going to duplicate them on the PsionicComponent.
    private const string PsionicLevelUpMessage = "psionic-skill-tree-level-up";
    private const string PsionicLevelUpColor = "#B55CFF";
    private const int PsionicLevelUpFontSize = 14;
    private const ChatChannel PsionicLevelUpChatChannel = ChatChannel.Emotes;

    /// <summary>
    ///     Unfortunately, since spawning as a normal role and anything else is so different,
    ///     this is the only way to unify them, for now at least.
    /// </summary>
    Queue<(PsionicComponent component, EntityUid uid)> _rollers = new();
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_cfg.GetCVar(CCVars.PsionicRollsEnabled))
            return;

        foreach (var roller in _rollers)
            RollPsionics(roller.uid, roller.component, true);
        _rollers.Clear();
    }
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PsionicComponent, MapInitEvent>(OnStartup);
        SubscribeLocalEvent<AntiPsionicWeaponComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<AntiPsionicWeaponComponent, TakeStaminaDamageEvent>(OnStamHit);
        SubscribeLocalEvent<PsionicComponent, MobStateChangedEvent>(OnMobstateChanged);
        SubscribeLocalEvent<PsionicComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<PsionicComponent, AttackAttemptEvent>(OnAttackAttempt);

        SubscribeLocalEvent<PsionicComponent, ComponentStartup>(OnInit);
        SubscribeLocalEvent<PsionicComponent, ComponentRemove>(OnRemove);
    }

    private void OnStartup(EntityUid uid, PsionicComponent component, MapInitEvent args)
    {
        if (!component.CanReroll)
            return;

        Timer.Spawn(TimeSpan.FromSeconds(30), () => DeferRollers(uid));

    }

    /// <summary>
    ///     We wait a short time before starting up the rolled powers, so that other systems have a chance to modify the list first.
    ///     This is primarily for the sake of TraitSystem and AddJobSpecial.
    /// </summary>
    private void DeferRollers(EntityUid uid)
    {
        if (!Exists(uid)
            || !TryComp(uid, out PsionicComponent? component))
            return;

        CheckPowerCost(uid, component);
        GenerateAvailablePowers(component);
        _rollers.Enqueue((component, uid));
    }

    /// <summary>
    ///     Keep the next threshold tied to level rather than to a randomly selected power count.
    /// </summary>
    private void CheckPowerCost(EntityUid uid, PsionicComponent component)
    {
        component.NextPowerCost = GetNextLevelCost(component);
        Dirty(uid, component);
    }

    private static float GetNextLevelCost(PsionicComponent component)
    {
        return Math.Max(1f,
            Math.Abs(component.BaselinePowerCost * MathF.Pow(2, Math.Max(0, component.PsionicLevel - 1))));
    }

    /// <summary>
    ///     The power pool is itself a DataField, and things like Traits/Antags are allowed to modify or replace the pool.
    /// </summary>
    private void GenerateAvailablePowers(PsionicComponent component)
    {
        if (!_protoMan.TryIndex<WeightedRandomPrototype>(component.PowerPool.Id, out var pool))
            return;

        foreach (var id in pool.Weights)
        {
            if (!_protoMan.TryIndex<PsionicPowerPrototype>(id.Key, out var power)
                || component.ActivePowers.Contains(power))
                continue;

            component.AvailablePowers.TryAdd(id.Key, id.Value);
        }
    }

    private void OnMeleeHit(EntityUid uid, AntiPsionicWeaponComponent component, MeleeHitEvent args)
    {
        foreach (var entity in args.HitEntities)
            CheckAntiPsionic(entity, component, args);
    }

    private void CheckAntiPsionic(EntityUid entity, AntiPsionicWeaponComponent component, MeleeHitEvent args)
    {
        if (HasComp<PsionicComponent>(entity))
        {
            _audio.PlayPvs("/Audio/Effects/lightburn.ogg", entity);
            args.ModifiersList.Add(component.Modifiers);

            if (!_random.Prob(component.DisableChance))
                return;

            _statusEffects.TryAddStatusEffect(entity, component.DisableStatus, TimeSpan.FromSeconds(component.DisableDuration), true, component.DisableStatus);
        }

        if (TryComp<MindSwappedComponent>(entity, out var swapped))
            _mindSwapPowerSystem.Swap(entity, swapped.OriginalEntity, true);

        if (!component.Punish
            || HasComp<PsionicComponent>(entity)
            || !_random.Prob(component.PunishChances))
            return;

        _electrocutionSystem.TryDoElectrocution(args.User, null, component.PunishSelfDamage, TimeSpan.FromSeconds(component.PunishStunDuration), false);
    }

    private void OnInit(EntityUid uid, PsionicComponent component, ComponentStartup args)
    {
        _skillTreeSystem.InitializeTreeAction(uid, component);
        component.AmplificationSources.Add(BaselineAmplification, _random.NextFloat(component.BaselineAmplification.Item1, component.BaselineAmplification.Item2));
        component.DampeningSources.Add(BaselineDampening, _random.NextFloat(component.BaselineDampening.Item1, component.BaselineDampening.Item2));

        if (!component.Removable
            || !TryComp<NpcFactionMemberComponent>(uid, out var factions)
            || _npcFactonSystem.ContainsFaction(uid, "GlimmerMonster", factions))
            return;

        _npcFactonSystem.AddFaction(uid, "PsionicInterloper");
    }

    private void OnRemove(EntityUid uid, PsionicComponent component, ComponentRemove args)
    {
        _skillTreeSystem.RemoveTreeAction(uid, component);

        if (!HasComp<NpcFactionMemberComponent>(uid))
            return;

        _npcFactonSystem.RemoveFaction(uid, "PsionicInterloper");
    }

    private void OnStamHit(EntityUid uid, AntiPsionicWeaponComponent component, TakeStaminaDamageEvent args)
    {
        if (!HasComp<PsionicComponent>(args.Target))
            return;

        args.FlatModifier += component.PsychicStaminaDamage;
    }

    /// <summary>
    ///     Potentia is psionic experience. Crossing a threshold grants a level and a point;
    ///     the player chooses the power through the skill tree instead of rolling one randomly.
    /// </summary>
    /// <remarks>
    ///     This exponential cost is mainly done to prevent stations from becoming "Space Hogwarts",
    ///     which was a common complaint with Psionic Refactor opening up the opportunity for people to have multiple powers.
    /// </remarks>
    private bool HandlePotentiaCalculations(EntityUid uid, PsionicComponent component, float psionicChance)
    {
        component.Potentia += _random.NextFloat(0 + psionicChance, 100 + psionicChance);

        if (component.Potentia < component.NextPowerCost)
            return false;

        while (component.Potentia >= component.NextPowerCost)
        {
            component.Potentia -= component.NextPowerCost;
            _skillTreeSystem.GainLevel(uid, component, feedback: false);
            component.NextPowerCost = GetNextLevelCost(component);
        }

        Dirty(uid, component);
        return true;
    }

    /// <summary>
    ///     Make a newly earned point difficult to miss.
    /// </summary>
    private void HandleLevelUpFeedback(EntityUid uid, PsionicComponent component)
    {
        if (!_playerManager.TryGetSessionByEntity(uid, out var session)
            || !Loc.TryGetString(PsionicLevelUpMessage, out var levelUpMessage, ("level", component.PsionicLevel)))
            return;

        _popups.PopupEntity(levelUpMessage, uid, uid, PopupType.MediumCaution);

        // Popups only last a few seconds, and are easily ignored.
        // So we also put a message in chat to make it harder to miss.
        var feedbackMessage = $"[font size={PsionicLevelUpFontSize}][color={PsionicLevelUpColor}]{levelUpMessage}[/color][/font]";
        _chatManager.ChatMessageToOne(
            PsionicLevelUpChatChannel,
            feedbackMessage,
            feedbackMessage,
            EntityUid.Invalid,
            false,
            session.Channel);
    }

    /// <summary>
    ///     This function attempts to generate a psionic power by incrementing a Psion's Potentia stat by a random amount, then checking if it beats a certain threshold.
    ///     Please consider going through RerollPsionics or PsionicAbilitiesSystem.InitializePsionicPower instead of this function, particularly if you don't have a good reason to call this directly.
    /// </summary>
    public void RollPsionics(EntityUid uid, PsionicComponent component, bool applyGlimmer = true, float rollEventMultiplier = 1f)
    {
        if (!_cfg.GetCVar(CCVars.PsionicRollsEnabled)
            || !component.Roller)
            return;

        // Calculate the initial odds based on the innate potential
        var baselineChance = component.Chance
            * component.PowerRollMultiplier
            + component.PowerRollFlatBonus
            + _random.NextFloat(0, 100);

        // Increase the initial odds based on Glimmer.
        baselineChance += (float) (applyGlimmer
            ? _glimmerSystem.GetGlimmerEquilibriumRatio() * 25
            : 0);

        // Certain sources of power rolls provide their own multiplier.
        baselineChance *= rollEventMultiplier;

        // Ask if the Roller has any other effects to contribute, such as Traits.
        var ev = new OnRollPsionicsEvent(uid, baselineChance);
        RaiseLocalEvent(uid, ref ev);

        if (!HandlePotentiaCalculations(uid, component, ev.BaselineChance))
            return;

        HandleLevelUpFeedback(uid, component);
    }

    /// <summary>
    /// Used by events that previously granted an immediate random power.
    /// </summary>
    public bool GrantPsionicLevel(EntityUid uid, PsionicComponent? component = null, int amount = 1)
    {
        if (!Resolve(uid, ref component, false) || amount <= 0)
            return false;

        _skillTreeSystem.GainLevel(uid, component, amount);
        component.NextPowerCost = GetNextLevelCost(component);
        Dirty(uid, component);
        return true;
    }

    /// <summary>
    ///     Each person has a single free reroll for their Psionics, which certain conditions can restore.
    ///     This function attempts to "Spend" a reroll, if one is available.
    /// </summary>
    public void RerollPsionics(EntityUid uid, PsionicComponent? psionic = null, float bonusMuliplier = 1f)
    {
        if (!Resolve(uid, ref psionic, false)
            || !psionic.CanReroll)
            return;

        psionic.CanReroll = false;
        RollPsionics(uid, psionic, true, bonusMuliplier);
    }
    private void OnMobstateChanged(EntityUid uid, PsionicComponent component, MobStateChangedEvent args)
    {
        if (component.Familiars.Count <= 0
            || args.NewMobState != MobState.Dead)
            return;

        foreach (var familiar in component.Familiars)
        {
            if (!TryComp<PsionicFamiliarComponent>(familiar, out var familiarComponent)
                || !familiarComponent.DespawnOnMasterDeath)
                continue;

            _psionicFamiliar.DespawnFamiliar(familiar, familiarComponent);
        }
    }

    /// <summary>
    ///     When a caster with active summons is attacked, aggro their familiars to the attacker.
    /// </summary>
    private void OnDamageChanged(EntityUid uid, PsionicComponent component, DamageChangedEvent args)
    {
        if (component.Familiars.Count <= 0
            || !args.DamageIncreased
            || args.Origin is not { } origin
            || origin == uid)
            return;

        SetFamiliarTarget(origin, component);
    }

    /// <summary>
    ///     When a caster with active summons attempts to attack something, aggro their familiars to the target.
    /// </summary>
    private void OnAttackAttempt(EntityUid uid, PsionicComponent component, AttackAttemptEvent args)
    {
        if (component.Familiars.Count <= 0
            || args.Target == uid
            || args.Target is not { } target
            || component.Familiars.Contains(target))
            return;

        SetFamiliarTarget(target, component);
    }

    private void SetFamiliarTarget(EntityUid target, PsionicComponent component)
    {
        foreach (var familiar in component.Familiars)
        {
            if (!TryComp<NPCRetaliationComponent>(familiar, out var retaliationComponent))
                continue;

            _retaliationSystem.TryRetaliate((familiar, retaliationComponent), target);
        }
    }
}
