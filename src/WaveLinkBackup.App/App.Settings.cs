using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Services;
using WaveLinkBackup.App.Startup;
using WaveLinkBackup.App.Updates;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App;

/// <summary>The settings dialog's backing, and every change it can commit.</summary>
public partial class App
{

    /// <summary>
    /// Save a settings change AND make it true of the running app. The Settings dialog has no Save
    /// button - every control commits immediately - so this is what "commits immediately" has to
    /// mean: written to disk, and reflected in the objects already running.
    ///
    /// Only writing the file was the old behaviour, and it made every control on that screen a
    /// control that appeared not to work until the next launch. The tier toggles were the visible
    /// case (App holds the record GatherPayload closes over, and it stayed stale), the automatic-
    /// backup switch the quiet one.
    /// </summary>
    private bool ApplySettings(BackupSettings next)
    {
        if (!settingsRepository!.Save(next).IsSuccess) return false;

        settings = next;

        if (host is not null)
        {
            host.AutoBackupEnabled = next.AutoBackupEnabled;
            host.Policy = AutoBackupPolicy.For(next);
        }

        return true;
    }

    /// <summary>
    /// Remember the theme the user picked, and repaint.
    ///
    /// <see cref="PreferredTheme.Refresh"/> rather than a call to ThemeManager.Apply: the wrapper
    /// raises the same Changed event an OS switch raises, and every subscriber - ThemeManager's own
    /// Follow, the main window's chrome and IsHighContrast, the tray menu and icon - is already
    /// wired to that. One route, so a preference change cannot repaint less than a Windows change
    /// does.
    /// </summary>
    internal void SetThemePreference(ThemePreference preference)
    {
        if (ShellState.Theme == preference) return;

        ShellState = ShellState with { Theme = preference };
        shellStateRepository?.Save(ShellState);

        systemTheme?.Refresh();
    }

    /// <summary>
    /// The settings view-model, built from the live store and current settings. Exposed separately
    /// so tests can drive the two sections (folder + when-to-back-up) without a window: they read
    /// the same seams the dialog binds to - the trash row and the free-space figure - and write
    /// through <see cref="SetStorePath"/> on a folder change.
    /// </summary>
    internal SettingsViewModel BuildSettingsViewModel()
    {
        var repo = settingsRepository!;
        // The size is the file's own byte count - read it through the same seam the rest of the
        // app uses (ReadSharedBytes), so a Wave Link lock can't make the figure lie. "not written
        // yet" when the file has never been saved: honest, and matches the mono line's tone.
        var settingsFileBytes = fileSystem!.FileExists(repo.FilePath)
            ? fileSystem.ReadSharedBytes(repo.FilePath).Length
            : 0L;

        var whereLive = new WhereSettingsLiveModel(
            repo.FilePath,
            settingsFileBytes > 0 ? Readable.Bytes(settingsFileBytes) : "not written yet");

        // WHICH WAVE LINK (Task 4): shown only when more than one installation exists and one has
        // been chosen. The locator never guesses between two installs (it returns
        // MultiplePackagesFound instead), so a successful inspect of the CHOSEN path is proof that
        // exactly one install is present - which is precisely when the section must hide itself,
        // because there is nothing to choose. A failure with no candidate is "not installed" or
        // "unreadable", also not this section's business; only MultiplePackagesFound + a chosen
        // path earns it. The version and path come from that live inspection; the CHOSEN date is
        // when our own settings file last recorded the choice (the moment the user picked).
        var whichLive = BuildWhichWaveLink(repo);

        // WHEN WINDOWS STARTS (the tray and updates spec): the Run key and the shell's own close behaviour. Both
        // seams were built and tested phases ago with nothing bound to either of them
        // (technical-debt.md §4.21 item 4). Null when autostart is unavailable, which hides the
        // whole section rather than drawing two toggles that write nowhere.
        var startup = autostart is null ? null : new StartupSeam(
            autostart,
            () => ShellState.ClosingHidesToTray,
            hides =>
            {
                ShellState = ShellState with { ClosingHidesToTray = hides };
                shellStateRepository?.Save(ShellState);
            });

        // HOW IT LOOKS: the theme preference. Shell state, not settings.json - and applied through
        // the same PreferredTheme the OS's own changes come through, so a pick here re-themes the
        // window, the dialogs, the tray menu and the tray icon by exactly the route a Windows
        // dark/light switch already did.
        var appearance = new AppearanceSeam(
            () => ShellState.Theme,
            SetThemePreference,
            () => systemTheme?.IsHighContrast ?? SystemParameters.HighContrast);

        var vm = SettingsViewModel.Build(
            settings,
            ApplySettings,
            whereLive,
            whichLive,
            startup,
            appearance);

        // WHAT GOES IN A BACKUP: every figure MEASURED from what a capture would take on this
        // machine right now (phase 6 §7) - never the design mock's 470 KB / 4 KB / 10 MB / 40 MB.
        // A number the user is asked to decide on has to be their number ([[ADR-006]]).
        //
        // Tier 1 is the settings file PLUS Wave Link's own backup copies; the effects list rides
        // inside the settings file, so it carries no separate byte count. The first two rows are
        // locked ON - they have no switch, deliberately - and the other two are the real toggles.
        var estimate = MeasureTiers();

        vm.EstimatedBackupBytes = estimate.TierOneBytes
            + (settings.IncludePresets ? estimate.PresetBytes : 0)
            + (settings.IncludePluginFiles ? estimate.PluginBinaryBytes : 0);

        vm.WhatGoesIn = new WhatGoesInModel(
            setup: new WhatGoesInRow("Your setup", "Every channel, routing and effect chain - the whole file, plus Wave Link's own copies.", estimate.TierOneBytes, true, true),
            effectsList: new WhatGoesInRow("A list of your effects", "The names of the effects in use. Travels inside the settings file above.", 0, true, true),
            presets: new WhatGoesInRow("Effect presets", "Your saved preset values for each effect.", estimate.PresetBytes, settings.IncludePresets, false),
            pluginFiles: new WhatGoesInRow("The effect plug-ins themselves", "The .vst3 files, so a new machine can load the effects.", estimate.PluginBinaryBytes, settings.IncludePluginFiles, false));

        // The toggle and stepper carry high-contrast triggers that bind to this through the
        // window's DataContext - the same value MainWindow hands ShellViewModel.
        vm.IsHighContrast = systemTheme?.IsHighContrast ?? SystemParameters.HighContrast;

        // WHERE BACKUPS ARE KEPT: the trash row is computed BEFORE anything is shown (Plan 6's
        // projection), re-detected per volume - never cached across a folder move.
        if (store is not null)
        {
            var (count, bytes) = store.TrashSize();
            vm.TrashRow = TrashRowModel.Build(
                count, bytes, store.TrashPath,
                store.TrashGoesToRecycleBin(new RecycleBin()));
            vm.FreeSpaceBytes = fileSystem!.GetAvailableFreeBytes(settings.StorePath);

            // The other two thirds of the design's stats line, from the same read (audit §2.9a).
            var snapshots = store.List();
            vm.BackupCount = snapshots.Count;
            vm.UsedBytes = snapshots.Sum(s => s.Manifest.TotalSizeBytes);
        }

        // UPDATES (the tray and updates spec). Built here because it needs the release feed and the HTTP client,
        // and hidden entirely when no feed is configured - a "Check now" that cannot reach anything
        // is worse than no button.
        vm.Updates = BuildUpdateViewModel();

        return vm;
    }

    /// <summary>
    /// What every capture puts in a snapshot beyond the settings file, read against the settings
    /// as they stand at that moment. Shared by the manual button, the watcher and the pre-restore
    /// snapshot, so all three produce the same shape of backup.
    /// </summary>
    private SnapshotPayload? GatherPayload(SettingsInspection live) =>
        fileSystem is null
            ? null
            : TierCapture.For(fileSystem).Gather(live, settings, NewestPluginManifest());

    /// <summary>
    /// The newest snapshot's plugins.json, or null when there is no snapshot, no store, or
    /// nothing readable in it. Handed to the capture for one purpose: letting tier 2 skip
    /// re-hashing a plug-in binary nothing has touched (technical-debt.md §4.16).
    ///
    /// Null on every doubt. The cost of a null is a capture that hashes as it always did.
    /// </summary>
    private PluginManifest? NewestPluginManifest()
    {
        if (fileSystem is null || store is null) return null;

        var newest = store.List().FirstOrDefault();
        return newest is null ? null : new SnapshotPluginReader(fileSystem).Read(newest);
    }

    /// <summary>
    /// What one backup would cost on this machine, per tier. Zero for every tier when Wave Link
    /// cannot be inspected: the dialog then prints dashes rather than a guess, which is the same
    /// answer the bottom bar gives when it cannot read free space.
    /// </summary>
    private CaptureEstimate MeasureTiers()
    {
        if (fileSystem is null) return CaptureEstimate.Nothing;

        var live = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        return live.IsSuccess
            ? TierCapture.For(fileSystem).Measure(live.Value)
            : CaptureEstimate.Nothing;
    }

    /// <summary>
    /// The WHICH WAVE LINK section's model, or null when the section must hide itself. The rule is
    /// "more than one installation AND one has been chosen": the locator never guesses between two
    /// (it returns MultiplePackagesFound), so a successful inspect of the CHOSEN path proves exactly
    /// one install is present - which is exactly when there is nothing to choose and the section
    /// stays hidden. A failure with no candidate is "not installed" or "unreadable", also not this
    /// section's business; only MultiplePackagesFound + a chosen path earns it
    /// (the settings-persistence spec: hide the whole section when only one installation exists).
    /// </summary>
    private WhichWaveLinkModel? BuildWhichWaveLink(SettingsRepository repo)
    {
        if (fileSystem is null || settings.ChosenWaveLinkPath is null) return null;

        var inspection = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        // Only a genuine "more than one" finding shows the section. One install or none is the
        // ordinary found / not-found fact the status strip already reports - not a choice.
        if (inspection.Error is not MultiplePackagesFound { Candidates.Count: > 1 }) return null;

        var chosen = settings.ChosenWaveLinkPath;
        if (!fileSystem.FileExists(chosen)) return null;

        // The version and path come from the live inspection of the CHOSEN install - the same seam
        // RefreshShellFacts uses, so the section never disagrees with the status strip. The version
        // is null-tolerant: a file without Update.LastUpdateVersion still shows its path and date.
        var version = inspection.Value.Analysis.WaveLinkVersion ?? "version unknown";

        // CHOSEN date = when our own settings file last recorded the choice (the moment the user
        // picked it), not when Wave Link's file was written. The repo file is the honest source:
        // it is what stores ChosenWaveLinkPath, so its last-write IS the choice time.
        var chosenAt = new DateTimeOffset(
            fileSystem.GetLastWriteTimeUtc(repo.FilePath), TimeSpan.Zero).ToLocalTime();

        return new WhichWaveLinkModel(version, chosen, chosenAt, Visible: true);
    }

    /// <summary>
    /// The WHICH WAVE LINK "Change…" action (Task 4): re-opens the error-2 chooser. It is the same
    /// dialog that fires at startup when two installations are found and none is chosen - here it
    /// is reached deliberately, so a user who picked the wrong install can correct it without
    /// uninstalling one. The pick persists through vm.ChooseWaveLink (which stores
    /// ChosenWaveLinkPath), which is what stops the chooser asking again on the next launch.
    /// </summary>
    internal void ChangeWaveLink(Window owner)
    {
        if (fileSystem is null || settingsRepository is null) return;

        var inspection = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        // Only a genuine "more than one" finding offers a choice. One install or none has nothing
        // to switch between - the button's section is hidden in those cases anyway, but this is
        // the seam's own guard against a stale reference re-entering it.
        if (inspection.Error is not MultiplePackagesFound { Candidates: var candidates }
            || candidates.Count <= 1)
            return;

        var dialog = new ErrorDialog(ErrorDialogModel.Build(inspection.Error, DescribeInstall(candidates)))
        {
            Owner = owner,
        };
        dialog.ShowDialog();

        if (dialog.Confirmed && dialog.SelectedInstallPath is not null)
        {
            settings = settings with { ChosenWaveLinkPath = dialog.SelectedInstallPath };
            settingsRepository.Save(settings);
        }
    }

    /// <summary>
    /// The settings dialog's "Change folder…": opens the picker, and on pick writes the new folder
    /// through (re-pointing every consumer that holds a store reference) then re-detects the trash
    /// row's volume - the Plan-6 rule that a folder move must never reuse a cached Recycle-Bin answer.
    /// </summary>
    internal void ChangeBackupFolder(Window owner, SettingsViewModel vm)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for Wave Link backups",
            InitialDirectory = settings.StorePath,
        };

        if (picker.ShowDialog(owner) != true) return;

        // Error 9 (the errors spec, §9), raised in place rather than as a modal. A folder holding
        // files but no snapshot is almost always a mis-click - a Recordings folder, a project
        // directory - and pointing the store at it silently would hide every existing backup
        // behind an empty list. An EMPTY folder is fine: that is "start fresh in an empty one",
        // which the error's own copy invites.
        if (NotABackupFolder(picker.FolderName) is { } fileCount)
        {
            vm.ShowNotABackupFolder(picker.FolderName, fileCount);
            return;
        }

        vm.ClearNotABackupFolder();
        SetStorePath(picker.FolderName);

        // Re-detect: the trash row and free-space figure both describe the NEW volume.
        if (store is not null)
        {
            var (count, bytes) = store.TrashSize();
            vm.TrashRow = TrashRowModel.Build(
                count, bytes, store.TrashPath,
                store.TrashGoesToRecycleBin(new RecycleBin()));
            vm.FreeSpaceBytes = fileSystem!.GetAvailableFreeBytes(settings.StorePath);
        }
    }

    /// <summary>
    /// The settings dialog's "Empty trash" (Plan 6's action, hosted in Plan 8). Local volumes run
    /// straight through - the Recycle Bin makes it reversible, and a confirmation guarding a
    /// reversible action is exactly the noise that teaches people to click through the ones that
    /// matter. Network/removable confirm first via <see cref="Views.EmptyTrashDialog"/>: there is no
    /// Recycle Bin to catch them, so emptying deletes for good. After either path the row and the
    /// free-space figure are re-read - both describe the volume's current state, not a cached one.
    /// </summary>
    internal async Task EmptyTrash(Window owner, SettingsViewModel vm)
    {
        if (store is null || vm.TrashRow is not { } row) return;

        if (row.RequiresConfirmation)
        {
            var (count, bytes) = store.TrashSize();
            var dialog = new Views.EmptyTrashDialog(
                EmptyTrashDialogModel.Build(count, bytes, store.TrashPath));
            dialog.Owner = owner;
            if (dialog.ShowDialog() != true) return;
        }

        // The empty runs off the UI thread so a large trash never freezes the window. Progress is
        // reported per removal and marshalled back to the UI thread by Progress<T>, driving the row's
        // determinate bar. The total is known up front (TrashSize), so the bar starts at 0 of N on
        // the press rather than flashing blank - the same "up for the whole operation" rule as the
        // backing-up strip.
        var (totalCount, _) = store.TrashSize();
        vm.TrashProgress = new TrashEmptyProgress(0, totalCount);

        await Task.Run(() =>
            store.EmptyTrash(new RecycleBin(), progress: new Progress<(int Done, int Total)>(report =>
                vm.TrashProgress = new TrashEmptyProgress(report.Done, report.Total))));

        // Re-detect: the row now reports whatever is left (usually "the trash is empty"), and the
        // free-space figure may have moved. Never reuse the pre-empty numbers. Clearing the progress
        // in the same pass makes the bar's removal "in place" - the count line replaces it, no flash.
        var (newCount, newBytes) = store.TrashSize();
        vm.TrashRow = TrashRowModel.Build(
            newCount, newBytes, store.TrashPath,
            store.TrashGoesToRecycleBin(new RecycleBin()));
        vm.TrashProgress = null;
        vm.FreeSpaceBytes = fileSystem!.GetAvailableFreeBytes(settings.StorePath);
    }

    /// <summary>
    /// Error 12's "Choose a folder…". Persists the new path and re-points every consumer that
    /// holds a store reference - the list, the service (next backup writes here), the host's
    /// coordinator, and the tray readout. Without the re-point the app would keep reading and
    /// writing the dead path after the user has told it where to go.
    /// </summary>
    internal void SetStorePath(string path)
    {
        // All three are set in OnStartup before any window exists, so this is a belt-and-braces
        // guard rather than an expected branch - but the fields are nullable and the compiler
        // will not see through the composition, so we narrow them to locals here.
        if (fileSystem is null || host is null || shell is null) return;

        settings = settings with { StorePath = path };
        settingsRepository?.Save(settings);

        var clock = new SystemClock();
        var inspector = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData);
        var newStore = new SnapshotStore(fileSystem, clock, path);
        store = newStore;

        // Rebuilt with the NEW store so that a backup taken after the folder change writes to
        // where the user pointed it - not the dead path. The coordinator's reference is swapped
        // inside the host; the watcher and its two timestamps survive (a pending write is still
        // a pending write, even if the destination moved).
        service = new BackupService(
            inspector, newStore, settings.AutoBackupKeepCount, settings.ChosenWaveLinkPath, GatherPayload);
        host.SetStore(newStore, service);

        shell.List.SetStorePath(path);

        RefreshTray();
        RefreshShellFacts();
    }

    /// <summary>
    /// "Copy diagnostics" (technical-debt.md §6): everything the app knows about itself, redacted,
    /// on the clipboard.
    ///
    /// On the clipboard and nowhere else. There is no upload here and no setting that would
    /// create one. The whole point is to give the user something safe to paste, not to collect
    /// anything. <see cref="Diagnostics.Report"/> runs every field through
    /// <see cref="Redaction"/>, so the promise printed beside the button is kept by construction
    /// rather than by whoever adds the next field remembering.
    /// </summary>
    internal void CopyDiagnostics(SettingsViewModel vm)
    {
        if (fileSystem is null) return;

        var live = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        var report = Core.Analysis.Diagnostics.Report(
            ReleaseVersion.Display(ReleaseVersion.Current),
            settings,
            live.IsSuccess ? live.Value : null,
            store?.List() ?? [],
            DateTimeOffset.Now);

        try
        {
            Clipboard.SetText(report);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process holds the clipboard - common, transient, and not worth an error
            // screen of its own on a diagnostic action. The button is pressable again.
            return;
        }

        vm.DiagnosticsCopied = true;
    }

    /// <summary>
    /// What each error-2 candidate turns out to be, so its row can say more than a path
    /// (06 §2, technical-debt.md §4.21 item 7).
    ///
    /// Each candidate is inspected on its own: the whole point of the dialog is that discovery
    /// could not choose between them, so nothing here may lean on the "current" one. A candidate
    /// that cannot be read yields null, and its row falls back to a path and a radio.
    ///
    /// The RUNNING chip is an approximation the model documents: Windows gives no mapping from a
    /// running MSIX process back to its package, so it goes to whichever candidate's settings file
    /// was written most recently, and only while Wave Link is actually up.
    /// </summary>
    private Func<string, ErrorInstallDetail?> DescribeInstall(IReadOnlyList<string> candidates)
    {
        var running = waveLinkProcess?.IsRunning ?? false;

        var newest = running
            ? candidates
                .Where(c => fileSystem!.FileExists(c))
                .OrderByDescending(c => fileSystem!.GetLastWriteTimeUtc(c))
                .FirstOrDefault()
            : null;

        return path =>
        {
            if (fileSystem is null) return null;

            var inspection = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
                .Inspect(path);

            if (!inspection.IsSuccess) return null;

            var saved = fileSystem.FileExists(path)
                ? new DateTimeOffset(fileSystem.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToLocalTime()
                : (DateTimeOffset?)null;

            return new ErrorInstallDetail(
                inspection.Value.Analysis.WaveLinkVersion,
                inspection.Value.Analysis.Fingerprint.InputCount,
                inspection.Value.Bytes.LongLength,
                saved,
                IsRunning: string.Equals(path, newest, StringComparison.OrdinalIgnoreCase));
        };
    }

    /// <summary>
    /// How many files a folder holds when it holds files but no backups, or null when it is a
    /// usable store: empty, or already holding snapshots.
    ///
    /// The test is for a snapshot directory rather than for a <c>manifest.json</c> at the top
    /// level: the store's own shape is one directory per snapshot, so a top-level manifest would
    /// never be there even in a perfectly good store.
    /// </summary>
    private int? NotABackupFolder(string path)
    {
        if (fileSystem is null) return null;

        var files = fileSystem.EnumerateFiles(path, "*");
        var directories = fileSystem.EnumerateDirectories(path, "*");

        if (files.Count == 0 && directories.Count == 0) return null;

        var holdsASnapshot = directories.Any(d =>
            fileSystem.FileExists(System.IO.Path.Combine(d, SnapshotManifest.ManifestFileName)));

        return holdsASnapshot ? null : files.Count;
    }

    /// <summary>Error 12's "Use the default folder": same as SetStorePath with the default.</summary>
    internal void UseDefaultStore() => SetStorePath(SnapshotStore.DefaultStorePath);

    /// <summary>
    /// Error 1's "Choose the settings file…": the escape hatch for a Wave Link discovery cannot
    /// find.
    ///
    /// This is the only route a non-MSIX install has into the app at all
    /// (technical-debt.md §2.2): <c>SettingsLocator.Locate(explicitSettingsPath)</c> bypasses
    /// discovery entirely, so an explicit path makes the program useful on a machine where every
    /// automatic answer is "not found". Persisted, so it is asked once.
    /// </summary>
    internal void ChooseSettingsFile(Window owner)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose Wave Link's Settings.json",
            Filter = "Wave Link settings (Settings.json)|Settings.json|JSON files (*.json)|*.json",
            CheckFileExists = true,
        };

        if (picker.ShowDialog(owner) != true) return;

        settings = settings with { ChosenWaveLinkPath = picker.FileName };
        settingsRepository?.Save(settings);

        // Rebuild the capture path around the chosen file, exactly as a folder change does: the
        // watcher and every later capture must read the file the user just pointed at, not the one
        // discovery failed to find.
        ApplyChosenWaveLink();
    }

    /// <summary>
    /// Re-point everything that reads Wave Link's settings at <c>settings.ChosenWaveLinkPath</c>,
    /// then re-read the facts so the not-found screen collapses on its own.
    /// </summary>
    private void ApplyChosenWaveLink()
    {
        if (fileSystem is null || host is null || store is null) return;

        service = new BackupService(
            SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData),
            store,
            settings.AutoBackupKeepCount,
            settings.ChosenWaveLinkPath,
            GatherPayload);

        host.SetStore(store, service);

        RefreshTray();
        RefreshShellFacts();
    }

    /// <summary>
    /// Error 12's "Look again": re-probe the CURRENT path. No settings change - the user is
    /// asking whether the drive came back, not where to put it. If the folder now exists the
    /// list re-reads and the full screen collapses on its own (State flips off FolderMissing).
    /// </summary>
    internal void RecheckStore()
    {
        RefreshTray();
        RefreshShellFacts();
    }

    /// <summary>
    /// The status strip's five facts, re-read from the live installation and re-applied to the
    /// shell. Called once before the window is ever shown and again on every 15-second tick,
    /// alongside RefreshTray - the tray icon and the status strip are two readouts of the same
    /// underlying state and neither should be able to go stale while the other updates.
    /// </summary>
    private void RefreshShellFacts()
    {
        // Same rule as RefreshTray, and the same caller: this one raises PropertyChanged on a
        // bound view model and can open the error-2 chooser, neither of which belongs on a
        // thread-pool thread.
        if (UiThread.Marshal(Dispatcher, RefreshShellFacts)) return;

        if (shell is null || fileSystem is null) return;

        // Error 2 (the errors spec): more than one Wave Link installation and none chosen yet is a
        // dialog, not a status-strip fact. It fires once per process - the chooser persists the
        // answer (or the user cancels), so it must never re-ask on every 15-second tick.
        if (!error2Prompted && settings.ChosenWaveLinkPath is null)
            PromptForInstallationChoice();

        var inspection = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        var savedAt = inspection.IsSuccess
            ? new DateTimeOffset(
                fileSystem.GetLastWriteTimeUtc(inspection.Value.Location.SettingsPath), TimeSpan.Zero)
                .ToLocalTime()
            : (DateTimeOffset?)null;

        shell.Apply(new ShellFacts(
            WaveLinkFound: inspection.IsSuccess,
            WaveLinkRunning: waveLinkProcess?.IsRunning ?? false,
            SettingsLastSavedLocal: savedAt,
            WaveLinkInputs: inspection.IsSuccess ? inspection.Value.Analysis.Fingerprint.InputCount : 0,
            WaveLinkSettingsPath: inspection.IsSuccess ? inspection.Value.Location.SettingsPath : null,
            AutoBackupEnabled: host?.AutoBackupEnabled ?? false,
            FolderMissing: !fileSystem.DirectoryExists(settings.StorePath),
            StorePath: settings.StorePath,
            FreeBytes: fileSystem.GetAvailableFreeBytes(settings.StorePath),
            WaveLinkVersion: inspection.IsSuccess ? inspection.Value.Analysis.WaveLinkVersion : null,
            LogsPath: inspection.IsSuccess ? inspection.Value.Location.LogsPath : null,
            UpdateAvailableVersion: updateAvailableVersion,
            UpdateFailureNotice: updateFailureNotice));
    }

    /// <summary>
    /// Error 2 (the errors spec): the chooser. It fires only when a live inspection finds more than
    /// one Wave Link installation and none has been chosen yet, so it is the FIRST thing the user
    /// sees in that situation - before any backup or restore can act on the wrong install. The
    /// answer (or a cancel) marks <see cref="error2Prompted"/> so the dialog never re-asks; picking
    /// an install also persists it, which is what stops the chooser asking again on every launch
    /// (the design decisions log, item 4).
    /// </summary>
    private void PromptForInstallationChoice()
    {
        if (fileSystem is null) return;

        var inspection = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        // Only a genuine "more than one" finding opens the dialog. One install or none is not an
        // error 2 - it is the ordinary found / not-found fact the status strip already reports.
        if (inspection.Error is not MultiplePackagesFound { Candidates: var candidates }
            || candidates.Count <= 1)
            return;

        error2Prompted = true;

        var dialog = new ErrorDialog(ErrorDialogModel.Build(inspection.Error, DescribeInstall(candidates)))
        {
            Owner = MainWindow,
        };
        dialog.ShowDialog();

        if (dialog.Confirmed && dialog.SelectedInstallPath is not null)
        {
            settings = settings with { ChosenWaveLinkPath = dialog.SelectedInstallPath };
            settingsRepository?.Save(settings);
        }
    }
}
