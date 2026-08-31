namespace Content.Shared.Atmos.Piping.Components;

/// <summary>
/// Raised on anchored atmosphere devices when their tile receives a new air mixture.
/// Devices that cache the mixture must refresh their reference when this happens.
/// </summary>
[ByRefEvent]
public readonly record struct AtmosDeviceTileChangedEvent;
