using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared.Psionics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Psionics.UI;

/// <summary>
/// A research-console-inspired, card-based view of the player's psionic progression.
/// Nodes are arranged vertically by tier and horizontally by discipline.
/// </summary>
public sealed class PsionicSkillTreeWindow : DefaultWindow
{
    private static readonly Color OwnedColor = Color.FromHex("#2AB043");
    private static readonly Color AvailableColor = Color.FromHex("#FAB325");
    private static readonly Color LockedColor = Color.FromHex("#57575F");
    private static readonly Color ExcludedColor = Color.FromHex("#9F3344");

    private readonly IPrototypeManager _prototypeManager;
    private readonly SpriteSystem _spriteSystem;
    private readonly Label _levelLabel;
    private readonly Label _pointsLabel;
    private readonly Label _progressLabel;
    private readonly ProgressBar _progressBar;
    private readonly BoxContainer _branches;

    public event Action<string>? OnSkillSelected;

    public PsionicSkillTreeWindow()
    {
        _prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        _spriteSystem = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();

        Title = Loc.GetString("psionic-skill-tree-window-title");
        MinSize = new Vector2(760, 500);
        SetSize = new Vector2(1180, 720);

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
                ContentMarginTopOverride = 10,
                ContentMarginBottomOverride = 10,
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

        _progressLabel = new Label { HorizontalExpand = true };
        _progressBar = new ProgressBar
        {
            HorizontalExpand = true,
            MinHeight = 14,
        };

        header.AddChild(stats);
        header.AddChild(_progressLabel);
        header.AddChild(_progressBar);
        headerPanel.AddChild(header);
        root.AddChild(headerPanel);

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = true,
            VScrollEnabled = true,
            Margin = new Thickness(8, 4, 8, 8),
        };

        _branches = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        scroll.AddChild(_branches);
        root.AddChild(scroll);
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

        _branches.DisposeAllChildren();

        if (!_prototypeManager.TryIndex<PsionicSkillTreePrototype>(state.TreeId, out var tree))
            return;

        // Indexer rather than ToDictionary: a tree prototype that lists the same skill twice would
        // otherwise throw and take the whole window down.
        var stateById = new Dictionary<string, PsionicSkillNodeState>();
        foreach (var node in state.Skills)
            stateById[node.SkillId] = node;
        foreach (var branchId in tree.Branches)
        {
            if (!_prototypeManager.TryIndex(branchId, out var branch))
                continue;

            var skills = tree.Skills
                .Select(id => _prototypeManager.TryIndex(id, out PsionicSkillPrototype? skill) ? skill : null)
                .Where(skill => skill != null && skill.Branch == branchId && stateById.ContainsKey(skill.ID))
                .Cast<PsionicSkillPrototype>()
                .OrderBy(skill => skill.Tier)
                .ThenBy(skill => skill.ID)
                .ToList();

            if (skills.Count == 0)
                continue;

            _branches.AddChild(CreateBranch(branch, skills, stateById));
        }
    }

    private Control CreateBranch(
        PsionicSkillBranchPrototype branch,
        List<PsionicSkillPrototype> skills,
        Dictionary<string, PsionicSkillNodeState> states)
    {
        var columnPanel = new PanelContainer
        {
            MinWidth = 270,
            MaxWidth = 300,
            VerticalExpand = true,
            Margin = new Thickness(4),
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

        column.AddChild(new Label
        {
            Text = Loc.GetString(branch.Name),
            Align = Label.AlignMode.Center,
            StyleClasses = { StyleBase.StyleClassLabelHeading },
            ModulateSelfOverride = branch.Color,
        });

        var branchDescription = new RichTextLabel
        {
            Text = Loc.GetString(branch.Description),
            HorizontalExpand = true,
            Margin = new Thickness(2, 2, 2, 8),
        };
        column.AddChild(branchDescription);

        foreach (var skill in skills)
        {
            var nodeState = states[skill.ID];
            column.AddChild(CreateSkillCard(skill, nodeState));
        }

        columnPanel.AddChild(column);
        return columnPanel;
    }

    private Control CreateSkillCard(PsionicSkillPrototype skill, PsionicSkillNodeState state)
    {
        var statusColor = state.Availability switch
        {
            PsionicSkillAvailability.Owned => OwnedColor,
            PsionicSkillAvailability.Available => AvailableColor,
            PsionicSkillAvailability.Excluded => ExcludedColor,
            _ => LockedColor,
        };

        var button = new Button
        {
            HorizontalExpand = true,
            MinHeight = 104,
            Margin = new Thickness(0, 3),
            Disabled = state.Availability != PsionicSkillAvailability.Available,
            ModulateSelfOverride = statusColor,
        };
        button.StyleClasses.Add(StyleBase.ButtonSquare);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(5),
        };

        row.AddChild(new TextureRect
        {
            Texture = _spriteSystem.Frame0(skill.Icon),
            SetSize = new Vector2(48, 48),
            MinSize = new Vector2(48, 48),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Stretch = TextureRect.StretchMode.KeepCentered,
        });

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        // Labels are single-line controls, so longer localized power names were clipped by the
        // fixed-width branch cards. RichTextLabel wraps to the available width and lets the card
        // grow vertically when the title needs a second line.
        var nameLabel = new RichTextLabel
        {
            HorizontalExpand = true,
        };
        var name = FormattedMessage.EscapeText(Loc.GetString(skill.Name));
        nameLabel.SetMessage(FormattedMessage.FromMarkupOrThrow($"[bold][font size=16]{name}[/font][/bold]"));
        text.AddChild(nameLabel);
        text.AddChild(new RichTextLabel
        {
            Text = Loc.GetString(skill.Description),
            HorizontalExpand = true,
        });
        text.AddChild(new Label
        {
            Text = GetStatusText(skill, state),
            ModulateSelfOverride = statusColor,
        });

        row.AddChild(text);
        button.AddChild(row);
        button.OnPressed += _ => OnSkillSelected?.Invoke(skill.ID);

        var tooltip = new Tooltip();
        tooltip.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(skill.Description)));
        if (!string.IsNullOrWhiteSpace(state.Reason))
            tooltip.SetMessage(FormattedMessage.FromUnformatted($"{Loc.GetString(skill.Description)}\n\n{state.Reason}"));
        button.TooltipSupplier = _ => tooltip;

        return button;
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
}
