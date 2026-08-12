using Content.Shared._Crescent.PassportControl;
using Robust.Client.GameObjects;

namespace Content.Client._Crescent.PassportControl;

public sealed class PassportControlBoundUserInterface(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    private PassportControlWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = new PassportControlWindow();
        _window.OnReset += () => SendMessage(new PassportControlResetMessage());
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is PassportControlBoundUserInterfaceState uiState)
            _window?.UpdateState(uiState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
