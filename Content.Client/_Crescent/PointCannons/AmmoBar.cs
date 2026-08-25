using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client._KS14.UI; // KS14
using Robust.Client.Graphics; // KS14
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Label = Robust.Client.UserInterface.Controls.Label;

namespace Content.Client.PointCannons;

public sealed class AmmoBar : Control
{
    private int _value;
    public int Value
    {
        get
        {
            return _value;
        }
        set
        {
            _value = value;
            _bar.Value = value;
            _label.Text = value.ToString();
        }
    }

    private int _maxValue;
    public int MaxValue
    {
        get
        {
            return _maxValue;
        }
        set
        {
            _maxValue = value;
            _bar.MaxValue = value;
        }
    }

    private Label _label;
    private ProgressBar _bar;

    public AmmoBar()
    {
        MinHeight = 15;
        HorizontalExpand = true;
        VerticalAlignment = VAlignment.Center;

        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 1),
            Children =
            {
                new Control { MinSize = new Vector2(5, 0) },
                new Control
                {
                    HorizontalExpand = true,
                    MaxHeight = 18,
                    Children =
                    {
                        (_bar = new ProgressBar
                        {
                            MinValue = 0,
                        }),
                        (_label = new Label
                        {
                            StyleClasses = { StyleNano.StyleClassItemStatus },
                            Align = Label.AlignMode.Center
                        })
                    }
                },
                new Control { MinSize = new Vector2(5, 0) },
            }
        });
    }

    /// <summary>
    ///     KS14: repaints the bar in the instrument look - flat bordered trough, filled block,
    ///         VT323 count. Cosmetic only: the values, their clamping and the control's layout
    ///         are untouched, and a bar this is never called on stays vanilla.
    /// </summary>
    public void KsMakeInstrument(KsInstrumentPalette palette)
    {
        _bar.BackgroundStyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = palette.Background,
            BorderColor = palette.AccentDim,
            BorderThickness = new Thickness(1),
        };

        _bar.ForegroundStyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = palette.AccentDim,
            BorderColor = palette.Accent,
            BorderThickness = new Thickness(1),
        };

        // The count sits on top of the fill, so it takes the bright text weight to stay readable
        // over both halves of the trough.
        _label.RemoveStyleClass(StyleNano.StyleClassItemStatus);
        _label.AddStyleClass(KsInstrumentSheetlet.StyleClassSmall);
        _label.FontColorOverride = palette.Text;
    }
}
