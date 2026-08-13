using Lidgren.Network;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Mapping;

/// <summary>
///     Asks the server to stop culling a grid's entities for the sending mapper, so that a grid screenshot can
///     draw the parts of the grid the mapper is not standing next to. Sent again with <see cref="Enabled"/>
///     false once the screenshot has been taken.
/// </summary>
public sealed class MappingScreenshotPvsMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableUnordered;

    public NetEntity Grid;
    public bool Enabled;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Grid = new NetEntity(buffer.ReadInt32());
        Enabled = buffer.ReadBoolean();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Grid.Id);
        buffer.Write(Enabled);
    }
}
