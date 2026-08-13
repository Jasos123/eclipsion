using Content.Shared.Power;
using Robust.Client.UserInterface;

namespace Content.Client.Power.Storage;

public sealed class PowerStorageControlBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private PowerStorageControlWindow? _window;

    public PowerStorageControlBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PowerStorageControlWindow>();
        _window.SetEntity(Owner);
        _window.OnInputEnabled += enabled => SendMessage(new PowerStorageSetInputEnabledMessage(enabled));
        _window.OnOutputEnabled += enabled => SendMessage(new PowerStorageSetOutputEnabledMessage(enabled));
        _window.OnInputLimit += limit => SendMessage(new PowerStorageSetInputLimitMessage(limit));
        _window.OnOutputLimit += limit => SendMessage(new PowerStorageSetOutputLimitMessage(limit));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is PowerStorageControlState controlState)
            _window?.UpdateState(controlState);
    }
}
