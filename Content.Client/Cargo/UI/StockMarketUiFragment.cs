using System.Linq;
using Content.Shared.Cargo.Cartridges;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Client.Cargo.UI;

public sealed partial class StockMarketUiFragment : BoxContainer
{
    private static readonly Color UpColor = StockPriceChart.UpColor;
    private static readonly Color DownColor = StockPriceChart.DownColor;
    private static readonly Color NeutralColor = StockPriceChart.NeutralColor;

    /// <summary>Dimmer than the body text, for the explanatory lines that sit under a figure.</summary>
    private static readonly Color HintColor = Color.FromHex("#7d848c");

    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();

    private readonly Label _balanceLabel;
    private readonly OptionButton _amountSelector;
    private readonly TabContainer _tabs;
    private readonly BoxContainer _marketList;
    private readonly BoxContainer _portfolioList;
    private readonly BoxContainer _historyList;
    private readonly BoxContainer _newsList;
    private readonly Label _toastLabel;

    private const int AllAmount = -1;

    private static readonly int[] TradeAmounts = { 1, 5, 10, 25, AllAmount };
    private int _tradeAmount = 1;
    private int _lastHistoryCount = -1;
    private float _toastTimer;
    private StockMarketUiState? _lastState;

    /// <summary>
    /// The written explanation of how the market works, as heading/body pairs. It exists because every
    /// figure on the market screen is answering a different question and no amount of labelling makes
    /// that obvious in the width of a PDA: players were reading the lifetime figure as a trend, and
    /// concluding the whole market was random noise.
    /// </summary>
    private static readonly (string Title, string Body)[] GuideSections =
    {
        ("stock-guide-basics-title", "stock-guide-basics-body"),
        ("stock-guide-row-title", "stock-guide-row-body"),
        ("stock-guide-faction-title", "stock-guide-faction-body"),
        ("stock-guide-neutral-title", "stock-guide-neutral-body"),
        ("stock-guide-asymmetry-title", "stock-guide-asymmetry-body"),
        ("stock-guide-persistence-title", "stock-guide-persistence-body"),
        ("stock-guide-liquidation-title", "stock-guide-liquidation-body"),
    };

    public Action<string, int>? OnBuyPressed;
    public Action<string, int>? OnSellPressed;

    public StockMarketUiFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(4);

        var header = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        header.AddChild(new Label
        {
            Text = Loc.GetString("stock-market-app-title"),
            StyleClasses = { "LabelHeading" },
            HorizontalExpand = true,
        });

        _balanceLabel = new Label
        {
            Text = Loc.GetString("stock-market-balance", ("balance", "—")),
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Center,
        };
        header.AddChild(_balanceLabel);
        AddChild(header);

        var controlsRow = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 2),
        };

        controlsRow.AddChild(new Label
        {
            Text = Loc.GetString("stock-market-qty"),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });

        _amountSelector = new OptionButton();
        for (var i = 0; i < TradeAmounts.Length; i++)
        {
            var label = TradeAmounts[i] == AllAmount
                ? Loc.GetString("stock-market-qty-all")
                : $"x{TradeAmounts[i]}";
            _amountSelector.AddItem(label, i);
        }
        _amountSelector.OnItemSelected += args =>
        {
            _amountSelector.SelectId(args.Id);
            _tradeAmount = TradeAmounts[args.Id];

            if (_lastState != null)
                UpdateMarketTab(_lastState);
        };
        controlsRow.AddChild(_amountSelector);
        AddChild(controlsRow);

        _tabs = new TabContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _marketList = MakeTab(out var marketScroll);
        _portfolioList = MakeTab(out var portfolioScroll);
        _historyList = MakeTab(out var historyScroll);
        _newsList = MakeTab(out var newsScroll);

        // The only tab whose content is prose. It has to wrap to the panel, which means giving up
        // horizontal scrolling; the other tabs keep it, since their rows are figures that must not
        // be silently clipped on a narrow PDA.
        var guideList = MakeTab(out var guideScroll, wrapContent: true);

        _tabs.AddChild(marketScroll);
        _tabs.AddChild(portfolioScroll);
        _tabs.AddChild(historyScroll);
        _tabs.AddChild(newsScroll);
        _tabs.AddChild(guideScroll);

        _tabs.SetTabTitle(0, Loc.GetString("stock-market-tab-market"));
        _tabs.SetTabTitle(1, Loc.GetString("stock-market-tab-portfolio"));
        _tabs.SetTabTitle(2, Loc.GetString("stock-market-tab-history"));
        _tabs.SetTabTitle(3, Loc.GetString("stock-market-tab-news"));
        _tabs.SetTabTitle(4, Loc.GetString("stock-market-tab-guide"));
        AddChild(_tabs);

        // The guide never changes, so it is filled once rather than rebuilt on every price update.
        BuildGuideTab(guideList);

        _toastLabel = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            Margin = new Thickness(0, 2),
            Visible = false,
        };
        AddChild(_toastLabel);
    }

    private static BoxContainer MakeTab(out ScrollContainer scroll, bool wrapContent = false)
    {
        var list = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };

        scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = !wrapContent,
        };
        scroll.AddChild(list);
        return list;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_toastLabel.Visible)
            return;

        _toastTimer -= args.DeltaSeconds;
        if (_toastTimer <= 0f)
            _toastLabel.Visible = false;
    }

    private void ShowToast(string text, Color color)
    {
        _toastLabel.Text = text;
        _toastLabel.FontColorOverride = color;
        _toastLabel.Visible = true;
        _toastTimer = 4f;
    }

    public void UpdateState(StockMarketUiState state)
    {
        _lastState = state;

        _balanceLabel.Text = state.Balance is { } balance
            ? Loc.GetString("stock-market-balance", ("balance", $"{balance}cr"))
            : Loc.GetString("stock-market-balance", ("balance", "—"));

        UpdateMarketTab(state);
        UpdatePortfolioTab(state);
        UpdateHistoryTab(state);
        UpdateNewsTab(state);

        if (_lastHistoryCount >= 0 && state.History.Count > _lastHistoryCount)
        {
            var trade = state.History[^1];
            var name = Loc.GetString(trade.CompanyId);
            var key = trade.IsBuy ? "stock-market-toast-bought" : "stock-market-toast-sold";
            var total = trade.Amount * trade.PricePerShare;
            ShowToast(
                Loc.GetString(key, ("amount", trade.Amount), ("company", name), ("total", $"{total:F0}")),
                trade.IsBuy ? UpColor : DownColor);
        }
        _lastHistoryCount = state.History.Count;
    }

    private void UpdateMarketTab(StockMarketUiState state)
    {
        _marketList.RemoveAllChildren();

        if (state.Prices.Count == 0)
        {
            _marketList.AddChild(new Label
            {
                Text = Loc.GetString("economic-console-no-data"),
                HorizontalAlignment = HAlignment.Center,
                Margin = new Thickness(0, 8),
            });
            return;
        }

        var stocks = state.Prices
            .OrderBy(p => Loc.GetString(p.Key))
            .ToList();

        foreach (var (id, price) in stocks)
        {
            _marketList.AddChild(CreateStockRow(id, price, state));
        }
    }

    private Control CreateStockRow(string id, StockPriceData price, StockMarketUiState state)
    {
        var owned = state.Portfolio.GetValueOrDefault(id, 0);

        var container = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(0, 2),
        };

        var infoRow = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        var displayName = Loc.GetString(id);
        infoRow.AddChild(new Label
        {
            Text = TruncateString(displayName, 20),
            HorizontalExpand = true,
            StyleClasses = { "LabelHeading" },
            MouseFilter = MouseFilterMode.Stop,
            ToolTip = Loc.GetString(price.Neutral
                ? "stock-market-kind-neutral-tooltip"
                : "stock-market-kind-faction-tooltip"),
        });

        infoRow.AddChild(new Label
        {
            Text = Loc.GetString("stock-market-price-now", ("price", $"{price.CurrentPrice:F0}")),
            HorizontalAlignment = HAlignment.Right,
            MouseFilter = MouseFilterMode.Stop,
            ToolTip = Loc.GetString("stock-market-price-now-tooltip"),
        });
        container.AddChild(infoRow);

        // Worked out once and then used for both the chart line and the figure printed under it, so the
        // picture and the number can never disagree about which way the price is going.
        var trend = RecentTrend(price.PriceHistory, StockMarketTrading.TrendTicks);

        if (price.PriceHistory is { Count: > 1 } history)
        {
            var chart = new StockPriceChart
            {
                HorizontalExpand = true,
                MinHeight = 42,
                Margin = new Thickness(0, 2),
                MouseFilter = MouseFilterMode.Stop,
                ToolTip = Loc.GetString("stock-market-chart-tooltip",
                    ("minutes", Math.Max(1, (int) MathF.Round(history.Count * StockMarketTrading.TickSeconds / 60f))),
                    ("open", $"{price.BasePrice:F0}")),
            };
            chart.SetData(history, (float) price.BasePrice, trend is { } t ? ChangeColor(t.Change) : null);
            container.AddChild(chart);
        }

        container.AddChild(CreateTrendRow(price, trend));

        // Only shown when something is actually running. A permanently visible "nothing incoming" line
        // would train people to stop reading the one row that tells them a move is already locked in.
        if (MathF.Abs(price.PendingShift) > 0.0005f && price.PendingTicks > 0)
            container.AddChild(CreatePendingRow(price));

        var actionRow = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        var ownedText = owned > 0
            ? Loc.GetString("stock-market-owned-worth",
                ("shares", owned),
                ("value", $"{owned * price.CurrentPrice:F0}"))
            : Loc.GetString("stock-market-owned") + ": 0";

        actionRow.AddChild(new Label
        {
            Text = ownedText,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        });

        var sellAmount = _tradeAmount;
        var buyAmount = _tradeAmount;
        if (_tradeAmount == AllAmount)
        {
            sellAmount = Math.Min(owned, StockMarketTrading.MaxStockAmount);

            var perShare = price.CurrentPrice;
            buyAmount = state.Balance is { } b && perShare > 0
                ? (int) Math.Clamp(Math.Floor(b / perShare), 0, StockMarketTrading.MaxStockAmount)
                : 0;
        }

        var proceeds = price.CurrentPrice * sellAmount;
        var sellButton = new Button
        {
            Text = Loc.GetString("stock-market-sell-btn"),
            MinWidth = 50,
            Disabled = sellAmount <= 0 || owned < sellAmount,
            ToolTip = Loc.GetString("stock-market-sell-tooltip",
                ("amount", sellAmount),
                ("total", $"{proceeds:F0}")),
        };
        var sellCapture = sellAmount;
        sellButton.OnPressed += _ => OnSellPressed?.Invoke(id, sellCapture);

        var cost = price.CurrentPrice * buyAmount;
        var buyButton = new Button
        {
            Text = Loc.GetString("stock-market-buy-btn"),
            MinWidth = 50,
            Disabled = buyAmount <= 0 || (state.Balance is { } bal && bal < cost),
            ToolTip = Loc.GetString("stock-market-buy-tooltip",
                ("amount", buyAmount),
                ("total", $"{cost:F0}")),
        };
        var buyCapture = buyAmount;
        buyButton.OnPressed += _ => OnBuyPressed?.Invoke(id, buyCapture);

        actionRow.AddChild(sellButton);
        actionRow.AddChild(buyButton);
        container.AddChild(actionRow);

        container.AddChild(new PanelContainer
        {
            MinHeight = 1,
            Margin = new Thickness(0, 4),
            StyleClasses = { "LowDivider" },
        });

        return container;
    }

    /// <summary>
    /// The two figures players kept confusing for each other, now side by side and named.
    ///
    /// On the left is the trend: how the price has moved recently, which is what someone deciding
    /// whether to buy actually wants. On the right is the lifetime figure against the company's opening
    /// price, which never resets — that is the one that used to carry the arrow, so a share that was
    /// climbing all shift still displayed a red ▼ and looked broken.
    /// </summary>
    private static Control CreateTrendRow(StockPriceData price, (float Change, int Ticks)? trend)
    {
        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        // Quoted over however much history there actually is, not over the window that was asked for.
        // A market that has only been running two minutes must not label a two-minute move "5m".
        var window = FormatDuration(
            (trend?.Ticks ?? StockMarketTrading.TrendTicks) * StockMarketTrading.TickSeconds);

        row.AddChild(new Label
        {
            Text = trend is { } value
                ? Loc.GetString("stock-market-trend-row", ("window", window), ("change", SignedPercent(value.Change)))
                : Loc.GetString("stock-market-trend-row-unknown", ("window", window)),
            HorizontalExpand = true,
            FontColorOverride = trend is { } t ? ChangeColor(t.Change) : NeutralColor,
            MouseFilter = MouseFilterMode.Stop,
            ToolTip = Loc.GetString("stock-market-trend-tooltip", ("window", window)),
        });

        row.AddChild(new Label
        {
            Text = Loc.GetString("stock-market-vs-open-row", ("change", SignedPercent(price.PriceChange))),
            HorizontalAlignment = HAlignment.Right,
            FontColorOverride = ChangeColor(price.PriceChange),
            MouseFilter = MouseFilterMode.Stop,
            ToolTip = Loc.GetString("stock-market-vs-open-tooltip", ("open", $"{price.BasePrice:F0}")),
        });

        return row;
    }

    /// <summary>
    /// Movement the round has already bought and paid for but that has not finished arriving. This is
    /// the answer to "how much will it keep going down, and for how long" — it is not a prediction, and
    /// nothing a trader does will call it off.
    /// </summary>
    private static Control CreatePendingRow(StockPriceData price)
    {
        var remaining = price.PendingTicks * StockMarketTrading.TickSeconds;

        return new Label
        {
            Text = Loc.GetString("stock-market-pending-row",
                ("change", SignedPercent(price.PendingShift)),
                ("time", FormatDuration(remaining))),
            HorizontalExpand = true,
            FontColorOverride = ChangeColor(price.PendingShift),
            MouseFilter = MouseFilterMode.Stop,
            ToolTip = Loc.GetString("stock-market-pending-tooltip"),
        };
    }

    private void UpdatePortfolioTab(StockMarketUiState state)
    {
        _portfolioList.RemoveAllChildren();

        var holdings = state.Portfolio
            .Where(p => p.Value > 0)
            .OrderBy(p => Loc.GetString(p.Key))
            .ToList();

        if (holdings.Count == 0)
        {
            _portfolioList.AddChild(new Label
            {
                Text = Loc.GetString("stock-market-no-holdings"),
                HorizontalAlignment = HAlignment.Center,
                Margin = new Thickness(0, 8),
            });
            return;
        }

        var totalValue = 0.0;
        foreach (var (id, shares) in holdings)
        {
            if (state.Prices.TryGetValue(id, out var price))
                totalValue += shares * price.CurrentPrice;
        }

        _portfolioList.AddChild(new Label
        {
            Text = Loc.GetString("stock-market-total-value", ("value", $"{totalValue:F0}")),
            StyleClasses = { "LabelHeading" },
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var (id, shares) in holdings)
        {
            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new Thickness(0, 1),
            };

            row.AddChild(new Label
            {
                Text = TruncateString(Loc.GetString(id), 20),
                HorizontalExpand = true,
            });

            // A holding with no quote belongs to a faction that is not fielded this round. It is still
            // owned and still carries over, it just cannot be traded until that faction is back.
            var valueText = state.Prices.TryGetValue(id, out var price)
                ? $"{shares} × {price.CurrentPrice:F0}cr = {shares * price.CurrentPrice:F0}cr"
                : Loc.GetString("stock-market-delisted", ("shares", shares));

            row.AddChild(new Label
            {
                Text = valueText,
                HorizontalAlignment = HAlignment.Right,
                MouseFilter = MouseFilterMode.Stop,
                ToolTip = state.Prices.ContainsKey(id)
                    ? null
                    : Loc.GetString("stock-market-delisted-tooltip"),
            });

            _portfolioList.AddChild(row);
        }
    }

    private void UpdateHistoryTab(StockMarketUiState state)
    {
        _historyList.RemoveAllChildren();

        if (state.History.Count == 0)
        {
            _historyList.AddChild(new Label
            {
                Text = Loc.GetString("stock-market-no-trades"),
                HorizontalAlignment = HAlignment.Center,
                Margin = new Thickness(0, 8),
            });
            return;
        }

        for (var i = state.History.Count - 1; i >= 0; i--)
        {
            var trade = state.History[i];

            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new Thickness(0, 1),
            };

            var verb = Loc.GetString(trade.IsBuy ? "stock-market-history-buy" : "stock-market-history-sell");
            row.AddChild(new Label
            {
                Text = $"[{trade.Time:hh\\:mm}] {verb} {trade.Amount} {TruncateString(Loc.GetString(trade.CompanyId), 14)}",
                HorizontalExpand = true,
                FontColorOverride = trade.IsBuy ? UpColor : DownColor,
            });

            row.AddChild(new Label
            {
                Text = $"@{trade.PricePerShare:F0}cr",
                HorizontalAlignment = HAlignment.Right,
            });

            _historyList.AddChild(row);
        }
    }

    /// <summary>
    /// The feed that tells a trader *why* a price moved. Without it the faction stocks look like
    /// random noise, when in fact they only ever move because of something that happened in the round.
    ///
    /// Each entry carries the size of the move and how long ago it landed, because "DSM treasury
    /// draining" on its own does not tell anyone whether to sell — the same headline is worth 0.1% or
    /// 4% depending on what caused it.
    /// </summary>
    private void UpdateNewsTab(StockMarketUiState state)
    {
        _newsList.RemoveAllChildren();

        if (state.News.Count == 0)
        {
            _newsList.AddChild(new Label
            {
                Text = Loc.GetString("stock-market-no-news"),
                HorizontalAlignment = HAlignment.Center,
                Margin = new Thickness(0, 8),
            });
            return;
        }

        _newsList.AddChild(new Label
        {
            Text = Loc.GetString("stock-market-news-header"),
            FontColorOverride = HintColor,
            Margin = new Thickness(0, 0, 0, 4),
        });

        for (var i = state.News.Count - 1; i >= 0; i--)
        {
            var entry = state.News[i];

            var block = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalExpand = true,
                Margin = new Thickness(0, 1),
            };

            var titleRow = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
            };

            titleRow.AddChild(new Label
            {
                Text = $"{(entry.Positive ? "▲" : "▼")} {TruncateString(Loc.GetString(entry.CompanyId), 18)}",
                HorizontalExpand = true,
                FontColorOverride = entry.Positive ? UpColor : DownColor,
            });

            // A magnitude of zero means the entry predates this field or came from an admin command,
            // in which case quoting "0.0%" would be a worse lie than saying nothing.
            if (MathF.Abs(entry.Magnitude) > 0.00005f)
            {
                titleRow.AddChild(new Label
                {
                    Text = SignedPercent(entry.Magnitude),
                    HorizontalAlignment = HAlignment.Right,
                    FontColorOverride = ChangeColor(entry.Magnitude),
                });
            }

            block.AddChild(titleRow);

            block.AddChild(new Label
            {
                Text = Loc.GetString("stock-market-news-detail",
                    ("reason", entry.Reason),
                    ("ago", FormatAgo(entry.Time))),
                FontColorOverride = HintColor,
                Margin = new Thickness(10, 0, 0, 2),
            });

            _newsList.AddChild(block);
        }
    }

    private static void BuildGuideTab(BoxContainer list)
    {
        foreach (var (titleKey, bodyKey) in GuideSections)
        {
            list.AddChild(new Label
            {
                Text = Loc.GetString(titleKey),
                StyleClasses = { "LabelHeading" },
                Margin = new Thickness(0, 6, 0, 2),
            });

            var body = new RichTextLabel
            {
                HorizontalExpand = true,
                Margin = new Thickness(0, 0, 0, 2),
            };

            // Plain text rather than markup: these strings are long, translated, and full of the "%"
            // and bracket characters that the markup parser throws on.
            body.SetMessage(Loc.GetString(bodyKey));
            list.AddChild(body);
        }
    }

    /// <summary>
    /// How the price has actually moved lately, which is what people mean by "trend".
    ///
    /// Deliberately not <see cref="StockPriceData.PriceChange"/>: that measures the price against what
    /// the company opened at and never resets, so a share can be climbing hard and still read negative.
    ///
    /// Returns the ticks it actually managed to span alongside the change, so the caller can label the
    /// figure with the window it really covers rather than the one it asked for.
    /// </summary>
    private static (float Change, int Ticks)? RecentTrend(IReadOnlyList<float>? history, int ticks)
    {
        if (history is not { Count: > 1 })
            return null;

        var span = Math.Min(ticks, history.Count - 1);
        var from = history[history.Count - 1 - span];
        if (from <= 0f)
            return null;

        return ((history[^1] - from) / from, span);
    }

    private static Color ChangeColor(float change)
    {
        return change > 0.00005f ? UpColor
            : change < -0.00005f ? DownColor
            : NeutralColor;
    }

    private static string SignedPercent(float change)
    {
        var arrow = change > 0.00005f ? "▲" : change < -0.00005f ? "▼" : "";
        return $"{arrow}{MathF.Abs(change) * 100:F1}%";
    }

    private static string FormatDuration(float seconds)
    {
        if (seconds < 60f)
            return Loc.GetString("stock-market-duration-seconds", ("value", Math.Max(1, (int) MathF.Round(seconds))));

        return Loc.GetString("stock-market-duration-minutes", ("value", Math.Max(1, (int) MathF.Round(seconds / 60f))));
    }

    private string FormatAgo(TimeSpan when)
    {
        // Server and client clocks are close but not identical, and a news entry that arrives a frame
        // early would otherwise read as having happened in the future.
        var elapsed = _timing.CurTime - when;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        return FormatDuration((float) elapsed.TotalSeconds);
    }

    private static string TruncateString(string str, int maxLength)
    {
        return str.Length <= maxLength ? str : str[..(maxLength - 2)] + "..";
    }
}
