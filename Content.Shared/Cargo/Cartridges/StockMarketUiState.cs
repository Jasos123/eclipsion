using Content.Shared.CartridgeLoader;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared.Cargo.Cartridges;

public static class StockMarketTrading
{
    public const int MaxStockAmount = 100000;

    /// <summary>
    /// Seconds between market ticks. Shared rather than server-only so the trading screen can quote a
    /// pending pressure as "45s left" instead of "3 ticks left", which means nothing to a player.
    /// </summary>
    public const float TickSeconds = 15f;

    /// <summary>
    /// How far back the screen looks when it quotes a stock's trend. Five minutes is long enough that a
    /// single tick of noise does not flip the arrow, and short enough to still read as "right now".
    /// </summary>
    public const int TrendTicks = 20;
}

[Serializable, NetSerializable]
public sealed class StockMarketUiState : BoundUserInterfaceState
{
    public Dictionary<string, StockPriceData> Prices { get; init; }
    public Dictionary<string, int> Portfolio { get; init; }

    public long? Balance { get; init; }

    public List<StockTradeRecord> History { get; init; }

    /// <summary>Most recent market movers, newest last. Explains why prices moved.</summary>
    public List<StockNewsRecord> News { get; init; }

    public StockMarketUiState(
        Dictionary<string, StockPriceData> prices,
        Dictionary<string, int> portfolio,
        long? balance = null,
        List<StockTradeRecord>? history = null,
        List<StockNewsRecord>? news = null)
    {
        Prices = prices;
        Portfolio = portfolio;
        Balance = balance;
        History = history ?? new List<StockTradeRecord>();
        News = news ?? new List<StockNewsRecord>();
    }
}

[Serializable, NetSerializable]
public sealed class StockMarketUiMessageEvent : CartridgeMessageEvent
{
    public StockMarketUiAction Action { get; init; }
    public string CompanyId { get; init; }
    public int Amount { get; init; }

    public StockMarketUiMessageEvent(StockMarketUiAction action, string companyId, int amount)
    {
        Action = action;
        CompanyId = companyId;
        Amount = amount;
    }
}

[Serializable, NetSerializable]
public enum StockMarketUiAction
{
    RequestPrices,
    Buy,
    Sell
}
