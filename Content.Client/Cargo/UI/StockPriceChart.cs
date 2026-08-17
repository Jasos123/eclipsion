using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client.Cargo.UI;

public sealed class StockPriceChart : Control
{
    public static readonly Color UpColor = Color.FromHex("#3fc47a");
    public static readonly Color DownColor = Color.FromHex("#d64f4f");
    public static readonly Color NeutralColor = Color.FromHex("#9fa8b0");
    public static readonly Color BackgroundColor = Color.FromHex("#141418");
    public static readonly Color GridColor = Color.FromHex("#2c2c31");
    public static readonly Color BaselineColor = Color.FromHex("#55555c");

    private readonly List<float> _data = new();
    private float _baseline;
    private bool _hasBaseline;
    private Color? _lineColor;

    public float Padding { get; set; } = 4f;

    public int GridDivisions { get; set; } = 3;

    /// <param name="lineColor">
    /// Colour for the plotted line. Pass the colour of whatever trend figure sits beside the chart:
    /// left to itself the chart colours by first-versus-last across the entire history, which is a
    /// window nothing on screen names, and it ends up drawn green next to a red percentage. Both are
    /// right about different spans, and readers reasonably conclude the market is broken.
    /// </param>
    public void SetData(IReadOnlyList<float>? prices, float? baseline = null, Color? lineColor = null)
    {
        _data.Clear();
        if (prices != null)
            _data.AddRange(prices);

        _hasBaseline = baseline != null;
        _baseline = baseline ?? 0f;
        _lineColor = lineColor;
        InvalidateMeasure();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        handle.DrawRect(box, BackgroundColor);

        var pad = Padding * UIScale;
        var left = box.Left + pad;
        var right = box.Right - pad;
        var top = box.Top + pad;
        var bottom = box.Bottom - pad;

        if (right - left < 2f || bottom - top < 2f)
            return;

        if (GridDivisions > 0)
        {
            for (var g = 1; g < GridDivisions; g++)
            {
                var y = top + (bottom - top) * g / GridDivisions;
                handle.DrawLine(new Vector2(left, y), new Vector2(right, y), GridColor);
            }
        }

        if (_data.Count < 2)
            return;

        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var v in _data)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        if (_hasBaseline)
        {
            if (_baseline < min) min = _baseline;
            if (_baseline > max) max = _baseline;
        }

        var range = max - min;
        if (range <= float.Epsilon)
        {
            var bump = Math.Max(Math.Abs(max) * 0.05f, 1f);
            min -= bump;
            max += bump;
            range = max - min;
        }

        float ToY(float value) => bottom - (value - min) / range * (bottom - top);

        if (_hasBaseline)
        {
            var by = ToY(_baseline);
            handle.DrawLine(new Vector2(left, by), new Vector2(right, by), BaselineColor);
        }

        var color = _lineColor ?? (_data[^1] > _data[0] ? UpColor
            : _data[^1] < _data[0] ? DownColor
            : NeutralColor);

        var spacing = (right - left) / (_data.Count - 1);
        for (var i = 0; i + 1 < _data.Count; i++)
        {
            var v1 = new Vector2(left + i * spacing, ToY(_data[i]));
            var v2 = new Vector2(left + (i + 1) * spacing, ToY(_data[i + 1]));

            handle.DrawLine(v1, v2, color);
            handle.DrawLine(v1 + new Vector2(0, 1), v2 + new Vector2(0, 1), color);
        }

        var last = new Vector2(right, ToY(_data[^1]));
        handle.DrawRect(new UIBox2(last - new Vector2(2, 2), last + new Vector2(2, 2)), color);
    }
}
