using Content.Shared.Ame.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;

namespace Content.Server.Ame.EntitySystems;

/// <summary>
/// Keeps an AME jar's FuelAmount and its reagent solution in step, so jars can be topped up with
/// chemistry-made fuel instead of only being crafted full.
/// </summary>
public sealed class AmeFuelRefillSystem : EntitySystem
{
    public const string FuelReagent = "AmeFuel";

    public const string RefillSolution = "tank";

    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;

    private readonly Dictionary<EntityUid, int> _lastFuel = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AmeFuelContainerComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<AmeFuelContainerComponent> ent, ref ComponentShutdown args)
    {
        _lastFuel.Remove(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<AmeFuelContainerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!_solution.TryGetSolution(uid, RefillSolution, out var soln, out var solution))
                continue;

            var reagent = solution.GetTotalPrototypeQuantity(FuelReagent).Int();

            if (!_lastFuel.TryGetValue(uid, out var last))
            {
                // First time we've seen this jar. Solutions aren't up yet at MapInit, so seed it here.
                if (comp.FuelAmount > reagent)
                {
                    _solution.TryAddReagent(soln.Value, FuelReagent,
                        FixedPoint2.New(comp.FuelAmount - reagent));
                }

                _lastFuel[uid] = comp.FuelAmount;
                continue;
            }

            if (comp.FuelAmount < last)
            {
                // The AME burned some, take the same amount back out of the solution.
                var burned = Math.Min(last - comp.FuelAmount, reagent);
                if (burned > 0)
                    _solution.RemoveReagent(soln.Value, FuelReagent, FixedPoint2.New(burned));
            }
            else
            {
                // Nothing burned, so someone poured in (or drained) fuel. Follow the solution.
                var fuel = Math.Min(comp.FuelCapacity, reagent);
                if (fuel != comp.FuelAmount)
                {
                    comp.FuelAmount = fuel;
                    Dirty(uid, comp);
                }
            }

            _lastFuel[uid] = comp.FuelAmount;
        }
    }
}
