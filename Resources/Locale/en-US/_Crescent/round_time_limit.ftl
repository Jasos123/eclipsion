# Timed announcements fired by MaxTimeRestartRule on the way to the 4h cap
# (Prototypes/_Crescent/GameRules/roundtimelimit.yml).
#
# The round ending is a Turning in-universe — a reversion front crossing the sector — so the warning is written as
# a forecast advisory rather than an OOC "round ends soon". See Guidebook/LoreRecurrence for what the Turning is:
# forecasting services can see a dangerous window coming, but not what specifically it takes.

round-time-recurrence-sender = Sector Command

round-time-recurrence-warning =
    RECURRENCE ADVISORY. Forecast stations across the basin are in agreement: a reversion front is closing on Taypan and will cross the sector in roughly { $minutes } minutes. Confidence on the arrival is good. Confidence on what it takes is not.
    {""}
    All vessels and installations: close out your contracts, get cargo into anchored holds, and stop leaving anything you intend to keep in exposed space. Seams, hulls, wetware and improvised yards are all fair game to the front.
    {""}
    Finish what you came here to do. Sector Command will not issue a second advisory.
