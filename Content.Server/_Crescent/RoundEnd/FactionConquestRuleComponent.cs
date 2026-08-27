namespace Content.Server._Crescent.RoundEnd;

/// <summary>
/// Configures the conquest win condition: a faction stops counting as a holder of Taypan once its station has
/// fallen, and the round ends when only one great power still holds one. A station falls when every
/// <see cref="Content.Shared._Crescent.RoundEnd.ConquestFlagComponent"/> planted on it has been in enemy hands
/// for <see cref="HoldToFall"/> (or when the grid is outright destroyed). Nothing here removes anyone from the
/// war or edits diplomacy — DSM and NCWL are at war by definition and remain so however the round goes.
/// </summary>
[RegisterComponent, Access(typeof(FactionConquestRuleSystem))]
public sealed partial class FactionConquestRuleComponent : Component
{
    /// <summary>
    /// The great powers — the two sides whose war the round is actually about. If exactly one of them still holds
    /// its station the sector is credited to it, whoever else is still standing. If both lose theirs the minors do
    /// NOT have to finish each other off — every faction still holding a station takes the sector together.
    ///
    /// This is the wish list, not the roster: the ones that actually turn up on the map are <see cref="ActiveMajors"/>.
    /// </summary>
    [DataField]
    public List<string> MajorFactions = new() { "DSM", "NCWL" };

    /// <summary>
    /// The great powers that actually fielded a station this round, resolved once from the map instead of trusted
    /// from <see cref="MajorFactions"/>. Freeplay | TFSC &amp; SHI maps no DSM or NCWL station at all, and a great
    /// power that was never in the round is not a great power that lost — without this the very first check would
    /// see zero majors standing, hand the sector to whoever spawned, and end the round the moment it began.
    /// Null until the map has spawned; nothing is evaluated before then.
    /// </summary>
    [ViewVariables]
    public List<string>? ActiveMajors;

    /// <summary>
    /// How long every banner on a station must stay in enemy hands before the station counts as fallen. Any of its
    /// banners being reclaimed by the owning faction clears this clock, so the defenders can always answer a push.
    /// </summary>
    [DataField]
    public TimeSpan HoldToFall = TimeSpan.FromMinutes(10);

    /// <summary>How often the win condition is evaluated.</summary>
    [DataField]
    public TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    /// <summary>Delay between declaring a winner and restarting.</summary>
    [DataField]
    public TimeSpan RestartDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Grace window between the war looking settled and the round actually ending, so the factions still standing
    /// get one last chance to move. This is separate from <see cref="HoldToFall"/>: after that capture warning
    /// expires and infestation starts, the fallen station cannot be reclaimed.
    /// </summary>
    [DataField]
    public TimeSpan VictoryDelay = TimeSpan.FromMinutes(20);

    /// <summary>Faction id -> the message broadcast when it wins.</summary>
    [DataField]
    public Dictionary<string, string> VictoryAnnouncements = new();

    /// <summary>Broadcast when both great powers are gone and the surviving minors inherit the sector.</summary>
    [DataField]
    public string MinorVictoryAnnouncement = "faction-victory-minors";

    /// <summary>Broadcast when the round ends with nobody having won.</summary>
    [DataField]
    public string TimeoutAnnouncement = "faction-victory-timeout";

    /// <summary>
    /// Broadcast when the war first looks settled, opening the response window. Without it the sector gets no
    /// warning at all before the round ends — and the window exists precisely so the losers can still act.
    /// </summary>
    [DataField]
    public string PendingAnnouncement = "faction-victory-pending";

    /// <summary>Broadcast when a pending victory is called off because the surviving-station or alliance state changed.</summary>
    [DataField]
    public string PendingCancelledAnnouncement = "faction-victory-cancelled";

    /// <summary>Broadcast the moment a station's last banner falls to the enemy, opening its <see cref="HoldToFall"/> clock.</summary>
    [DataField]
    public string ControlAnnouncement = "faction-station-captured";

    /// <summary>Broadcast when defenders retake a station during the Turning warning window.</summary>
    [DataField]
    public string ControlCancelledAnnouncement = "faction-station-capture-cancelled";

    /// <summary>When the next evaluation is due.</summary>
    [ViewVariables]
    public TimeSpan NextCheck;

    /// <summary>Station grid -> when its last banner fell to the enemy. Cleared as soon as any banner is reclaimed.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> ControlledSince = new();

    /// <summary>Stations whose capture warning has gone out. Cleared when reclaimed, so a second capture warns again.</summary>
    [ViewVariables]
    public HashSet<EntityUid> AnnouncedControl = new();

    /// <summary>
    /// Stations whose projectile shield marker was removed when their last banner fell. Only entries recorded here
    /// are restored on a successful defence, so a station that never had protection does not gain it by accident.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> DisabledShields = new();

    /// <summary>Station grid -> the real shield emitters forced offline by that station's capture warning.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, HashSet<EntityUid>> DisabledShieldEmitters = new();

    /// <summary>Stations whose fall has already been broadcast, so it is announced once.</summary>
    [ViewVariables]
    public HashSet<EntityUid> AnnouncedFallen = new();

    /// <summary>
    /// Every station seen so far and its obituary, kept so a grid that is outright destroyed — and therefore gone
    /// from the entity query before it can ever be seen "dark" — still gets announced.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, string> KnownStations = new();

    /// <summary>Factions currently set to win, and when the countdown started. Cleared if the war reopens.</summary>
    [ViewVariables]
    public List<string>? PendingWinners;

    [ViewVariables]
    public TimeSpan PendingSince;

    /// <summary>Set once a winner has been declared so the round only ends once.</summary>
    [ViewVariables]
    public bool Decided;

    /// <summary>Who was credited with the war, for the round end summary.</summary>
    [ViewVariables]
    public List<string> Winners = new();
}
