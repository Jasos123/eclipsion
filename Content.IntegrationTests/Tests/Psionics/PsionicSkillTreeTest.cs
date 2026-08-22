using System.Collections.Generic;
using Content.Server.Abilities.Psionics;
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

    /// <summary>
    ///     Recurrence is the only chain that starts above level one, so a mistake in its gates or costs
    ///     leaves the capstone out of reach for anyone who did not build around it. Walk the whole branch
    ///     the way a normal Psion would: primary discipline first at level one, then the class on top.
    /// </summary>
    [Test]
    public async Task RecurrenceBranchCanBeFullyUnlocked()
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
            var psionic = entMan.AddComponent<PsionicComponent>(entity);

            Assert.Multiple(() =>
            {
                Assert.That(psionic.SkillTree.Id, Is.EqualTo("PsionicGeneralTree"));

                // The branch root is deliberately shut at level one, unlike every other class root.
                Assert.That(
                    skillTree.BuildState(entity).Skills
                        .Single(skill => skill.SkillId == "PsiSkillStasisField").Availability,
                    Is.EqualTo(PsionicSkillAvailability.InsufficientLevel));
            });

            // A Psion who skipped their primary discipline would have a point spare at every step, which
            // would hide a gate that is one level too high. Spend the level one point the way the tree
            // expects instead, so the branch has to fit in what is actually left over.
            Assert.That(skillTree.TryUnlock(entity, "PsiSkillTelepathy"), Is.True);
            Assert.That(psionic.SkillPoints, Is.Zero);

            // Node id, the level it opens at, and what it costs.
            var chain = new[]
            {
                ("PsiSkillStasisField", 2, 1),
                ("PsiSkillArmorReweave", 3, 1),
                ("PsiSkillRecurrencePulse", 4, 1),
            };

            foreach (var (skillId, level, cost) in chain)
            {
                var skill = prototypes.Index<PsionicSkillPrototype>(skillId);

                Assert.Multiple(() =>
                {
                    Assert.That(skill.MinimumLevel, Is.EqualTo(level), $"{skillId} opens at an unexpected level.");
                    Assert.That(skill.Cost, Is.EqualTo(cost), $"{skillId} costs an unexpected number of points.");
                });

                while (psionic.PsionicLevel < level)
                    skillTree.GainLevel(entity, psionic, feedback: false);

                Assert.That(
                    skillTree.BuildState(entity).Skills.Single(node => node.SkillId == skillId).Availability,
                    Is.EqualTo(PsionicSkillAvailability.Available),
                    $"{skillId} is not purchasable at level {level}.");
                Assert.That(skillTree.TryUnlock(entity, skillId), Is.True, $"{skillId} refused to unlock.");
                Assert.That(
                    entMan.GetComponent<PsionicComponent>(entity).ActivePowers,
                    Does.Contain(prototypes.Index(skill.Power)),
                    $"{skillId} unlocked without granting its power.");
            }

            // Four points earned by level four, four points spent: a primary discipline plus the whole
            // branch, with nothing left over. Raising a gate or a cost here puts the capstone out of reach.
            Assert.Multiple(() =>
            {
                Assert.That(psionic.PsionicLevel, Is.EqualTo(4));
                Assert.That(psionic.SkillPoints, Is.Zero);
            });
            Assert.That(psionic.UnlockedSkills, Is.SupersetOf(chain.Select(node => node.Item1)));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Bone holds no noosphere. Skeletons must not be able to buy their way into psionics at character
    ///     creation, nor be handed it in-round by a glimmer event or the accept-psionics prompt.
    /// </summary>
    [Test]
    public async Task SkeletonsCannotBecomePsionic()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var prototypes = server.ResolveDependency<IPrototypeManager>();

            foreach (var traitId in new[] { "LatentPsychic", "PsychoHistorian", "Biomancer", "Pyromancer" })
            {
                var excludesSkeletons = prototypes.Index<TraitPrototype>(traitId).Requirements
                    .OfType<CharacterLogicRequirement>()
                    .SelectMany(requirement => requirement.Requirements)
                    .OfType<CharacterSpeciesRequirement>()
                    .Any(requirement =>
                        requirement.Inverted &&
                        requirement.Species.Any(species => species.Id == "Skeleton"));

                Assert.That(excludesSkeletons, Is.True, $"{traitId} should exclude Skeleton.");
            }

            var skeleton = entMan.SpawnEntity("MobSkeletonPerson", map.GridCoords);

            Assert.That(entMan.HasComponent<MindbrokenComponent>(skeleton), Is.True);

            server.System<PsionicAbilitiesSystem>().AddPsionics(skeleton);

            Assert.That(entMan.HasComponent<PsionicComponent>(skeleton), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Atyrians are a shade quicker to the noosphere than everyone else. The bonus lives on the species
    ///     prototype rather than on PsionicComponent, which a caster trait only adds later, so this checks
    ///     that the progression system actually reads it.
    /// </summary>
    [Test]
    public async Task AtyriansEarnPotentiaFasterThanBaseline()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;

            float Cast(string protoId)
            {
                var caster = entMan.SpawnEntity(protoId, map.GridCoords);
                var psionic = entMan.EnsureComponent<PsionicComponent>(caster);
                psionic.Potentia = 0;

                // Raised by hand rather than through LogPowerUsed, whose glimmer roll is random.
                var castEv = new PsionicPowerCastEvent(caster, "test power", 10f);
                entMan.EventBus.RaiseLocalEvent(caster, ref castEv);

                return psionic.Potentia;
            }

            var human = Cast("MobHuman");
            var atyrian = Cast("MobMoth");

            Assert.Multiple(() =>
            {
                Assert.That(human, Is.GreaterThan(0));
                Assert.That(atyrian, Is.GreaterThan(human));
                Assert.That(atyrian, Is.EqualTo(human * 1.2f).Within(0.01f));
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
