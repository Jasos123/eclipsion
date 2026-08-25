using Content.Client.UserInterface.Fragments;
using Content.Shared._Crescent.CartridgeLoader.Cartridges;
using Content.Shared.CartridgeLoader;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Network; // Eclipsion - blocking

namespace Content.Client._Crescent.CartridgeLoader.Cartridges;

public sealed partial class MoneyTransferUi : UIFragment
{
    private MoneyTransferUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new MoneyTransferUiFragment();
        _fragment.OnTransfer += (recipient, amount, comment) => Send(userInterface, recipient, amount, comment);
        // Eclipsion - blocking
        _fragment.OnBlock += (target, user, block) => SendBlock(userInterface, target, user, block);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not MoneyTransferUiState s)
            return;

        _fragment?.UpdateState(s);
    }

    private static void Send(BoundUserInterface bui, NetEntity recipient, int amount, string comment)
    {
        var ev = new MoneyTransferUiMessageEvent(recipient, amount, comment);
        bui.SendMessage(new CartridgeUiMessage(ev));
    }

    // Eclipsion - blocking
    private static void SendBlock(BoundUserInterface bui, NetEntity target, NetUserId? user, bool block)
    {
        var ev = new MoneyTransferBlockUiMessageEvent(target, block, user);
        bui.SendMessage(new CartridgeUiMessage(ev));
    }
}
