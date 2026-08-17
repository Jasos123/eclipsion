using System.Globalization;
using Content.Server.Administration;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Crescent.RoundEnd;

/// <summary>
/// Crescent - admin control over the hard round-length cap (<see cref="MaxTimeRestartRuleComponent"/>, 4h on every
/// Crescent preset). The cap exists so a dead round cannot run forever, but it is blind: it will happily cut off a
/// boarding action or a negotiation that is minutes from paying off. This lets a round admin push it back, freeze
/// it, or drop it entirely for the round, instead of the only options being "let it fire" or "restartround".
///
/// Nothing here touches the round itself — only the deadline the rule is counting toward.
/// </summary>
[AdminCommand(AdminFlags.Round)]
public sealed class RoundTimerCommand : IConsoleCommand
{
    public string Command => "roundtimer";

    public string Description =>
        "Shows or adjusts the hard round-length cap: postpone, pause or cancel the automatic round end.";

    public string Help => $"""
        Usage: {Command} [status | extend <time> | set <time> | cancel | reset | pause | resume]

          status            Time left before the cap ends the round. Default when called with no arguments.
          extend <time>     Postpone the round end by <time>. Negative values pull it in. Alias: postpone.
          set <time>        Set the time left outright. Re-arms the cap if it was cancelled.
          cancel            Stop the round from ending on the clock at all. Lasts this round only.
          reset             Re-arm at the rule's full configured length, from now.
          pause             Freeze the countdown where it is.
          resume            Restart a frozen countdown.

        <time> takes a unit suffix: 90s, 30m, 2h, or 1h30m. A bare number is read as minutes.
        """;

    private static readonly string[] SubCommands =
        new[] { "cancel", "extend", "pause", "postpone", "reset", "resume", "set", "status" };

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var ticker = entMan.System<GameTicker>();
        var rule = entMan.System<MaxTimeRestartRuleSystem>();

        var caps = rule.GetActiveCaps();
        if (caps.Count == 0)
        {
            shell.WriteError("No round-length cap is running. This round has no automatic time limit to adjust.");
            return;
        }

        // A preset should carry exactly one. Two disagreeing deadlines is a mapping/preset bug, and an admin who
        // "postponed the round end" while a second cap quietly ends it in ten minutes needs to know about it.
        if (caps.Count > 1)
            shell.WriteLine($"Note: {caps.Count} round-length caps are running. Applying to all of them.");

        var sub = args.Length == 0 ? "status" : args[0].ToLowerInvariant();

        if (sub == "status")
        {
            if (args.Length > 1)
            {
                shell.WriteError($"'{sub}' takes no arguments.");
                return;
            }

            foreach (var cap in caps)
            {
                shell.WriteLine(StatusLine(rule, cap.Comp));
            }

            if (ticker.RunLevel != GameRunLevel.InRound)
                shell.WriteLine("The round is not running, so the cap is idle regardless of the above.");

            if (ticker.IsRoundEndBypassed())
                shell.WriteLine("A RoundEndBypass rule is active: the cap will not end the round even if it fires.");

            return;
        }

        switch (sub)
        {
            case "extend":
            case "postpone":
            {
                if (!TryParseDuration(shell, args, out var delta))
                    return;

                if (delta == TimeSpan.Zero)
                {
                    shell.WriteError("That is a zero-length adjustment. Nothing to do.");
                    return;
                }

                var moved = false;
                foreach (var cap in caps)
                {
                    if (rule.AddTime(cap.Comp, delta) == null)
                    {
                        shell.WriteError("The cap is cancelled, so there is no deadline to move. Use 'set' or 'reset' to arm it first.");
                        continue;
                    }

                    moved = true;
                    shell.WriteLine(StatusLine(rule, cap.Comp));
                }

                // Only announce a deadline that actually moved: telling every admin the round was postponed when
                // the cap was cancelled and nothing changed is worse than saying nothing.
                if (moved)
                {
                    Announce(delta > TimeSpan.Zero
                        ? $"Round end postponed by {Format(delta)}."
                        : $"Round end brought forward by {Format(-delta)}.");
                }

                return;
            }

            case "set":
            {
                if (!TryParseDuration(shell, args, out var remaining))
                    return;

                if (remaining < TimeSpan.Zero)
                {
                    shell.WriteError("Time left cannot be negative.");
                    return;
                }

                foreach (var cap in caps)
                {
                    rule.SetRemaining(cap.Comp, remaining);
                    shell.WriteLine(StatusLine(rule, cap.Comp));
                }

                Announce($"Round end set to {Format(remaining)} from now.");
                return;
            }

            case "cancel":
            {
                if (args.Length > 1)
                {
                    shell.WriteError($"'{sub}' takes no arguments.");
                    return;
                }

                foreach (var cap in caps)
                {
                    rule.CancelCap(cap.Comp);
                }

                shell.WriteLine("Round-length cap cancelled: this round will not end on the clock. Use 'roundtimer reset' to put it back, or endround/restartround to finish manually. The next round re-arms it normally.");
                Announce("Round end cancelled: the round no longer has a time limit.");
                return;
            }

            case "reset":
            {
                if (args.Length > 1)
                {
                    shell.WriteError($"'{sub}' takes no arguments.");
                    return;
                }

                foreach (var cap in caps)
                {
                    rule.RestartTimer(cap.Comp);
                    shell.WriteLine(StatusLine(rule, cap.Comp));
                }

                Announce("Round-length cap re-armed at its full configured length.");
                return;
            }

            case "pause":
            {
                if (args.Length > 1)
                {
                    shell.WriteError($"'{sub}' takes no arguments.");
                    return;
                }

                var any = false;
                foreach (var cap in caps)
                {
                    if (!rule.PauseCap(cap.Comp))
                    {
                        shell.WriteError("The cap is already paused or cancelled, so there is no countdown to freeze.");
                        continue;
                    }

                    any = true;
                    shell.WriteLine(StatusLine(rule, cap.Comp));
                }

                if (any)
                    Announce("Round-end countdown paused.");
                return;
            }

            case "resume":
            {
                if (args.Length > 1)
                {
                    shell.WriteError($"'{sub}' takes no arguments.");
                    return;
                }

                var any = false;
                foreach (var cap in caps)
                {
                    if (!rule.ResumeCap(cap.Comp))
                    {
                        shell.WriteError("The cap is not paused.");
                        continue;
                    }

                    any = true;
                    shell.WriteLine(StatusLine(rule, cap.Comp));
                }

                if (any)
                    Announce("Round-end countdown resumed.");
                return;
            }

            default:
                shell.WriteError($"Unknown argument '{args[0]}'.\n{Help}");
                return;
        }
    }

    /// <summary>
    /// Other admins are the ones who need to know the round just got longer or stopped counting — a round admin
    /// planning around "we have twenty minutes" should not have to re-run the command to find out it moved.
    /// </summary>
    private static void Announce(string message)
    {
        IoCManager.Resolve<IChatManager>().SendAdminAnnouncement(message);
    }

    private static string StatusLine(MaxTimeRestartRuleSystem rule, MaxTimeRestartRuleComponent cap)
    {
        if (rule.GetRemaining(cap) is not { } remaining)
            return "Round end: CANCELLED (no time limit).";

        return rule.IsPaused(cap)
            ? $"Round end: PAUSED with {Format(remaining)} left."
            : $"Round end: {Format(remaining)} left (cap is {Format(cap.RoundMaxTime)}).";
    }

    private static bool TryParseDuration(IConsoleShell shell, string[] args, out TimeSpan duration)
    {
        duration = default;

        if (args.Length != 2)
        {
            shell.WriteError($"'{args[0]}' needs exactly one time argument, e.g. '{args[0]} 30m'.");
            return false;
        }

        if (!TryParseDuration(args[1], out duration))
        {
            shell.WriteError($"'{args[1]}' is not a valid duration. Use 90s, 30m, 2h or 1h30m; a bare number is minutes.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Parses "90s", "30m", "2h", "1h30m" and bare numbers (minutes). A leading '-' negates the whole value, so
    /// '-1h30m' is an hour and a half off rather than an hour off and thirty minutes on.
    /// </summary>
    public static bool TryParseDuration(string input, out TimeSpan duration)
    {
        duration = default;

        input = input.Trim().ToLowerInvariant();
        if (input.Length == 0)
            return false;

        var negative = input[0] == '-';
        if (negative || input[0] == '+')
            input = input[1..];

        if (input.Length == 0)
            return false;

        // Bare number: minutes, because the thing being adjusted is hours long. Invariant culture throughout —
        // a server running under a comma-decimal locale must not read "1.5h" differently to one that is not.
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var bare))
        {
            if (double.IsNaN(bare) || double.IsInfinity(bare) || Math.Abs(bare) > 100000)
                return false;

            duration = TimeSpan.FromMinutes(negative ? -bare : bare);
            return true;
        }

        var total = TimeSpan.Zero;
        var start = 0;
        var sawUnit = false;

        for (var i = 0; i < input.Length; i++)
        {
            var unit = input[i] switch
            {
                'h' => TimeSpan.FromHours(1),
                'm' => TimeSpan.FromMinutes(1),
                's' => TimeSpan.FromSeconds(1),
                _ => TimeSpan.Zero,
            };

            if (unit == TimeSpan.Zero)
                continue;

            if (!double.TryParse(input[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || double.IsNaN(value)
                || double.IsInfinity(value)
                || value < 0
                || value > 100000)
                return false;

            total += unit * value;
            start = i + 1;
            sawUnit = true;
        }

        // Trailing digits with no unit ("1h30") are a typo we should reject rather than guess at.
        if (!sawUnit || start != input.Length)
            return false;

        duration = negative ? -total : total;
        return true;
    }

    private static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            return $"-{Format(-time)}";

        var parts = new List<string>();

        if (time.Days > 0 || time.Hours > 0)
            parts.Add($"{(int) time.TotalHours}h");

        if (parts.Count > 0 || time.Minutes > 0)
            parts.Add($"{time.Minutes}m");

        parts.Add($"{time.Seconds}s");

        return string.Join(' ', parts);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(SubCommands, "<action>");

        if (args.Length == 2 && args[0].ToLowerInvariant() is "extend" or "postpone" or "set")
            return CompletionResult.FromHintOptions(new[] { "15m", "30m", "1h" }, "<time>");

        return CompletionResult.Empty;
    }
}
