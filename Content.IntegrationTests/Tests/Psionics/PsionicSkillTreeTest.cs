using System.Collections.Generic;
using Content.Server.Psionics;
using Content.Server.Traits;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions;
using Content.Shared.Customization.Systems;
using Content.Shared.Psionics;
using Content.Shared.Psionics.Glimmer;
using Content.Shared.Traits;
using Content.Shared.Voidborn;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Psionics;

[TestFixture]
[TestOf(typeof(PsionicSkillTreeSystem))]
public sealed class PsionicSkillTreeTest
{
    [Test]
    public async Task InnatePowerExcludesOtherSkillsInItsExclusiveGroup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var entity = entMan.SpawnEntity(null, map.GridCoords);
            var psionic = entMan.AddComponent<PsionicComponent>(entity);
            psionic.ActivePowers.Add(prototypes.Index<PsionicPowerPrototype>("PyrokineticFlare"));

            var state = server.System<PsionicSkillTreeSystem>().BuildState(entity);
            var excluded = state.Skills.Single(skill => skill.SkillId == "PsiSkillVeilSight");

            Assert.That(excluded.Availability, Is.EqualTo(PsionicSkillAvailability.Excluded));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PsychoHistorianStartsWithTelepathyAndEarnsFirstPointAtLevelTwo()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var skillTree = server.System<PsionicSkillTreeSystem>();
            var entity = entMan.SpawnEntity(null, map.GridCoords);

            server.System<TraitSystem>().AddTrait(
                entity,
                prototypes.Index<TraitPrototype>("PsychoHistorian"));

            var psionic = entMan.GetComponent<PsionicComponent>(entity);
            var telepathy = prototypes.Index<PsionicPowerPrototype>("TelepathyPower");
            var noosphericZap = prototypes.Index<PsionicPowerPrototype>("NoosphericZapPower");
            var initialState = skillTree.BuildState(entity);

            Assert.Multiple(() =>
            {
                Assert.That(psionic.SkillTree.Id, Is.EqualTo("PsionicMindTree"));
                Assert.That(psionic.PsionicLevel, Is.EqualTo(1));
                Assert.That(psionic.SkillPoints, Is.Zero);
                Assert.That(psionic.ActivePowers, Does.Contain(telepathy));
                Assert.That(
                    initialState.Skills.Single(skill => skill.SkillId == "PsiSkillTelepathy").Availability,
                    Is.EqualTo(PsionicSkillAvailability.Owned));
                Assert.That(
                    initialState.Skills.Single(skill => skill.SkillId == "PsiSkillNoosphericZap").Availability,
                    Is.EqualTo(PsionicSkillAvailability.InsufficientLevel));
            });

            skillTree.GainLevel(entity, psionic, feedback: false);

            Assert.That(
                skillTree.BuildState(entity).Skills
                    .Single(skill => skill.SkillId == "PsiSkillNoosphericZap").Availability,
                Is.EqualTo(PsionicSkillAvailability.Available));
            Assert.That(skillTree.TryUnlock(entity, "PsiSkillNoosphericZap"), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(psionic.PsionicLevel, Is.EqualTo(2));
                Assert.That(psionic.SkillPoints, Is.Zero);
                Assert.That(psionic.ActivePowers, Does.Contain(noosphericZap));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyOilAndDustCanTriggerRerolls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var entity = entMan.SpawnEntity(null, map.GridCoords);
            var psionic = entMan.AddComponent<PsionicComponent>(entity);
            var psionics = server.System<PsionicsSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(psionics.RerollPsionics(entity, "SpaceDrugs", psionic), Is.False);
                Assert.That(psionic.CanReroll, Is.True);
                Assert.That(psionics.RerollPsionics(entity, "LotophagoiOil", psionic), Is.True);
                Assert.That(psionic.CanReroll, Is.False);
            });

            psionic.CanReroll = true;
            Assert.That(psionics.RerollPsionics(entity, "OusianaDust", psionic), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UsingAPowerEarnsPotentiaWithDiminishingReturns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var caster = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var psionic = entMan.EnsureComponent<PsionicComponent>(caster);
            var abilities = server.System<SharedPsionicAbilitiesSystem>();
            psionic.Potentia = 0;

            abilities.LogPowerUsed(caster, "test power");
            var firstCast = psionic.Potentia;

            abilities.LogPowerUsed(caster, "test power");
            var secondCast = psionic.Potentia - firstCast;

            Assert.Multiple(() =>
            {
                Assert.That(firstCast, Is.GreaterThan(0));

                // Fatigue has had no time to decay between the two, so the follow-up is worth about half.
                Assert.That(secondCast, Is.GreaterThan(0));
                Assert.That(secondCast, Is.LessThan(firstCast));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AmbientGlimmerFeedsPotentia()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var timing = server.ResolveDependency<IGameTiming>();
        var glimmer = server.System<GlimmerSystem>();
        PsionicComponent psionic = default!;

        await server.WaitAssertion(() =>
        {
            var psion = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            psionic = server.EntMan.EnsureComponent<PsionicComponent>(psion);
            psionic.Potentia = 0;
            glimmer.SetGlimmerOutput(500);
        });

        // The ambient tick runs on a five second timer, so give it room for at least one pass.
        await server.WaitRunTicks(timing.TickRate * 6);

        await server.WaitAssertion(() =>
        {
            Assert.That(psionic.Potentia, Is.GreaterThan(0));

            // Glimmer is process-wide state, so leave it as we found it for the next test out of the pool.
            glimmer.SetGlimmerInput(0);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShadeskipSpawnsTemporaryShadowField()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var timing = server.ResolveDependency<IGameTiming>();
            var caster = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<PsionicComponent>(caster);
            var action = entMan.SpawnEntity("ActionShadeskip", map.GridCoords);
            var instant = entMan.GetComponent<InstantActionComponent>(action);

            var shadowsBefore = entMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "ShadowKudzuTemp");

            server.System<SharedActionsSystem>().PerformAction(
                caster,
                null,
                action,
                instant,
                instant.Event,
                timing.CurTime,
                predicted: false);

            var shadowsAfter = entMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "ShadowKudzuTemp");

            Assert.Multiple(() =>
            {
                Assert.That(instant.Event?.Handled, Is.True);
                Assert.That(shadowsAfter, Is.GreaterThan(shadowsBefore));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DarkSwapCanEnterAndLeaveShadowState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var timing = server.ResolveDependency<IGameTiming>();
            var caster = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<PsionicComponent>(caster);
            var action = entMan.SpawnEntity("ActionDarkSwap", map.GridCoords);
            var instant = entMan.GetComponent<InstantActionComponent>(action);
            var actions = server.System<SharedActionsSystem>();

            actions.PerformAction(caster, null, action, instant, instant.Event, timing.CurTime, predicted: false);
            Assert.That(entMan.HasComponent<EtherealComponent>(caster), Is.True);

            actions.PerformAction(caster, null, action, instant, instant.Event, timing.CurTime, predicted: false);
            Assert.That(entMan.HasComponent<EtherealComponent>(caster), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DependentFeatAppliesAfterCasterConfiguration()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var entity = entMan.SpawnEntity(null, map.GridCoords);
            var traits = new List<TraitPrototype>
            {
                prototypes.Index<TraitPrototype>("PowerOverwhelming"),
                prototypes.Index<TraitPrototype>("Pyromancer"),
            };

            traits.Sort();
            foreach (var trait in traits)
                server.System<TraitSystem>().AddTrait(entity, trait);

            var psionic = entMan.GetComponent<PsionicComponent>(entity);
            var overwhelming = prototypes.Index<PsionicPowerPrototype>("PowerOverwhelming");

            Assert.Multiple(() =>
            {
                Assert.That(traits[0].ID, Is.EqualTo("Pyromancer"));
                Assert.That(psionic.SkillTree.Id, Is.EqualTo("PsionicFireTree"));
                Assert.That(psionic.PsionicLevel, Is.EqualTo(1));
                Assert.That(psionic.SkillPoints, Is.EqualTo(1));
                Assert.That(psionic.ActivePowers, Does.Contain(overwhelming));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CasterTraitsExcludeInnatelyPsionicVoidborn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var casterTraits = new[]
            {
                "LatentPsychic",
                "PsionicInsulation",
                "PsychoHistorian",
                "Biomancer",
                "Pyromancer",
            };

            foreach (var traitId in casterTraits)
            {
                var trait = prototypes.Index<TraitPrototype>(traitId);
                var excludesVoidborn = trait.Requirements
                    .OfType<CharacterLogicRequirement>()
                    .SelectMany(requirement => requirement.Requirements)
                    .OfType<CharacterSpeciesRequirement>()
                    .Any(requirement =>
                        requirement.Inverted &&
                        requirement.Species.Any(species => species.Id == "Voidborn"));

                Assert.That(excludesVoidborn, Is.True, $"{traitId} should exclude Voidborn.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
