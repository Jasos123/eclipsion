using Content.Shared.Movement.Pulling.Events;

namespace Content.Shared._Crescent.Corpses;

/// <summary>
/// Applies the weight of a corpse to the movement speed of whoever is dragging it.
/// This is shared so client prediction and the server use the same movement speed.
/// </summary>
public sealed class CorpseDraggingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CorpseWeightComponent, GetPullingSpeedModifiersEvent>(OnGetPullingSpeedModifiers);
    }

    private void OnGetPullingSpeedModifiers(EntityUid uid,
        CorpseWeightComponent corpse,
        ref GetPullingSpeedModifiersEvent args)
    {
        if (!corpse.Applied)
            return;

        args.ModifySpeed(corpse.DragWalkSpeedMultiplier, corpse.DragSprintSpeedMultiplier);
    }
}
