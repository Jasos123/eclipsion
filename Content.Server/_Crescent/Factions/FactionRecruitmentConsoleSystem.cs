using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Crescent.Chat;
using Content.Server.Jobs;
using Content.Server.Popups;
using Content.Server._Crescent.Squad;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared._Crescent.Factions;
using Content.Shared._Crescent.HullrotFaction;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Crescent.Factions;

/// <summary>
/// Backs the faction recruitment (muster) console. An authorized operator picks a nearby person and a role of
/// the console's faction; the console then sets that person's <see cref="HullrotFactionComponent"/> — read live
/// by diplomacy, squads, payroll and the chat name prefix — and rewrites their held ID card to the chosen role's
/// title, icon, department and access. It can also dismiss a member, clearing their faction membership.
///
/// Membership lives on the body, not the ID card, so the console acts on nearby people rather than on an inserted
/// card. No character profile is touched, so death/respawn returns a recruit to their character's own faction.
/// </summary>
public sealed class FactionRecruitmentConsoleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IdCardSystem _idCard = default!;
    [Dependency] private readonly AccessSystem _access = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly HullrotNpcFactionSyncSystem _hullrotNpcFaction = default!;
    [Dependency] private readonly SquadSystem _squad = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactionRecruitmentConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<FactionRecruitmentConsoleComponent, FactionRecruitmentAssignMessage>(OnAssign);
        SubscribeLocalEvent<FactionRecruitmentConsoleComponent, FactionRecruitmentAssignDoAfterEvent>(OnAssignDoAfter);
        SubscribeLocalEvent<FactionRecruitmentConsoleComponent, FactionRecruitmentDismissMessage>(OnDismiss);
        SubscribeLocalEvent<FactionRecruitmentConsoleComponent, FactionRecruitmentRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<FactionRecruitmentConsoleComponent, ExaminedEvent>(OnExamine);
    }

    private void OnExamine(EntityUid uid, FactionRecruitmentConsoleComponent comp, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("faction-recruitment-examine"));
    }

    private void OnRefresh(EntityUid uid, FactionRecruitmentConsoleComponent comp, FactionRecruitmentRefreshMessage args)
    {
        UpdateUi(uid, comp);
    }

    private void OnOpened(EntityUid uid, FactionRecruitmentConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, comp);
    }

    /// <summary>Every humanoid within the console's range. Also used to re-validate action targets server-side.</summary>
    private IEnumerable<EntityUid> GetNearbyPeople(EntityUid uid, FactionRecruitmentConsoleComponent comp)
    {
        var coords = Transform(uid).Coordinates;
        foreach (var (person, _) in _lookup.GetEntitiesInRange<HumanoidAppearanceComponent>(coords, comp.Range))
        {
            if (person == uid)
                continue;

            yield return person;
        }
    }

    private string FactionDisplayName(string factionId)
    {
        if (string.IsNullOrEmpty(factionId))
            return string.Empty;

        return _proto.TryIndex<FactionPrototype>(factionId, out var faction) ? faction.Name : factionId;
    }

    /// <summary>The ChatRank a role grants through its AddComponentSpecial, or null if it grants none.</summary>
    private string? GetRoleRank(JobPrototype job)
    {
        foreach (var special in job.Special)
        {
            if (special is not AddComponentSpecial addComp)
                continue;

            foreach (var (_, entry) in addComp.Components)
            {
                if (entry.Component is ChatRankComponent rank)
                    return rank.Rank;
            }
        }

        return null;
    }

    /// <summary>Every ChatRank this console can grant — one per assignable role that carries one.</summary>
    private HashSet<string> GetAssignableRanks(FactionRecruitmentConsoleComponent comp)
    {
        var ranks = new HashSet<string>();
        foreach (var jobId in comp.Roles)
        {
            if (_proto.TryIndex<JobPrototype>(jobId, out var job) && GetRoleRank(job) is { } rank)
                ranks.Add(rank);
        }

        return ranks;
    }

    private void UpdateUi(EntityUid uid, FactionRecruitmentConsoleComponent comp)
    {
        var options = new List<FactionRecruitmentOption>();
        foreach (var jobId in comp.Roles)
        {
            if (!_proto.TryIndex<JobPrototype>(jobId, out var job))
                continue;

            options.Add(new FactionRecruitmentOption
            {
                JobId = jobId,
                RoleName = job.LocalizedName,
                Description = job.LocalizedDescription ?? string.Empty,
            });
        }

        var targets = new List<FactionRecruitmentTarget>();
        foreach (var person in GetNearbyPeople(uid, comp))
        {
            var currentFaction = CompOrNull<HullrotFactionComponent>(person)?.Faction ?? string.Empty;
            targets.Add(new FactionRecruitmentTarget
            {
                Entity = GetNetEntity(person),
                Name = Name(person),
                CurrentFactionName = FactionDisplayName(currentFaction),
                IsMember = currentFaction == comp.Faction,
            });
        }

        _ui.SetUiState(uid, FactionRecruitmentUiKey.Key,
            new FactionRecruitmentConsoleState(FactionDisplayName(comp.Faction), options, targets));
    }

    /// <summary>
    /// Re-check the console's access gate server-side. ActivatableUIRequiresAccess already gates opening,
    /// but a message must never be able to bypass it.
    /// </summary>
    private bool IsAuthorized(EntityUid uid, EntityUid actor)
    {
        if (!TryComp<AccessReaderComponent>(uid, out var reader))
            return true;

        if (_accessReader.IsAllowed(actor, uid, reader))
            return true;

        _popup.PopupEntity(Loc.GetString("faction-recruitment-denied"), uid, actor);
        return false;
    }

    /// <summary>Resolves a client-sent target back to an entity, rejecting anything not currently in range.</summary>
    private bool TryResolveTarget(EntityUid uid, FactionRecruitmentConsoleComponent comp, NetEntity netTarget, out EntityUid target)
    {
        target = default;
        if (!TryGetEntity(netTarget, out var resolved))
            return false;

        if (!GetNearbyPeople(uid, comp).Contains(resolved.Value))
            return false;

        target = resolved.Value;
        return true;
    }

    private void OnAssign(EntityUid uid, FactionRecruitmentConsoleComponent comp, FactionRecruitmentAssignMessage args)
    {
        if (args.Actor is not { Valid: true } actor || !IsAuthorized(uid, actor))
            return;

        if (!comp.Roles.Contains(args.JobId) || !_proto.TryIndex<JobPrototype>(args.JobId, out _))
            return;

        if (!TryResolveTarget(uid, comp, args.Target, out var target))
        {
            _popup.PopupEntity(Loc.GetString("faction-recruitment-out-of-range"), uid, actor);
            return;
        }

        if (!CanAssign(uid, comp, actor, target))
            return;

        // Don't commit the recruitment (and its ID rewrite) instantly. A short do-after both gives feedback and
        // stops the role change from being spam-triggered; everything is re-validated when it completes.
        var doAfter = new DoAfterArgs(EntityManager, actor, comp.AssignDelay,
            new FactionRecruitmentAssignDoAfterEvent(args.JobId), uid, target: target, used: uid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnAssignDoAfter(EntityUid uid, FactionRecruitmentConsoleComponent comp, FactionRecruitmentAssignDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (args.User is not { Valid: true } actor || args.Target is not { Valid: true } target)
            return;

        // Re-validate from scratch: the delay means access, range and rank can all have changed since the click.
        if (!IsAuthorized(uid, actor))
            return;

        if (!comp.Roles.Contains(args.JobId) || !_proto.TryIndex<JobPrototype>(args.JobId, out var job))
            return;

        if (!GetNearbyPeople(uid, comp).Contains(target))
        {
            _popup.PopupEntity(Loc.GetString("faction-recruitment-out-of-range"), uid, actor);
            return;
        }

        if (!CanAssign(uid, comp, actor, target))
            return;

        args.Handled = true;
        ApplyRecruitment(uid, comp, actor, target, job);
    }

    /// <summary>
    /// Never let a console strip a rank it cannot itself grant. Each faction's high command (Governor, Adjutant,
    /// Kommandant, …) is deliberately kept off every console's role list, so a member who already holds such a
    /// post must not be reassignable here — doing so would silently demote them. This only guards members of this
    /// console's own faction; unaffiliated people and the default private rank still enlist freely (a fresh
    /// recruit or a lateral faction switch is not a demotion).
    /// </summary>
    private bool CanAssign(EntityUid uid, FactionRecruitmentConsoleComponent comp, EntityUid actor, EntityUid target)
    {
        if (!TryComp<HullrotFactionComponent>(target, out var targetFaction)
            || targetFaction.Faction != comp.Faction)
        {
            return true;
        }

        // Chat rank is the primary signal, but it only works for factions that hand ranks out: Shinohara
        // gives none at all, and two TFCF member-organization leaders carry none either. So also refuse
        // anyone whose round-start job belongs to this faction yet is off this console's list — that job is
        // command by definition. The faction check matters: a drifter recruited off the street holds a job
        // that is on nobody's list, and moving them between the faction's own roles is not a demotion.
        var protectedByJob = _mind.TryGetMind(target, out var mindId, out _)
            && _jobs.MindTryGetJobId(mindId, out var currentJob)
            && currentJob is { } jobId
            && !comp.Roles.Contains(jobId.Id)
            && IsFactionJob(jobId.Id, comp.Faction);

        var protectedByRank = TryComp<ChatRankComponent>(target, out var targetRank)
            && targetRank.Rank != ChatRankComponent.DefaultRank
            && !GetAssignableRanks(comp).Contains(targetRank.Rank);

        if (protectedByJob || protectedByRank)
        {
            _popup.PopupEntity(Loc.GetString("faction-recruitment-outranks", ("target", Name(target))), uid, actor);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a job prototype enlists its holder into <paramref name="faction"/>, read off the same
    /// HullrotFaction grant a fresh spawn of that job would apply.
    /// </summary>
    private bool IsFactionJob(string jobId, string faction)
    {
        if (string.IsNullOrEmpty(faction) || !_proto.TryIndex<JobPrototype>(jobId, out var job))
            return false;

        foreach (var special in job.Special)
        {
            if (special is not AddComponentSpecial add)
                continue;

            foreach (var entry in add.Components.Values)
            {
                if (entry.Component is HullrotFactionComponent hullrot && hullrot.Faction == faction)
                    return true;
            }
        }

        return false;
    }

    private void ApplyRecruitment(EntityUid uid, FactionRecruitmentConsoleComponent comp, EntityUid actor, EntityUid target, JobPrototype job)
    {
        // Squad IDs belong to a faction. Keep a same-faction reassignment in its existing squad, but never carry
        // an old faction's squad membership through a lateral recruitment (or revive an already-stale one).
        if (!TryComp<HullrotFactionComponent>(target, out var previousFaction) ||
            previousFaction.Faction != comp.Faction)
        {
            _squad.RemoveFromSquad(target);
        }

        // 1. Re-run the role's component grants (AddComponentSpecial) so the recruit matches a fresh spawn of the
        //    job: this is what refreshes ChatRankComponent — the source of the chat/radio name prefix ("rank") —
        //    along with faction languages. Only AddComponentSpecial is replayed; item/implant/trait specials would
        //    stack on a body that already exists, so they are skipped.
        foreach (var special in job.Special)
        {
            if (special is AddComponentSpecial)
                special.AfterEquip(target);
        }

        // 2. Faction membership. Read live everywhere via HullrotFactionComponent, so this drives diplomacy,
        //    squads and treasury. Set it after the specials above (a job's own HullrotFaction special would
        //    otherwise overwrite it) so the console's faction stays authoritative.
        var factionComp = EnsureComp<HullrotFactionComponent>(target);
        factionComp.Faction = comp.Faction;
        Dirty(target, factionComp);
        // Assigning the field raises nothing, so the NPC-facing membership has to be pushed by hand or the
        // recruit still reads as their old side to every turret in the sector.
        _hullrotNpcFaction.Sync(target, factionComp);

        // 3. ID card: title, icon, department, and (additively) the role's access.
        if (_idCard.TryFindIdCard(target, out var idCard))
        {
            _idCard.TryChangeJobTitle(idCard, job.LocalizedName, player: actor);

            if (_proto.TryIndex(job.Icon, out var icon))
            {
                _idCard.TryChangeJobIcon(idCard, icon, player: actor);
                _idCard.TryChangeJobDepartment(idCard, job);
            }

            // Grant the role's access without stripping what the recruit already carries.
            var tags = (_access.TryGetTags(idCard) ?? Enumerable.Empty<ProtoId<AccessLevelPrototype>>()).ToHashSet();
            tags.UnionWith(job.Access);
            _access.TrySetTags(idCard, tags);
            _access.TryAddGroups(idCard, job.AccessGroups);
        }

        var factionName = FactionDisplayName(comp.Faction);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):player} recruited {ToPrettyString(target):target} into {factionName} as {job.LocalizedName} via {ToPrettyString(uid):console}");

        _popup.PopupEntity(
            Loc.GetString("faction-recruitment-recruited", ("target", Name(target)), ("faction", factionName), ("role", job.LocalizedName)),
            actor,
            actor);

        // The operator is in their own nearby list, so they can enlist themselves. When they do,
        // target == actor and this recruit-facing popup would stack on top of the one above — both
        // land on the same character at the same time and neither can be read. Only show it to a
        // separate recruit.
        if (target != actor)
            _popup.PopupEntity(
                Loc.GetString("faction-recruitment-you-joined", ("faction", factionName), ("role", job.LocalizedName)),
                target,
                target);

        UpdateUi(uid, comp);
    }

    private void OnDismiss(EntityUid uid, FactionRecruitmentConsoleComponent comp, FactionRecruitmentDismissMessage args)
    {
        if (args.Actor is not { Valid: true } actor || !IsAuthorized(uid, actor))
            return;

        if (!TryResolveTarget(uid, comp, args.Target, out var target))
        {
            _popup.PopupEntity(Loc.GetString("faction-recruitment-out-of-range"), uid, actor);
            return;
        }

        // Only dismiss our own members; refuse to touch someone who belongs to another faction.
        if (!TryComp<HullrotFactionComponent>(target, out var factionComp) || factionComp.Faction != comp.Faction)
        {
            _popup.PopupEntity(Loc.GetString("faction-recruitment-not-member"), uid, actor);
            return;
        }

        // Removing the membership component lets every lifecycle subscriber invalidate its state immediately:
        // NPC factions are cleaned up and overwatch cannot retain this member in a stale roster cache.
        _squad.RemoveFromSquad(target);
        RemComp<HullrotFactionComponent>(target);

        var factionName = FactionDisplayName(comp.Faction);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):player} dismissed {ToPrettyString(target):target} from {factionName} via {ToPrettyString(uid):console}");

        _popup.PopupEntity(
            Loc.GetString("faction-recruitment-dismissed", ("target", Name(target)), ("faction", factionName)),
            actor,
            actor);

        UpdateUi(uid, comp);
    }
}
