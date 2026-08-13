using Robust.Shared.Serialization;

namespace Content.Shared.Power;

/// <summary>
/// The cable voltage classes displayed by the power storage control UI.
/// </summary>
[Serializable, NetSerializable]
public enum PowerStorageVoltage : byte
{
    High,
    Medium,
    Low,
}

/// <summary>
/// State displayed by the SMES and substation control UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class PowerStorageControlState : BoundUserInterfaceState
{
    public readonly float CurrentCharge;
    public readonly float MaxCharge;
    public readonly float CurrentInput;
    public readonly float CurrentOutput;
    public readonly float InputLimit;
    public readonly float OutputLimit;
    public readonly float MaxInputLimit;
    public readonly float MaxOutputLimit;
    public readonly bool InputEnabled;
    public readonly bool OutputEnabled;
    public readonly PowerStorageVoltage InputVoltage;
    public readonly PowerStorageVoltage OutputVoltage;

    public PowerStorageControlState(
        float currentCharge,
        float maxCharge,
        float currentInput,
        float currentOutput,
        float inputLimit,
        float outputLimit,
        float maxInputLimit,
        float maxOutputLimit,
        bool inputEnabled,
        bool outputEnabled,
        PowerStorageVoltage inputVoltage,
        PowerStorageVoltage outputVoltage)
    {
        CurrentCharge = currentCharge;
        MaxCharge = maxCharge;
        CurrentInput = currentInput;
        CurrentOutput = currentOutput;
        InputLimit = inputLimit;
        OutputLimit = outputLimit;
        MaxInputLimit = maxInputLimit;
        MaxOutputLimit = maxOutputLimit;
        InputEnabled = inputEnabled;
        OutputEnabled = outputEnabled;
        InputVoltage = inputVoltage;
        OutputVoltage = outputVoltage;
    }
}

[Serializable, NetSerializable]
public sealed class PowerStorageSetInputEnabledMessage : BoundUserInterfaceMessage
{
    public readonly bool Enabled;

    public PowerStorageSetInputEnabledMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

[Serializable, NetSerializable]
public sealed class PowerStorageSetOutputEnabledMessage : BoundUserInterfaceMessage
{
    public readonly bool Enabled;

    public PowerStorageSetOutputEnabledMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

[Serializable, NetSerializable]
public sealed class PowerStorageSetInputLimitMessage : BoundUserInterfaceMessage
{
    public readonly float Limit;

    public PowerStorageSetInputLimitMessage(float limit)
    {
        Limit = limit;
    }
}

[Serializable, NetSerializable]
public sealed class PowerStorageSetOutputLimitMessage : BoundUserInterfaceMessage
{
    public readonly float Limit;

    public PowerStorageSetOutputLimitMessage(float limit)
    {
        Limit = limit;
    }
}

[Serializable, NetSerializable]
public enum PowerStorageControlUiKey : byte
{
    Key,
}
