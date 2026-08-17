using System.Numerics;
using Content.Shared.Crescent.Radar;
using Content.Shared.Shuttles.BUIStates;
using Robust.Shared.Serialization;

namespace Content.Shared.PointCannons;

[Serializable, NetSerializable]
public sealed class TargetingConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public NavInterfaceState NavState;
    public IFFInterfaceState IFFState;
    public List<string>? CannonGroups;
    public List<NetEntity>? ControlledCannons;

    public TargetingConsoleBoundUserInterfaceState(
        NavInterfaceState navState,
        IFFInterfaceState iffState,
        List<string>? groups,
        List<NetEntity>? controlled)
    {
        NavState = navState;
        IFFState = iffState;
        CannonGroups = groups;
        ControlledCannons = controlled;
    }
}

[Serializable, NetSerializable]
public sealed class TargetingConsoleFireMessage : BoundUserInterfaceMessage
{
    public Vector2 Coordinates;

    public TargetingConsoleFireMessage(Vector2 coords)
    {
        Coordinates = coords;
    }
}

/// <summary>
///     Crescent - the trigger came up, drop the standing fire order now.
/// </summary>
/// <remarks>
///     Purely an optimisation over the order's own expiry, never the thing that is relied on. The server times a
///     fire order out on its own precisely because this message can go missing - the client can be disposed,
///     lose focus or drop the packet mid-drag - and the guns must not keep shooting when that happens.
/// </remarks>
[Serializable, NetSerializable]
public sealed class TargetingConsoleStopFireMessage : BoundUserInterfaceMessage
{

}

[Serializable, NetSerializable]
public sealed class FireControlConsoleRefreshServerMessage : BoundUserInterfaceMessage
{

}

[Serializable, NetSerializable]
public sealed class TargetingConsoleGroupChangedMessage : BoundUserInterfaceMessage
{
    public string GroupName;

    public TargetingConsoleGroupChangedMessage(string name)
    {
        GroupName = name;
    }
}

[Serializable, NetSerializable]
public enum TargetingConsoleUiKey : byte
{
    Key,
}
