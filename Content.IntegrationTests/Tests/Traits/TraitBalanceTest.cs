using Content.Server.Traits;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared.Customization.Systems;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Traits;

[TestFixture]
[TestOf(typeof(TraitSystem))]
public sealed class TraitBalanceTest
{
    [Test]
    public async Task SurgeryTraitsApplyAdvertisedSpeedWithoutDowngradingJobBonus()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var traitSystem = server.System<TraitSystem>();
            var entity = entMan.SpawnEntity(null, map.GridCoords);
            var training = prototypes.Index<TraitPrototype>("SurgeryTraining");
            var experienced = prototypes.Index<TraitPrototype>("ExperiencedSurgeon");

            traitSystem.AddTrait(entity, training);
            var speed = entMan.GetComponent<SurgerySpeedModifierComponent>(entity);
            Assert.That(speed.SpeedModifier, Is.EqualTo(1.6f));

            traitSystem.AddTrait(entity, experienced);
            Assert.That(speed.SpeedModifier, Is.EqualTo(2.5f));

            speed.SpeedModifier = 3f;
            traitSystem.AddTrait(entity, experienced);
            Assert.That(speed.SpeedModifier, Is.EqualTo(3f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PointTrapRequirementsAndRebalancedCostsStayConfigured()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var training = prototypes.Index<TraitPrototype>("SurgeryTraining");
            var experienced = prototypes.Index<TraitPrototype>("ExperiencedSurgeon");

            Assert.Multiple(() =>
            {
                Assert.That(training.Points, Is.EqualTo(-3));
                Assert.That(experienced.Points, Is.EqualTo(-4));
                Assert.That(prototypes.Index<TraitPrototype>("ParkourTraining").Points, Is.EqualTo(-4));
                Assert.That(prototypes.Index<TraitPrototype>("Vigor").Points, Is.EqualTo(-5));
                Assert.That(prototypes.Index<TraitPrototype>("Bodybuilder").Points, Is.EqualTo(-3));
            });

            Assert.That(ExcludesTrait(training, "ExperiencedSurgeon"), Is.True);
            Assert.That(ExcludesTrait(experienced, "SurgeryTraining"), Is.True);
            Assert.That(ExcludesJob(training, "MedicalDoctor"), Is.True);
            Assert.That(ExcludesJob(training, "PhysicianCMM"), Is.True);
            Assert.That(ExcludesJob(experienced, "ChiefMedicalOfficer"), Is.True);
            Assert.That(ExcludesJob(experienced, "CoordinatorTFSC"), Is.True);

            var cpr = prototypes.Index<TraitPrototype>("CPRTraining");
            Assert.That(ExcludesJob(cpr, "MedicalDoctor"), Is.True);
            Assert.That(ExcludesJob(cpr, "PhysicianCMM"), Is.True);

            var factionLanguages = new[]
            {
                (Trait: "LowImperial", Faction: "DSM"),
                (Trait: "Dockta", Faction: "NCWL"),
                (Trait: "Freespeak", Faction: "TFSC"),
                (Trait: "Kaishago", Faction: "SHI"),
            };

            foreach (var (traitId, factionId) in factionLanguages)
            {
                var trait = prototypes.Index<TraitPrototype>(traitId);
                var requirement = trait.Requirements.OfType<FactionRequirement>().Single();

                Assert.Multiple(() =>
                {
                    Assert.That(trait.Points, Is.EqualTo(-3), traitId);
                    Assert.That(requirement.Inverted, Is.True, traitId);
                    Assert.That(requirement.FactionID, Is.EqualTo(factionId), traitId);
                });
            }
        });

        await pair.CleanReturnAsync();
    }

    private static bool ExcludesTrait(TraitPrototype trait, string excludedTrait)
    {
        return trait.Requirements
            .OfType<CharacterTraitRequirement>()
            .Any(requirement => requirement.Inverted &&
                requirement.Traits.Any(id => id.Id == excludedTrait));
    }

    private static bool ExcludesJob(TraitPrototype trait, string excludedJob)
    {
        return trait.Requirements
            .OfType<CharacterJobRequirement>()
            .Any(requirement => requirement.Inverted &&
                requirement.Jobs.Any(id => id.Id == excludedJob));
    }
}
