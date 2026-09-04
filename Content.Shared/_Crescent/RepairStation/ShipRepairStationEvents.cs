namespace Content.Shared._Crescent.RepairStation;

/// <summary>
/// Raised on a grid once its structural snapshot has been written, so anything keeping a parallel
/// file of the same hull can rebuild it in step.
/// </summary>
[ByRefEvent]
public record struct ShipSnapshotGeneratedEvent;
