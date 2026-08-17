using System;
using Content.Server.GameTicking.Rules.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Crescent;

/// <summary>
/// The 4h cap ends the round out from under whatever is happening at the time, so it announces itself first. The
/// warning is scheduled as "this long BEFORE the cap", which only lands where it is meant to if the numbers in the
/// prototype are read in the units the rule expects — a warning parsed as minutes instead of seconds sits 15 hours
/// out and never fires at all, silently, for the rest of the fork's life.
/// </summary>
[TestFixture]
public sealed class RoundTimeWarningTest
{
    /// <summary>The round-length cap every Crescent preset carries.</summary>
    private const string RuleId = "HullrotRoundTimeLimit";

    /// <summary>How far ahead of the cap the sector is supposed to get told.</summary>
    private static readonly TimeSpan Notice = TimeSpan.FromMinutes(15);

    [Test]
    public async Task CapWarnsTheSectorBeforeItFires()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var compFactory = server.ResolveDependency<IComponentFactory>();
        var loc = server.ResolveDependency<ILocalizationManager>();

        await server.WaitPost(() =>
        {
            var rulePrototype = protoManager.Index<EntityPrototype>(RuleId);
            Assert.That(rulePrototype.TryGetComponent<MaxTimeRestartRuleComponent>(out var rule, compFactory), Is.True,
                $"{RuleId} is not a MaxTimeRestartRule.");

            // Guards the seconds-vs-minutes reading of every TimeSpan on this component, warnings included.
            Assert.That(rule!.RoundMaxTime, Is.EqualTo(TimeSpan.FromHours(4)),
                $"{RuleId} no longer caps the round at 4 hours, so the warning below is timed off the wrong clock.");

            Assert.That(rule.Warnings, Is.Not.Empty,
                $"{RuleId} ends the round with no warning at all — it just stops dead on the crew.");

            var noticed = false;

            foreach (var warning in rule.Warnings)
            {
                Assert.That(warning.Before, Is.GreaterThan(TimeSpan.Zero),
                    "A warning at or after the cap fires into a round that has already ended.");
                Assert.That(warning.Before, Is.LessThan(rule.RoundMaxTime),
                    $"A warning {warning.Before} before a {rule.RoundMaxTime} cap is scheduled before the round starts, so it never fires.");

                // A mistyped id is not an error anywhere — it just broadcasts the raw locale key to every player.
                Assert.That(loc.HasString(warning.Message), Is.True,
                    $"Warning message '{warning.Message}' has no locale string.");

                if (warning.Sender is { } sender)
                    Assert.That(loc.HasString(sender), Is.True, $"Warning sender '{sender}' has no locale string.");

                noticed |= warning.Before == Notice;
            }

            Assert.That(noticed, Is.True, $"Nothing warns the sector {Notice.TotalMinutes} minutes out from the cap.");
        });

        await pair.CleanReturnAsync();
    }
}
