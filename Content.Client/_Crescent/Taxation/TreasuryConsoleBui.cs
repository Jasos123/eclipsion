using Content.Shared._Crescent.Taxation;
using Robust.Client.UserInterface;

namespace Content.Client._Crescent.Taxation;

public sealed class TreasuryConsoleBui : BoundUserInterface
{
    private TreasuryConsoleWindow? _window;

    public TreasuryConsoleBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new TreasuryConsoleWindow();
        _window.OnClose += Close;
        _window.OnWithdraw += amount => SendMessage(new TreasuryWithdrawMessage(amount));
        // Eclipsion - purchase approval
        _window.OnPurchaseDecision += (id, approve) => SendMessage(new TreasuryPurchaseDecisionMessage(id, approve));
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is TreasuryConsoleState cast)
            _window?.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Dispose();
    }
}
