using Content.Server.EUI;
using Content.Shared.Eui;
using Content.Shared.Psionics;

namespace Content.Server.Psionics;

public sealed class PsionicSkillTreeEui : BaseEui
{
    private readonly EntityUid _owner;
    private readonly PsionicSkillTreeSystem _system;

    public PsionicSkillTreeEui(EntityUid owner, PsionicSkillTreeSystem system)
    {
        _owner = owner;
        _system = system;
    }

    public override void Opened()
    {
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        _system.OnEuiClosed(this);
    }

    public override EuiStateBase GetNewState()
    {
        return _system.BuildState(_owner);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not UnlockPsionicSkillMessage unlock || IsShutDown)
            return;

        // The UI belongs to the body that opened it. It cannot be used after ghosting or a mind swap.
        if (Player.AttachedEntity != _owner)
        {
            Close();
            return;
        }

        _system.TryUnlock(_owner, unlock.SkillId);
        StateDirty();
    }
}
