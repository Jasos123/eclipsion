using System.Numerics;
using System.Collections.Generic;
using Content.Server._Crescent.ProximityFuse;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.HeatSeeking;
using Content.Shared._Crescent.SpaceArtillery;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Projectiles;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class ShipWeaponBalanceTest
{
    [Test]
    public async Task EnergyRechargeConsumesBatteryAndLegacyOptOutRemainsFree()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var batterySystem = entMan.System<BatterySystem>();

        await server.WaitAssertion(() =>
        {
            var retributionUid = entMan.SpawnEntity("WeaponTurretRetribution", map.GridCoords);
            var recharge = entMan.GetComponent<RechargeBasicEntityAmmoComponent>(retributionUid);
            var ammo = entMan.GetComponent<BasicEntityAmmoProviderComponent>(retributionUid);
            var gun = entMan.GetComponent<GunComponent>(retributionUid);
            var battery = entMan.GetComponent<BatteryComponent>(retributionUid);

            Assert.Multiple(() =>
            {
                Assert.That(recharge.EnergyPerCharge, Is.EqualTo(75f));
                Assert.That(ammo.Capacity, Is.EqualTo(36));
                Assert.That(gun.FireRate, Is.EqualTo(6f));
                Assert.That(gun.ProjectileSpeed, Is.EqualTo(100f));
            });

            batterySystem.SetCharge(retributionUid, 0f, battery);
            var unpoweredAttempt = new RechargeBasicEntityAmmoAttemptEvent(recharge.EnergyPerCharge);
            entMan.EventBus.RaiseLocalEvent(retributionUid, ref unpoweredAttempt);

            Assert.Multiple(() =>
            {
                Assert.That(unpoweredAttempt.Allowed, Is.False);
                Assert.That(battery.CurrentCharge, Is.Zero);
            });

            batterySystem.SetCharge(retributionUid, 500f, battery);
            var poweredAttempt = new RechargeBasicEntityAmmoAttemptEvent(recharge.EnergyPerCharge);
            entMan.EventBus.RaiseLocalEvent(retributionUid, ref poweredAttempt);

            Assert.Multiple(() =>
            {
                Assert.That(poweredAttempt.Allowed, Is.True);
                Assert.That(battery.CurrentCharge, Is.EqualTo(425f));
            });

            var exhumerUid = entMan.SpawnEntity("ShuttleGunExhumer", map.GridCoords);
            var legacyRecharge = entMan.GetComponent<RechargeBasicEntityAmmoComponent>(exhumerUid);
            Assert.That(legacyRecharge.EnergyPerCharge, Is.Zero,
                "Opt-in power costs must not alter free-recharge legacy or utility weapons.");

            var pilaUid = entMan.SpawnEntity("BaseWeaponTurretPila", map.GridCoords);
            var pilaRecharge = entMan.GetComponent<RechargeBasicEntityAmmoComponent>(pilaUid);
            Assert.That(pilaRecharge.EnergyPerCharge, Is.EqualTo(125f),
                "The active Pila's unlimited synthesized ammunition must have an energy cost.");

            entMan.DeleteEntity(retributionUid);
            entMan.DeleteEntity(exhumerUid);
            entMan.DeleteEntity(pilaUid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ActiveReloadableWeaponsUseDelayedLoadingAndIntendedAmmunition()
    {
        var reloadableLaunchers = new Dictionary<string, (float Delay, string Ammo)>
        {
            ["BaseWeaponTurretShortbow"] = (0.6f, "Cartridge48mmRocket"),
            ["BaseWeaponTurretShortbowGorlex"] = (0.6f, "Cartridge48mmRocketGorlex"),
            ["BaseWeaponTurretAzimuth"] = (0.8f, "Cartridge60mmRocket"),
            ["BaseWeaponTurretAzimuthCDT"] = (0.8f, "Cartridge60mmRocket"),
            ["BaseWeaponTurretAzimuthNCWL"] = (0.8f, "Cartridge60mmRocketNCWL"),
            ["BaseWeaponTurretAzimuthGSC"] = (0.8f, "Cartridge60mmRocketGorlex"),
            ["BaseWeaponTurretArbalest"] = (1.25f, "Cartridge95mmRocket"),
            ["BaseWeaponTurretArbalestTFSC"] = (1.25f, "Cartridge95mmRocketGSC"),
            ["BaseWeaponTurretArbalestNCWL"] = (1.25f, "Cartridge95mmRocketNCWL"),
            ["BaseWeaponTurretGargoyleTorpedo"] = (2.5f, "Cartridge240mmRocket"),
            ["BaseWeaponTurretGargoyleTorpedoNCWL"] = (2.5f, "Cartridge240mmRocketNCWL"),
            ["BaseWeaponTurretCatapult"] = (4f, "Cartridge330mmRocket"),
        };

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var (prototype, expected) in reloadableLaunchers)
            {
                var uid = entMan.SpawnEntity(prototype, map.GridCoords);
                var ammo = entMan.GetComponent<BallisticAmmoProviderComponent>(uid);

                Assert.Multiple(() =>
                {
                    Assert.That(ammo.Proto, Is.Not.Null);
                    Assert.That(ammo.Proto!.Value.Id, Is.EqualTo(expected.Ammo));
                    Assert.That(ammo.FillWithDoAfter, Is.True,
                        $"{prototype} must pay a logistics reload-time cost.");
                    Assert.That(ammo.FillDelay, Is.EqualTo(TimeSpan.FromSeconds(expected.Delay)));
                });

                entMan.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ActiveNewMissilesBypassShieldsAndUseCurvedGuidance()
    {
        var missilePrototypes = new[]
        {
            "ShortbowRocketProjectile",
            "ShortbowRocketProjectileGorlex",
            "AzimuthRocketProjectile",
            "AzimuthRocketProjectileGorlex",
            "AzimuthRocketProjectileNCWL",
            "ArbalestRocketProjectile",
            "ArbalestRocketProjectileNCWL",
            "ArbalestRocketProjectileGSC",
            "ProjectileGargoyle",
            "ProjectileGargoyleNCWL",
            "ProjectileCatapultMissile",
            "PilaRocketProjectile",
        };

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var prototype in missilePrototypes)
            {
                var uid = entMan.SpawnEntity(prototype, map.GridCoords);
                var heatSeeking = entMan.GetComponent<HeatSeekingComponent>(uid);

                Assert.Multiple(() =>
                {
                    Assert.That(entMan.HasComponent<IgnoresHullrotShieldsComponent>(uid), Is.True,
                        $"{prototype} must pass through Hullrot ship shields.");
                    Assert.That(heatSeeking.WeaveAmplitude.Degrees, Is.GreaterThan(0f),
                        $"{prototype} must use curved heat-seeking guidance.");
                    Assert.That(heatSeeking.WeaveFrequency, Is.GreaterThan(0f));
                    Assert.That(heatSeeking.WeaveFadeDistance, Is.GreaterThan(0f));
                });

                entMan.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ActiveMagazineWeaponsApplyCaliberBasedReloadLockouts()
    {
        var magazineWeapons = new Dictionary<string, float>
        {
            ["WeaponTurretSmallMachinegun"] = 3f,
            ["WeaponTurretSmallMachinegunNCWL"] = 3f,
            ["WeaponTurretSmallMachinegunSAW"] = 4.5f,
            ["WeaponTurretMediumMachinegun"] = 3.5f,
            ["WeaponTurretMediumMachinegunNCWL"] = 3.5f,
            ["WeaponTurretSmallArtillery"] = 4f,
            ["WeaponTurretSmallArtilleryNCWL"] = 4f,
            ["WeaponTurretLargeMachinegun"] = 5f,
            ["WeaponTurretLargeMachinegunNCWL"] = 5f,
            ["FlakCannonTurretCorpus"] = 5f,
        };

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            foreach (var (prototype, reloadDuration) in magazineWeapons)
            {
                var uid = entMan.SpawnEntity(prototype, map.GridCoords);
                var provider = entMan.GetComponent<MagazineAmmoProviderComponent>(uid);
                Assert.That(provider.ReloadDuration, Is.EqualTo(reloadDuration));

                entMan.DeleteEntity(uid);
            }

            var screamerUid = entMan.SpawnEntity("WeaponTurretSmallMachinegun", map.GridCoords);
            var slots = entMan.GetComponent<ItemSlotsComponent>(screamerUid);
            var gun = entMan.GetComponent<GunComponent>(screamerUid);
            var inserted = new ItemSlotInsertEvent(
                screamerUid,
                screamerUid,
                screamerUid,
                slots.Slots["gun_magazine"]);
            var expectedReadyTime = timing.CurTime + TimeSpan.FromSeconds(3);

            entMan.EventBus.RaiseLocalEvent(screamerUid, ref inserted);

            Assert.That(gun.NextFire, Is.EqualTo(expectedReadyTime),
                "A user-loaded 20mm magazine must lock firing for its reload duration.");
            entMan.DeleteEntity(screamerUid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ActivePointDefenceRetainsItsInterceptionEnvelope()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var screamerUid = entMan.SpawnEntity("WeaponTurretSmallMachinegun", map.GridCoords);
            var screamerGun = entMan.GetComponent<GunComponent>(screamerUid);
            var screamerAmmo = new GetAmmoCountEvent();
            entMan.EventBus.RaiseLocalEvent(screamerUid, ref screamerAmmo);

            var warthogUid = entMan.SpawnEntity("WeaponTurretSmallMachinegunNCWL", map.GridCoords);
            var warthogGun = entMan.GetComponent<GunComponent>(warthogUid);
            var warthogAmmo = new GetAmmoCountEvent();
            entMan.EventBus.RaiseLocalEvent(warthogUid, ref warthogAmmo);

            var stormreignUid = entMan.SpawnEntity("FlakCannonTurretCorpus", map.GridCoords);
            var stormreignGun = entMan.GetComponent<GunComponent>(stormreignUid);
            var stormreignBoxUid = entMan.SpawnEntity("MagazineCorpus", map.GridCoords);
            var stormreignBox = entMan.GetComponent<BallisticAmmoProviderComponent>(stormreignBoxUid);

            var bulletUid = entMan.SpawnEntity("Bullet20mmBase", map.GridCoords);
            var bullet = entMan.GetComponent<ProjectileComponent>(bulletUid);
            var flakUid = entMan.SpawnEntity("CorpusProjectile", map.GridCoords);
            var flak = entMan.GetComponent<ProjectileComponent>(flakUid);

            Assert.Multiple(() =>
            {
                Assert.That(screamerGun.ProjectileSpeed, Is.EqualTo(110f));
                Assert.That(screamerGun.FireRate, Is.EqualTo(10f));
                Assert.That(screamerAmmo.Count / screamerGun.FireRate, Is.GreaterThanOrEqualTo(24f));
                Assert.That((float) bullet.Damage.GetTotal(), Is.EqualTo(130f));

                Assert.That(warthogGun.ProjectileSpeed, Is.EqualTo(110f));
                Assert.That(warthogGun.FireRate, Is.EqualTo(8f));
                Assert.That(warthogAmmo.Count / warthogGun.FireRate, Is.GreaterThanOrEqualTo(30f));

                Assert.That(stormreignGun.ProjectileSpeed, Is.EqualTo(105f));
                Assert.That(stormreignGun.FireRate, Is.EqualTo(1.2f));
                Assert.That(stormreignBox.Capacity / stormreignGun.FireRate, Is.EqualTo(25f).Within(0.001f));
                Assert.That((float) flak.Damage.GetTotal(), Is.EqualTo(150f));
            });

            entMan.DeleteEntity(screamerUid);
            entMan.DeleteEntity(warthogUid);
            entMan.DeleteEntity(stormreignUid);
            entMan.DeleteEntity(stormreignBoxUid);
            entMan.DeleteEntity(bulletUid);
            entMan.DeleteEntity(flakUid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProximityFuseIgnoresAlliedHullTargets()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var transformSystem = entMan.System<SharedTransformSystem>();
        var shuttleSystem = entMan.System<SharedShuttleSystem>();

        EntityUid fuseUid = default;
        EntityUid alliedThrusterUid = default;
        EntityUid hostileThrusterUid = default;

        await server.WaitAssertion(() =>
        {
            var alliedGrid = mapSystem.CreateGridEntity(map.MapId);
            var hostileGrid = mapSystem.CreateGridEntity(map.MapId);
            mapSystem.SetTile(alliedGrid.Owner, alliedGrid.Comp, new EntityCoordinates(alliedGrid, 0, 0), map.Tile.Tile);
            mapSystem.SetTile(hostileGrid.Owner, hostileGrid.Comp, new EntityCoordinates(hostileGrid, 0, 0), map.Tile.Tile);
            transformSystem.SetWorldPosition(alliedGrid, new Vector2(2f, 0f));
            transformSystem.SetWorldPosition(hostileGrid, new Vector2(4f, 0f));

            shuttleSystem.SetIFFFaction(map.Grid, "FuseTestFaction");
            shuttleSystem.SetIFFFaction(alliedGrid, "FuseTestFaction");
            shuttleSystem.SetIFFFaction(hostileGrid, "HostileFuseTestFaction");

            var shooterUid = entMan.SpawnEntity(null, map.GridCoords);
            alliedThrusterUid = entMan.SpawnEntity("Thruster", new EntityCoordinates(alliedGrid, 0.5f, 0.5f));
            hostileThrusterUid = entMan.SpawnEntity("Thruster", new EntityCoordinates(hostileGrid, 0.5f, 0.5f));

            fuseUid = entMan.SpawnEntity(null, map.MapCoords);
            var projectile = entMan.AddComponent<ProjectileComponent>(fuseUid);
            projectile.Shooter = shooterUid;
            var fuse = entMan.AddComponent<ProximityFuseComponent>(fuseUid);
            fuse.Safety = 0f;
            fuse.MaxRange = 8f;
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var fuse = entMan.GetComponent<ProximityFuseComponent>(fuseUid);
            Assert.Multiple(() =>
            {
                Assert.That(fuse.Targets, Does.Not.ContainKey(alliedThrusterUid),
                    "An allied hull target must be rejected by its grid IFF.");
                Assert.That(fuse.Targets, Does.ContainKey(hostileThrusterUid),
                    "Static hostile hull targets must remain valid proximity-fuse targets.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
