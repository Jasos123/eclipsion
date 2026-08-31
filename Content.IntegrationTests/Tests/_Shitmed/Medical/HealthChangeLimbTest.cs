using System.Collections.Generic;
using System.Linq;
using Content.Server.EntityEffects.Effects;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Shitmed.Medical;

[TestFixture]
[TestOf(typeof(HealthChange))]
public sealed class HealthChangeLimbTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: MedicalLimbTestMob
  name: medical limb test mob
  components:
  - type: Body
    prototype: Human
  - type: Damageable
    damageContainer: Biological
  - type: Targeting
";

    [Test]
    public async Task HealthChangeHealsBodyAndEveryLimb()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var bodySystem = entities.System<SharedBodySystem>();
        var damageableSystem = entities.System<DamageableSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity("MedicalLimbTestMob", new MapCoordinates(0, 0, map.MapId));
            var parts = bodySystem.GetBodyChildren(mob).ToArray();
            var initialDamage = new DamageSpecifier
            {
                DamageDict = new Dictionary<string, FixedPoint2>
                {
                    { "Blunt", 10 },
                },
            };

            // Damage the global health pool and each limb independently so the reagent effect is isolated.
            damageableSystem.TryChangeDamage(mob, initialDamage, true, doPartDamage: false);
            foreach (var part in parts)
                damageableSystem.TryChangeDamage(part.Id, initialDamage, true);

            var effect = new HealthChange
            {
                Damage = new DamageSpecifier
                {
                    DamageDict = new Dictionary<string, FixedPoint2>
                    {
                        { "Blunt", -10 },
                    },
                },
            };

            effect.Effect(new EntityEffectBaseArgs(mob, entities));

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<DamageableComponent>(mob).TotalDamage,
                    Is.EqualTo(FixedPoint2.Zero),
                    "The reagent should apply its full effect to the mob's global health pool.");
                Assert.That(parts, Has.Length.EqualTo(10), "The human body prototype should contain ten targetable parts.");

                foreach (var part in parts)
                {
                    Assert.That(entities.GetComponent<DamageableComponent>(part.Id).TotalDamage,
                        Is.EqualTo(FixedPoint2.New(5)),
                        $"The reagent should apply its 0.5 limb multiplier to {entities.ToPrettyString(part.Id)}.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
