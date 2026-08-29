namespace Content.Shared._Crescent.Storage;

/// <summary>
/// Prevents an item from being inserted into grid storage while still allowing it in hands and inventory pockets.
/// </summary>
[RegisterComponent]
public sealed partial class PocketOnlyComponent : Component;
