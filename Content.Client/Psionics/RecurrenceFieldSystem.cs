using Content.Shared.Abilities.Psionics;
using Robust.Client.Graphics;

namespace Content.Client.Psionics;

/// <summary>
/// Client half of the recurrence field. Movement prediction comes from the shared base; this only
/// owns the greyscale overlay's lifetime.
/// </summary>
public sealed class RecurrenceFieldSystem : SharedRecurrenceFieldSystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        // The overlay costs one entity query per frame and bails immediately when no field is up,
        // so it is cheaper to leave registered than to add and remove it per field.
        _overlay.AddOverlay(new RecurrenceFieldOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<RecurrenceFieldOverlay>();
    }
}
