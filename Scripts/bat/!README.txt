Run these scripts from any working directory. Extra arguments are passed to dotnet.

buildAllDebug       Update submodules and build in Debug.
buildAllRelease     Update submodules and build in Release.
buildAllTools       Update submodules and build with mapping tools.

runQuickAll         Start the server and client without rebuilding.
runQuickClient      Start the client without rebuilding.
runQuickServer      Start the server without rebuilding.

Pass -c Release or -c Tools to run the configuration you built.
Without -c, dotnet uses Debug. Close the client to stop the server started by
runQuickAll.sh; Windows launches each process in its own window.

runTests            Run unit tests in DebugOpt.
runTestsIntegration Run integration tests in DebugOpt.
runTestsYAML        Validate prototypes in DebugOpt.

Test output is saved under Scripts/logs and printed when the command finishes.
The scripts return the build or test exit code for use in automation.
