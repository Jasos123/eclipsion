using System.Numerics;
using Content.Server._Crescent.Territory;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Crescent.Territory;
using Content.Server.PointCannons;
using Content.Shared.CaptureFlag;
using Content.Shared.PointCannons;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._Crescent;

/// <summary>
/// Reproduction for the reported server exceptions when the tile underneath a hardpoint-mounted ship gun or a
/// persistent capture flag is destroyed.
/// </summary>
[TestFixture]
public sealed class HardpointTileLossTest
{
    private const string Gun = "WeaponTurretPDT";
    private const string Hardpoint = "AAAHardpointSmallBallistic";

    /// <summary>
    /// Blow the floor out from under a mounted gun, then let a player pry the gun loose afterwards.
    /// </summary>
    [Test]
    public async Task TileUnderMountedGunDestroyed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mapSys = server.System<SharedMapSystem>();
        var xformSys = server.System<SharedTransformSystem>();

        EntityUid gunUid = default;
        EntityUid hardpointUid = default;

        await server.WaitPost(() =>
        {
            hardpointUid = entMan.SpawnEntity(Hardpoint, map.GridCoords);
            gunUid = entMan.SpawnEntity(Gun, map.GridCoords);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<HardpointAnchorableOnlyComponent>(gunUid).anchoredTo,
                Is.EqualTo(hardpointUid), "Test setup failed: the gun never mounted on the hardpoint.");
        });

        // Destroy the tile, then the hardpoint, the way a shipgun hit on that tile would.
        await server.WaitPost(() =>
        {
            var indices = mapSys.TileIndicesFor(map.Grid.Owner, map.Grid.Comp, map.GridCoords);
            mapSys.SetTile(map.Grid.Owner, map.Grid.Comp, indices, Tile.Empty);
        });

        await server.WaitRunTicks(5);

        await server.WaitPost(() => entMan.DeleteEntity(hardpointUid));
        await server.WaitRunTicks(5);

        // Whatever state the gun is left in, moving it must not throw.
        await server.WaitPost(() =>
        {
            if (!entMan.Deleted(gunUid))
                xformSys.Unanchor(gunUid, entMan.GetComponent<TransformComponent>(gunUid));
        });

        await server.WaitRunTicks(5);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Delete the hardpoint while the gun survives, then unanchor the gun. This is the state a gun is left in when
    /// the hardpoint under it is destroyed but the gun itself is not.
    /// </summary>
    [Test]
    public async Task HardpointDeletedUnderSurvivingGun()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var xformSys = server.System<SharedTransformSystem>();
        var cannons = server.System<PointCannonSystem>();

        EntityUid gunUid = default;
        EntityUid hardpointUid = default;
        EntityUid consoleUid = default;

        await server.WaitPost(() =>
        {
            hardpointUid = entMan.SpawnEntity(Hardpoint, map.GridCoords);
            gunUid = entMan.SpawnEntity(Gun, map.GridCoords);
            consoleUid = entMan.SpawnEntity("ComputerTargeting", map.GridCoords);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var console = entMan.GetComponent<TargetingConsoleComponent>(consoleUid);
            cannons.LinkAllCannonsToConsole(consoleUid, console);
            Assert.That(console.CannonGroups["all"], Does.Contain(gunUid),
                "Test setup failed: the console never picked the gun up.");
        });

        await server.WaitPost(() => entMan.DeleteEntity(hardpointUid));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var anchor = entMan.GetComponent<HardpointAnchorableOnlyComponent>(gunUid);
            Assert.That(anchor.anchoredTo, Is.Null,
                "The gun still points at a deleted hardpoint; every later read of it throws.");

            var console = entMan.GetComponent<TargetingConsoleComponent>(consoleUid);
            Assert.That(console.CannonGroups["all"], Does.Not.Contain(gunUid),
                "A gun whose hardpoint was destroyed is still linked to its targeting console.");

            Assert.That(cannons.CanAimAt(gunUid, Vector2.One * 50f), Is.False,
                "A gun with no hardpoint left reports that it can bear on a target.");

            var xform = entMan.GetComponent<TransformComponent>(gunUid);
            if (xform.Anchored)
                xformSys.Unanchor(gunUid, xform);
        });

        await server.WaitRunTicks(5);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Delete the grid a persistent capture flag lives on, both after it has settled and on the very tick it
    /// initializes, when the system still has a queued next-tick application for it.
    /// </summary>
    [Test]
    public async Task CaptureFlagGridDeleted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        EntityUid flagUid = default;

        await server.WaitPost(() =>
        {
            flagUid = entMan.CreateEntityUninitialized("PersistentCaptureRegionFlag", map.GridCoords);
            entMan.GetComponent<PersistentCaptureRegionComponent>(flagUid).RegionId =
                $"integration-test-{Guid.NewGuid():N}";
            entMan.InitializeAndStartEntity(flagUid);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() => entMan.DeleteEntity(map.Grid.Owner));
        await server.WaitRunTicks(5);

        // Same again, but deleted inside the window between MapInit and the queued application.
        var map2 = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            var uid = entMan.CreateEntityUninitialized("PersistentCaptureRegionFlag", map2.GridCoords);
            entMan.GetComponent<PersistentCaptureRegionComponent>(uid).RegionId =
                $"integration-test-{Guid.NewGuid():N}";
            entMan.InitializeAndStartEntity(uid);
            entMan.DeleteEntity(uid);
        });

        await server.WaitRunTicks(5);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Unanchor the hardpoint first (which is what leaves the gun loose), then delete it, then move the gun.
    /// </summary>
    [Test]
    public async Task HardpointUnanchoredThenDeleted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var xformSys = server.System<SharedTransformSystem>();

        EntityUid gunUid = default;
        EntityUid hardpointUid = default;

        await server.WaitPost(() =>
        {
            hardpointUid = entMan.SpawnEntity(Hardpoint, map.GridCoords);
            gunUid = entMan.SpawnEntity(Gun, map.GridCoords);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
            xformSys.Unanchor(hardpointUid, entMan.GetComponent<TransformComponent>(hardpointUid)));
        await server.WaitRunTicks(2);
        await server.WaitPost(() => entMan.DeleteEntity(hardpointUid));
        await server.WaitRunTicks(2);

        await server.WaitPost(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(gunUid);
            if (!xform.Anchored)
                xformSys.AnchorEntity((gunUid, xform));
        });
        await server.WaitRunTicks(2);
        await server.WaitPost(() =>
            xformSys.Unanchor(gunUid, entMan.GetComponent<TransformComponent>(gunUid)));

        await server.WaitRunTicks(5);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Both the gun and its hardpoint deleted in the same tick, hardpoint first, the way one shipgun hit destroys
    /// everything standing on a tile.
    /// </summary>
    [Test]
    public async Task HardpointAndGunDeletedTogether()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mapSys = server.System<SharedMapSystem>();

        EntityUid gunUid = default;
        EntityUid hardpointUid = default;

        await server.WaitPost(() =>
        {
            hardpointUid = entMan.SpawnEntity(Hardpoint, map.GridCoords);
            gunUid = entMan.SpawnEntity(Gun, map.GridCoords);
        });

        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            var indices = mapSys.TileIndicesFor(map.Grid.Owner, map.Grid.Comp, map.GridCoords);
            mapSys.SetTile(map.Grid.Owner, map.Grid.Comp, indices, Tile.Empty);
            entMan.QueueDeleteEntity(hardpointUid);
            entMan.QueueDeleteEntity(gunUid);
        });

        await server.WaitRunTicks(5);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Gun deleted first, hardpoint second, in the same tick.
    /// </summary>
    [Test]
    public async Task GunDeletedBeforeHardpoint()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid gunUid = default;
        EntityUid hardpointUid = default;

        await server.WaitPost(() =>
        {
            hardpointUid = entMan.SpawnEntity(Hardpoint, map.GridCoords);
            gunUid = entMan.SpawnEntity(Gun, map.GridCoords);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            entMan.QueueDeleteEntity(gunUid);
            entMan.QueueDeleteEntity(hardpointUid);
        });

        await server.WaitRunTicks(5);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The real shape of the report: a gun that is mid-burst when the floor and the hardpoint under it go.
    /// </summary>
    [Test]
    public async Task FiringGunLosesTileAndHardpoint()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mapSys = server.System<SharedMapSystem>();
        var gunSys = server.System<SharedGunSystem>();

        EntityUid gunUid = default;
        EntityUid hardpointUid = default;
        EntityUid consoleUid = default;

        await server.WaitPost(() =>
        {
            hardpointUid = entMan.SpawnEntity(Hardpoint, map.GridCoords);
            gunUid = entMan.SpawnEntity(Gun, map.GridCoords);
            consoleUid = entMan.SpawnEntity("ComputerTargeting", map.GridCoords);
        });

        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            var console = entMan.GetComponent<TargetingConsoleComponent>(consoleUid);
            server.System<PointCannonSystem>().LinkAllCannonsToConsole(consoleUid, console);

            var autoShoot = entMan.EnsureComponent<AutoShootGunComponent>(gunUid);
            gunSys.SetEnabled(gunUid, autoShoot, true);
        });

        // Let it actually get rounds off.
        await server.WaitRunTicks(20);

        await server.WaitPost(() =>
        {
            var indices = mapSys.TileIndicesFor(map.Grid.Owner, map.Grid.Comp, map.GridCoords);
            mapSys.SetTile(map.Grid.Owner, map.Grid.Comp, indices, Tile.Empty);
            entMan.QueueDeleteEntity(hardpointUid);
        });

        await server.WaitRunTicks(20);

        await server.WaitAssertion(() =>
        {
            var anchor = entMan.GetComponent<HardpointAnchorableOnlyComponent>(gunUid);
            Assert.That(anchor.anchoredTo, Is.Null);

            var console = entMan.GetComponent<TargetingConsoleComponent>(consoleUid);
            Assert.That(console.CannonGroups["all"], Does.Not.Contain(gunUid),
                "A gun that lost its floor and its hardpoint is still linked to its targeting console.");

            Assert.That(entMan.GetComponent<AutoShootGunComponent>(gunUid).Enabled, Is.False,
                "A gun that came off its mount is still set to fire on its own.");
        });

        await server.WaitRunTicks(10);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A player standing on a persistent territory flag when the floor under the flag is destroyed.
    /// </summary>
    [Test]
    public async Task TileUnderCaptureFlagDestroyed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mapSys = server.System<SharedMapSystem>();

        EntityUid flagUid = default;
        EntityUid turretUid = default;

        await server.WaitPost(() =>
        {
            flagUid = entMan.CreateEntityUninitialized("PersistentCaptureRegionFlag", map.GridCoords);
            entMan.GetComponent<PersistentCaptureRegionComponent>(flagUid).RegionId =
                $"integration-test-{Guid.NewGuid():N}";
            entMan.InitializeAndStartEntity(flagUid);
            turretUid = entMan.SpawnEntity("WeaponTurretAutoPDCapturable", map.GridCoords);
        });

        await server.WaitRunTicks(10);

        await server.WaitPost(() =>
        {
            var indices = mapSys.TileIndicesFor(map.Grid.Owner, map.Grid.Comp, map.GridCoords);
            mapSys.SetTile(map.Grid.Owner, map.Grid.Comp, indices, Tile.Empty);
        });

        await server.WaitRunTicks(10);

        await server.WaitPost(() => entMan.QueueDeleteEntity(turretUid));
        await server.WaitRunTicks(5);
        await server.WaitPost(() => entMan.QueueDeleteEntity(flagUid));
        await server.WaitRunTicks(10);

        await pair.CleanReturnAsync();
    }
}
