using Content.Server.Lathe;
using Content.Server.Research.Systems;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class FactionGrenadeResearchTest
{
    private const string TwistcapGrenade = "GrenadeDSMG4Twistcap";
    private const string IncendiaryGrenade = "M14IncendiaryHandgrenade";
    private const string SmokeGrenade = "M18SmokeHandgrenade";

    private static readonly string[] Microforges =
    [
        "PristineMicroforge",
        "StationMicroforgeDSM",
        "StationMicroforgeNCWL",
        "StationMicroforgeSHI",
        "StationMicroforgeTFSC",
        "MicroforgeDSM",
        "MicroforgeNCWL",
    ];

    private static readonly (string Lathe, string Technology)[] FactionTechnologies =
    [
        ("StationMicroforgeDSM", "SurplusImperialRifling"),
        ("StationMicroforgeNCWL", "CommunardInfantry"),
        ("StationMicroforgeSHI", "ShinoharaBasicArms"),
        ("PristineMicroforge", "MinutemenBoltRifle"),
        ("StationMicroforgeTFSC", "CyberdawnBasicBallistics"),
        ("StationMicroforgeTFSC", "InterdyneArmor"),
        ("StationMicroforgeTFSC", "FamiliesAuxiliaryArtillery"),
        ("StationMicroforgeTFSC", "CoalitionCyberdawnBasicBallistics"),
    ];

    [Test]
    public async Task FactionTechnologyMakesBothGrenadesAvailableInItsLathe()
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
            foreach (var (lathePrototype, technologyId) in FactionTechnologies)
            {
                var latheUid = entMan.SpawnEntity(lathePrototype, map.GridCoords);
                var lathe = entMan.GetComponent<LatheComponent>(latheUid);
                var technology = protoMan.Index<TechnologyPrototype>(technologyId);

                Assert.Multiple(() =>
                {
                    Assert.That(lathe.DynamicRecipes, Does.Contain(IncendiaryGrenade),
                        $"{lathePrototype} cannot print the incendiary grenade even after research.");
                    Assert.That(lathe.DynamicRecipes, Does.Contain(SmokeGrenade),
                        $"{lathePrototype} cannot print the smoke grenade even after research.");
                    Assert.That(latheSystem.GetAvailableRecipes(latheUid, lathe), Does.Not.Contain(IncendiaryGrenade),
                        $"{lathePrototype} exposes the incendiary grenade before {technologyId} is researched.");
                    Assert.That(latheSystem.GetAvailableRecipes(latheUid, lathe), Does.Not.Contain(SmokeGrenade),
                        $"{lathePrototype} exposes the smoke grenade before {technologyId} is researched.");
                });

                researchSystem.AddTechnology(latheUid, technology);
                var availableRecipes = latheSystem.GetAvailableRecipes(latheUid, lathe);

                Assert.Multiple(() =>
                {
                    Assert.That(availableRecipes, Does.Contain(IncendiaryGrenade),
                        $"{technologyId} did not unlock the incendiary grenade in {lathePrototype}.");
                    Assert.That(availableRecipes, Does.Contain(SmokeGrenade),
                        $"{technologyId} did not unlock the smoke grenade in {lathePrototype}.");
                });

                entMan.DeleteEntity(latheUid);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TwistcapGrenadeIsAvailableInEveryMicroforgeWithoutResearch()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.ResolveDependency<IEntityManager>();
        var latheSystem = entMan.System<LatheSystem>();

        await server.WaitAssertion(() =>
        {
            foreach (var lathePrototype in Microforges)
            {
                var latheUid = entMan.SpawnEntity(lathePrototype, map.GridCoords);
                var lathe = entMan.GetComponent<LatheComponent>(latheUid);

                Assert.That(latheSystem.GetAvailableRecipes(latheUid, lathe), Does.Contain(TwistcapGrenade),
                    $"{lathePrototype} does not expose the twistcap grenade as a base recipe.");

                entMan.DeleteEntity(latheUid);
            }
        });

        await pair.CleanReturnAsync();
    }
}
