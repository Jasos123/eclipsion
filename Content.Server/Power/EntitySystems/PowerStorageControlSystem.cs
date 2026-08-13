using Content.Server.Power.Components;
using Content.Shared.Power;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.Power.EntitySystems;

/// <summary>
/// Handles the SMES and substation control UI.
/// </summary>
public sealed class PowerStorageControlSystem : EntitySystem
{
    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(0.5);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(PowerNetSystem));

        SubscribeLocalEvent<PowerStorageControlComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PowerStorageControlComponent, AfterActivatableUIOpenEvent>(OnUiOpened);
        SubscribeLocalEvent<PowerStorageControlComponent, PowerStorageSetInputEnabledMessage>(OnSetInputEnabled);
        SubscribeLocalEvent<PowerStorageControlComponent, PowerStorageSetOutputEnabledMessage>(OnSetOutputEnabled);
        SubscribeLocalEvent<PowerStorageControlComponent, PowerStorageSetInputLimitMessage>(OnSetInputLimit);
        SubscribeLocalEvent<PowerStorageControlComponent, PowerStorageSetOutputLimitMessage>(OnSetOutputLimit);
    }

    private void OnMapInit(Entity<PowerStorageControlComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp(ent, out PowerNetworkBatteryComponent? battery))
            return;

        if (ent.Comp.MaxInputLimit <= 0f)
            ent.Comp.MaxInputLimit = battery.MaxChargeRate;

        if (ent.Comp.MaxOutputLimit <= 0f)
            ent.Comp.MaxOutputLimit = battery.MaxSupply;

        battery.MaxChargeRate = Math.Clamp(battery.MaxChargeRate, 0f, ent.Comp.MaxInputLimit);
        battery.MaxSupply = Math.Clamp(battery.MaxSupply, 0f, ent.Comp.MaxOutputLimit);
    }

    private void OnUiOpened(Entity<PowerStorageControlComponent> ent, ref AfterActivatableUIOpenEvent args)
    {
        UpdateUi(ent);
    }

    private void OnSetInputEnabled(
        Entity<PowerStorageControlComponent> ent,
        ref PowerStorageSetInputEnabledMessage args)
    {
        if (!TryComp(ent, out PowerNetworkBatteryComponent? battery))
            return;

        battery.CanCharge = args.Enabled;
        UpdateUi(ent, battery);
    }

    private void OnSetOutputEnabled(
        Entity<PowerStorageControlComponent> ent,
        ref PowerStorageSetOutputEnabledMessage args)
    {
        if (!TryComp(ent, out PowerNetworkBatteryComponent? battery))
            return;

        battery.CanDischarge = args.Enabled;
        UpdateUi(ent, battery);
    }

    private void OnSetInputLimit(
        Entity<PowerStorageControlComponent> ent,
        ref PowerStorageSetInputLimitMessage args)
    {
        if (!TryComp(ent, out PowerNetworkBatteryComponent? battery) || !float.IsFinite(args.Limit))
            return;

        battery.MaxChargeRate = Math.Clamp(args.Limit, 0f, ent.Comp.MaxInputLimit);
        UpdateUi(ent, battery);
    }

    private void OnSetOutputLimit(
        Entity<PowerStorageControlComponent> ent,
        ref PowerStorageSetOutputLimitMessage args)
    {
        if (!TryComp(ent, out PowerNetworkBatteryComponent? battery) || !float.IsFinite(args.Limit))
            return;

        battery.MaxSupply = Math.Clamp(args.Limit, 0f, ent.Comp.MaxOutputLimit);
        UpdateUi(ent, battery);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<PowerStorageControlComponent, PowerNetworkBatteryComponent>();
        while (query.MoveNext(out var uid, out var control, out var networkBattery))
        {
            if (control.NextUiUpdate > _timing.CurTime ||
                !_ui.IsUiOpen(uid, PowerStorageControlUiKey.Key))
            {
                continue;
            }

            control.NextUiUpdate = _timing.CurTime + UiUpdateInterval;
            UpdateUi((uid, control), networkBattery);
        }
    }

    private void UpdateUi(
        Entity<PowerStorageControlComponent> ent,
        PowerNetworkBatteryComponent? networkBattery = null,
        BatteryComponent? battery = null,
        BatteryChargerComponent? charger = null,
        BatteryDischargerComponent? discharger = null)
    {
        if (!Resolve(ent, ref networkBattery, false) ||
            !Resolve(ent, ref battery, false) ||
            !Resolve(ent, ref charger, false) ||
            !Resolve(ent, ref discharger, false))
            return;

        var state = new PowerStorageControlState(
            battery.CurrentCharge,
            battery.MaxCharge,
            networkBattery.CurrentReceiving,
            networkBattery.CurrentSupply,
            networkBattery.MaxChargeRate,
            networkBattery.MaxSupply,
            ent.Comp.MaxInputLimit,
            ent.Comp.MaxOutputLimit,
            networkBattery.CanCharge,
            networkBattery.CanDischarge,
            ToUiVoltage(charger.Voltage),
            ToUiVoltage(discharger.Voltage));

        _ui.SetUiState(ent.Owner, PowerStorageControlUiKey.Key, state);
    }

    private static PowerStorageVoltage ToUiVoltage(Voltage voltage)
    {
        return voltage switch
        {
            Voltage.High => PowerStorageVoltage.High,
            Voltage.Medium => PowerStorageVoltage.Medium,
            Voltage.Apc => PowerStorageVoltage.Low,
            _ => throw new ArgumentOutOfRangeException(nameof(voltage), voltage, null),
        };
    }
}
