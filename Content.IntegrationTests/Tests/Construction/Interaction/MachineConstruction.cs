using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Stack;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Construction.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests.Construction.Interaction;

public sealed class MachineConstruction : InteractionTest
{
    private const string MachineFrame = "MachineFrame";
    private const string Unfinished = "UnfinishedMachineFrame";
    private const string ProtolatheBoard = "ProtolatheMachineCircuitboard";
    private const string Protolathe = "Protolathe";
    private const string BiofabricatorBoard = "BiofabricatorMachineCircuitboard";
    private const string Biofabricator = "Biofabricator";
    private const string AdvancedBin = "AdvancedMatterBinStockPart";
    private const string BluespaceBin = "BluespaceMatterBinStockPart";
    private const string Beaker = "Beaker";

    [Test]
    public async Task ConstructProtolathe()
    {
        await StartConstruction(MachineFrame);
        await InteractUsing(Steel, 5);
        ClientAssertPrototype(Unfinished, Target);
        await Interact(Wrench, Cable);
        AssertPrototype(MachineFrame);
        await Interact(ProtolatheBoard, Bin1, Bin1, Manipulator1, Manipulator1, Beaker, Beaker, Screw);
        AssertPrototype(Protolathe);
    }

    [Test]
    public async Task DeconstructProtolathe()
    {
        await StartDeconstruction(Protolathe);
        await Interact(Screw, Pry);
        AssertPrototype(MachineFrame);
        await Interact(Pry, Cut);
        AssertPrototype(Unfinished);
        await Interact(Wrench, Screw);
        AssertDeleted();
        await AssertEntityLookup(
            (Steel, 5),
            (Cable, 1),
            (Beaker, 2),
            (Manipulator1, 2),
            (Bin1, 2),
            (ProtolatheBoard, 1));
    }

    [Test]
    public async Task ReconstructBiofabricatorWithPartsInFrame()
    {
        await StartDeconstruction(Biofabricator);
        await Interact(Screw, Pry);
        AssertPrototype(MachineFrame);
        await Interact(Screw);
        AssertPrototype(Biofabricator);
    }

    [Test]
    public async Task RegenerateProgressCountsStackedParts()
    {
        await StartConstruction(MachineFrame);
        await InteractUsing(Steel, 5);
        await Interact(Wrench, Cable);
        await InteractUsing(ProtolatheBoard);
        await InteractUsing(Bin1, 2);
        await InteractUsing(Manipulator1, 2);
        await Interact(Beaker, Beaker);
        await InteractUsing(Glass);

        await Server.WaitPost(() =>
        {
            var target = ToServer(Target!.Value);
            var frame = SEntMan.GetComponent<MachineFrameComponent>(target);
            SEntMan.System<MachineFrameSystem>().RegenerateProgress(frame);

            Assert.Multiple(() =>
            {
                Assert.That(frame.Progress["MatterBin"], Is.EqualTo(2));
                Assert.That(frame.Progress["Manipulator"], Is.EqualTo(2));
            });
        });

        await InteractUsing(Screw);
        AssertPrototype(Protolathe);
    }

    [Test]
    public async Task StackedPartsWeightUpgradeRatingsByCount()
    {
        await StartConstruction(MachineFrame);
        await InteractUsing(Steel, 5);
        await Interact(Wrench, Cable);
        await InteractUsing(BiofabricatorBoard);
        await InteractUsing(Bin1, 2);
        await InteractUsing(AdvancedBin);
        await InteractUsing(Manipulator1);
        await InteractUsing(Glass);
        await InteractUsing(Screw);
        AssertPrototype(Biofabricator);

        await Server.WaitPost(() =>
        {
            var target = ToServer(Target!.Value);
            var construction = SEntMan.System<ConstructionSystem>();
            var ratings = construction.GetPartsRatings(construction.GetAllParts(target));

            Assert.That(ratings["MatterBin"], Is.EqualTo(4f / 3f).Within(0.001f));
        });
    }

    [Test]
    public async Task RpedOnlyTakesRequiredAmountFromPartStack()
    {
        await SpawnTarget(Biofabricator);
        await InteractUsing(Screw);

        var rped = await PlaceInHands("RPED");
        await Server.WaitPost(() =>
        {
            var rpedUid = ToServer(rped);
            var part = SEntMan.SpawnEntity(BluespaceBin, Position(rpedUid));
            SEntMan.System<StackSystem>().SetCount(part, 10);

            var storage = SEntMan.GetComponent<StorageComponent>(rpedUid);
            Assert.That(SEntMan.System<SharedContainerSystem>().Insert(part, storage.Container!));
        });

        await Interact();

        await Server.WaitPost(() =>
        {
            var target = ToServer(Target!.Value);
            var machine = SEntMan.GetComponent<MachineComponent>(target);
            var installedBins = 0;
            var installedRating = 0;

            foreach (var part in machine.PartContainer.ContainedEntities)
            {
                if (!SEntMan.TryGetComponent<MachinePartComponent>(part, out var machinePart) ||
                    machinePart.PartType != "MatterBin")
                {
                    continue;
                }

                var count = SEntMan.TryGetComponent<StackComponent>(part, out var stack) ? stack.Count : 1;
                installedBins += count;
                installedRating += count * machinePart.Rating;
            }

            var rpedUid = ToServer(rped);
            var storage = SEntMan.GetComponent<StorageComponent>(rpedUid);
            var remainingBins = storage.Container!.ContainedEntities
                .Where(part => SEntMan.TryGetComponent<StackComponent>(part, out var stack) &&
                    stack.StackTypeId == "BluespaceMatterBin")
                .Sum(part => SEntMan.GetComponent<StackComponent>(part).Count);

            Assert.Multiple(() =>
            {
                Assert.That(installedBins, Is.EqualTo(3));
                Assert.That(installedRating, Is.EqualTo(12));
                Assert.That(remainingBins, Is.EqualTo(7));
            });
        });
    }

    [Test]
    public async Task ChangeMachine()
    {
        // Partially deconstruct a protolathe.
        await SpawnTarget(Protolathe);
        await Interact(Screw, Pry, Pry);
        AssertPrototype(MachineFrame);

        // Change it into an autolathe
        await InteractUsing("AutolatheMachineCircuitboard");
        AssertPrototype(MachineFrame);
        await Interact(Bin1, Bin1, Bin1, Manipulator1, Glass, Screw);
        AssertPrototype("Autolathe");
    }
}
