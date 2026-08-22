using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.Fuel;

/// <summary>
/// Extracts fuel from the <see cref="FuelSeepComponent"/> beneath it.
/// </summary>
[RegisterComponent]
public sealed partial class FuelExtractorComponent : Component
{
    /// <summary>
    /// Internal fuel buffer.
    /// </summary>
    [DataField]
    public string SolutionId = "buffer";

    /// <summary>
    /// Slot for the container filled from the buffer.
    /// </summary>
    [DataField]
    public string ContainerSlotId = "fuelContainer";

    /// <summary>
    /// Delay between pump cycles.
    /// </summary>
    [DataField]
    public TimeSpan CycleDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Minimum supplied fraction of <c>PowerConsumer.DrawRate</c>.
    /// </summary>
    [DataField]
    public float RequiredPowerRatio = 0.99f;

    /// <summary>
    /// Delay between spill warnings.
    /// </summary>
    [DataField]
    public TimeSpan SpillWarningDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether excess fuel is being spilled.
    /// </summary>
    [ViewVariables]
    public bool BufferFull;

    [ViewVariables]
    public TimeSpan NextSpillWarning;

    [ViewVariables]
    public TimeSpan NextCycle;

    /// <summary>
    /// Cached seep beneath the drill.
    /// </summary>
    [ViewVariables]
    public EntityUid? Seep;
}

[Serializable, NetSerializable]
public enum FuelExtractorVisuals : byte
{
    Running,
}

[Serializable, NetSerializable]
public enum FuelExtractorVisualLayers : byte
{
    Body,
}
