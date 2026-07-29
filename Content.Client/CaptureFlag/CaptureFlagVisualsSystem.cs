using Content.Shared.CaptureFlag;
using Robust.Client.GameObjects;

namespace Content.Client.CaptureFlag;

public sealed class CaptureFlagVisualsSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CaptureFlagComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CaptureFlagComponent, AfterAutoHandleStateEvent>(OnStateChanged);
    }

    private void OnStartup(Entity<CaptureFlagComponent> ent, ref ComponentStartup args)
    {
        UpdateVisuals(ent);
    }

    private void OnStateChanged(Entity<CaptureFlagComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateVisuals(ent);
    }

    private void UpdateVisuals(Entity<CaptureFlagComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var state = ent.Comp.OwnerTeam?.ToUpperInvariant() switch
        {
            "DSM" => ent.Comp.DsmState,
            "NCWL" => ent.Comp.NcwlState,
            _ => ent.Comp.NeutralState
        };

        sprite.LayerSetState(0, state);
    }
}
