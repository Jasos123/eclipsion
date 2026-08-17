using Content.Shared.CartridgeLoader;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared.Cargo.Cartridges;

[Serializable, NetSerializable]
public sealed class StockMarketRequestPricesMsg : CartridgeMessageEvent
{
}

[Serializable, NetSerializable]
public sealed class StockMarketPricesResponseMsg : EntityEventArgs
{
    public Dictionary<string, StockPriceData> Prices { get; init; } = new();
    public Dictionary<string, int> Portfolio { get; init; } = new();
}

[Serializable, NetSerializable]
public sealed class StockMarketBuyMsg : CartridgeMessageEvent
{
    public string CompanyId { get; init; } = string.Empty;
    public int Amount { get; init; } = 1;
}

[Serializable, NetSerializable]
public sealed class StockMarketSellMsg : CartridgeMessageEvent
{
    public string CompanyId { get; init; } = string.Empty;
    public int Amount { get; init; } = 1;
}

[Serializable, NetSerializable]
public sealed class StockMarketTransactionMsg : EntityEventArgs
{
    public bool Success { get; init; }
    public string CompanyId { get; init; } = string.Empty;
    public int Amount { get; init; }
    public double TotalCost { get; init; }
    public bool IsBuy { get; init; }
}

[Serializable, NetSerializable]
public sealed class StockMarketErrorMsg : EntityEventArgs
{
    public string Message { get; init; } = string.Empty;
}

[Serializable, NetSerializable]
public record struct StockPriceData(
    string CompanyId,
    double BasePrice,
    double CurrentPrice,
    float Multiplier,
    float PriceChange = 0f,
    List<float>? PriceHistory = null,
    // Movement already committed to this company but not yet paid out, as a fraction of its value.
    // Quoted to traders because it is not a forecast: it is going to land whatever anyone does.
    float PendingShift = 0f,
    // Ticks the longest-running committed pressure still has to run.
    int PendingTicks = 0,
    // Whether this company drifts on its own, as opposed to only moving on war events.
    bool Neutral = false
);

/// <summary>One line of the market news feed, explaining why a price moved.</summary>
[Serializable, NetSerializable]
public record struct StockNewsRecord(
    string CompanyId,
    string Reason,
    bool Positive,
    TimeSpan Time,
    // How big the move was, as a fraction of the company's value.
    float Magnitude = 0f
);

[Serializable, NetSerializable]
public record struct StockTradeRecord(
    string CompanyId,
    int Amount,
    double PricePerShare,
    bool IsBuy,
    TimeSpan Time
);
