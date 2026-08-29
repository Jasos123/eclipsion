namespace Content.Shared._Crescent.Clothing;

/// <summary>
/// Marks a hardsuit helmet that can display head clothing stored in its hat slot.
/// </summary>
[RegisterComponent]
public sealed partial class HardsuitHelmetHatSlotComponent : Component
{
    public const string DefaultSlotId = "hardsuit_helmet_hat";

    /// <summary>
    /// The item slot containing the hat to render over the helmet.
    /// </summary>
    [DataField]
    public string SlotId = DefaultSlotId;
}
