using Content.Shared._Crescent.DnaDatabase;
using Robust.Client.GameObjects;

namespace Content.Client._Crescent.DnaDatabase;

public sealed class DnaDatabaseBoundUserInterface : BoundUserInterface
{
    private DnaDatabaseWindow? _window;

    public DnaDatabaseBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new DnaDatabaseWindow();
        _window.OnClose += Close;
        _window.OnToggle += () => SendMessage(new DnaDatabaseToggleMessage());
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is DnaDatabaseBoundUserInterfaceState uiState)
            _window?.UpdateState(uiState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
    }
}
