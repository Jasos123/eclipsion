using Content.Shared.Power.Components;

namespace Content.Shared.Power.EntitySystems;

public abstract class SharedPowerNetSystem : EntitySystem
{
    public void SetReceiverBatteryPowerDraw(
        EntityUid uid,
        bool enabled,
        ApcPowerReceiverBatteryComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        component.PowerDrawEnabled = enabled;
    }
}
