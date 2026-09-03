namespace Content.Shared._Crescent.RepairStation;

/// <summary>
/// Marks wreckage the automated repair slip may clear off a tile before rebuilding what stood there:
/// the girder a wall leaves behind, the frame a machine leaves behind.
/// </summary>
/// <remarks>
/// A marker component rather than a tag, because re-declaring a prototype's tag list replaces what it
/// inherits - girders would have lost their Structure tag for the sake of this one.
/// Which of these the slip actually clears is decided by the scope files, not by the component alone.
/// </remarks>
[RegisterComponent]
public sealed partial class ShipRepairRemnantComponent : Component;
