using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.ActionBlocker;
using Robust.Shared.Timing;

namespace Content.Server.Body.Systems;

public sealed class ThermalRegulatorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly TemperatureSystem _tempSys = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlockerSys = default!;

    /// <summary>
    /// How much of the gap between normal body temperature and the temperature that starts burning the
    /// body the comfortable range is allowed to cover.
    /// </summary>
    private const float SafeOverheatFraction = 0.25f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ThermalRegulatorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ThermalRegulatorComponent, EntityUnpausedEvent>(OnUnpaused);
    }

    private void OnMapInit(Entity<ThermalRegulatorComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _gameTiming.CurTime + ent.Comp.UpdateInterval;
    }

    private void OnUnpaused(Entity<ThermalRegulatorComponent> ent, ref EntityUnpausedEvent args)
    {
        ent.Comp.NextUpdate += args.PausedTime;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ThermalRegulatorComponent>();
        while (query.MoveNext(out var uid, out var regulator))
        {
            if (_gameTiming.CurTime < regulator.NextUpdate)
                continue;

            regulator.NextUpdate += regulator.UpdateInterval;
            ProcessThermalRegulation((uid, regulator));
        }
    }

    /// <summary>
    /// Processes thermal regulation for a mob
    /// </summary>
    private void ProcessThermalRegulation(Entity<ThermalRegulatorComponent, TemperatureComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp2, logMissing: false))
            return;

        var totalMetabolismTempChange = ent.Comp1.MetabolismHeat - ent.Comp1.RadiatedHeat;

        // implicit heat regulation
        var tempDiff = Math.Abs(ent.Comp2.CurrentTemperature - ent.Comp1.NormalBodyTemperature);
        var heatCapacity = _tempSys.GetHeatCapacity(ent, ent);
        var targetHeat = tempDiff * heatCapacity;
        if (ent.Comp2.CurrentTemperature > ent.Comp1.NormalBodyTemperature)
        {
            totalMetabolismTempChange -= Math.Min(targetHeat, ent.Comp1.ImplicitHeatRegulation);
        }
        else
        {
            totalMetabolismTempChange += Math.Min(targetHeat, ent.Comp1.ImplicitHeatRegulation);
        }

        _tempSys.ChangeHeat(ent, totalMetabolismTempChange, ignoreHeatResistance: true, ent);

        // recalc difference and target heat
        tempDiff = Math.Abs(ent.Comp2.CurrentTemperature - ent.Comp1.NormalBodyTemperature);
        targetHeat = tempDiff * heatCapacity;

        var overheating = ent.Comp2.CurrentTemperature > ent.Comp1.NormalBodyTemperature;

        // Active regulation starts once the body temperature leaves the comfortable range.
        if (tempDiff < GetRegulationThreshold(ent.Comp1, ent.Comp2, overheating))
            return;

        if (overheating)
        {
            if (!_actionBlockerSys.CanSweat(ent))
                return;

            _tempSys.ChangeHeat(ent, -Math.Min(targetHeat, ent.Comp1.SweatHeatRegulation), ignoreHeatResistance: true, ent);
        }
        else
        {
            if (!_actionBlockerSys.CanShiver(ent))
                return;

            _tempSys.ChangeHeat(ent, Math.Min(targetHeat, ent.Comp1.ShiveringHeatRegulation), ignoreHeatResistance: true, ent);
        }
    }

    /// <summary>
    /// How far the body is allowed to drift from its normal temperature before sweating or shivering starts.
    /// </summary>
    /// <remarks>
    /// Metabolism always outpaces implicit regulation (a human nets +200 J/s above normal), so the only thing
    /// dumping that heat is the surrounding air. Anything that stops the air taking it - a hardsuit's
    /// TemperatureProtection multiplies atmospheric exchange by as little as 0.001, while metabolism ignores heat
    /// resistance entirely - parks the body at normalBodyTemperature + the configured threshold. For a human that
    /// is 335K, above the 325K where burn damage starts, so a sealed suit slowly cooked its wearer. Clamp the hot
    /// side of the range so the parking spot always stays under the burn threshold. The cold side is untouched.
    /// </remarks>
    private float GetRegulationThreshold(ThermalRegulatorComponent regulator, TemperatureComponent temperature, bool overheating)
    {
        if (!overheating)
            return regulator.ThermalRegulationTemperatureThreshold;

        var heatDamageThreshold = temperature.ParentHeatDamageThreshold ?? temperature.HeatDamageThreshold;
        var headroom = heatDamageThreshold - regulator.NormalBodyTemperature;

        return Math.Min(regulator.ThermalRegulationTemperatureThreshold, Math.Max(0f, headroom * SafeOverheatFraction));
    }
}
