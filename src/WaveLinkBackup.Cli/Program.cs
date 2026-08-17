using WaveLinkBackup.Cli.CommandLine;
using WaveLinkBackup.Cli.Commands;
using WaveLinkBackup.Cli.Output;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Process;

// The entry point does three things: build the real dependencies, parse, dispatch. Everything
// interesting lives in CommandRunner, which takes its dependencies as parameters and is
// therefore testable without a console.

var fileSystem = new FileSystem();

// The same file the GUI writes. Without this, `wlbackup list` would ignore the folder chosen
// in the app, and "a flag overrides this file for that one run" would be a claim about only
// half the product.
var settings = new SettingsRepository(fileSystem, SettingsRepository.DefaultDirectory).Read();

var runner = new CommandRunner(
    fileSystem,
    new WaveLinkProcess(),
    new SystemClock(),
    new ConsoleOutput(),
    WaveLinkBackup.Core.Discovery.SettingsLocator.SystemLocalAppData,
    new RecycleBin(),
    settings);

return runner.Run(CommandLineParser.Parse(args));
