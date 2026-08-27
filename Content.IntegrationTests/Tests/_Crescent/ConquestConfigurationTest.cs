#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server.GameTicking.Presets;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Server._Crescent.RoundEnd;
using Content.Shared._Crescent.RoundEnd;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.IntegrationTests.Tests._Crescent;

/// <summary>
/// Keeps the conquest and hard round-end rules wired to the actual station maps used by Crescent presets.
/// A conquest station needs all three pieces: a FactionStation grid override, a matching banner on the map,
/// and a victory/fall announcement. Missing any one of them makes the station impossible to capture or silently
/// removes its faction from the round-end calculation.
/// </summary>
[TestFixture]
public sealed class ConquestConfigurationTest
{
    private const string ConquestRuleId = "HullrotConquest";
    private const string RoundTimeLimitRuleId = "HullrotRoundTimeLimit";
    private static readonly string[] TapStationMapIds = ["Aasim", "TribalHideout"];

    [Test]
    public async Task TapStationsLoadWithConquestFlags()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true // Station initialization creates nullspace entities that outlive the loaded grid.
        });
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var mapLoader = entManager.System<MapLoaderSystem>();
        var stationSystem = entManager.System<StationSystem>();

        await server.WaitPost(() =>
        {
            foreach (var mapId in TapStationMapIds)
            {
                var mapProto = protoManager.Index<GameMapPrototype>(mapId);
                mapSystem.CreateMap(out var testMap);

                var loadedSuccessfully = mapLoader.TryLoadGrid(testMap, mapProto.MapPath, out var loaded);
                var foundStationConfig = mapProto.Stations.TryGetValue(mapId, out var config);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(loadedSuccessfully, Is.True,
                        $"Failed to load conquest station map {mapProto.MapPath}.");
                    Assert.That(foundStationConfig, Is.True,
                        $"{mapId}'s station key must match its gameMap id.");
                }

                var grid = loaded!.Value.Owner;
                stationSystem.InitializeNewStation(config!, new[] { grid });

                var foundFactionStation = entManager.TryGetComponent<FactionStationComponent>(grid, out var factionStation);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(foundFactionStation, Is.True,
                        $"{mapId} did not receive its FactionStation grid override.");
                    Assert.That(factionStation?.Faction, Is.EqualTo("TAP"));
                }

                var flags = new List<ConquestFlagComponent>();
                var query = entManager.AllEntityQueryEnumerator<ConquestFlagComponent, TransformComponent>();
                while (query.MoveNext(out _, out var flag, out var xform))
                {
                    if (xform.GridUid == grid)
                        flags.Add(flag);
                }

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(flags, Has.Count.EqualTo(1),
                        $"{mapId} must load exactly one conquest flag on its station grid.");

                    if (flags.Count == 1)
                    {
                        Assert.That(flags[0].HomeFaction, Is.EqualTo("TAP"));
                        Assert.That(flags[0].OwnerFaction, Is.EqualTo("TAP"),
                            $"The TAP flag on {mapId} did not initialize in its home faction's control.");
                    }
                }

                mapSystem.DeleteMap(testMap);
            }
        });

        await server.WaitRunTicks(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdventurePresetsHaveWorkingRoundEndAndConquestStations()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();
        var resourceManager = server.ResolveDependency<IResourceManager>();
        var localization = server.ResolveDependency<ILocalizationManager>();
        var errors = new List<string>();

        await server.WaitPost(() =>
        {
            var conquestProto = protoManager.Index<EntityPrototype>(ConquestRuleId);
            if (!conquestProto.TryGetComponent<FactionConquestRuleComponent>(out var conquest, componentFactory))
            {
                errors.Add($"{ConquestRuleId} has no FactionConquestRule component.");
                return;
            }

            foreach (var localeId in conquest!.VictoryAnnouncements.Values.Append(conquest.MinorVictoryAnnouncement)
                         .Append(conquest.TimeoutAnnouncement)
                         .Append(conquest.PendingAnnouncement)
                         .Append(conquest.PendingCancelledAnnouncement)
                         .Append(conquest.ControlAnnouncement))
            {
                if (!localization.HasString(localeId))
                    errors.Add($"Conquest announcement '{localeId}' has no locale string.");
            }

            foreach (var preset in protoManager.EnumeratePrototypes<GamePresetPrototype>())
            {
                var adventureRules = new List<(string Id, AdventureRuleComponent Component)>();
                var bypassesRoundEnd = false;

                foreach (var ruleId in preset.Rules)
                {
                    if (!protoManager.TryIndex<EntityPrototype>(ruleId, out var ruleProto))
                        continue;

                    if (ruleProto.TryGetComponent<AdventureRuleComponent>(out var adventure, componentFactory))
                    {
                        adventureRules.Add((ruleId, adventure!));
                    }

                    bypassesRoundEnd |= ruleProto.TryGetComponent<RoundEndBypassRuleComponent>(out _, componentFactory);
                }

                if (adventureRules.Count == 0)
                    continue;

                if (!bypassesRoundEnd && !preset.Rules.Contains(RoundTimeLimitRuleId))
                    errors.Add($"Adventure preset '{preset.ID}' has no {RoundTimeLimitRuleId}; its round has no hard ending.");

                if (!preset.Rules.Contains(ConquestRuleId))
                    continue;

                if (adventureRules.Count != 1)
                {
                    errors.Add($"Conquest preset '{preset.ID}' has {adventureRules.Count} AdventureRule entities; expected exactly one.");
                    continue;
                }

                var stationFactions = new HashSet<string>();
                var (_, adventureRule) = adventureRules[0];

                foreach (var element in adventureRule.GameMapsID.Values)
                {
                    if (!protoManager.TryIndex<GameMapPrototype>(element.GameMapID, out var gameMap))
                    {
                        errors.Add($"Conquest preset '{preset.ID}' references unknown gameMap '{element.GameMapID}'.");
                        continue;
                    }

                    if (!gameMap.Stations.TryGetValue(element.GameMapID, out var stationConfig))
                    {
                        errors.Add($"gameMap '{gameMap.ID}' has no station key matching its id; AdventureRule cannot initialize it.");
                        continue;
                    }

                    FactionStationComponent? factionStation = null;
                    var hasFactionStation = stationConfig.gridComponents != null &&
                                            stationConfig.gridComponents.TryGetComponent(
                                                componentFactory,
                                                out factionStation);

                    // An explicit faction IFF marks a faction base in these presets. It must also participate in
                    // conquest; otherwise players can spawn there but taking that base can never affect the round.
                    if (!string.IsNullOrWhiteSpace(element.IFFFaction) && !hasFactionStation)
                    {
                        errors.Add($"Faction map '{gameMap.ID}' ({element.IFFFaction}) in preset '{preset.ID}' has no FactionStation grid component.");
                        continue;
                    }

                    if (!hasFactionStation)
                        continue;

                    var faction = factionStation!.Faction;
                    stationFactions.Add(faction);

                    if (!string.IsNullOrWhiteSpace(element.IFFFaction) && element.IFFFaction != faction)
                    {
                        errors.Add($"gameMap '{gameMap.ID}' uses IFF faction '{element.IFFFaction}' but its FactionStation belongs to '{faction}'.");
                    }

                    if (string.IsNullOrWhiteSpace(factionStation.FallAnnouncement) ||
                        !localization.HasString(factionStation.FallAnnouncement))
                    {
                        errors.Add($"Faction station '{gameMap.ID}' has no valid fall announcement.");
                    }

                    var victoryLocale = conquest.VictoryAnnouncements
                        .Where(entry => entry.Key == faction)
                        .Select(entry => entry.Value)
                        .FirstOrDefault();

                    if (victoryLocale == null || !localization.HasString(victoryLocale))
                    {
                        errors.Add($"Faction '{faction}' on '{gameMap.ID}' has no valid conquest victory announcement.");
                    }

                    var flagFactions = ReadConquestFlagFactions(gameMap.MapPath, resourceManager, protoManager,
                        componentFactory, errors);

                    if (flagFactions.Count == 0)
                    {
                        errors.Add($"Faction station '{gameMap.ID}' contains no ConquestFlag; it can only fall if its whole grid is destroyed.");
                        continue;
                    }

                    foreach (var flagFaction in flagFactions)
                    {
                        // The generic ConquestFlag deliberately has a blank home and inherits from its station.
                        if (!string.IsNullOrWhiteSpace(flagFaction) && flagFaction != faction)
                        {
                            errors.Add($"Faction station '{gameMap.ID}' belongs to '{faction}' but contains a '{flagFaction}' conquest flag.");
                        }
                    }
                }

                if (stationFactions.Count < 2)
                {
                    errors.Add($"Conquest preset '{preset.ID}' spawns only {stationFactions.Count} faction station(s); it would resolve without a real contest.");
                }
            }
        });

        await pair.CleanReturnAsync();

        Assert.That(errors, Is.Empty,
            $"Found {errors.Count} Crescent round-end/conquest configuration error(s):\n{string.Join("\n", errors)}");
    }

    private static List<string?> ReadConquestFlagFactions(
        ResPath mapPath,
        IResourceManager resourceManager,
        IPrototypeManager protoManager,
        IComponentFactory componentFactory,
        List<string> errors)
    {
        var factions = new List<string?>();

        using var stream = resourceManager.ContentFileRead(mapPath);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var yaml = new YamlStream();
        yaml.Load(reader);

        foreach (var document in yaml.Documents)
        {
            if (document.RootNode is not YamlMappingNode root ||
                !root.Children.TryGetValue(new YamlScalarNode("entities"), out var entitiesNode) ||
                entitiesNode is not YamlSequenceNode entities)
            {
                continue;
            }

            foreach (var group in entities.Children.OfType<YamlMappingNode>())
            {
                if (!group.Children.TryGetValue(new YamlScalarNode("proto"), out var protoNode) ||
                    protoNode is not YamlScalarNode protoScalar ||
                    string.IsNullOrWhiteSpace(protoScalar.Value))
                {
                    continue;
                }

                if (!protoManager.TryIndex<EntityPrototype>(protoScalar.Value, out var entityProto))
                {
                    errors.Add($"{mapPath}:{protoScalar.Start.Line + 1}: unknown entity prototype '{protoScalar.Value}'.");
                    continue;
                }

                if (entityProto.TryGetComponent<ConquestFlagComponent>(out var flag, componentFactory))
                    factions.Add(flag!.HomeFaction);
            }
        }

        return factions;
    }
}
