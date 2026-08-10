using System.Linq;
using Content.Server.Database;
using Content.Server.Players.JobWhitelist;
using Content.Shared.Administration;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server.Administration.Commands;

/// <summary>
/// Shared argument handling for the jobwhitelist commands. A whitelist target is either a plain job ID or the
/// name of a <see cref="JobPrototype.WhitelistGroup"/>, which covers every job that declares it.
/// </summary>
public static class JobWhitelistTarget
{
    /// <summary>
    /// Resolves a command argument to the key whitelists are stored under, plus a human readable name for it.
    /// Passing a grouped job resolves to that job's whole group.
    /// </summary>
    public static bool TryResolve(IPrototypeManager prototypes, string arg, out string key, out string name)
    {
        key = arg;
        name = arg;

        var isJob = prototypes.TryIndex<JobPrototype>(arg, out var job);
        if (isJob)
            key = job!.WhitelistKey;

        var resolvedKey = key;
        var members = prototypes.EnumeratePrototypes<JobPrototype>()
            .Where(p => p.WhitelistGroup == resolvedKey)
            .Select(p => p.LocalizedName)
            .ToList();

        if (members.Count > 0)
        {
            name = string.Join(", ", members);
            return true;
        }

        if (!isJob)
            return false;

        name = job!.LocalizedName;
        return true;
    }

    /// <summary>
    /// Every valid argument: group names for grouped jobs, job IDs for the rest.
    /// </summary>
    public static IEnumerable<string> Options(IPrototypeManager prototypes)
    {
        return prototypes.EnumeratePrototypes<JobPrototype>()
            .Select(p => p.WhitelistKey)
            .Distinct();
    }
}

[AdminCommand(AdminFlags.Ban)]
public sealed class JobWhitelistAddCommand : LocalizedCommands
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly JobWhitelistManager _jobWhitelist = default!;
    [Dependency] private readonly IPlayerLocator _playerLocator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override string Command => "jobwhitelistadd";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number-need-specific",
                ("properAmount", 2),
                ("currentAmount", args.Length)));
            shell.WriteLine(Help);
            return;
        }

        var player = args[0].Trim();
        var target = args[1].Trim();
        if (!JobWhitelistTarget.TryResolve(_prototypes, target, out var key, out var jobName))
        {
            shell.WriteError(Loc.GetString("cmd-jobwhitelist-job-does-not-exist", ("job", target)));
            shell.WriteLine(Help);
            return;
        }

        var job = new ProtoId<JobPrototype>(key);
        var data = await _playerLocator.LookupIdByNameAsync(player);
        if (data != null)
        {
            var guid = data.UserId;
            var isWhitelisted = await _db.IsJobWhitelisted(guid, job);
            if (isWhitelisted)
            {
                shell.WriteLine(Loc.GetString("cmd-jobwhitelistadd-already-whitelisted",
                    ("player", player),
                    ("jobId", job.Id),
                    ("jobName", jobName)));
                return;
            }

            _jobWhitelist.AddWhitelist(guid, job);
            shell.WriteLine(Loc.GetString("cmd-jobwhitelistadd-added",
                ("player", player),
                ("jobId", job.Id),
                ("jobName", jobName)));
            return;
        }

        shell.WriteError(Loc.GetString("cmd-jobwhitelist-player-not-found", ("player", player)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                _players.Sessions.Select(s => s.Name),
                Loc.GetString("cmd-jobwhitelist-hint-player"));
        }

        if (args.Length == 2)
        {
            return CompletionResult.FromHintOptions(
                JobWhitelistTarget.Options(_prototypes),
                Loc.GetString("cmd-jobwhitelist-hint-job"));
        }

        return CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Ban)]
public sealed class GetJobWhitelistCommand : LocalizedCommands
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPlayerLocator _playerLocator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override string Command => "jobwhitelistget";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteError("This command needs at least one argument.");
            shell.WriteLine(Help);
            return;
        }

        var player = string.Join(' ', args).Trim();
        var data = await _playerLocator.LookupIdByNameAsync(player);
        if (data != null)
        {
            var guid = data.UserId;
            var whitelists = await _db.GetJobWhitelists(guid);
            if (whitelists.Count == 0)
            {
                shell.WriteLine(Loc.GetString("cmd-jobwhitelistget-whitelisted-none", ("player", player)));
                return;
            }

            shell.WriteLine(Loc.GetString("cmd-jobwhitelistget-whitelisted-for",
                ("player", player),
                ("jobs", string.Join(", ", whitelists))));
            return;
        }

        shell.WriteError(Loc.GetString("cmd-jobwhitelist-player-not-found", ("player", player)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                _players.Sessions.Select(s => s.Name),
                Loc.GetString("cmd-jobwhitelist-hint-player"));
        }

        return CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Ban)]
public sealed class RemoveJobWhitelistCommand : LocalizedCommands
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly JobWhitelistManager _jobWhitelist = default!;
    [Dependency] private readonly IPlayerLocator _playerLocator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override string Command => "jobwhitelistremove";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number-need-specific",
                ("properAmount", 2),
                ("currentAmount", args.Length)));
            shell.WriteLine(Help);
            return;
        }

        var player = args[0].Trim();
        var target = args[1].Trim();
        if (!JobWhitelistTarget.TryResolve(_prototypes, target, out var key, out var jobName))
        {
            shell.WriteError(Loc.GetString("cmd-jobwhitelist-job-does-not-exist", ("job", target)));
            shell.WriteLine(Help);
            return;
        }

        var job = new ProtoId<JobPrototype>(key);
        var data = await _playerLocator.LookupIdByNameAsync(player);
        if (data != null)
        {
            var guid = data.UserId;
            var isWhitelisted = await _db.IsJobWhitelisted(guid, job);
            if (!isWhitelisted)
            {
                shell.WriteError(Loc.GetString("cmd-jobwhitelistremove-was-not-whitelisted",
                    ("player", player),
                    ("jobId", job.Id),
                    ("jobName", jobName)));
                return;
            }

            _jobWhitelist.RemoveWhitelist(guid, job);
            shell.WriteLine(Loc.GetString("cmd-jobwhitelistremove-removed",
                ("player", player),
                ("jobId", job.Id),
                ("jobName", jobName)));
            return;
        }

        shell.WriteError(Loc.GetString("cmd-jobwhitelist-player-not-found", ("player", player)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                _players.Sessions.Select(s => s.Name),
                Loc.GetString("cmd-jobwhitelist-hint-player"));
        }

        if (args.Length == 2)
        {
            return CompletionResult.FromHintOptions(
                JobWhitelistTarget.Options(_prototypes),
                Loc.GetString("cmd-jobwhitelist-hint-job"));
        }

        return CompletionResult.Empty;
    }
}
