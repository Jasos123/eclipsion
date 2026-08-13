using Content.Server.Abilities.Psionics;
using Content.Server.EUI;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions;
using Content.Shared.Popups;
using Content.Shared.Psionics;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Psionics;

/// <summary>
/// Owns psionic progression and performs all authoritative skill-tree validation.
/// </summary>
public sealed class PsionicSkillTreeSystem : EntitySystem
{
    private const string SkillTreeAction = "ActionPsionicSkillTree";

    [Dependency] private readonly EuiManager _euiManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly PsionicAbilitiesSystem _abilities = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;

    /// <summary>
    /// The skill tree window each player currently has open, so pressing the action again reopens the
    /// existing one instead of registering another live EUI beside it.
    /// </summary>
    private readonly Dictionary<NetUserId, PsionicSkillTreeEui> _openEuis = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PsionicComponent, OpenPsionicSkillTreeActionEvent>(OnOpenSkillTree);
    }

    public void InitializeTreeAction(EntityUid uid, PsionicComponent component)
    {
        _actions.AddAction(uid, ref component.SkillTreeAction, SkillTreeAction);
    }

    public void RemoveTreeAction(EntityUid uid, PsionicComponent component)
    {
        _actions.RemoveAction(uid, component.SkillTreeAction);
        component.SkillTreeAction = null;
    }

    private void OnOpenSkillTree(Entity<PsionicComponent> ent, ref OpenPsionicSkillTreeActionEvent args)
    {
        if (args.Handled || !_playerManager.TryGetSessionByEntity(args.Performer, out var session))
            return;

        // The action can be pressed as often as the player likes. Reopen the window rather than
        // stacking another one on top: every OpenEui registers a server-side EUI that receives state
        // pushes until it is closed by hand.
        if (_openEuis.Remove(session.UserId, out var previous) && !previous.IsShutDown)
            previous.Close();

        var eui = new PsionicSkillTreeEui(ent.Owner, this);
        _openEuis[session.UserId] = eui;
        _euiManager.OpenEui(eui, session);
        args.Handled = true;
    }

    /// <summary>
    /// Called by the EUI as it shuts down (player closed it, or disconnected), so a window closed from
    /// the client side does not leave a stale entry behind.
    /// </summary>
    internal void OnEuiClosed(PsionicSkillTreeEui eui)
    {
        if (_openEuis.TryGetValue(eui.Player.UserId, out var current) && current == eui)
            _openEuis.Remove(eui.Player.UserId);
    }

    /// <summary>
    /// Pushes fresh state to the given Psion's open tree window, if they have one.
    /// </summary>
    public void RefreshEui(EntityUid uid)
    {
        foreach (var eui in _openEuis.Values)
        {
            if (eui.Owner != uid || eui.IsShutDown)
                continue;

            eui.StateDirty();
            return;
        }
    }

    /// <summary>
    /// Grants a full level and one spendable point. Potentia is handled by <see cref="PsionicsSystem"/>.
    /// </summary>
    public void GainLevel(EntityUid uid, PsionicComponent component, int amount = 1, bool feedback = true)
    {
        if (amount <= 0)
            return;

        component.PsionicLevel += amount;
        component.SkillPoints += amount;
        Dirty(uid, component);

        // A tree left open while the Psion levels would otherwise keep showing the old point count and
        // greyed-out nodes until it was closed and reopened.
        RefreshEui(uid);

        if (feedback)
        {
            _popups.PopupEntity(
                Loc.GetString("psionic-skill-tree-level-up", ("level", component.PsionicLevel)),
                uid,
                uid,
                PopupType.MediumCaution);
        }
    }

    public PsionicSkillTreeEuiState BuildState(EntityUid uid)
    {
        if (!TryComp<PsionicComponent>(uid, out var component) ||
            !_prototypeManager.TryIndex(component.SkillTree, out var tree))
        {
            return new PsionicSkillTreeEuiState(
                string.Empty,
                0,
                0,
                0,
                1,
                new List<PsionicSkillNodeState>());
        }

        var states = new List<PsionicSkillNodeState>(tree.Skills.Count);
        foreach (var skillId in tree.Skills)
        {
            if (!_prototypeManager.TryIndex(skillId, out var skill))
                continue;

            var (availability, reason) = GetAvailability(component, tree, skill);
            states.Add(new PsionicSkillNodeState(skill.ID, availability, reason));
        }

        return new PsionicSkillTreeEuiState(
            tree.ID,
            component.PsionicLevel,
            component.SkillPoints,
            component.Potentia,
            Math.Max(1f, component.NextPowerCost),
            states);
    }

    public bool TryUnlock(EntityUid uid, string skillId)
    {
        if (!TryComp<PsionicComponent>(uid, out var component) ||
            !_prototypeManager.TryIndex(component.SkillTree, out var tree) ||
            !tree.Skills.Any(id => id.Id == skillId) ||
            !_prototypeManager.TryIndex<PsionicSkillPrototype>(skillId, out var skill))
        {
            return false;
        }

        var (availability, _) = GetAvailability(component, tree, skill);
        if (availability != PsionicSkillAvailability.Available)
            return false;

        if (!_prototypeManager.TryIndex(skill.Power, out var power))
            return false;

        if (component.ActivePowers.Contains(power))
            return false;

        _abilities.InitializePsionicPower(uid, power, component);

        // Do not charge a point unless the underlying power initialized successfully.
        if (!component.ActivePowers.Contains(power))
            return false;

        component.SkillPoints -= skill.Cost;
        component.UnlockedSkills.Add(skill.ID);
        Dirty(uid, component);
        return true;
    }

    private (PsionicSkillAvailability Availability, string? Reason) GetAvailability(
        PsionicComponent component,
        PsionicSkillTreePrototype tree,
        PsionicSkillPrototype skill)
    {
        if (IsOwned(component, skill))
            return (PsionicSkillAvailability.Owned, Loc.GetString("psionic-skill-tree-owned"));

        if (!string.IsNullOrEmpty(skill.ExclusiveGroup))
        {
            foreach (var otherId in tree.Skills)
            {
                if (!_prototypeManager.TryIndex(otherId, out var other) ||
                    other.ID == skill.ID ||
                    other.ExclusiveGroup != skill.ExclusiveGroup ||
                    !IsOwned(component, other))
                {
                    continue;
                }

                return (
                    PsionicSkillAvailability.Excluded,
                    Loc.GetString("psionic-skill-tree-exclusive", ("skill", Loc.GetString(other.Name))));
            }
        }

        foreach (var prerequisiteId in skill.Prerequisites)
        {
            if (!_prototypeManager.TryIndex(prerequisiteId, out var prerequisite) ||
                !IsOwned(component, prerequisite))
            {
                var prerequisiteName = _prototypeManager.TryIndex(prerequisiteId, out prerequisite)
                    ? Loc.GetString(prerequisite.Name)
                    : prerequisiteId.Id;

                return (
                    PsionicSkillAvailability.Locked,
                    Loc.GetString("psionic-skill-tree-requires", ("skill", prerequisiteName)));
            }
        }

        if (component.PsionicLevel < skill.MinimumLevel)
        {
            return (
                PsionicSkillAvailability.InsufficientLevel,
                Loc.GetString("psionic-skill-tree-requires-level", ("level", skill.MinimumLevel)));
        }

        if (component.SkillPoints < skill.Cost)
        {
            return (
                PsionicSkillAvailability.InsufficientPoints,
                Loc.GetString("psionic-skill-tree-requires-points", ("points", skill.Cost)));
        }

        return (PsionicSkillAvailability.Available, null);
    }

    private bool IsOwned(PsionicComponent component, PsionicSkillPrototype skill)
    {
        if (IsTreeUnlocked(component, skill))
            return true;

        return _prototypeManager.TryIndex(skill.Power, out var power) && component.ActivePowers.Contains(power);
    }

    private static bool IsTreeUnlocked(PsionicComponent component, PsionicSkillPrototype skill) =>
        component.UnlockedSkills.Contains(skill.ID);
}
