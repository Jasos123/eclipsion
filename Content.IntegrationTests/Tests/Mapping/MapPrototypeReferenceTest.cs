#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.IntegrationTests.Tests.Mapping;

[TestFixture]
public sealed class MapPrototypeReferenceTest
{
    [Test]
    public async Task MapEntityPrototypesResolve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var resourceManager = server.ResolveDependency<IResourceManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var migrations = ReadMigrations(resourceManager);
        var errors = new List<string>();

        foreach (var (oldId, newId) in migrations)
        {
            if (IsDeleted(newId) || prototypeManager.HasIndex<EntityPrototype>(newId!))
                continue;

            errors.Add($"Migration '{oldId}' points to unknown entity prototype '{newId}'.");
        }

        var mapFolder = new ResPath("/Maps");
        var mapPaths = resourceManager
            .ContentFindFiles(mapFolder)
            .Where(path => path.Extension == "yml" &&
                           !path.Filename.StartsWith(".", StringComparison.Ordinal));

        foreach (var path in mapPaths)
        {
            using var stream = resourceManager.ContentFileRead(path);
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

                foreach (var entityGroup in entities.Children.OfType<YamlMappingNode>())
                {
                    YamlScalarNode? protoScalar = null;
                    if (entityGroup.Children.TryGetValue(new YamlScalarNode("proto"), out var protoNode))
                        protoScalar = protoNode as YamlScalarNode;
                    else if (entityGroup.Children.TryGetValue(new YamlScalarNode("type"), out var typeNode))
                        protoScalar = typeNode as YamlScalarNode;

                    if (string.IsNullOrWhiteSpace(protoScalar?.Value))
                        continue;

                    var protoId = protoScalar!.Value!;
                    if (migrations.TryGetValue(protoId, out var migratedId))
                    {
                        if (IsDeleted(migratedId) || prototypeManager.HasIndex<EntityPrototype>(migratedId!))
                            continue;

                        errors.Add($"{path}:{protoScalar.Start.Line + 1}: '{protoId}' migrates to unknown entity prototype '{migratedId}'.");
                        continue;
                    }

                    if (!prototypeManager.HasIndex<EntityPrototype>(protoId))
                        errors.Add($"{path}:{protoScalar.Start.Line + 1}: unknown entity prototype '{protoId}'.");
                }
            }
        }

        await pair.CleanReturnAsync();

        Assert.That(errors, Is.Empty,
            $"Found {errors.Count} unresolved map prototype reference(s):\n{string.Join("\n", errors)}");
    }

    private static Dictionary<string, string?> ReadMigrations(IResourceManager resourceManager)
    {
        var migrations = new Dictionary<string, string?>();
        var migrationFolder = new ResPath("/Migrations");

        foreach (var path in resourceManager.ContentFindFiles(migrationFolder))
        {
            if (path.Extension != "yml" || path.Filename.StartsWith(".", StringComparison.Ordinal))
                continue;

            using var stream = resourceManager.ContentFileRead(path);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var yaml = new YamlStream();
            yaml.Load(reader);

            foreach (var document in yaml.Documents)
            {
                if (document.RootNode is not YamlMappingNode root)
                    continue;

                foreach (var (keyNode, valueNode) in root.Children)
                {
                    if (keyNode is not YamlScalarNode key || valueNode is not YamlScalarNode value)
                        continue;

                    migrations.TryAdd(key.Value!, value.Value);
                }
            }
        }

        return migrations;
    }

    private static bool IsDeleted(string? prototypeId)
    {
        return string.IsNullOrWhiteSpace(prototypeId) || prototypeId == "null";
    }
}
