using System.Numerics;
using Content.Shared.Abilities.Psionics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Psionics;

/// <summary>
/// Drains the colour out of whatever a recurrence field is holding.
///
/// A sprite could only tint the bubble itself; the point of the power is that the world inside it
/// stops being part of the same moment as the world outside, so this samples the finished frame and
/// desaturates the region instead. Structured after <see cref="Singularity.SingularityOverlay"/>,
/// which solves the same "shade a world-space circle from a screen texture" problem.
/// </summary>
public sealed class RecurrenceFieldOverlay : Overlay
{
    /// <summary>
    /// Maximum number of fields that can shade the screen at once. The shader declares the same
    /// bound, and both have to move together.
    /// </summary>
    public const int MaxCount = 5;

    /// <summary>
    /// Fields further than this from the viewport are not worth uploading.
    /// </summary>
    private const float MaxDistance = 24f;

    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private readonly ShaderInstance _shader;
    private readonly Vector2[] _positions = new Vector2[MaxCount];
    private readonly float[] _radii = new float[MaxCount];

    private SharedTransformSystem? _xformSystem;
    private int _count;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public RecurrenceFieldOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index<ShaderPrototype>("RecurrenceField").Instance().Duplicate();
        ZIndex = 100;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null)
            return false;

        if (_xformSystem is null && !_entMan.TrySystem(out _xformSystem))
            return false;

        _count = 0;
        var query = _entMan.EntityQueryEnumerator<RecurrenceFieldComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var field, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var mapPos = _xformSystem.GetWorldPosition(uid);
            if ((mapPos - args.WorldAABB.ClosestPoint(mapPos)).LengthSquared() > MaxDistance * MaxDistance)
                continue;

            // Inside-viewport pixels, then flipped into fragment space, exactly as the shader expects.
            var coords = args.Viewport.WorldToLocal(mapPos);
            coords.Y = args.Viewport.Size.Y - coords.Y;

            _positions[_count] = coords;
            _radii[_count] = field.Radius * EyeManager.PixelsPerMeter;
            _count++;

            if (_count == MaxCount)
                break;
        }

        return _count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || args.Viewport.Eye == null)
            return;

        _shader.SetParameter("renderScale", args.Viewport.RenderScale * args.Viewport.Eye.Scale);
        _shader.SetParameter("count", _count);
        _shader.SetParameter("position", _positions);
        _shader.SetParameter("radius", _radii);
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_shader);
        worldHandle.DrawRect(args.WorldAABB, Color.White);
        worldHandle.UseShader(null);
    }
}
