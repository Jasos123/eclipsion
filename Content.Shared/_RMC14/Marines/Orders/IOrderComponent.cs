namespace Content.Shared._RMC14.Marines.Orders;

/// <summary>
/// Shared state for order effects.
/// </summary>
public partial interface IOrderComponent : IComponent
{
    /// <summary>
    /// Current order power.
    /// </summary>
    int Power { get; set; }

    /// <summary>
    /// When the order wears off.
    /// </summary>
    TimeSpan ExpiresAt { get; set; }
}
