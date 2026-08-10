using Content.Client.Eui;
using Content.Shared.Eui;
using Content.Shared.Psionics;

namespace Content.Client.Psionics.UI;

public sealed class PsionicSkillTreeEui : BaseEui
{
    private readonly PsionicSkillTreeWindow _window;

    public PsionicSkillTreeEui()
    {
        _window = new PsionicSkillTreeWindow();
        _window.OnSkillSelected += skillId => SendMessage(new UnlockPsionicSkillMessage(skillId));
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is PsionicSkillTreeEuiState treeState)
            _window.UpdateState(treeState);
    }

    public override void Closed()
    {
        _window.Close();
    }
}
