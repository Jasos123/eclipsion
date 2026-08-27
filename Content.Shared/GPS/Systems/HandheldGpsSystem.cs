using Content.Shared.Examine;
using Content.Shared.GPS.Components;
using Robust.Shared.Map;

namespace Content.Shared.GPS.Systems;

public sealed partial class HandheldGpsSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HandheldGPSComponent, ExaminedEvent>(OnExamine);
    }

    private void OnExamine(Entity<HandheldGPSComponent> ent, ref ExaminedEvent args)
    {
        var posText = "Error";
        var pos = _transform.GetMapCoordinates(ent);

        if (pos.MapId != MapId.Nullspace)
        {
            var x = (int) pos.Position.X;
            var y = (int) pos.Position.Y;
            posText = $"({x}, {y})";
        }

        args.PushMarkup(Loc.GetString("handheld-gps-coordinates-title", ("coordinates", posText)));
    }
}
