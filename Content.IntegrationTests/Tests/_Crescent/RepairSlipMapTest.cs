using System.Collections.Generic;
using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.RepairStation;
using Content.Shared._Mono.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Content.Server.Maps;

namespace Content.IntegrationTests.Tests._Crescent;

/// <summary>
/// The repair slip is a generated grid file rather than one saved out of the mapping editor, so this
/// checks what a bad generator would break: that it loads through the ordinary game map path, that
/// its four hull docks survive map init, and that the RTG reaches the consoles through the substation
/// and the APC.
/// </summary>
[TestFixture]
public sealed class RepairSlipMapTest
{
    private const string SlipMap = "RepairSlip";

    [Test]
    public async Task RepairSlipLoadsWithDocksAndPower()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true // Loading a station leaves nullspace entities behind.
        });
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var ticker = entManager.System<GameTicker>();

        var grid = EntityUid.Invalid;
        var mapId = MapId.Nullspace;

        await server.WaitPost(() =>
        {
            var proto = protoManager.Index<GameMapPrototype>(SlipMap);
            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            var grids = ticker.LoadGameMap(proto, out mapId, opts);

            Assert.That(grids, Has.Count.EqualTo(1), $"{SlipMap} should load as a single grid.");
            grid = grids[0];
        });

        // Long enough for the substation and APC batteries to fill off the RTG.
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var docks = 0;
            var dockQuery = entManager.EntityQueryEnumerator<DockingComponent, TransformComponent>();
            while (dockQuery.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    docks++;
            }

            Assert.That(docks, Is.EqualTo(4),
                "The slip should offer a docking port on each of its four hull faces.");

            var consoles = new List<EntityUid>();
            var consoleQuery = entManager.EntityQueryEnumerator<ShipRepairStationComponent, TransformComponent>();
            while (consoleQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    consoles.Add(uid);
            }

            Assert.That(consoles, Has.Count.EqualTo(1),
                "The slip should carry its repair console.");

            foreach (var console in consoles)
            {
                Assert.That(entManager.TryGetComponent<ApcPowerReceiverComponent>(console, out var receiver), Is.True,
                    "A repair console must draw from the APC net.");
                Assert.That(receiver!.Powered, Is.True,
                    "The slip's own generator should be powering its consoles.");
            }
        });

        await server.WaitPost(() => mapSystem.DeleteMap(mapId));
        await server.WaitRunTicks(1);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The slip files the whole hull while the hand-held device keeps the narrow scope it always had.
    /// Both snapshots are written by the same call, so this checks they came out with different reach
    /// and that nothing is on both files - a structure on both would be billed and built twice.
    /// </summary>
    [Test]
    public async Task DrydockFilesMoreOfTheHullThanTheHandTool()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true // Loading a station leaves nullspace entities behind.
        });
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var ticker = entManager.System<GameTicker>();
        var shipRepair = entManager.System<SharedShipRepairSystem>();

        var grid = EntityUid.Invalid;
        var mapId = MapId.Nullspace;

        await server.WaitPost(() =>
        {
            var proto = protoManager.Index<GameMapPrototype>(SlipMap);
            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            grid = ticker.LoadGameMap(proto, out mapId, opts)[0];

            shipRepair.GenerateRepairData(grid);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<ShipRepairDataComponent>(grid, out var handTool), Is.True);
            Assert.That(entManager.TryGetComponent<ShipDrydockSnapshotComponent>(grid, out var drydock), Is.True,
                "Generating a snapshot should have filed the slip's own copy alongside it.");

            var handProtos = handTool!.EntityPalette.Select(p => p.Id).ToHashSet();
            var drydockProtos = drydock!.Parts.Select(p => p.Proto.Id).ToHashSet();

            Assert.That(handProtos, Is.EquivalentTo(new[] { "WallReinforced" }),
                "The hand-held device's scope must stay exactly what ShipRepairable gives it.");

            Assert.Multiple(() =>
            {
                foreach (var expected in new[]
                         {
                             "AirlockShuttle", "Poweredlight", "CableApcExtension", "CableHV", "CableMV",
                             "APCBasic", "SubstationBasic", "GeneratorRTG", "ComputerShipRepairStation",
                         })
                {
                    Assert.That(drydockProtos, Does.Contain(expected),
                        $"The slip should have {expected} on file.");
                }
            });

            Assert.That(drydockProtos.Overlaps(handProtos), Is.False,
                "A structure on both files would be quoted and rebuilt twice.");

            // The tool it stocks is loose cargo, not hull, and the blacklist should have kept it off.
            Assert.That(drydockProtos, Does.Not.Contain("ShipRepairDeviceRecharging"));
        });

        await server.WaitPost(() => mapSystem.DeleteMap(mapId));
        await server.WaitRunTicks(1);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A wall beaten past its first threshold leaves a girder standing on the tile. The slip has to
    /// recognise that as the wall's wreckage and clear it, or it welds the new wall straight through
    /// and the tile ends up holding both. This checks the prototype marker and the scope file that
    /// drive that decision, and that nothing finished is caught by them.
    /// </summary>
    [Test]
    public async Task GirdersAreClearableButFinishedStructuresAreNot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var drydock = entManager.System<Content.Server._Crescent.RepairStation.ShipDrydockSnapshotSystem>();

        var mapId = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            var map = mapSystem.CreateMap(out mapId);
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);

            Assert.Multiple(() =>
            {
                foreach (var wreckage in new[]
                         {
                             "Girder", "ReinforcedGirder", "MachineFrame", "MachineFrameDestroyed",
                             "GasPipeBroken", "StationMapBroken",
                         })
                {
                    Assert.That(drydock.IsClearable(entManager.SpawnEntity(wreckage, coords)), Is.True,
                        $"{wreckage} is wreckage and should be clearable.");
                }

                foreach (var finished in new[] { "WallReinforced", "Poweredlight", "AirlockShuttle" })
                {
                    Assert.That(drydock.IsClearable(entManager.SpawnEntity(finished, coords)), Is.False,
                        $"{finished} is a finished structure and tearing it down would be vandalism.");
                }
            });
        });

        await server.WaitPost(() => mapSystem.DeleteMap(mapId));
        await server.WaitRunTicks(1);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The slip sweeps up what a battered hull sheds so the customer does not get his ship back whole
    /// but full of the old one's remains. The sweep is driven by the scope file, and this pins both
    /// ends of it: the wreckage it has to catch, and what it must not touch, since anything on that
    /// list is binned wherever it happens to be lying.
    /// </summary>
    [Test]
    public async Task HullWreckageIsSweptButValuablesAreNot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var drydock = entManager.System<Content.Server._Crescent.RepairStation.ShipDrydockSnapshotSystem>();

        var mapId = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            var map = mapSystem.CreateMap(out mapId);
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);

            Assert.Multiple(() =>
            {
                foreach (var wreckage in new[]
                         {
                             "SheetSteel1", "SheetPlasteel1", "SheetGlass1", "PartRodMetal1", "ShardGlass",
                             "ShardGlassReinforced", "CableApcStack1", "SteelScrap1", "FloorTileItemSteel",
                             "MaterialWoodPlank1",
                         })
                {
                    Assert.That(drydock.IsDebris(entManager.SpawnEntity(wreckage, coords)), Is.True,
                        $"{wreckage} is what a broken hull sheds and should be swept up.");
                }

                // Everything here carries the Material component, which is why the sweep cannot be
                // driven off that component.
                foreach (var keep in new[] { "SpaceCash", "Paper", "IngotGold1" })
                {
                    Assert.That(drydock.IsDebris(entManager.SpawnEntity(keep, coords)), Is.False,
                        $"{keep} is the customer's property, not wreckage.");
                }
            });
        });

        await server.WaitPost(() => mapSystem.DeleteMap(mapId));
        await server.WaitRunTicks(1);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Plating and framing are welding, but a shield emitter, a reactor or a gun comes off a crane and
    /// gets aligned, and the yard bills for that. This pins the ladder the scope file lays out, in
    /// particular that a gun's grade comes off the hardpoint it needs rather than off its name.
    /// </summary>
    [Test]
    public async Task SpecialistPartsCarryTheirSurcharge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var drydock = entManager.System<Content.Server._Crescent.RepairStation.ShipDrydockSnapshotSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var (protoId, expected) in new[]
                         {
                             // Hull the yard welds back, charged at what it is worth and no more.
                             // Thrusters are bolted on rather than commissioned and stay at par.
                             ("WallReinforced", 1f),
                             ("Poweredlight", 1f),
                             ("ThrusterDSMWarship", 1f),
                             ("ShieldEmitter", 1.5f),
                             ("AmeController", 1.5f),
                             ("AmeShielding", 1.5f),
                             ("BoriaticGeneratorHercules", 1.5f),
                             ("WeaponTurretVulcan", 1.5f),   // small mount
                             ("WeaponTurretMortar", 1.75f),  // medium mount
                             ("FlakCannonTurret", 2f),       // artillery bracket
                         })
                {
                    var proto = protoManager.Index<EntityPrototype>(protoId);
                    Assert.That(drydock.GetSurcharge(proto), Is.EqualTo(expected).Within(0.001f),
                        $"{protoId} should be quoted at {expected} times what it is worth in parts.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
