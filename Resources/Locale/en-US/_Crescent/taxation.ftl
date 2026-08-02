# Trade point sale popup (shown when a good is sold at a trade point / dispenser)
rat-station-trade-market = Sold for {$finalAmount} cr ({$pct}% market value) — tax {$taxPct}% ({$taxAmount} cr withheld).

# --- Taxation console ---
taxation-console-title = Trade Taxation Console
taxation-console-treasury = Faction treasury: {$balance} cr
taxation-console-access-granted = Authorized — you may adjust tax rates.
taxation-console-access-view = View only — a faction card is required to change rates.
taxation-console-access-denied = Access denied. A valid faction card is required.
taxation-console-default-label = Default tax rate:
taxation-console-goods-header = Trade goods
taxation-console-good-price = base {$price} cr
taxation-console-set = Set
taxation-console-clear = Reset

# --- Faction treasury console ---
treasury-console-title = Faction Treasury Vault
treasury-console-balance = {$balance} cr
treasury-console-amount-label = Amount:
treasury-console-withdraw = Withdraw
treasury-console-withdraw-all = Withdraw all
treasury-console-authorized = Authorized
treasury-console-unauthorized = Unauthorized — access the vault with a faction card
treasury-console-alarm = ⚠ SECURITY BREACH — VAULT COMPROMISED ⚠
treasury-console-withdrew = Withdrew {$amount} cr.
treasury-console-limit-reached = Withdrawal denied — you may only draw up to {$percent}% of the vault per round.
treasury-console-intrusion = Unauthorized access! Security alarm engaged.
treasury-console-robbery-started = Vault cracked — siphoning {$amount} cr ({$percent}% of the vault) over the next {$minutes} min. It stops on its own; work the console again once it's done to take another cut.
treasury-console-robbery-progress = Robbery already running — {$stolen} cr taken so far, {$remaining} cr still to come, ~{$minutes} min left.
treasury-console-robbery-empty = The vault is empty — nothing left to steal.
treasury-console-looted = The vault is being looted — {$amount} cr stolen!
treasury-console-deposited = Deposited {$amount} cr into the treasury.
treasury-console-not-command = The vault only answers to command. Your card is logged and rejected.
treasury-console-unpowered = The vault console is dead — no power, no locks to pick.
treasury-console-robbery-cut = The vault console loses power and the siphon dies with it — the robbery stops.

# Sector-wide intrusion broadcast (fired when someone without access tampers with the vault)
treasury-console-alarm-sender = Treasury Vault Security
treasury-console-alarm-unknown-station = an unknown holding
treasury-console-alarm-announcement = Security alert: unauthorized hands are at the {$station} treasury vault. A robbery is in progress — all command and security personnel, respond at once.

# Your own share of the vault, so "withdraw all" stops looking like it short-changed you.
treasury-console-remaining = Your allowance: { $remaining } cr ({ $percent }% share)
