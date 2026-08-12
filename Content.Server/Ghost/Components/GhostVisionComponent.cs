namespace Content.Server.Ghost.Components;

/// <summary>
///     Lets this entity's eye see the ghost visibility layer without changing what anyone else sees.
/// </summary>
/// <remarks>
///     This is the per-player counterpart to <see cref="GhostSystem.MakeVisible"/>, which moves every
///     ghost onto the Normal layer and therefore reveals them to the whole server. Here the ghosts are
///     left alone and only the observer's own eye mask is widened, so it is both private and reversible.
/// </remarks>
[RegisterComponent]
public sealed partial class GhostVisionComponent : Component
{
}
