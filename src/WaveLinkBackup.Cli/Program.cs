using WaveLinkBackup.Cli.CommandLine;
using WaveLinkBackup.Cli.Commands;
using WaveLinkBackup.Cli.Output;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Process;

// The entry point does three things: build the real dependencies, parse, dispatch. Everything
// interesting lives in CommandRunner, which takes its dependencies as parameters and is
// therefore testable without a console.

var runner = new CommandRunner(
    new FileSystem(),
    new WaveLinkProcess(),
    new SystemClock(),
    new ConsoleOutput(),
    WaveLinkBackup.Core.Discovery.SettingsLocator.SystemLocalAppData,
    new RecycleBin());

return runner.Run(CommandLineParser.Parse(args));
