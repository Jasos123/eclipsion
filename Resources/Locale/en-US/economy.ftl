ent-EconomicConsole = economic monitor
    .desc = A terminal for monitoring market prices and stock trends.
ent-StockMarketCartridge = stock market cartridge
    .desc = A cartridge for trading stocks and monitoring market trends.
cmd-viewprices-desc = View current dynamic prices for all tracked goods
cmd-viewprices-help = Usage: {$command} [goodId] - Shows all prices or specific good
cmd-viewprices-good-not-found = Good '{$goodId}' not found in dynamic pricing system.
cmd-viewprices-available-goods = Available goods:
cmd-viewprices-header === Dynamic Pricing Overview ===
cmd-viewprices-column-good-id = Good ID
cmd-viewprices-column-multiplier = Multiplier
cmd-viewprices-column-change = Change
cmd-viewprices-total = Total tracked goods: {$count}
cmd-forceupdateprices-desc = Force update all dynamic prices immediately
cmd-forceupdateprices-help = Usage: {$command} - Forces immediate price recalculation
cmd-forceupdateprices-success = All prices have been force updated.
cmd-resetprices-desc = Reset all dynamic prices to baseline (1.0 multiplier)
cmd-resetprices-help = Usage: {$command} - Resets all prices
cmd-resetprices-success = All prices have been reset to baseline.
cmd-viewshuttleprices-desc = View current shuttle prices
cmd-viewshuttleprices-help = Usage: {$command} - Shows all shuttle prices
cmd-viewshuttleprices-header === Shuttle Pricing Overview ===
cmd-viewshuttleprices-column-entity = Entity ID
cmd-viewshuttleprices-column-base = Base Price
cmd-viewshuttleprices-column-current = Current Price
cmd-viewshuttleprices-column-change = Change
cmd-viewshuttleprices-total = Total shuttles tracked: {$count}
cmd-viewshuttleprices-none = No shuttles with dynamic pricing found.
rat-economy-log-price-change = {$goodId} price changed by {$deviation ->
    [one] {$deviation}%
    *[other] {$deviation}%
} (multiplier: {$multiplier})
rat-economy-log-shuttle-price-change = Shuttle {$shuttleUid} price changed by {$percentChange ->
    [one] {$percentChange}%
    *[other] {$percentChange}%
} to {$currentPrice} credits
price-monitor-window-title = Economic Monitor
price-monitor-tab-prices = Trade Goods
price-monitor-tab-shuttles = Shuttles
price-monitor-refresh = Refresh
price-monitor-auto-update = Auto Update
price-monitor-good-id = Good ID
price-monitor-base-price = Base Price
price-monitor-current-price = Current Price
price-monitor-multiplier = Multiplier
price-monitor-change = Change
price-monitor-shuttle-name = Shuttle
cmd-setmarkettrend-desc = Set market trend direction for specific goods
cmd-setmarkettrend-help = Usage: {$command} <goodId> <bullish|bearish|volatile|stable> [strength] [duration]
cmd-setmarkettrend-invalid-direction = Invalid trend direction. Valid: bullish, bearish, volatile, stable
cmd-setmarkettrend-valid-directions = Valid directions: bullish (prices up), bearish (prices down), volatile (high fluctuation), stable (low fluctuation)
cmd-setmarkettrend-invalid-strength = Invalid strength value. Must be a number (0.1-10.0)
cmd-setmarkettrend-invalid-duration = Invalid duration value. Must be seconds (60-3600)
cmd-setmarkettrend-success = Set market trend for {$goodId}: {$direction} (strength: {$strength}, duration: {$duration}s)
cmd-crashmarket-desc = Crash the market (force all prices down)
cmd-crashmarket-executed = Market crashed! All prices decreased significantly.
cmd-volatilemarket-desc = Make market highly volatile
cmd-volatilemarket-executed = Market is now highly volatile!
cmd-stabilizemarket-desc = Stabilize the market (reset all prices)
cmd-stabilizemarket-executed = Market stabilized. All prices reset to baseline.
stock-market-buy = Buy
stock-market-sell = Sell
stock-market-price = Price
stock-market-shares = Shares
stock-market-value = Value
stock-market-trend = Trend
stock-market-buy-success = Bought {$amount} shares of {$company} for {$cost} credits
stock-market-buy-fail = Insufficient funds to buy shares
stock-market-sell-success = Sold {$amount} shares of {$company} for {$profit} credits
stock-market-sell-fail = You don't have enough shares to sell
economic-console-title = Economic Monitor Console
economic-console-loading = Loading market data...
economic-console-no-data = No market data available
economic-console-status = Tracking {$goods} goods and {$stocks} shuttles
economic-console-status-count = Tracking: {$count}
economic-console-search = Search
economic-console-search-placeholder = Filter...
economic-console-no-results = No items match your search
economic-console-more-items = ... and {$count} more items
economic-console-prices = Market Prices
economic-console-items = Items
economic-console-shuttles = Shuttles
economic-console-stocks = Stock Market
economic-console-admin = Admin Controls
economic-console-refresh = Refresh
economic-console-auto-update = Auto-Update
economic-console-trend-up = UP
economic-console-trend-down = DOWN
economic-console-trend-stable = STABLE
economic-console-trend-volatile = VOLATILE
stock-market-cartridge-name = Stock Market
stock-market-cartridge-desc = Trade stocks and monitor market trends
stock-market-app-title = Stock Market
stock-market-portfolio = Portfolio
stock-market-available = Stocks
stock-market-owned = Owned
stock-market-amount = Amount
stock-market-total = Total
stock-market-buy-btn = Buy
stock-market-sell-btn = Sell
stock-market-refresh = Refresh
stock-market-no-portfolio = You don't own any stocks yet
stock-market-price-up = [color=lime]UP[/color]
stock-market-price-down = [color=red]DOWN[/color]
stock-market-balance = Balance: {$balance}
stock-market-qty = Qty:
stock-market-qty-all = All
stock-market-tab-market = Market
stock-market-tab-portfolio = Portfolio
stock-market-tab-history = History
stock-market-total-value = Total value: {$value}cr
stock-market-no-holdings = You don't own any stocks yet
stock-market-no-trades = No trades this shift
stock-market-history-buy = Bought
stock-market-history-sell = Sold
stock-market-toast-bought = Bought {$amount} × {$company} for {$total}cr
stock-market-toast-sold = Sold {$amount} × {$company} for {$total}cr
stock-company-shi = SHI
stock-company-tfsc = TFCF
stock-company-dsm = Olywier Charter Holdings
stock-company-ncwl = Potato Trade Corp
stock-company-fmn = Free Merchant Nobles
stock-company-tccc = Taypan Civil Construction Company

stock-market-tab-news = News
stock-market-delisted = {$shares} — delisted this shift
stock-market-no-news = The market is quiet

# The news feed already prints the company name on the line above the reason, so the reason must not
# repeat it. It used to name the raw faction tag ("DSM treasury draining") under a company headed
# "Olywier Charter Holdings", which read as two unrelated things happening at once.
stock-news-casualties = Taking casualties
stock-news-merchant-war = Wartime demand up
stock-news-station-lost = {$station} lost
stock-news-rebuild = Rebuild contracts for {$station}
stock-news-victory = Won the war
stock-news-contract = Contract fulfilled
stock-news-treasury-up = Treasury growing
stock-news-treasury-down = Treasury draining
stock-news-admin = Unexplained market movement

stock-war-liquidation = {$company} has gone to war with your faction and closed your position. Your {$shares} shares were sold at the floor price of {$price}cr and {$total}cr has been transferred to your account.

# --- Market screen: the figures, and what each one is actually measuring ---
stock-market-price-now = {$price}cr
stock-market-price-now-tooltip = What one share costs right now, to buy or to sell.
stock-market-trend-row = {$window} {$change}
stock-market-trend-row-unknown = {$window} —
stock-market-trend-tooltip = The trend: how this share has moved over the last {$window}. This is the figure that tells you which way the price is going right now.
stock-market-vs-open-row = open {$change}
stock-market-vs-open-tooltip = Where the price stands against the {$open}cr this company first opened at, counted across every shift it has ever traded. This is NOT a trend — a share can be climbing hard all shift and still read below its opening price.
stock-market-pending-row = {$change} locked in, {$time} left
stock-market-pending-tooltip = A move that events in the round have already bought and that is still being paid out, tick by tick. It is not a forecast and nothing you do will call it off — it is going to land.
stock-market-chart-tooltip = The last {$minutes} minutes of price. The flat line is the {$open}cr this company opened at, and the plotted line is coloured by the trend figure below it.
stock-market-kind-faction-tooltip = A faction company. It has no randomness at all: it moves only when something happens to that faction in the round. Every move has a line in the News tab.
stock-market-kind-neutral-tooltip = A neutral company. It drifts a little on its own and pulls back toward its opening price, and it profits from the chaos the factions generate — somewhere to hide when every faction is sinking.
stock-market-owned-worth = Owned: {$shares} ({$value}cr)
stock-market-buy-tooltip = Buy {$amount} shares for {$total}cr.
stock-market-sell-tooltip = Sell {$amount} shares for {$total}cr.
stock-market-delisted-tooltip = This faction is not fielded this shift, so its company is not trading. You still own the shares and they carry over — you just cannot sell them until it is back.
stock-market-duration-seconds = {$value}s
stock-market-duration-minutes = {$value}m

stock-market-news-header = Why prices moved:
stock-market-news-detail = {$reason} · {$ago} ago

# --- Guide tab ---
stock-market-tab-guide = Guide
stock-guide-basics-title = How it works
stock-guide-basics-body =
    Prices update every 15 seconds.

    The listed companies split one fixed pool of market value between them. A company can only rise by taking value from the others, so the whole market can never go up at once. If something is climbing, something else is paying for it.
stock-guide-row-title = Reading a row
stock-guide-row-body =
    327cr — what one share costs right now.

    5m ▲2.1% — the trend. How the price moved over the last five minutes. This is the number to trade on.

    open ▼6.5% — where the price stands against what the company first opened at, ever. It does not reset between shifts. It is not a trend: a share climbing hard all shift can still read open ▼6.5%.

    "locked in" — a move that events in the round have already bought and that has not finished arriving yet. It is not a guess. It will land, and the line tells you how long it has left to run.
stock-guide-faction-title = Faction companies
stock-guide-faction-body =
    SHI, TFCF, Olywier Charter Holdings and Potato Trade Corp have no randomness whatsoever. They never move on their own.

    They move only when something happens to that faction: its people dying, one of its stations lost, its treasury rising or draining, a contract fulfilled, the war won. Every single one of those writes a line in the News tab, so you can always find out exactly why a price moved.

    A faction that is not fielded this shift is delisted. Its price freezes, it cannot be traded, and it does not recover.
stock-guide-neutral-title = Neutral companies
stock-guide-neutral-body =
    Free Merchant Nobles and Taypan Civil Construction do drift on their own, and they pull back toward their opening price over time.

    They also feed on the war: the Merchants gain from casualties, Taypan from stations needing rebuilt. They are where you hide when every faction is sinking at once.
stock-guide-asymmetry-title = Rising is slow, falling is fast
stock-guide-asymmetry-body =
    Every upward push lands at 70% of its strength and every downward one at 120%. The same size of event moves a price further down than it does up.

    Holding a winning position is work, not a coast.
stock-guide-persistence-title = Prices carry over
stock-guide-persistence-body =
    The market does not reset when the round does. A faction that spent three shifts losing opens the fourth cheap.

    A company that traded this shift closes 20% of the gap back toward its opening price at the round change. A delisted one does not — it comes back at exactly the price it crashed to. You recover by playing.
stock-guide-liquidation-title = War liquidation
stock-guide-liquidation-body =
    If you are holding shares in a company whose faction goes to war with yours, your position is closed for you at the floor price — 25% of what the company opened at.

    That is well below what the shares are worth on the open market. Being liquidated is a loss, not an exit.

cmd-stockmarket-desc = Inspect and control the sector stock market.
cmd-stockmarket-help = Usage: {$command} <list | reset | freeze | clearportfolios | shock <company> <percent> [ticks]>
cmd-stockmarket-reset = Market reset to opening shares.
cmd-stockmarket-frozen = Market frozen. Prices will not move until unfrozen.
cmd-stockmarket-unfrozen = Market running again.
cmd-stockmarket-portfolios-cleared = All stored player holdings cleared.
cmd-stockmarket-unknown-company = No such company: {$id}
cmd-stockmarket-hint-subcommand = <subcommand>
cmd-stockmarket-hint-company = <company id>
