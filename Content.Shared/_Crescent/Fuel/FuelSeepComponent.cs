using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.Fuel;

/// <summary>
/// A finite pocket of fuel that can be drained by an extractor.
/// </summary>
[RegisterComponent]
public sealed partial class FuelSeepComponent : Component
{
    /// <summary>
    /// Reagent stored in the pocket.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    /// <summary>
    /// Remaining fuel.
    /// </summary>
    [DataField]
    public FixedPoint2 Reserve = FixedPoint2.New(1500);

    /// <summary>
    /// Initial capacity used for examine text.
    /// </summary>
    [DataField]
    public FixedPoint2 MaxReserve = FixedPoint2.New(1500);

    /// <summary>
    /// Fuel removed per extraction cycle.
    /// </summary>
    [DataField]
    public FixedPoint2 YieldPerCycle = FixedPoint2.New(6);
}

[Serializable, NetSerializable]
public enum FuelSeepVisuals : byte
{
    Depleted,
}

[Serializable, NetSerializable]
public enum FuelSeepVisualLayers : byte
{
    Base,
}
