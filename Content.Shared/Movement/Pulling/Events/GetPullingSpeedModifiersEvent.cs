namespace Content.Shared.Movement.Pulling.Events;

/// <summary>
/// Raised on the pulled entity when its puller refreshes movement speed.
/// </summary>
[ByRefEvent]
public record struct GetPullingSpeedModifiersEvent
{
    public float WalkModifier { get; private set; } = 1f;
    public float SprintModifier { get; private set; } = 1f;

    public GetPullingSpeedModifiersEvent()
    {
    }

    public void ModifySpeed(float walkModifier, float sprintModifier)
    {
        WalkModifier *= walkModifier;
        SprintModifier *= sprintModifier;
    }
}
