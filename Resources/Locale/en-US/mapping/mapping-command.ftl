cmd-toggleautosave-desc = Toggles autosaving for a map.
cmd-toggleautosave-help = Usage: toggleautosave <map> <path if enabling>
cmd-toggleautosave-started = Autosaving map {$mapId} to {$path} every {$minutes} minutes.
cmd-toggleautosave-stopped = Stopped autosaving map {$mapId}.
cmd-toggleautosave-disabled = Autosaving is disabled. Set the mapping.autosave cvar to true to enable it.
cmd-toggleautosave-failed = Can't autosave map {$mapId}. It has to exist and must not be initialized.

cmd-mapping-desc = Create or load a map and teleports you to it.
cmd-mapping-help = Usage: mapping [MapID] [Path]
cmd-mapping-server = Only players can use this command.
cmd-mapping-error = An error occurred when creating the new map.
cmd-mapping-success-load = Created uninitialized map from file {$path} with id {$mapId}.
cmd-mapping-success = Created uninitialized map with id {$mapId}.
cmd-mapping-warning = WARNING: The server is using a debug build. You are risking losing your changes.


# duplicate text from engine load/save map commands.
# I CBF making this PR depend on that one.
cmd-mapping-failure-integer = {$arg} is not a valid integer.
cmd-mapping-failure-float = {$arg} is not a valid float.
cmd-mapping-failure-bool = {$arg} is not a valid bool.
cmd-mapping-nullspace = You cannot load into map 0.
cmd-hint-mapping-id = [MapID]
cmd-hint-mapping-path = [Path]
cmd-mapping-exists = Map {$mapId} already exists.
