using Content.Server.Mining.Components;
using Content.Server.Stack;
using Content.Shared.Destructible;
using Content.Shared.Mining;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Mining;

/// <summary>
/// This handles creating ores when the entity is destroyed.
/// </summary>
public sealed class MiningSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StackSystem _stack = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OreVeinComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OreVeinComponent, DestructionEventArgs>(OnDestruction);
    }

    private void OnDestruction(EntityUid uid, OreVeinComponent component, DestructionEventArgs args)
    {
        if (component.CurrentOre == null)
            return;

        var proto = _proto.Index<OrePrototype>(component.CurrentOre);

        if (proto.OreEntity == null)
            return;

        var coords = Transform(uid).Coordinates;
        var toSpawn = _random.Next(proto.MinOreYield, proto.MaxOreYield);

        if (toSpawn <= 0)
            return;

        var firstOre = Spawn(proto.OreEntity, coords.Offset(_random.NextVector2(0.2f)));

        if (!TryComp<StackComponent>(firstOre, out var stack))
        {
            for (var i = 1; i < toSpawn; i++)
            {
                Spawn(proto.OreEntity, coords.Offset(_random.NextVector2(0.2f)));
            }

            return;
        }

        var maxStackSize = Math.Max(1, _stack.GetMaxCount(stack));
        _stack.SetCount(firstOre, Math.Min(toSpawn, maxStackSize), stack);

        for (var remaining = toSpawn - maxStackSize; remaining > 0; remaining -= maxStackSize)
        {
            var ore = Spawn(proto.OreEntity, coords.Offset(_random.NextVector2(0.2f)));
            _stack.SetCount(ore, Math.Min(remaining, maxStackSize));
        }
    }

    private void OnMapInit(EntityUid uid, OreVeinComponent component, MapInitEvent args)
    {
        if (component.CurrentOre != null || component.OreRarityPrototypeId == null || !_random.Prob(component.OreChance))
            return;

        component.CurrentOre = _proto.Index<WeightedRandomOrePrototype>(component.OreRarityPrototypeId).Pick(_random);
    }
}
