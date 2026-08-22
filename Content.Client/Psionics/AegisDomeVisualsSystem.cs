using Content.Shared.Abilities.Psionics;
using Robust.Client.GameObjects;

namespace Content.Client.Psionics;

/// <summary>
/// Shades an aegis dome by how much punishment it has left.
///
/// "Breakable" is only a real mechanic if the people standing under it can see it failing, and a
/// number in a popup is no use in a firefight - so the barrier fades and warms from clear blue to
/// amber as its integrity drains.
/// </summary>
public sealed class AegisDomeVisualsSystem : EntitySystem
{
    private static readonly Color Healthy = Color.FromHex("#8FD6F5");
    private static readonly Color Failing = Color.FromHex("#F5A24B");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AegisDomeComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AegisDomeComponent, AfterAutoHandleStateEvent>(OnStateHandled);
    }

    private void OnStartup(EntityUid uid, AegisDomeComponent component, ComponentStartup args)
    {
        UpdateVisuals(uid, component);
    }

    private void OnStateHandled(EntityUid uid, AegisDomeComponent component, ref AfterAutoHandleStateEvent args)
    {
        UpdateVisuals(uid, component);
    }

    private void UpdateVisuals(EntityUid uid, AegisDomeComponent component)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite) || component.MaxIntegrity <= 0)
            return;

        var fraction = Math.Clamp(component.Integrity / component.MaxIntegrity, 0f, 1f);

        // Never all the way transparent: a barrier you cannot see is a barrier you walk out of.
        var colour = Color.InterpolateBetween(Failing, Healthy, fraction);
        sprite.Color = colour.WithAlpha(0.45f + 0.55f * fraction);
    }
}
