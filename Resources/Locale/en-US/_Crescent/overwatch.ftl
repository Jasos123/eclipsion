overwatch-window-title = Overwatch Console

overwatch-filters-title = Filter
overwatch-search-placeholder = Search by name...
overwatch-status-label = Status:
overwatch-squad-label = Squad:

overwatch-status-all = All
overwatch-status-alive = Alive
overwatch-status-ssd = SSD
overwatch-status-dead = Dead

overwatch-squad-all = All Squads
overwatch-squad-unassigned = Unassigned

overwatch-members-title = Faction Members

overwatch-admin-announcement-title = Announcement
overwatch-admin-announcement-to-label = To:
overwatch-admin-announcement-to-all = Everyone
overwatch-admin-announcement-placeholder = Announcement text...
overwatch-admin-announcement-send = Send

overwatch-admin-create-squad-title = Create Squad
overwatch-admin-create-squad-placeholder = Squad Name
overwatch-admin-create-squad-button = +

overwatch-admin-squads-title = Squads
overwatch-squad-delete-button = Delete
overwatch-squad-member-count = ({ $count })

overwatch-member-view-camera-button = View Camera
overwatch-member-watching-button = Observing
overwatch-member-no-camera-button = No Camera
overwatch-member-squad-button = Squad
overwatch-member-squad-assign = Reassign
overwatch-member-squad-no-squad = Unassigned
overwatch-member-status-alive = Alive
overwatch-member-status-ssd = SSD
overwatch-member-status-dead = Dead
overwatch-member-status-unknown = Unknown
overwatch-member-coordinates-none = —

overwatch-stop-watching-button = Stop Watching

overwatch-title-dsm = KAISER'S EYE
overwatch-title-ncwl = WATCHMAN
overwatch-title-shi = SATORI
overwatch-title-tap = ECHO
overwatch-title-tfsc = DARK WEB
overwatch-title-ipm = DARK WEB
overwatch-title-saw = DARK WEB
overwatch-title-gsc = DARK WEB
overwatch-title-cd = DARK WEB
overwatch-title-srm = HUNTER EYES
overwatch-title-default = Overwatch

overwatch-announcement-title = [{ $overwatchTitle }] - { $targetName }

overwatch-announcement-target-all = Everyone
overwatch-announcement-target-squad = Squad

overwatch-job-title-unknown = Unknown

ent-BaseComputerOverwatch = overwatch console
    .desc = A computer terminal used to monitor faction members through wearable cameras.
ent-DSMOverwatchComputer = overwatch console «KAISER'S EYE»
    .desc = The «KAISER'S EYE» surveillance system console. Allows monitoring of faction members and squad coordination.
ent-NCWLOverwatchComputer = overwatch console «WATCHMAN»
    .desc = The «WATCHMAN» surveillance system console. Allows monitoring of faction members and squad coordination.
ent-SHIOverwatchComputer = overwatch console «SATORI»
    .desc = The «SATORI» surveillance system console. Allows monitoring of faction members and squad coordination.
ent-TAPOverwatchComputer = overwatch console «ECHO»
    .desc = The «ECHO» surveillance system console. Allows monitoring of faction members and squad coordination.
ent-TFSCOverwatchComputer = overwatch console «DARK WEB»
    .desc = The «DARK WEB» surveillance system console. Allows monitoring of faction members and squad coordination.
ent-SRMOverwatchComputer = overwatch console «HUNTER EYES»
    .desc = The «HUNTER EYES» surveillance system console. Allows monitoring of faction members and squad coordination.

ent-C_ComputerTabletopOverwatch = overwatch console
    .desc = A computer terminal used to monitor faction members through wearable cameras.
ent-DSMOverwatchComputerTabletop = overwatch console «KAISER'S EYE»
    .desc = The «KAISER'S EYE» surveillance system console. Allows monitoring of faction members and squad coordination.
ent-NCWLOverwatchComputerTabletop = overwatch console «WATCHMAN»
    .desc = The «WATCHMAN» surveillance system console. Allows monitoring of faction members and squad coordination.
ent-SHIOverwatchComputerTabletop = overwatch console «SATORI»
    .desc = The «SATORI» surveillance system console. Allows monitoring of faction members and squad coordination.
ent-TAPOverwatchComputerTabletop = overwatch console «ECHO»
    .desc = The «ECHO» surveillance system console. Allows monitoring of faction members and squad coordination.
ent-TFSCOverwatchComputerTabletop = overwatch console «DARK WEB»
    .desc = The «DARK WEB» surveillance system console. Allows monitoring of faction members and squad coordination.
ent-SRMOverwatchComputerTabletop = overwatch console «HUNTER EYES»
    .desc = The «HUNTER EYES» surveillance system console. Allows monitoring of faction members and squad coordination.

ent-BaseOverwatchClipboard = Overwatch digital tablet
    .desc = A bulky digital tablet containing information on faction members. It should be carefully protected.
ent-DSMOverwatchClipboard = Squad Overwatch Tablet «KAISER'S EYE»
    .desc = { ent-BaseOverwatchClipboard.desc }
ent-NCWLOverwatchClipboard = Squad Overwatch Tablet «WATCHMAN»
    .desc = { ent-BaseOverwatchClipboard.desc }
ent-SHIOverwatchClipboard = Squad Overwatch Tablet «SATORI»
    .desc = { ent-BaseOverwatchClipboard.desc }
ent-TAPOverwatchClipboard = Squad Overwatch Tablet «ECHO»
    .desc = { ent-BaseOverwatchClipboard.desc }
ent-TFSCOverwatchClipboard = Squad Overwatch Tablet «DARK WEB»
    .desc = { ent-BaseOverwatchClipboard.desc }
ent-SRMOverwatchClipboard = Squad Overwatch Tablet «HUNTER EYES»
    .desc = { ent-BaseOverwatchClipboard.desc }

overwatch-clipboard-computer-verb-text = Toggle Overwatch Menu

# Access control and limits
overwatch-console-access-denied = ACCESS DENIED - this terminal is keyed to another organisation.
overwatch-announcement-cooldown = Transmitter recharging. { $seconds }s until the next broadcast.
overwatch-squad-limit-reached = Squad register full ({ $max } maximum). Disband one first.
overwatch-squad-not-empty = Squad still has personnel assigned. Reassign them first.

# Grid-local position. Coordinates alone are meaningless across maps, so the grid travels with them.
overwatch-member-location = { $location } ({ $x }, { $y })
