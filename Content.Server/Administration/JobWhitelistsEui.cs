using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.Players.JobWhitelist;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Administration;

public sealed class JobWhitelistsEui : BaseEui
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly JobWhitelistManager _jobWhitelist = default!;

    private readonly ISawmill _sawmill;

    public NetUserId PlayerId;
    public string PlayerName;

    public HashSet<ProtoId<JobPrototype>> Whitelists = new();

    public JobWhitelistsEui(NetUserId playerId, string playerName)
    {
        IoCManager.InjectDependencies(this);

        _sawmill = _log.GetSawmill("admin.job_whitelists_eui");

        PlayerId = playerId;
        PlayerName = playerName;
    }

    public async void LoadWhitelists()
    {
        // A set, not the list the DB hands back: this is tested once per job prototype, and there are
        // hundreds of those.
        var jobs = (await _db.GetJobWhitelists(PlayerId.UserId)).ToHashSet();
        foreach (var job in _proto.EnumeratePrototypes<JobPrototype>())
        {
            // Entries are stored per whitelist key, so a single group entry ticks every job in that group.
            if (jobs.Contains(job.WhitelistKey))
                Whitelists.Add(job.ID);
        }

        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new JobWhitelistsEuiState(PlayerName, Whitelists);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not SetJobWhitelistedMessage args)
            return;

        if (!_admin.HasAdminFlag(Player, AdminFlags.Whitelist))
        {
            _sawmill.Warning($"{Player.Name} ({Player.UserId}) tried to change role whitelists for {PlayerName} without whitelists flag");
            return;
        }

        if (!_proto.TryIndex<JobPrototype>(args.Job, out var job))
            return;

        // Grouped jobs share one entry, so toggling any of them toggles the whole group.
        var key = new ProtoId<JobPrototype>(job.WhitelistKey);
        var affected = _proto.EnumeratePrototypes<JobPrototype>()
            .Where(p => p.WhitelistKey == job.WhitelistKey)
            .Select(p => new ProtoId<JobPrototype>(p.ID))
            .ToList();

        if (args.Whitelisting)
        {
            _jobWhitelist.AddWhitelist(PlayerId, key);
            Whitelists.UnionWith(affected);
        }
        else
        {
            _jobWhitelist.RemoveWhitelist(PlayerId, key);
            Whitelists.ExceptWith(affected);
        }

        var verb = args.Whitelisting ? "added" : "removed";
        _sawmill.Info($"{Player.Name} ({Player.UserId}) {verb} whitelist for {args.Job} to player {PlayerName} ({PlayerId.UserId})");

        StateDirty();
    }
}
