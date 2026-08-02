using Content.Server.Lathe;
using Content.Server.Research.Systems;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class ImperialMicroforgeRestrictionsTest
{
    private const string ImperialTubeLoader = "TubeLoaderDSM";

    private static readonly string[] RestrictedPatterns =
    [
        "ClothingOuterCoatClarizianOfficer",
        "ClothingUniformJumpsuitClarizeCombat",
        "ClothingUniformJumpsuitClarizeDoctor",
        "ClothingUniformJumpsuitClarizeOfficer",
        "ClothingHeadHatClarizeBeret",
        "ClothingOuterArmorClarizianTrooperArmor",
        "ClothingHeadHelmetClarizianTrooperHelmet",
        "ClothingOuterHardsuitClarizianWorker",
        "ClothingOuterHardsuitClarize",
    ];

    private static readonly string[] DuplicateTubeLoaders =
    [
        "TubeLoader1Craft",
        "TubeLoader1",
        "TubeLoaderNCWL",
    ];

    [Test]
    public async Task ImperialForgeRejectsClarizianPatternsAndDuplicateTubeLoaders()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var latheSystem = entMan.System<LatheSystem>();
        var researchSystem = entMan.System<ResearchSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(protoMan.TryIndex<TechnologyPrototype>("ImperialClarizianPatterns", out _), Is.False,
                "Clarizian patterns are still present in the Imperial research tree.");

            var armorTechnology = protoMan.Index<TechnologyPrototype>("AdvancedImperialArmor");
            Assert.That(armorTechnology.RecipeUnlocks, Does.Contain("ClothingShoesBootsMagDSM"),
                "Removing the Clarizian technology also removed the legitimate Imperial magboot unlock.");
            Assert.That(armorTechnology.RecipeUnlocks, Does.Contain("ClothingOuterHardsuitHunterElite"),
                "Removing the Clarizian technology also removed the House Romaine armor unlock.");

            var forgeUid = entMan.SpawnEntity("StationMicroforgeDSM", map.GridCoords);
            var lathe = entMan.GetComponent<LatheComponent>(forgeUid);
            var database = entMan.GetComponent<TechnologyDatabaseComponent>(forgeUid);

            Assert.That(lathe.DynamicRecipes, Does.Contain(ImperialTubeLoader),
                "The Imperial station forge lost its inherited research catalogue.");

            researchSystem.AddLatheRecipe(forgeUid, ImperialTubeLoader, database);
            foreach (var recipe in RestrictedPatterns)
                researchSystem.AddLatheRecipe(forgeUid, recipe, database);
            foreach (var recipe in DuplicateTubeLoaders)
                researchSystem.AddLatheRecipe(forgeUid, recipe, database);

            var availableRecipes = latheSystem.GetAvailableRecipes(forgeUid, lathe);
            Assert.Multiple(() =>
            {
                Assert.That(availableRecipes, Does.Contain(ImperialTubeLoader),
                    "The Imperial tube loader recipe was blocked with the foreign duplicates.");
                foreach (var recipe in RestrictedPatterns)
                    Assert.That(availableRecipes, Does.Not.Contain(recipe),
                        $"Imperial station forge exposes restricted pattern {recipe}.");
                foreach (var recipe in DuplicateTubeLoaders)
                    Assert.That(availableRecipes, Does.Not.Contain(recipe),
                        $"Imperial station forge exposes duplicate tube loader {recipe}.");
            });

            var fieldForgeUid = entMan.SpawnEntity("MicroforgeDSM", map.GridCoords);
            var fieldLathe = entMan.GetComponent<LatheComponent>(fieldForgeUid);
            Assert.That(latheSystem.GetAvailableRecipes(fieldForgeUid, fieldLathe),
                Does.Not.Contain("ClothingHeadHatClarizeBeret"),
                "Imperial field forge still has the Clarizian beret pre-loaded.");
        });

        await pair.CleanReturnAsync();
    }
}
