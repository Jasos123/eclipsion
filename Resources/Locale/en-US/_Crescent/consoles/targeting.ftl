targeting-rejection-shuttle-console = You cannot use this and a shuttle console at the same time.

# KS14: instrument shell chrome for the targeting console. Shared chrome (the close glyph)
# lives in _KS14/instrument.ftl; these are the strings only this console uses.
targeting-console-window-title = TARGETING CONSOLE
targeting-console-panel-fire-control = FIRE CONTROL
targeting-console-panel-ammo = AMMO
targeting-console-panel-groups = GROUPS

# Always-on status strip: the panels above it, totalled.
targeting-console-status = GROUP { $group } // CANNONS { $cannons } // AMMO { $ammo }/{ $capacity } // SHIELD { $shield }
targeting-console-status-no-group = ---
targeting-console-status-shield-none = ---
targeting-console-status-shield-down = DOWN { $time }s
targeting-console-status-shield-up = { $percent }%
targeting-console-alert-dry = ! NO AMMO !
