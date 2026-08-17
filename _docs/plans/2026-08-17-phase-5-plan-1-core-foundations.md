---
title: "Phase 5 Plan 1 — Core foundations"
status: published
created: 2026-08-17
updated: 2026-08-17
related_adrs: [ADR-004]
tags: [plan, implementation, core, phase-5]
---

# Phase 5 Plan 1 — Core Foundations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Core the four things the shell needs but does not have — persisted settings, a chosen-installation field, snapshot search, and disk-free reporting — and make the CLI honour the same settings file.

**Architecture:** Everything here is Core (`net10.0`, headless) plus a thin wiring change in the CLI. Settings persist as hand-written JSON through the existing `IFileSystem` seam, written atomically the way `SettingsWriter` already writes Wave Link's file. Search is a pure function over an already-loaded list. Disk-free is a new `IFileSystem` member backed by P/Invoke, because the store may legitimately live on a UNC path.

**Tech Stack:** C# / .NET 10, xunit.v3, `Utf8JsonWriter` + `JsonDocument`, `DllImport`.

**Spec:** [2026-08-17-phase-5-shell-design.md](2026-08-17-phase-5-shell-design.md)

## Global Constraints

- `WaveLinkBackup.Core` targets **`net10.0`**, never `net10.0-windows`. `GuardNoDesktopFramework` fails the build otherwise.
- `TreatWarningsAsErrors` is on, repo-wide.
- **No `JsonSerializer.Serialize`/`Deserialize` anywhere in Core.** `SourceGuardTests.Core_never_uses_reflection_based_json_serialization` fails the build. Use `Utf8JsonWriter` and `JsonDocument`.
- **No `File.ReadAllBytes`/`ReadAllText`/`ReadAllLines`/`File.OpenRead` in Core.** Read through `IFileSystem.ReadSharedBytes`.
- **No `Console.*` in Core** (ADR-004).
- `DllImport`, not `LibraryImport` — the generator would require `AllowUnsafeBlocks` for the whole project (technical-debt §7.1).
- Settings file path: `%LOCALAPPDATA%\WaveLinkBackup\settings.json`.
- Command-line flags win **for one run** and are **never written back**.
- Build: `dotnet build WaveLinkBackup.slnx` · Test: `dotnet test WaveLinkBackup.slnx`
- Baseline before starting: **351 tests green** (266 Core, 85 CLI).

## File Structure

| File | Responsibility |
|---|---|
| `src/WaveLinkBackup.Core/Automation/BackupSettings.cs` | *Modify* — add `ChosenWaveLinkPath` |
| `src/WaveLinkBackup.Core/Automation/SettingsSerializer.cs` | *Create* — `BackupSettings` ⇄ bytes. Pure, no IO |
| `src/WaveLinkBackup.Core/Automation/SettingsRepository.cs` | *Create* — read/save through `IFileSystem`, atomically |
| `src/WaveLinkBackup.Core/Snapshots/SnapshotSearch.cs` | *Create* — filter by name, and match segments |
| `src/WaveLinkBackup.Core/Abstractions/IFileSystem.cs` | *Modify* — add `GetAvailableFreeBytes` |
| `src/WaveLinkBackup.Core/Abstractions/FileSystem.cs` | *Modify* — implement it via `GetDiskFreeSpaceEx` |
| `src/WaveLinkBackup.Cli/Commands/CommandRunner.cs` | *Modify* — layer flags over the settings file |
| `src/WaveLinkBackup.Cli/Program.cs` | *Modify* — load settings, pass them in |
| `tests/WaveLinkBackup.Core.Tests/Fakes/FakeFileSystem.cs` | *Modify* — implement the new member |

---

### Task 1: Settings serialization

`BackupSettings` gains the chosen-installation field, and learns to become bytes and back.

**Design note being implemented:** a malformed or partially-broken `settings.json` falls back **per field** to defaults rather than raising a `CoreError`. This is a preferences file, not a backup — refusing to start because preferences are corrupt is worse than starting with defaults, and none of the design's twelve errors covers it.

**Files:**
- Modify: `src/WaveLinkBackup.Core/Automation/BackupSettings.cs`
- Create: `src/WaveLinkBackup.Core/Automation/SettingsSerializer.cs`
- Test: `tests/WaveLinkBackup.Core.Tests/Automation/SettingsSerializerTests.cs`

**Interfaces:**
- Consumes: `BackupSettings`, `SnapshotRetention.DefaultKeepCount`, `SnapshotStore.DefaultStorePath`
- Produces:
  - `BackupSettings(string StorePath, bool AutoBackupEnabled = true, int AutoBackupKeepCount = SnapshotRetention.DefaultKeepCount, string? ChosenWaveLinkPath = null)`
  - `static byte[] SettingsSerializer.Write(BackupSettings settings)`
  - `static BackupSettings SettingsSerializer.Read(ReadOnlySpan<byte> utf8Json)`
  - `const int SettingsSerializer.CurrentSchemaVersion = 1`

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.Core.Tests/Automation/SettingsSerializerTests.cs`:

```csharp
using System.Text;
using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.Core.Tests.Automation;

public sealed class SettingsSerializerTests
{
    [Fact]
    public void Round_trips_every_field()
    {
        var settings = new BackupSettings(
            StorePath: @"D:\Backups\WaveLink",
            AutoBackupEnabled: false,
            AutoBackupKeepCount: 7,
            ChosenWaveLinkPath: @"C:\Program Files\Elgato\WaveLink\Settings.json");

        var read = SettingsSerializer.Read(SettingsSerializer.Write(settings));

        Assert.Equal(settings, read);
    }

    [Fact]
    public void Writes_a_schema_version()
    {
        var json = Encoding.UTF8.GetString(SettingsSerializer.Write(BackupSettings.Default));

        Assert.Contains("\"schemaVersion\": 1", json);
    }

    [Fact]
    public void A_null_chosen_installation_survives_the_round_trip()
    {
        var read = SettingsSerializer.Read(SettingsSerializer.Write(BackupSettings.Default));

        Assert.Null(read.ChosenWaveLinkPath);
    }

    [Fact]
    public void Unparseable_bytes_fall_back_to_defaults()
    {
        var read = SettingsSerializer.Read("this is not json"u8);

        Assert.Equal(BackupSettings.Default, read);
    }

    [Fact]
    public void Empty_bytes_fall_back_to_defaults()
    {
        Assert.Equal(BackupSettings.Default, SettingsSerializer.Read([]));
    }

    [Fact]
    public void A_json_array_falls_back_to_defaults()
    {
        Assert.Equal(BackupSettings.Default, SettingsSerializer.Read("[1,2,3]"u8));
    }

    // One broken field must not cost the user the other three. This is the whole reason
    // Read is tolerant per-field rather than all-or-nothing.
    [Fact]
    public void A_wrongly_typed_field_falls_back_alone()
    {
        var json = """
            {
              "schemaVersion": 1,
              "storePath": "D:\\Backups",
              "autoBackupEnabled": "yes please",
              "autoBackupKeepCount": 12
            }
            """u8;

        var read = SettingsSerializer.Read(json);

        Assert.Equal(@"D:\Backups", read.StorePath);
        Assert.True(read.AutoBackupEnabled);      // defaulted
        Assert.Equal(12, read.AutoBackupKeepCount); // kept
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        var json = """
            {"schemaVersion": 1, "storePath": "D:\\B", "somethingFromTheFuture": 42}
            """u8;

        Assert.Equal(@"D:\B", SettingsSerializer.Read(json).StorePath);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.Core.Tests --filter FullyQualifiedName~SettingsSerializerTests`
Expected: FAIL to compile — `SettingsSerializer` does not exist, and `BackupSettings` has no `ChosenWaveLinkPath`.

- [ ] **Step 3: Add the field to `BackupSettings`**

Replace the record declaration in `src/WaveLinkBackup.Core/Automation/BackupSettings.cs`:

```csharp
public sealed record BackupSettings(
    string StorePath,
    bool AutoBackupEnabled = true,
    int AutoBackupKeepCount = SnapshotRetention.DefaultKeepCount,
    string? ChosenWaveLinkPath = null)
{
    public static BackupSettings Default => new(SnapshotStore.DefaultStorePath);
}
```

Add to the existing XML doc comment, above the record:

```csharp
/// <param name="ChosenWaveLinkPath">
/// Which installation to watch and restore into, when more than one exists. Null means
/// "not chosen yet" - required by error 2, because without storing the answer the chooser
/// asks again on every launch (screens/10-decisions.md §4).
/// </param>
```

- [ ] **Step 4: Write the serializer**

Create `src/WaveLinkBackup.Core/Automation/SettingsSerializer.cs`:

```csharp
using System.Buffers;
using System.Text.Json;

namespace WaveLinkBackup.Core.Automation;

/// <summary>
/// settings.json in and out. PURE - bytes to a record, a record to bytes, no IO.
///
/// Hand-written with Utf8JsonWriter and JsonDocument rather than JsonSerializer, matching
/// ManifestSerializer: reflection-based serialization would close off NativeAOT for the CLI,
/// and SourceGuardTests fails the build if anyone reaches for the shortcut.
///
/// Read is deliberately TOLERANT. Every field falls back to its default independently, and a
/// document that cannot be parsed at all yields BackupSettings.Default. This is a preferences
/// file - refusing to start because it is corrupt would be worse than starting with defaults,
/// and one broken field must not cost the user the other three.
/// </summary>
public static class SettingsSerializer
{
    public const int CurrentSchemaVersion = 1;

    public static byte[] Write(BackupSettings settings)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("storePath", settings.StorePath);
            writer.WriteBoolean("autoBackupEnabled", settings.AutoBackupEnabled);
            writer.WriteNumber("autoBackupKeepCount", settings.AutoBackupKeepCount);

            if (settings.ChosenWaveLinkPath is null) writer.WriteNull("chosenWaveLinkPath");
            else writer.WriteString("chosenWaveLinkPath", settings.ChosenWaveLinkPath);

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static BackupSettings Read(ReadOnlySpan<byte> utf8Json)
    {
        var defaults = BackupSettings.Default;

        if (utf8Json.IsEmpty) return defaults;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return defaults;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return defaults;

            return new BackupSettings(
                StorePath: String(root, "storePath") ?? defaults.StorePath,
                AutoBackupEnabled: Bool(root, "autoBackupEnabled") ?? defaults.AutoBackupEnabled,
                AutoBackupKeepCount: Int(root, "autoBackupKeepCount") ?? defaults.AutoBackupKeepCount,
                ChosenWaveLinkPath: String(root, "chosenWaveLinkPath"));
        }
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.Core.Tests --filter FullyQualifiedName~SettingsSerializerTests`
Expected: PASS, 8 tests.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test WaveLinkBackup.slnx`
Expected: PASS. `BackupSettings` gained an optional parameter, so nothing that constructs it should break.

- [ ] **Step 7: Commit**

```bash
git add src/WaveLinkBackup.Core/Automation/BackupSettings.cs \
        src/WaveLinkBackup.Core/Automation/SettingsSerializer.cs \
        tests/WaveLinkBackup.Core.Tests/Automation/SettingsSerializerTests.cs
git commit -m "feat: serialize BackupSettings, and record the chosen Wave Link

Read is tolerant per field. A preferences file that fails to parse should cost
defaults, not a refusal to start, and one broken field should not cost the
other three."
```

---

### Task 2: The settings repository

Reads and saves `settings.json` through `IFileSystem`, atomically.

**The trap in this task:** `File.Replace` **throws when the destination does not exist**, which is exactly the first-ever save. `SettingsWriter` never hits this because Wave Link's `Settings.json` is always already there. The implementation must branch on existence.

**Files:**
- Create: `src/WaveLinkBackup.Core/Automation/SettingsRepository.cs`
- Test: `tests/WaveLinkBackup.Core.Tests/Automation/SettingsRepositoryTests.cs`

**Interfaces:**
- Consumes: `IFileSystem`, `SettingsSerializer`, `BackupSettings`, `Result`, `WriteFailed`
- Produces:
  - `new SettingsRepository(IFileSystem fileSystem, string directoryPath)`
  - `static string SettingsRepository.DefaultDirectory`
  - `string SettingsRepository.FilePath`
  - `BackupSettings SettingsRepository.Read()`
  - `Result SettingsRepository.Save(BackupSettings settings)`

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.Core.Tests/Automation/SettingsRepositoryTests.cs`:

```csharp
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests.Automation;

public sealed class SettingsRepositoryTests
{
    private const string Directory = @"C:\Users\t\AppData\Local\WaveLinkBackup";
    private const string File = @"C:\Users\t\AppData\Local\WaveLinkBackup\settings.json";

    private static SettingsRepository Repository(FakeFileSystem fileSystem) => new(fileSystem, Directory);

    [Fact]
    public void Reads_defaults_when_the_file_does_not_exist()
    {
        Assert.Equal(BackupSettings.Default, Repository(new FakeFileSystem()).Read());
    }

    [Fact]
    public void Saves_then_reads_the_same_settings()
    {
        var fileSystem = new FakeFileSystem();
        var repository = Repository(fileSystem);
        var settings = new BackupSettings(@"D:\B", AutoBackupEnabled: false, AutoBackupKeepCount: 9);

        Assert.True(repository.Save(settings).IsSuccess);

        Assert.Equal(settings, repository.Read());
    }

    // The first save has no destination to replace. File.Replace throws in that case, so
    // this is the path that breaks if the implementation copies SettingsWriter blindly.
    [Fact]
    public void The_first_save_writes_directly_rather_than_replacing()
    {
        var fileSystem = new FakeFileSystem();

        Assert.True(Repository(fileSystem).Save(BackupSettings.Default).IsSuccess);

        Assert.True(fileSystem.FileExists(File));
        Assert.Empty(fileSystem.Replacements);
    }

    [Fact]
    public void A_later_save_replaces_atomically()
    {
        var fileSystem = new FakeFileSystem();
        var repository = Repository(fileSystem);

        repository.Save(BackupSettings.Default);
        repository.Save(BackupSettings.Default with { AutoBackupKeepCount = 3 });

        var replacement = Assert.Single(fileSystem.Replacements);
        Assert.Equal(File, replacement.Destination);
        Assert.Equal(3, repository.Read().AutoBackupKeepCount);
    }

    [Fact]
    public void Leaves_no_temporary_file_behind()
    {
        var fileSystem = new FakeFileSystem();
        var repository = Repository(fileSystem);

        repository.Save(BackupSettings.Default);
        repository.Save(BackupSettings.Default with { AutoBackupKeepCount = 3 });

        Assert.Empty(fileSystem.EnumerateFiles(Directory, "*.tmp"));
    }

    [Fact]
    public void Reads_defaults_when_the_file_cannot_be_read()
    {
        var fileSystem = new FakeFileSystem().AddFile(File, "{}");
        fileSystem.ReadFailures[File] = new Queue<Exception>([new IOException("locked")]);

        Assert.Equal(BackupSettings.Default, Repository(fileSystem).Read());
    }

    [Fact]
    public void Reports_a_failure_when_the_directory_cannot_be_created()
    {
        var fileSystem = new FakeFileSystem { FailDirectoryCreation = true };

        var result = Repository(fileSystem).Save(BackupSettings.Default);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void The_file_sits_directly_in_the_given_directory()
    {
        Assert.Equal(File, Repository(new FakeFileSystem()).FilePath);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.Core.Tests --filter FullyQualifiedName~SettingsRepositoryTests`
Expected: FAIL — `SettingsRepository` does not exist.

- [ ] **Step 3: Write the repository**

Create `src/WaveLinkBackup.Core/Automation/SettingsRepository.cs`:

```csharp
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Automation;

/// <summary>
/// Where the user's choices live: %LOCALAPPDATA%\WaveLinkBackup\settings.json.
///
/// In Core rather than in the shell because the design's own sentence - "a command-line flag
/// overrides this file for that one run and isn't saved" - is a claim about the CLI as much as
/// the GUI. If this lived in the App project, `wlbackup list` would keep ignoring the folder
/// chosen in the GUI. See screens/08-settings-persistence.md.
///
/// Write on change, never on exit.
/// </summary>
public sealed class SettingsRepository(IFileSystem fileSystem, string directoryPath)
{
    public const string FileName = "settings.json";

    /// <summary>
    /// Resolved through GetFolderPath rather than a composed string - %LOCALAPPDATA% is
    /// redirected on some corporate and OneDrive setups, the same reason SnapshotStore does it.
    /// </summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WaveLinkBackup");

    public string FilePath { get; } = Path.Combine(directoryPath, FileName);

    /// <summary>
    /// Never fails. A missing file means "not configured yet"; an unreadable one means the
    /// user gets defaults rather than a dead app. Both are preferences problems, not data loss.
    /// </summary>
    public BackupSettings Read()
    {
        if (!fileSystem.FileExists(FilePath)) return BackupSettings.Default;

        try
        {
            return SettingsSerializer.Read(fileSystem.ReadSharedBytes(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BackupSettings.Default;
        }
    }

    public Result Save(BackupSettings settings)
    {
        var bytes = SettingsSerializer.Write(settings);

        try
        {
            fileSystem.CreateDirectory(directoryPath);

            // File.Replace THROWS when the destination does not exist, which is exactly the
            // first-ever save. SettingsWriter never meets this case because Wave Link's
            // Settings.json is always already there.
            if (!fileSystem.FileExists(FilePath))
            {
                fileSystem.WriteBytes(FilePath, bytes);
                return Result.Ok();
            }

            // Same directory, because File.Replace requires one volume.
            var temp = Path.Combine(directoryPath, $".{FileName}.{Guid.NewGuid():N}.tmp");
            var rollback = Path.Combine(directoryPath, $".{FileName}.{Guid.NewGuid():N}.rollback");

            try
            {
                fileSystem.WriteBytes(temp, bytes);
                fileSystem.Replace(temp, FilePath, rollback);
                return Result.Ok();
            }
            finally
            {
                try { fileSystem.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
                try { fileSystem.Delete(rollback); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WriteFailed(ex.Message);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.Core.Tests --filter FullyQualifiedName~SettingsRepositoryTests`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/WaveLinkBackup.Core/Automation/SettingsRepository.cs \
        tests/WaveLinkBackup.Core.Tests/Automation/SettingsRepositoryTests.cs
git commit -m "feat: persist settings at %LOCALAPPDATA%\\WaveLinkBackup\\settings.json

Atomic on every save after the first. The first has no destination, and
File.Replace throws in that case - a path SettingsWriter never meets because
Wave Link's file always already exists."
```

---

### Task 3: The CLI reads the same file

Flags layer over `settings.json` and are never written back.

**Files:**
- Modify: `src/WaveLinkBackup.Cli/Commands/CommandRunner.cs`
- Modify: `src/WaveLinkBackup.Cli/Program.cs`
- Test: `tests/WaveLinkBackup.Cli.Tests/SettingsFileTests.cs`

**Interfaces:**
- Consumes: `SettingsRepository`, `BackupSettings`, `CommandRunner`
- Produces: `CommandRunner` gains a trailing optional parameter `BackupSettings? settings = null`, defaulting to `BackupSettings.Default` — so every existing construction site still compiles unchanged.

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.Cli.Tests/SettingsFileTests.cs`.

This mirrors `CommandRunnerTests.Harness` with two deliberate differences: it takes a `BackupSettings`, and its `Run` does **not** inject `--store`. The existing harness forces `--store` on every call, which is exactly the behaviour under test here.

```csharp
using System.Text;
using WaveLinkBackup.Cli.CommandLine;
using WaveLinkBackup.Cli.Commands;
using WaveLinkBackup.Cli.Output;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Cli.Tests;

/// <summary>
/// settings.json is the base; flags win for one run and are never written back
/// (screens/08-settings-persistence.md).
/// </summary>
public sealed class SettingsFileTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string SettingsPath = LocalState + @"\Settings.json";
    private const string FromSettings = @"D:\from-settings";
    private const string FromFlag = @"D:\from-flag";

    private const string Healthy = """
        {"Update":{"LastUpdateVersion":"3.3.0.4108"},
         "MixerConfiguration":{"InputSettings":{
           "BS33J1A05009\\PCM_IN_01_C_00_SD1":{"InputName":"Wave Mic 1","AudioPluginConfigurations":[{"Name":"Pro-Q 4"}]},
           "PCM_OUT_00_V_14_SD8":{"InputName":"Voice","AudioPluginConfigurations":[]}}}}
        """;

    private sealed class Harness(BackupSettings settings)
    {
        public FakeFileSystem Fs { get; } = new FakeFileSystem().AddFile(SettingsPath, Healthy);
        public FakeClock Clock { get; } = new();
        public FakeWaveLinkProcess Process { get; } = new() { Running = false };
        public FakeOutput Out { get; } = new();

        private FakeRecycleBin? bin;
        public FakeRecycleBin Bin => bin ??= new FakeRecycleBin(Fs);

        // No --store injection: which store gets used IS the assertion.
        public int Run(params string[] args) =>
            new CommandRunner(Fs, Process, Clock, Out, LocalAppData, Bin, settings)
                .Run(CommandLineParser.Parse(args));

        public SnapshotStore StoreAt(string path) => new(Fs, Clock, path);

        public void EditSettings(string micName) => Fs.WriteBytes(SettingsPath,
            Encoding.UTF8.GetBytes(Healthy.Replace("Wave Mic 1", micName, StringComparison.Ordinal)));
    }

    [Fact]
    public void The_store_from_the_settings_file_is_used_when_no_flag_is_given()
    {
        var h = new Harness(BackupSettings.Default with { StorePath = FromSettings });

        Assert.Equal(ExitCode.Success, h.Run("backup", "--name", "x"));

        Assert.Single(h.StoreAt(FromSettings).List());
    }

    [Fact]
    public void A_store_flag_beats_the_settings_file()
    {
        var h = new Harness(BackupSettings.Default with { StorePath = FromSettings });

        Assert.Equal(ExitCode.Success, h.Run("backup", "--name", "x", "--store", FromFlag));

        Assert.Single(h.StoreAt(FromFlag).List());
        Assert.Empty(h.StoreAt(FromSettings).List());
    }

    [Fact]
    public void The_keep_count_from_the_settings_file_is_used_when_no_flag_is_given()
    {
        var h = new Harness(BackupSettings.Default with
        {
            StorePath = FromSettings,
            AutoBackupKeepCount = 7,
        });

        h.Run("prune");

        Assert.Contains("keeping 7", h.Out.All, StringComparison.Ordinal);
    }

    [Fact]
    public void A_keep_count_flag_beats_the_settings_file()
    {
        var h = new Harness(BackupSettings.Default with
        {
            StorePath = FromSettings,
            AutoBackupKeepCount = 7,
        });

        h.Run("prune", "--keep", "3");

        Assert.Contains("keeping 3", h.Out.All, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chosen_installation_is_used_when_no_settings_path_flag_is_given()
    {
        var h = new Harness(BackupSettings.Default with
        {
            StorePath = FromSettings,
            ChosenWaveLinkPath = SettingsPath,
        });

        Assert.Equal(ExitCode.Success, h.Run("backup", "--name", "x"));
    }

    // The whole point of "a flag isn't saved": nothing the CLI does writes settings.json.
    [Fact]
    public void Running_a_command_never_writes_the_settings_file()
    {
        var h = new Harness(BackupSettings.Default with { StorePath = FromSettings });

        h.Run("backup", "--name", "x", "--store", FromFlag, "--keep", "3");

        var settingsFile = System.IO.Path.Combine(
            SettingsRepository.DefaultDirectory, SettingsRepository.FileName);

        Assert.False(h.Fs.FileExists(settingsFile));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.Cli.Tests --filter FullyQualifiedName~SettingsFileTests`
Expected: FAIL.

- [ ] **Step 3: Add the parameter to `CommandRunner`**

In `src/WaveLinkBackup.Cli/Commands/CommandRunner.cs`, add a trailing parameter to the primary constructor:

```csharp
public sealed class CommandRunner(
    IFileSystem fileSystem,
    IWaveLinkProcess process,
    IClock clock,
    IOutput output,
    string localAppDataPath,
    IRecycleBin recycleBin,
    BackupSettings? settings = null)
{
    /// <summary>
    /// What the settings file says, with command-line flags layered on top per command.
    /// Flags win for this run only and are never written back
    /// (screens/08-settings-persistence.md).
    /// </summary>
    private readonly BackupSettings settings = settings ?? BackupSettings.Default;
```

Add `using WaveLinkBackup.Core.Automation;` if it is not already present — it is, since `BackupService` lives there.

- [ ] **Step 4: Layer the flags over the settings**

In the same file, replace the three plumbing methods at the bottom:

```csharp
    private SnapshotStore Store(ParsedCommand command) =>
        new(fileSystem, clock, command.StorePath ?? settings.StorePath);

    private BackupService Service(ParsedCommand command) => new(
        Inspector(),
        Store(command),
        command.KeepCount ?? settings.AutoBackupKeepCount,
        command.SettingsPath ?? settings.ChosenWaveLinkPath);
```

- [ ] **Step 4b: Fix the second keep-count resolution**

`Prune` resolves the keep count **again**, independently of `Service()`. Leave it and the
printed message will contradict what was actually pruned. In the `Prune` method, change:

```csharp
        var keep = command.KeepCount ?? SnapshotRetention.DefaultKeepCount;
```

to:

```csharp
        var keep = command.KeepCount ?? settings.AutoBackupKeepCount;
```

Then scan the whole file for any other `?? SnapshotRetention.DefaultKeepCount` or
`?? SnapshotStore.DefaultStorePath` and give it the same treatment — the settings file is now
the only source of a default.

- [ ] **Step 5: Load the settings in `Program.cs`**

Open `src/WaveLinkBackup.Cli/Program.cs`, find where `CommandRunner` is constructed, and pass the settings read from disk. The repository takes the same filesystem the runner already uses:

```csharp
var settingsRepository = new SettingsRepository(fileSystem, SettingsRepository.DefaultDirectory);

var runner = new CommandRunner(
    fileSystem, process, clock, output, localAppDataPath, recycleBin,
    settingsRepository.Read());
```

Add `using WaveLinkBackup.Core.Automation;` if absent.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.Cli.Tests`
Expected: PASS — the new tests, and all 85 existing ones. If an existing test broke, it is asserting default-store behaviour that now depends on the settings file; give that test an explicit `BackupSettings` rather than changing the production default.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test WaveLinkBackup.slnx`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/WaveLinkBackup.Cli/Commands/CommandRunner.cs \
        src/WaveLinkBackup.Cli/Program.cs \
        tests/WaveLinkBackup.Cli.Tests/SettingsFileTests.cs
git commit -m "feat: the CLI honours settings.json, with flags winning for one run

'A command-line flag overrides this file for that one run and isn't saved' is a
claim about the CLI too. Without this, the folder chosen in the GUI would be
ignored by every verb."
```

---

### Task 4: Snapshot search

Filter by name, and expose the match so the shell can highlight it.

**Files:**
- Create: `src/WaveLinkBackup.Core/Snapshots/SnapshotSearch.cs`
- Test: `tests/WaveLinkBackup.Core.Tests/Snapshots/SnapshotSearchTests.cs`

**Interfaces:**
- Consumes: `Snapshot`, `SnapshotManifest`
- Produces:
  - `readonly record struct NameSegment(string Text, bool IsMatch)`
  - `static IReadOnlyList<Snapshot> SnapshotSearch.Filter(IReadOnlyList<Snapshot> snapshots, string? query)`
  - `static IReadOnlyList<NameSegment> SnapshotSearch.Segments(string name, string? query)`

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.Core.Tests/Snapshots/SnapshotSearchTests.cs`:

```csharp
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Tests.Snapshots;

public sealed class SnapshotSearchTests
{
    private static Snapshot Named(string name) => new(
        Id: name,
        Directory: $@"C:\store\{name}",
        Manifest: new SnapshotManifest(
            SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
            DisplayName: name,
            Notes: "",
            CreatedUtc: new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero),
            Trigger: SnapshotTrigger.Manual,
            SettingsSha256: "abc",
            WaveLinkVersion: null,
            InputCount: 5,
            InputNames: [],
            EffectCount: 0,
            EffectChannelCount: 0,
            HasDuplicateKeys: false,
            Tiers: ["settings"],
            Files: new Dictionary<string, SnapshotFile>()));

    private static readonly IReadOnlyList<Snapshot> Store =
        [Named("Before 3.3 beta"), Named("Full rig + plugins"), Named("Auto"), Named("BETA test")];

    [Fact]
    public void An_empty_query_returns_everything()
    {
        Assert.Equal(4, SnapshotSearch.Filter(Store, "").Count);
        Assert.Equal(4, SnapshotSearch.Filter(Store, null).Count);
        Assert.Equal(4, SnapshotSearch.Filter(Store, "   ").Count);
    }

    [Fact]
    public void Matching_is_case_insensitive_and_substring()
    {
        var matches = SnapshotSearch.Filter(Store, "beta");

        Assert.Equal(["Before 3.3 beta", "BETA test"], matches.Select(s => s.Manifest.DisplayName));
    }

    [Fact]
    public void A_query_that_matches_nothing_returns_nothing()
    {
        Assert.Empty(SnapshotSearch.Filter(Store, "wave:3"));
    }

    // "Search looks at names only. Say so rather than implying full-text." - screens/07
    [Fact]
    public void Notes_and_id_are_not_searched()
    {
        Assert.Empty(SnapshotSearch.Filter([Named("Auto")], "store"));
    }

    [Fact]
    public void Segments_split_a_match_into_three_parts()
    {
        var segments = SnapshotSearch.Segments("Before 3.3 beta", "beta");

        Assert.Equal([("Before 3.3 ", false), ("beta", true)],
                     segments.Select(s => (s.Text, s.IsMatch)));
    }

    [Fact]
    public void Segments_preserve_the_original_casing_of_the_match()
    {
        var segments = SnapshotSearch.Segments("BETA test", "beta");

        Assert.Equal("BETA", segments[0].Text);
        Assert.True(segments[0].IsMatch);
    }

    [Fact]
    public void Every_occurrence_is_marked()
    {
        var segments = SnapshotSearch.Segments("beta beta", "beta");

        Assert.Equal(3, segments.Count);
        Assert.Equal([true, false, true], segments.Select(s => s.IsMatch));
    }

    [Fact]
    public void An_empty_query_yields_one_unmatched_segment()
    {
        var segments = SnapshotSearch.Segments("Auto", "");

        Assert.Equal([("Auto", false)], segments.Select(s => (s.Text, s.IsMatch)));
    }

    [Fact]
    public void A_name_with_no_match_yields_one_unmatched_segment()
    {
        Assert.Equal([("Auto", false)],
                     SnapshotSearch.Segments("Auto", "zzz").Select(s => (s.Text, s.IsMatch)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.Core.Tests --filter FullyQualifiedName~SnapshotSearchTests`
Expected: FAIL — `SnapshotSearch` does not exist.

> If `SnapshotManifest`'s constructor does not match `Named(...)` above, open `src/WaveLinkBackup.Core/Snapshots/SnapshotManifest.cs` and correct the test helper to the real parameter list. Do not change the record to fit the test.

- [ ] **Step 3: Write the search**

Create `src/WaveLinkBackup.Core/Snapshots/SnapshotSearch.cs`:

```csharp
namespace WaveLinkBackup.Core.Snapshots;

/// <summary>A run of a snapshot's name, marked according to whether the query matched it.</summary>
public readonly record struct NameSegment(string Text, bool IsMatch);

/// <summary>
/// Filtering the list. PURE - it operates on an already-loaded list and touches no disk, so
/// typing in the search field costs nothing.
///
/// Names ONLY. screens/07-search.md: "Search looks at names only. Say so rather than implying
/// full-text." The footer copy makes that promise to the user, so widening this later would
/// make the copy a lie.
/// </summary>
public static class SnapshotSearch
{
    public static IReadOnlyList<Snapshot> Filter(IReadOnlyList<Snapshot> snapshots, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return snapshots;

        return [.. snapshots.Where(s =>
            s.Manifest.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase))];
    }

    /// <summary>
    /// Splits a name into matched and unmatched runs, preserving the original casing - the
    /// shell renders the matched runs on --wl-accent-soft. Every occurrence is marked.
    /// </summary>
    public static IReadOnlyList<NameSegment> Segments(string name, string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || name.Length == 0) return [new NameSegment(name, false)];

        var segments = new List<NameSegment>();
        var position = 0;

        while (position < name.Length)
        {
            var found = name.IndexOf(query, position, StringComparison.CurrentCultureIgnoreCase);
            if (found < 0) break;

            if (found > position) segments.Add(new NameSegment(name[position..found], false));

            // Slice from the NAME, not the query, so the row shows what the user actually
            // called the backup rather than what they typed.
            segments.Add(new NameSegment(name.Substring(found, query.Length), true));
            position = found + query.Length;
        }

        if (position < name.Length) segments.Add(new NameSegment(name[position..], false));

        return segments.Count == 0 ? [new NameSegment(name, false)] : segments;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.Core.Tests --filter FullyQualifiedName~SnapshotSearchTests`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/WaveLinkBackup.Core/Snapshots/SnapshotSearch.cs \
        tests/WaveLinkBackup.Core.Tests/Snapshots/SnapshotSearchTests.cs
git commit -m "feat: filter snapshots by name, with match segments for highlighting

Segments slice from the name rather than the query so the row shows what the
user called the backup, not what they typed."
```

---

### Task 5: Disk-free reporting

The bottom bar shows `118 GB FREE ON THIS DRIVE`. Nothing in Core can answer that yet.

**Why not `DriveInfo`:** it throws on UNC paths, and keeping backups on a NAS is a supported case — it is the entire reason deletion goes to `.trash` instead of the Recycle Bin (`10-decisions.md` §3). `GetDiskFreeSpaceEx` handles UNC paths, mapped drives and local volumes alike.

Returns `long?`. When the free space cannot be determined the shell omits the readout rather than printing a wrong number.

**Files:**
- Modify: `src/WaveLinkBackup.Core/Abstractions/IFileSystem.cs`
- Modify: `src/WaveLinkBackup.Core/Abstractions/FileSystem.cs`
- Modify: `tests/WaveLinkBackup.Core.Tests/Fakes/FakeFileSystem.cs`
- Test: `tests/WaveLinkBackup.Core.Tests/Abstractions/FileSystemFreeSpaceTests.cs`

**Interfaces:**
- Produces: `long? IFileSystem.GetAvailableFreeBytes(string path)`; `FakeFileSystem.FreeBytes { get; set; }` (a `long?`, default `null`)

- [ ] **Step 1: Add the member to the interface**

In `src/WaveLinkBackup.Core/Abstractions/IFileSystem.cs`, add before the closing brace:

```csharp
    /// <summary>
    /// Bytes available to this user on the volume holding <paramref name="path"/>, or null
    /// when it cannot be determined.
    ///
    /// Null rather than 0 or a throw: the design's bottom bar shows "118 GB FREE ON THIS
    /// DRIVE", and omitting the readout is honest where printing 0 would not be.
    /// </summary>
    long? GetAvailableFreeBytes(string path);
```

- [ ] **Step 2: Run the build to verify it fails**

Run: `dotnet build WaveLinkBackup.slnx`
Expected: FAIL — `FileSystem` and `FakeFileSystem` do not implement the new member.

- [ ] **Step 3: Write the failing test**

Create `tests/WaveLinkBackup.Core.Tests/Abstractions/FileSystemFreeSpaceTests.cs`:

```csharp
using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.Core.Tests.Abstractions;

/// <summary>
/// Against the real filesystem, because the whole value of this member is the P/Invoke - a
/// fake would assert nothing about whether GetDiskFreeSpaceEx was called correctly.
/// </summary>
public sealed class FileSystemFreeSpaceTests
{
    private static readonly FileSystem Real = new();

    [Fact]
    public void Reports_a_positive_figure_for_the_temp_directory()
    {
        var free = Real.GetAvailableFreeBytes(Path.GetTempPath());

        Assert.NotNull(free);
        Assert.True(free > 0, $"Expected a positive figure, got {free}.");
    }

    [Fact]
    public void Reports_null_for_a_volume_that_does_not_exist()
    {
        Assert.Null(Real.GetAvailableFreeBytes(@"Q:\nothing\here"));
    }

    [Fact]
    public void Reports_null_rather_than_throwing_for_a_malformed_path()
    {
        Assert.Null(Real.GetAvailableFreeBytes(""));
    }
}
```

- [ ] **Step 4: Implement it on the real filesystem**

In `src/WaveLinkBackup.Core/Abstractions/FileSystem.cs`, add the P/Invoke and the method. Match the existing `DllImport` style used by `RecycleBin.cs` — open that file first and follow it:

```csharp
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    /// <summary>
    /// GetDiskFreeSpaceEx rather than DriveInfo, which throws on UNC paths. Keeping backups
    /// on a NAS is supported - it is why deletion goes to .trash instead of the Recycle Bin
    /// (screens/10-decisions.md §3) - so a UNC store must report free space like any other.
    ///
    /// Takes the FIRST EXISTING ancestor of the path: the store directory may not have been
    /// created yet when the shell first draws the bottom bar.
    /// </summary>
    public long? GetAvailableFreeBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        for (var probe = path; !string.IsNullOrEmpty(probe); probe = Path.GetDirectoryName(probe))
        {
            if (!Directory.Exists(probe)) continue;

            return GetDiskFreeSpaceExW(probe, out var available, out _, out _)
                ? (long)available
                : null;
        }

        return null;
    }
```

- [ ] **Step 5: Implement it on the fake**

In `tests/WaveLinkBackup.Core.Tests/Fakes/FakeFileSystem.cs`, add near the other test-control properties:

```csharp
    /// <summary>What GetAvailableFreeBytes reports. Null models "cannot be determined".</summary>
    public long? FreeBytes { get; set; }
```

and near the other interface members:

```csharp
    public long? GetAvailableFreeBytes(string path) => FreeBytes;
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.Core.Tests --filter FullyQualifiedName~FileSystemFreeSpaceTests`
Expected: PASS, 3 tests.

> If `Reports_null_for_a_volume_that_does_not_exist` fails because drive `Q:` exists on this machine, change the letter in the test to one that does not.

- [ ] **Step 7: Run the whole suite and a Release build**

Run: `dotnet test WaveLinkBackup.slnx`
Then: `dotnet build WaveLinkBackup.slnx -c Release`
Expected: PASS, and Release with zero warnings — `TreatWarningsAsErrors` means a new `DllImport` with a missing `SetLastError` or marshalling attribute will surface here.

- [ ] **Step 8: Commit**

```bash
git add src/WaveLinkBackup.Core/Abstractions/IFileSystem.cs \
        src/WaveLinkBackup.Core/Abstractions/FileSystem.cs \
        tests/WaveLinkBackup.Core.Tests/Fakes/FakeFileSystem.cs \
        tests/WaveLinkBackup.Core.Tests/Abstractions/FileSystemFreeSpaceTests.cs
git commit -m "feat: report free space on the store's volume

GetDiskFreeSpaceEx rather than DriveInfo, which throws on UNC paths - and a UNC
store is supported, being the reason deletion goes to .trash at all. Returns
null when unknown so the bottom bar can omit the figure instead of printing 0."
```

---

## Done when

- [ ] `dotnet build WaveLinkBackup.slnx -c Release` — zero warnings
- [ ] `dotnet test WaveLinkBackup.slnx` — all green, and **385** tests (351 baseline + 34 new: 8 + 8 + 6 + 9 + 3)
- [ ] `wlbackup list` honours a `storePath` written into `settings.json`
- [ ] `wlbackup list --store D:\elsewhere` uses the flag and leaves `settings.json` byte-identical

## Deviations, as built

Recorded so the plan matches the commits rather than quietly disagreeing with them.

| Planned | Built | Why |
|---|---|---|
| Tests in `Automation/`, `Snapshots/`, `Abstractions/` subfolders | Flat at the test-project root | Every existing test file is flat; only `Fakes/` is a folder |
| A new `FileSystemFreeSpaceTests.cs` | Four tests appended to the existing `FileSystemTests.cs` | That file already tests the real adapter against a real temp directory |
| 3 free-space tests | 4 | Added `Free_space_falls_back_to_the_first_existing_ancestor`, which pins the behaviour the bottom bar depends on before the store exists |
| 34 new tests | 35 (386 total) | The extra ancestor test |
| `EnumerateFiles(dir, "*.tmp")` | `EnumerateFiles(dir, "*")` filtered on the extension | `FakeFileSystem.Glob` only understands `prefix*`, so the planned assertion would have passed vacuously |

**Not verified:** the NativeAOT publish. `dotnet publish -p:PublishAot=true` fails in this
environment at the native link step — `vswhere.exe` is not resolvable, so the MSVC linker cannot
be located. Managed compilation completed with no AOT or trim warnings, and the new
`GetDiskFreeSpaceEx` `DllImport` follows the same shape as `RecycleBin`, which is already
AOT-verified. The default publish (self-contained single-file, 70.3 MB) succeeds.

## What this plan does not do

Plan 2 (tray shell) needs `SettingsRepository` and `BackupSettings`; both exist after this.
Plan 4 (screen 1) needs `SnapshotSearch` and `GetAvailableFreeBytes`; both exist after this.
Nothing here touches the App project, and no XAML is written.
