using Content.Shared._EE.Contractors.Components;
using Robust.Client.UserInterface;

namespace Content.Client._EE.Contractors.UI;

public sealed class PassportBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PassportWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PassportWindow>();
        _window.SaveRequested += SendMessage;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is PassportBoundUserInterfaceState passportState)
            _window?.UpdateState(passportState);
    }
}
