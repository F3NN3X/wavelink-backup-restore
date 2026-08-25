using WaveLinkBackup.Cli.CommandLine;
using WaveLinkBackup.Cli.Output;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Cli.Commands;

/// <summary>
/// Translates a parsed command into Core calls and an exit code. Thin by contract (ADR-004):
/// if a verb wants something Core cannot do, that is a Core change with its own tests, not a
/// helper here.
///
/// Everything it needs is injected, so the whole surface is testable against fakes with no
/// console and no filesystem.
/// </summary>
/// <param name="localAppDataPath">
/// Passed in rather than read here, so tests can point the whole runner at a fake tree. See
/// <see cref="SettingsLocator.SystemLocalAppData"/>.
/// </param>
/// <param name="settings">
/// What settings.json says, with command-line flags layered on top per command. Flags win for
/// this run only and are never written back
/// (operations/design/screens/08-settings-persistence.md).
///
/// Optional so that tests which do not care about persistence get the old defaults.
/// </param>
public sealed class CommandRunner(
    IFileSystem fileSystem,
    IWaveLinkProcess process,
    IClock clock,
    IOutput output,
    string localAppDataPath,
    IRecycleBin recycleBin,
    BackupSettings? settings = null,
    string? appDataPath = null,
    string? documentsPath = null)
{
    private readonly BackupSettings settings = settings ?? BackupSettings.Default;

    /// <summary>Roaming AppData, where tier 3 looks. Same reason as localAppDataPath.</summary>
    private readonly string appDataPath = appDataPath ?? TierCapture.SystemAppData;
    private readonly string documentsPath = documentsPath ?? TierCapture.SystemDocuments;

    public int Run(ParsedCommand command)
    {
        if (!command.IsValid)
        {
            output.WriteError(command.Error!);
            output.WriteError("Try 'wlbackup help'.");
            return ExitCode.Usage;
        }

        return command.Verb switch
        {
            Verb.Help => Help(),
            Verb.Version => Version(),
            Verb.Backup => Backup(command),
            Verb.List => List(command),
            Verb.Restore => Restore(command),
            Verb.Rename => Rename(command),
            Verb.Delete => Delete(command),
            Verb.Verify => Verify(command),
            Verb.Prune => Prune(command),
            Verb.EmptyTrash => EmptyTrash(command),
            Verb.Watch => Watch(command),
            Verb.Diagnostics => Diagnostics(command),
            _ => Help(),
        };
    }

    // --------------------------------------------------------------------- verbs

    private int Backup(ParsedCommand command)
    {
        var service = Service(command);
        var name = command.Name ?? $"Backup {clock.UtcNow.ToLocalTime():yyyy-MM-dd HH:mm}";

        var result = service.BackUpNow(name);
        if (!result.IsSuccess) return Fail(result.Error);

        output.Write(command.Json
            ? Format.SnapshotJson(result.Value)
            : $"Backed up: {result.Value.Manifest.DisplayName}  ({result.Value.Id})");

        return ExitCode.Success;
    }

    private int List(ParsedCommand command)
    {
        var snapshots = Store(command).List();

        if (command.Json)
        {
            output.Write("[" + string.Join(",", snapshots.Select(Format.SnapshotJson)) + "]");
            return ExitCode.Success;
        }

        if (snapshots.Count == 0)
        {
            output.Write("No backups yet.");
            return ExitCode.Success;
        }

        foreach (var snapshot in snapshots) output.Write(Format.SnapshotLine(snapshot));
        return ExitCode.Success;
    }

    private int Restore(ParsedCommand command)
    {
        if (command.Arguments.Count == 0) return Usage("restore needs a backup id. Try 'wlbackup list'.");

        var live = Inspector().Inspect(command.SettingsPath);
        if (!live.IsSuccess) return Fail(live.Error);

        var store = Store(command);
        // Bring WavelinkSEService back before relaunching so Wave Link does not show its own
        // "Start Service" box. The CLI runs unelevated unless the user ran it as admin; a denied
        // start is reported, never fatal - the restore still launches the app and Wave Link falls
        // back to its own prompt exactly as before this seam existed.
        var orchestrator = new RestoreOrchestrator(
            fileSystem, process, store, new SettingsWriter(fileSystem, process),
            new SettingsReader(fileSystem), live => GatherPayload(live, store), appDataPath, documentsPath,
            service: new WaveLinkService());

        var plan = orchestrator.Plan(command.Arguments[0], live.Value);
        if (!plan.IsSuccess) return Fail(plan.Error);

        // The one irreversible action in the program. It states the consequence, then asks.
        foreach (var line in Format.PlanLines(plan.Value)) output.Write(line);

        if (!command.AssumeYes && !output.Confirm("Restore this backup?"))
        {
            output.Write("Nothing was changed.");
            return ExitCode.Declined;
        }

        // Presets always; binaries only when asked. Writing into Program Files needs
        // administrator rights, and everything that matters restores without them ([[ADR-006]]).
        var result = orchestrator.Restore(
            command.Arguments[0], live.Value,
            new RestoreOptions(Presets: true, PluginBinaries: command.WithPlugins));

        if (!result.IsSuccess) return Fail(result.Error);

        output.Write($"Restored. Your previous settings are saved as \"{result.Value.PreRestoreSnapshot.Manifest.DisplayName}\".");

        foreach (var line in Format.TierRestoreLines(result.Value.Tiers, command.WithPlugins))
        {
            output.Write(line);
        }

        if (!result.Value.Relaunched)
        {
            output.Write("Wave Link was not restarted automatically — start it yourself.");
        }

        if (result.Value.Verdict is null)
        {
            output.Write("Could not read Wave Link's log, so the restore is unconfirmed.");
        }
        else if (!result.Value.Confirmed)
        {
            output.WriteError("Wave Link rejected the settings file. Restore the \"Before restore\" backup.");
            return ExitCode.Failure;
        }

        return ExitCode.Success;
    }

    private int Rename(ParsedCommand command)
    {
        if (command.Arguments.Count < 2) return Usage("rename needs a backup id and a new name.");

        var result = Store(command).Rename(command.Arguments[0], command.Arguments[1]);
        if (!result.IsSuccess) return Fail(result.Error);

        output.Write($"Renamed to \"{result.Value.Manifest.DisplayName}\".");
        return ExitCode.Success;
    }

    private int Delete(ParsedCommand command)
    {
        if (command.Arguments.Count == 0) return Usage("delete needs a backup id.");

        var store = Store(command);
        var found = store.Get(command.Arguments[0]);
        if (!found.IsSuccess) return Fail(found.Error);

        // Names the trash, not the Recycle Bin. Deleting is a move; the Recycle Bin only
        // enters at `empty-trash`, and on a network store it never does at all. Saying
        // "Recycle Bin" here would be untrue for exactly the users most careful about backups.
        if (!command.AssumeYes &&
            !output.Confirm($"Move \"{found.Value.Manifest.DisplayName}\" to the trash?"))
        {
            output.Write("Nothing was deleted.");
            return ExitCode.Declined;
        }

        var result = store.Delete(command.Arguments[0]);
        if (!result.IsSuccess) return Fail(result.Error);

        output.Write("Moved to the trash. Run 'wlbackup empty-trash' to remove it for good.");
        return ExitCode.Success;
    }

    private int EmptyTrash(ParsedCommand command)
    {
        var store = Store(command);
        var (count, bytes) = store.TrashSize();

        if (count == 0)
        {
            output.Write("The trash is empty.");
            return ExitCode.Success;
        }

        var toRecycleBin = store.TrashGoesToRecycleBin(recycleBin);
        var summary = $"{count} backup{(count == 1 ? "" : "s")}, {bytes / 1024.0 / 1024.0:0.0} MB";

        // CONFIRM ONLY WHERE THE RECYCLE BIN CANNOT CATCH IT.
        //
        // On a local drive emptying is reversible, and per the design "a dialog guarding a
        // reversible action is the noise that teaches people to click through the ones that
        // matter". On a network or removable store it is permanent, so it asks, and says why.
        if (!toRecycleBin && !command.AssumeYes &&
            !output.Confirm($"Empty the trash? {summary} go for good — this backup folder is " +
                            "somewhere Windows keeps no Recycle Bin."))
        {
            output.Write("The trash was left alone.");
            return ExitCode.Declined;
        }

        var emptied = store.EmptyTrash(recycleBin);

        if (emptied.Count < count)
        {
            output.WriteError($"Removed {emptied.Count} of {count}; the rest are still in use.");
            return ExitCode.StoreFailed;
        }

        output.Write(toRecycleBin
            ? $"Removed {emptied.Count} to the Recycle Bin."
            : $"Removed {emptied.Count} permanently.");

        return ExitCode.Success;
    }

    private int Verify(ParsedCommand command)
    {
        var store = Store(command);
        var snapshots = command.Arguments.Count > 0
            ? [store.Get(command.Arguments[0])]
            : store.List().Select(Result<Snapshot>.Ok).ToArray();

        if (snapshots.Length == 0)
        {
            output.Write("No backups to check.");
            return ExitCode.Success;
        }

        var guard = new SnapshotGuard(fileSystem);
        var worst = ExitCode.Success;

        foreach (var found in snapshots)
        {
            if (!found.IsSuccess) return Fail(found.Error);

            var verified = guard.Verify(found.Value.Directory);
            if (verified.IsSuccess)
            {
                output.Write($"OK       {found.Value.Id}  {found.Value.Manifest.DisplayName}");
            }
            else
            {
                output.WriteError($"DAMAGED  {found.Value.Id}  {verified.Error.Message}");
                worst = ExitCode.For(verified.Error);
            }
        }

        return worst;
    }

    private int Prune(ParsedCommand command)
    {
        // Resolved a second time, independently of Service() - leave this on the hard-coded
        // default and the message would contradict what was actually pruned.
        var keep = command.KeepCount ?? settings.AutoBackupKeepCount;
        var pruned = Service(command).Prune();

        if (pruned.Count == 0)
        {
            output.Write($"Nothing to remove (keeping {keep} automatic backups).");
            return ExitCode.Success;
        }

        foreach (var snapshot in pruned) output.Write($"Removed {snapshot.Id}  {snapshot.Manifest.DisplayName}");
        output.Write($"Removed {pruned.Count} automatic backup{(pruned.Count == 1 ? "" : "s")}.");
        return ExitCode.Success;
    }

    /// <summary>
    /// The redacted self-description (technical-debt.md §6). It prints; it never sends.
    ///
    /// A live inspection that FAILS is not a failure of this verb: "Wave Link could not be read"
    /// is often the very thing being diagnosed, and refusing to produce a report in that case
    /// would leave the user with nothing to paste but their settings file.
    /// </summary>
    private int Diagnostics(ParsedCommand command)
    {
        var live = Inspector().Inspect(command.SettingsPath);

        foreach (var line in Core.Analysis.Diagnostics.Lines(
                     typeof(CommandRunner).Assembly.GetName().Version?.ToString() ?? "unknown",
                     settings,
                     live.IsSuccess ? live.Value : null,
                     Store(command).List(),
                     clock.UtcNow.ToLocalTime(),
                     userName: null,
                     // Counts only, and never an id or a name - see the section in Diagnostics.
                     // This is also the one caller that keeps WindowsAudioEndpointInspector
                     // reachable from the CLI, which is what makes the AOT publish a real test of
                     // technical-debt.md 2.4 rather than a test of dead-code elimination.
                     endpoints: new WindowsAudioEndpointInspector().List()))
        {
            output.Write(line);
        }

        return ExitCode.Success;
    }

    /// <summary>
    /// The first production caller of <see cref="AutoBackupCoordinator.Tick"/>. Three phases
    /// of automation have never run outside a test until this verb.
    /// </summary>
    private int Watch(ParsedCommand command)
    {
        var live = Inspector().Inspect(command.SettingsPath);
        if (!live.IsSuccess) return Fail(live.Error);

        var interval = TimeSpan.FromSeconds(command.IntervalSeconds ?? 15);

        using var watcher = new FileSystemSettingsWatcher(live.Value.Location.LocalStatePath);
        // The settings file's timings, not this verb's --interval: that flag is how often `watch`
        // LOOKS, and the policy is how often it is allowed to CAPTURE. Two different numbers that
        // would be confusing to conflate, which is why the flag keeps its own name.
        using var coordinator = new AutoBackupCoordinator(
            watcher, Service(command), clock, AutoBackupPolicy.For(settings));

        using var stopping = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            // Handle it ourselves rather than letting the runtime kill us: the shutdown
            // capture is the whole reason CaptureOnShutdown exists, and a hard exit skips it.
            e.Cancel = true;
            stopping.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            coordinator.Start();
            output.Write($"Watching {live.Value.Location.SettingsPath}");
            output.Write($"Checking every {interval.TotalSeconds:0}s. Press Ctrl+C to stop.");

            while (!stopping.IsCancellationRequested)
            {
                var tick = coordinator.Tick();
                if (tick.Captured)
                {
                    output.Write($"Backed up: {tick.Capture!.Snapshot!.Id}");
                }

                try { Task.Delay(interval, stopping.Token).Wait(stopping.Token); }
                catch (OperationCanceledException) { break; }
                catch (AggregateException) { break; }
            }

            coordinator.Stop();

            // The original incident happened during an update, while the app was restarting.
            var final = coordinator.CaptureOnShutdown();
            output.Write(final.Captured
                ? $"Backed up before exit: {final.Capture!.Snapshot!.Id}"
                : "Nothing changed since the last backup.");

            return ExitCode.Success;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    // --------------------------------------------------------------------- plumbing

    private SettingsInspector Inspector() => SettingsInspector.For(fileSystem, localAppDataPath);

    private SnapshotStore Store(ParsedCommand command) =>
        new(fileSystem, clock, command.StorePath ?? settings.StorePath);

    private BackupService Service(ParsedCommand command)
    {
        var store = Store(command);

        return new BackupService(
            Inspector(),
            store,
            command.KeepCount ?? settings.AutoBackupKeepCount,
            command.SettingsPath ?? settings.ChosenWaveLinkPath,
            live => GatherPayload(live, store));
    }

    /// <summary>
    /// Tiers 1-extra, 2, 3 and 4, resolved at capture time against the settings this run is
    /// using. Every capture path goes through here so a `wlbackup backup` and a backup taken by
    /// the GUI put the same things in a snapshot.
    /// </summary>
    private SnapshotPayload? GatherPayload(SettingsInspection live, SnapshotStore store)
    {
        // The newest snapshot's plugins.json, so tier 2 can skip re-hashing a binary nothing has
        // touched (technical-debt.md §4.16). Null on every doubt. It only ever costs a hash.
        var newest = store.List().FirstOrDefault();
        var previous = newest is null ? null : new SnapshotPluginReader(fileSystem).Read(newest);

        return new TierCapture(fileSystem, appDataPath, documentsPath)
            .Gather(live, settings, previous);
    }

    private int Fail(CoreError error)
    {
        output.WriteError(error.Message);
        return ExitCode.For(error);
    }

    private int Usage(string message)
    {
        output.WriteError(message);
        return ExitCode.Usage;
    }

    private int Version()
    {
        var version = typeof(CommandRunner).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "unknown";

        output.Write($"wlbackup {version}");
        return ExitCode.Success;
    }

    private int Help()
    {
        foreach (var line in HelpText.Lines) output.Write(line);
        return ExitCode.Success;
    }
}
