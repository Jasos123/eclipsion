using System.Linq;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared.Psionics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Psionics.UI;

/// <summary>
/// A research-console-inspired, card-based view of the player's psionic progression.
/// The whole tree lives on a single plane: one column per discipline, one row per tier. Vertical
/// scrolling is disabled on purpose - the plane is sized to the window and the mouse wheel pans it
/// left/right instead, so reading the tree never means hunting up and down.
/// </summary>
public sealed class PsionicSkillTreeWindow : DefaultWindow
{
    private static readonly Color OwnedColor = Color.FromHex("#2AB043");
    private static readonly Color AvailableColor = Color.FromHex("#FAB325");
    private static readonly Color LockedColor = Color.FromHex("#57575F");
    private static readonly Color ExcludedColor = Color.FromHex("#9F3344");

    private const float BranchWidth = 300f;
    private const float BranchHeaderHeight = 56f;
    private const float TierGutterWidth = 26f;
    private const float MaxCardHeight = 168f;

    /// <summary>
    /// Column margin, border and content margin around the card stack inside a discipline column.
    /// The tier gutter on the left is offset by this so its labels line up with the rows.
    /// </summary>
    private const float ColumnTopOffset = 14f;

    /// <summary>
    /// Width a card gets to lay its text out in: the column minus its margins and content margins
    /// (300 - 8 - 16), kept a few pixels narrower than that on purpose. Measuring against a
    /// narrower card can only overestimate the height, which errs towards trimming a word too many
    /// rather than letting a line fall out of the row.
    /// </summary>
    private const float CardMeasureWidth = BranchWidth - 24f - 4f;

    /// <summary>
    /// Plane height assumed for the frame between building the tree and the first arrange, after
    /// which the real arranged height takes over. Deliberately pessimistic.
    /// </summary>
    private const float FallbackPlaneHeight = 430f;

    private readonly IPrototypeManager _prototypeManager;
    private readonly SpriteSystem _spriteSystem;
    private readonly Label _levelLabel;
    private readonly Label _pointsLabel;
    private readonly Label _progressLabel;
    private readonly ProgressBar _progressBar;
    private readonly BoxContainer _tierGutter;
    private readonly BoxContainer _branches;
    private readonly ScrollContainer _scroll;

    private readonly List<SkillCard> _cards = new();
    private readonly List<(int Tier, BoxContainer Row)> _rows = new();
    private readonly Dictionary<int, int> _cardsPerTier = new();
    private List<int> _tiers = new();
    private float _fittedPlane = -1f;

    public event Action<string>? OnSkillSelected;

    public PsionicSkillTreeWindow()
    {
        _prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        _spriteSystem = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();

        Title = Loc.GetString("psionic-skill-tree-window-title");
        MinSize = new Vector2(860, 620);
        SetSize = new Vector2(1280, 700);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var headerPanel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(8, 8, 8, 4),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#202027"),
                BorderColor = Color.FromHex("#7848A8"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 10,
                ContentMarginRightOverride = 10,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8,
            },
        };

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };

        var stats = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        _levelLabel = new Label
        {
            StyleClasses = { StyleBase.StyleClassLabelHeading },
            HorizontalExpand = true,
        };
        _pointsLabel = new Label
        {
            StyleClasses = { StyleBase.StyleClassLabelHeading },
            HorizontalAlignment = HAlignment.Right,
        };
        stats.AddChild(_levelLabel);
        stats.AddChild(_pointsLabel);

        var progressRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        _progressLabel = new Label
        {
            StyleClasses = { StyleBase.StyleClassLabelSubText },
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VAlignment.Center,
        };
        _progressBar = new ProgressBar
        {
            HorizontalExpand = true,
            MinHeight = 12,
            VerticalAlignment = VAlignment.Center,
        };

        progressRow.AddChild(_progressLabel);
        progressRow.AddChild(_progressBar);
        progressRow.AddChild(BuildLegend());

        header.AddChild(stats);
        header.AddChild(progressRow);
        headerPanel.AddChild(header);
        root.AddChild(headerPanel);

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(8, 4, 8, 8),
        };

        _tierGutter = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = TierGutterWidth,
            VerticalExpand = true,
        };
        body.AddChild(_tierGutter);

        // Horizontal-only scrolling: the wheel falls back to the X axis when the Y axis is off, so
        // a plain scroll pans the tree sideways.
        _scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = true,
            VScrollEnabled = false,
        };

        _branches = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            VerticalExpand = true,
        };
        _scroll.AddChild(_branches);
        body.AddChild(_scroll);

        root.AddChild(body);
        Contents.AddChild(root);
    }

    public void UpdateState(PsionicSkillTreeEuiState state)
    {
        _levelLabel.Text = Loc.GetString("psionic-skill-tree-level", ("level", state.Level));
        _pointsLabel.Text = Loc.GetString("psionic-skill-tree-points", ("points", state.SkillPoints));
        _progressLabel.Text = Loc.GetString(
            "psionic-skill-tree-progress",
            ("current", MathF.Floor(state.Potentia)),
            ("required", MathF.Ceiling(state.NextLevelCost)));
        _progressBar.MaxValue = Math.Max(1f, state.NextLevelCost);
        _progressBar.Value = Math.Clamp(state.Potentia, 0f, _progressBar.MaxValue);

        // Buying a skill pushes a fresh state; rebuilding the plane must not throw the player back
        // to the leftmost discipline.
        var scrollX = _scroll.GetScrollValue().X;

        _tierGutter.DisposeAllChildren();
        _branches.DisposeAllChildren();
        _cards.Clear();
        _rows.Clear();

        if (!_prototypeManager.TryIndex<PsionicSkillTreePrototype>(state.TreeId, out var tree))
            return;

        // Indexer rather than ToDictionary: a tree prototype that lists the same skill twice would
        // otherwise throw and take the whole window down.
        var stateById = new Dictionary<string, PsionicSkillNodeState>();
        foreach (var node in state.Skills)
            stateById[node.SkillId] = node;

        var shown = tree.Skills
            .Select(id => _prototypeManager.TryIndex(id, out PsionicSkillPrototype? skill) ? skill : null)
            .Where(skill => skill != null && stateById.ContainsKey(skill.ID))
            .Cast<PsionicSkillPrototype>()
            .ToList();

        // One row per tier that actually holds skills, sized for the busiest cell in it so every
        // column keeps its tiers on the same line.
        _tiers = shown.Select(skill => skill.Tier).Distinct().OrderBy(tier => tier).ToList();
        _cardsPerTier.Clear();
        foreach (var tier in _tiers)
        {
            _cardsPerTier[tier] = shown
                .Where(skill => skill.Tier == tier)
                .GroupBy(skill => skill.Branch.Id)
                .Select(group => group.Count())
                .DefaultIfEmpty(1)
                .Max();
        }

        foreach (var branchId in tree.Branches)
        {
            if (!_prototypeManager.TryIndex(branchId, out var branch))
                continue;

            var skills = shown
                .Where(skill => skill.Branch == branchId)
                .OrderBy(skill => skill.Tier)
                .ThenBy(skill => skill.ID)
                .ToList();

            if (skills.Count == 0)
                continue;

            _branches.AddChild(CreateBranch(branch, skills, stateById));
        }

        _fittedPlane = -1f;
        ApplyFit();

        _scroll.SetScrollValue(new Vector2(scrollX, 0));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // The plane only changes when the window is resized, and the columns are a fixed width, so
        // a refit cannot feed back into itself. Doing it here rather than off a resize event also
        // covers the first arrange, where the branch strip still has no size to fit against.
        var plane = _branches.Size.Y;
        if (_cards.Count > 0 && plane > 0f && MathF.Abs(plane - _fittedPlane) > 4f)
            ApplyFit();
    }

    /// <summary>
    /// Measures the cards against the height the plane actually has, trims the descriptions that do
    /// not fit and sizes the tier rows to what is left. The measuring uses the real fonts, so
    /// nothing is cut off mid-line, and a taller window simply shows more of the text.
    /// </summary>
    private void ApplyFit()
    {
        var plane = _branches.Size.Y > 0f ? _branches.Size.Y : FallbackPlaneHeight;
        _fittedPlane = plane;

        var rowsBudget = MathF.Max(80f, plane - ColumnTopOffset * 2f - BranchHeaderHeight - 4f);
        var slots = Math.Max(1, _cardsPerTier.Values.Sum());
        var slotHeight = MathF.Min(MaxCardHeight, rowsBudget / slots);

        var rowHeights = new Dictionary<int, float>();
        foreach (var tier in _tiers)
            rowHeights[tier] = 0f;

        foreach (var card in _cards)
            rowHeights[card.Tier] = MathF.Max(rowHeights[card.Tier], card.Fit(slotHeight));

        // A tier whose cards all came out short keeps its row tight instead of leaving a gap, and a
        // cell holding several skills still gets one slot per skill.
        foreach (var tier in _tiers)
        {
            var height = rowHeights[tier] <= 0f ? slotHeight : rowHeights[tier];
            rowHeights[tier] = height * _cardsPerTier[tier];
        }

        foreach (var (tier, row) in _rows)
            row.SetHeight = rowHeights[tier];

        BuildTierGutter(_tiers, rowHeights);
    }

    private Control BuildLegend()
    {
        var legend = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VAlignment.Center,
        };

        legend.AddChild(LegendEntry(OwnedColor, "psionic-skill-tree-owned"));
        legend.AddChild(LegendEntry(AvailableColor, "psionic-skill-tree-available"));
        legend.AddChild(LegendEntry(LockedColor, "psionic-skill-tree-locked"));
        return legend;
    }

    private static Control LegendEntry(Color color, string locId)
    {
        var entry = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(0, 0, 10, 0),
        };

        entry.AddChild(new PanelContainer
        {
            MinSize = new Vector2(10, 10),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            PanelOverride = new StyleBoxFlat { BackgroundColor = color },
        });
        entry.AddChild(new Label
        {
            Text = Loc.GetString(locId),
            StyleClasses = { StyleBase.StyleClassLabelSubText },
            VerticalAlignment = VAlignment.Center,
        });

        return entry;
    }

    private void BuildTierGutter(List<int> tiers, Dictionary<int, float> rowHeights)
    {
        _tierGutter.DisposeAllChildren();
        _tierGutter.AddChild(new Control { SetHeight = BranchHeaderHeight + ColumnTopOffset });

        foreach (var tier in tiers)
        {
            _tierGutter.AddChild(new Label
            {
                Text = Loc.GetString("psionic-skill-tree-tier-short", ("tier", tier + 1)),
                StyleClasses = { StyleBase.StyleClassLabelSubText },
                SetHeight = rowHeights[tier],
                Align = Label.AlignMode.Center,
                VAlign = Label.VAlignMode.Center,
                ModulateSelfOverride = Color.FromHex("#7848A8"),
            });
        }

        _tierGutter.AddChild(new Control { VerticalExpand = true });
    }

    private Control CreateBranch(
        PsionicSkillBranchPrototype branch,
        List<PsionicSkillPrototype> skills,
        Dictionary<string, PsionicSkillNodeState> states)
    {
        var columnPanel = new PanelContainer
        {
            MinWidth = BranchWidth,
            MaxWidth = BranchWidth,
            VerticalExpand = true,
            Margin = new Thickness(4, 4, 4, 4),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.InterpolateBetween(branch.Color, Color.Black, 0.82f),
                BorderColor = branch.Color,
                BorderThickness = new Thickness(2),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8,
            },
        };

        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };

        column.AddChild(CreateBranchHeader(branch));

        foreach (var tier in _tiers)
        {
            // The height is filled in once the cards have been measured; until then the row just
            // wraps whatever it holds.
            var row = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                RectClipContent = true,
            };

            foreach (var skill in skills.Where(skill => skill.Tier == tier))
            {
                var card = CreateSkillCard(skill, states[skill.ID], tier);
                _cards.Add(card);
                row.AddChild(card.Control);
            }

            _rows.Add((tier, row));
            column.AddChild(row);
        }

        column.AddChild(new Control { VerticalExpand = true });

        columnPanel.AddChild(column);
        return columnPanel;
    }

    private static Control CreateBranchHeader(PsionicSkillBranchPrototype branch)
    {
        var description = Loc.GetString(branch.Description);

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SetHeight = BranchHeaderHeight,
            RectClipContent = true,
        };

        header.AddChild(new Label
        {
            Text = Loc.GetString(branch.Name),
            Align = Label.AlignMode.Center,
            StyleClasses = { StyleBase.StyleClassLabelHeading },
            ModulateSelfOverride = branch.Color,
        });

        var branchDescription = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(2, 0, 2, 4),
        };
        branchDescription.SetMessage(SubText(description));
        header.AddChild(branchDescription);

        var tooltip = new Tooltip();
        tooltip.SetMessage(FormattedMessage.FromUnformatted(description));
        header.TooltipSupplier = _ => tooltip;

        return header;
    }

    private SkillCard CreateSkillCard(PsionicSkillPrototype skill, PsionicSkillNodeState state, int tier)
    {
        var statusColor = state.Availability switch
        {
            PsionicSkillAvailability.Owned => OwnedColor,
            PsionicSkillAvailability.Available => AvailableColor,
            PsionicSkillAvailability.Excluded => ExcludedColor,
            _ => LockedColor,
        };

        var description = Loc.GetString(skill.Description);

        var button = new Button
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(0, 3),
            Disabled = state.Availability != PsionicSkillAvailability.Available,
            ModulateSelfOverride = statusColor,
            RectClipContent = true,
        };
        button.StyleClasses.Add(StyleBase.ButtonSquare);

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(5, 4),
        };

        var titleRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        titleRow.AddChild(new TextureRect
        {
            Texture = _spriteSystem.Frame0(skill.Icon),
            SetSize = new Vector2(36, 36),
            MinSize = new Vector2(36, 36),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Stretch = TextureRect.StretchMode.KeepCentered,
        });

        // Labels are single-line controls, so longer localized power names were clipped by the
        // fixed-width branch cards. RichTextLabel wraps to the available width.
        var nameLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        var name = FormattedMessage.EscapeText(Loc.GetString(skill.Name));
        nameLabel.SetMessage(FormattedMessage.FromMarkupOrThrow($"[bold][font size=14]{name}[/font][/bold]"));
        titleRow.AddChild(nameLabel);
        content.AddChild(titleRow);

        // The description runs the full card width instead of the strip beside the icon, so it
        // wraps in far fewer lines. The untrimmed text is always in the tooltip.
        var descriptionLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 2, 0, 0),
        };
        descriptionLabel.SetMessage(SubText(description));
        content.AddChild(descriptionLabel);

        content.AddChild(new Control { VerticalExpand = true });

        content.AddChild(new Label
        {
            Text = GetStatusText(skill, state),
            ModulateSelfOverride = statusColor,
            StyleClasses = { StyleBase.StyleClassLabelSubText },
            ClipText = true,
        });

        button.AddChild(content);
        button.OnPressed += _ => OnSkillSelected?.Invoke(skill.ID);

        var tooltip = new Tooltip();
        tooltip.SetMessage(FormattedMessage.FromUnformatted(
            string.IsNullOrWhiteSpace(state.Reason)
                ? description
                : $"{description}\n\n{state.Reason}"));
        button.TooltipSupplier = _ => tooltip;

        return new SkillCard(button, descriptionLabel, description, tier);
    }

    /// <summary>
    /// The LabelSubText style class only matches <see cref="Label"/>, so rich text has to ask for
    /// the small grey face itself.
    /// </summary>
    private static FormattedMessage SubText(string text)
    {
        return FormattedMessage.FromMarkupOrThrow(
            $"[color=#A8A8B0][font size=10]{FormattedMessage.EscapeText(text)}[/font][/color]");
    }

    private static string GetStatusText(PsionicSkillPrototype skill, PsionicSkillNodeState state)
    {
        return state.Availability switch
        {
            PsionicSkillAvailability.Owned => Loc.GetString("psionic-skill-tree-owned"),
            PsionicSkillAvailability.Available => Loc.GetString(
                "psionic-skill-tree-unlock-cost",
                ("points", skill.Cost),
                ("level", skill.MinimumLevel)),
            _ => state.Reason ?? Loc.GetString("psionic-skill-tree-locked"),
        };
    }

    /// <summary>
    /// One built card, kept around so its description can be measured and trimmed to the slot the
    /// plane can spare for it.
    /// </summary>
    private sealed class SkillCard
    {
        public readonly Control Control;
        public readonly int Tier;

        private readonly RichTextLabel _description;
        private readonly string _fullDescription;

        public SkillCard(Control control, RichTextLabel description, string fullDescription, int tier)
        {
            Control = control;
            _description = description;
            _fullDescription = fullDescription;
            Tier = tier;
        }

        /// <summary>
        /// Drops words off the description until the card measures within
        /// <paramref name="slotHeight"/>, and returns the height it settled on.
        /// </summary>
        public float Fit(float slotHeight)
        {
            var text = _fullDescription;
            var height = Measure(text);
            if (height <= slotHeight)
                return height;

            // One proportional cut to get close, then walk down a word at a time.
            text = TrimToLength(text, (int) (text.Length * (slotHeight / height)));
            height = Measure(WithEllipsis(text));

            while (height > slotHeight && text.Length > 0)
            {
                text = DropLastWord(text);
                height = Measure(WithEllipsis(text));
            }

            return MathF.Min(height, slotHeight);
        }

        private float Measure(string text)
        {
            _description.SetMessage(SubText(text));
            _description.Visible = text.Length > 0;
            Control.InvalidateMeasure();
            Control.Measure(new Vector2(CardMeasureWidth, float.PositiveInfinity));
            return Control.DesiredSize.Y;
        }

        private static string DropLastWord(string text)
        {
            var cut = text.LastIndexOf(' ');
            return cut <= 0 ? string.Empty : Clean(text.Substring(0, cut));
        }

        private static string TrimToLength(string text, int length)
        {
            if (length >= text.Length)
                return text;

            var cut = text.LastIndexOf(' ', Math.Max(0, Math.Min(length, text.Length - 1)));
            return cut <= 0 ? string.Empty : Clean(text.Substring(0, cut));
        }

        private static string WithEllipsis(string text)
        {
            return text.Length == 0 ? string.Empty : text + "...";
        }

        private static string Clean(string text)
        {
            return text.TrimEnd(' ', ',', ';', '-', '.');
        }
    }
}
