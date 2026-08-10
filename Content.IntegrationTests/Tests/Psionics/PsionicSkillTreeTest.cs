using Content.Server.Psionics;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Psionics;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

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
}
