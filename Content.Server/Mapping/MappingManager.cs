using System.IO;
using System.Linq;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Mapping;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.Server.Mapping;

public sealed class MappingManager : IPostInjectInit
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly ILogManager _log = default!;
    // SharedMapSystem is an entity system, not an IoC service.
    private SharedMapSystem Map => _ent.System<SharedMapSystem>();
    [Dependency] private readonly IServerNetManager _net = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IEntitySystemManager _systems = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly IResourceManager _resourceMan = default!;
    [Dependency] private readonly IEntityManager _ent = default!;

    private ISawmill _sawmill = default!;
    private ZStdCompressionContext _zstd = default!;

    private const string FavoritesPath = "/mapping_editor_favorites.yml";

    public void PostInject()
    {
        _net.RegisterNetMessage<MappingFavoritesSaveMessage>(OnMappingFavoritesSave);
        _net.RegisterNetMessage<MappingFavoritesLoadMessage>(OnMappingFavoritesLoad);
        _net.RegisterNetMessage<MappingFavoritesDataMessage>();

        _sawmill = _log.GetSawmill("mapping");

        // These used to be compiled out of release builds, which left the client registering messages the
        // server didn't know about and silently killed map saving on every published build.
        _net.RegisterNetMessage<MappingSaveMapMessage>(OnMappingSaveMap);
        _net.RegisterNetMessage<MappingSaveMapErrorMessage>();
        _net.RegisterNetMessage<MappingMapDataMessage>();

        _zstd = new ZStdCompressionContext();
    }

    private void OnMappingSaveMap(MappingSaveMapMessage message)
    {
        try
        {
            if (!_players.TryGetSessionByChannel(message.MsgChannel, out var session) ||
                !_admin.IsAdmin(session, true) ||
                !(_admin.HasAdminFlag(session, AdminFlags.Host) || _admin.HasAdminFlag(session, AdminFlags.Mapping)) ||
                !_ent.TryGetComponent(session.AttachedEntity, out TransformComponent? xform) ||
                xform.MapUid is not { } mapUid)
            {
                return;
            }

            // Save the grid the mapper is standing on, so that what comes out of the save is the thing they
            // were actually working on, as a grid file. Only when they're off-grid (in space, or on a map that
            // is itself the grid, like a planet) does the whole map get saved instead.
            var opts = SerializationOptions.Default;
            EntityUid target;

            if (xform.GridUid is { } gridUid && gridUid != mapUid)
            {
                target = gridUid;
                opts.Category = FileCategory.Grid;
            }
            else
            {
                target = mapUid;
                opts.Category = FileCategory.Map;
            }

            var sys = _systems.GetEntitySystem<MapLoaderSystem>();
            var (data, category) = sys.SerializeEntitiesRecursive([target], opts);

            if (category != opts.Category)
            {
                _sawmill.Error($"Saved {_ent.ToPrettyString(target)} as {category} instead of {opts.Category}.");
                _net.ServerSendMessage(new MappingSaveMapErrorMessage(), message.MsgChannel);
                return;
            }

            _sawmill.Info($"Mapper {session.Name} saved {category.ToString().ToLower()} {_ent.ToPrettyString(target)}.");

            var document = new YamlDocument(data.ToYaml());
            var stream = new YamlStream { document };
            var writer = new StringWriter();
            stream.Save(new YamlMappingFix(new Emitter(writer)), false);

            var msg = new MappingMapDataMessage()
            {
                Context = _zstd,
                Yml = writer.ToString()
            };
            _net.ServerSendMessage(msg, message.MsgChannel);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Error saving map in mapping mode:\n{e}");
            var msg = new MappingSaveMapErrorMessage();
            _net.ServerSendMessage(msg, message.MsgChannel);
        }
    }

    private void OnMappingFavoritesSave(MappingFavoritesSaveMessage message)
    {
        var mapping = new MappingDataNode();
        mapping.Add("prototypes", _serialization.WriteValue(message.PrototypeIDs, notNullableOverride: true));

        var path = new ResPath(FavoritesPath);
        using var writer = _resourceMan.UserData.OpenWriteText(path);
        var stream = new YamlStream { new(mapping.ToYaml()) };
        stream.Save(new YamlMappingFix(new Emitter(writer)), false);
    }

    private void OnMappingFavoritesLoad(MappingFavoritesLoadMessage message)
    {
        var path = new ResPath(FavoritesPath);

        if (!_resourceMan.UserData.Exists(path))
            return;

        try
        {
            var reader = _resourceMan.UserData.OpenText(path);
            var documents = DataNodeParser.ParseYamlStream(reader).First();
            var mapping = (MappingDataNode) documents.Root;

            if (!mapping.TryGet("prototypes", out var prototypesNode))
                return;

            var ids = _serialization.Read<string[]>(prototypesNode, notNullableOverride: true).ToList();

            var msg = new MappingFavoritesDataMessage()
            {
                PrototypeIDs = ids,
            };
            _net.ServerSendMessage(msg, message.MsgChannel);
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to load user favorite objects: " + e);
        }
    }
}
