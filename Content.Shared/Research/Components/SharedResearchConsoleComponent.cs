using Content.Shared._Goobstation.Research;
using Robust.Shared.Serialization;

namespace Content.Shared.Research.Components
{
    [NetSerializable, Serializable]
    public enum ResearchConsoleUiKey : byte
    {
        Key,
    }

    [Serializable, NetSerializable]
    public sealed class ConsoleUnlockTechnologyMessage : BoundUserInterfaceMessage
    {
        public string Id;

        public ConsoleUnlockTechnologyMessage(string id)
        {
            Id = id;
        }
    }

    [Serializable, NetSerializable]
    public sealed class ConsoleServerSelectionMessage : BoundUserInterfaceMessage
    {

    }

    [Serializable, NetSerializable]
    public sealed class ResearchConsolePointsChangedMessage : BoundUserInterfaceMessage
    {
        public int Points;

        public ResearchConsolePointsChangedMessage(int points)
        {
            Points = points;
        }
    }

    [Serializable, NetSerializable]
    public sealed class ResearchConsoleBoundInterfaceState : BoundUserInterfaceState
    {
        public int Points;
        public float SoftCapMultiplier;
        public Dictionary<string, ResearchAvailability> Researches;

        public ResearchConsoleBoundInterfaceState(
            int points,
            float softCapMultiplier,
            Dictionary<string, ResearchAvailability>? researches = null)
        {
            Points = points;
            SoftCapMultiplier = softCapMultiplier;
            Researches = researches ?? new();
        }
    }
}
