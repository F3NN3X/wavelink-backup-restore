<#
.SYNOPSIS
    Answers technical-debt.md 7.6: does Wave Link resolve a channel's plug-in by PluginId,
    or by FilePath?

.DESCRIPTION
    Scripts the protocol in _docs/audits/2026-08-20-plugin-resolution-and-elevation.md 3,
    which is otherwise about ten minutes of careful manual file surgery on a live install.
    The manual version is not hard, it is just easy to get half-way through and lose track of
    which copy is the original - so the state lives in a journal file rather than in your head,
    and -Undo works from a fresh shell, after a reboot, or a week later.

    The experiment moves one plug-in that is on a channel out of the shared VST3 folder and
    into the user-level one, then asks whether the channel still loads. It is reversible. The
    original is RENAMED, never deleted, and -Undo puts it back.

.PARAMETER Status
    Read-only. Reports where the experiment stands and what it would act on. Safe at any time.

.PARAMETER Setup
    Steps 2 and 3: copy the chosen plug-in to the user VST3 folder, rename the shared copy so
    the recorded path stops resolving. Refuses if a journal already exists.

.PARAMETER Record
    Steps 5 and 6, after you have restarted Wave Link and looked at the channel. Reads
    Settings.json, compares FilePath against what was recorded, and prints the outcome row from
    the audit's table. Pass -EffectLoaded or -EffectDropped to say what you saw.

.PARAMETER Undo
    Step 7. Puts the shared copy back, removes the user-folder copy, retires the journal.
    Idempotent, and safe to run even if Setup failed part-way.

.PARAMETER PluginName
    Which plug-in to move. Defaults to the first on-channel plug-in whose recorded FilePath
    resolves. The audit suggests "Pro-L 2".

.EXAMPLE
    .\tools\plugin-resolution-experiment.ps1 -Status
    .\tools\plugin-resolution-experiment.ps1 -Setup
    # quit Wave Link, start it again, let the scan finish, look at the channel
    .\tools\plugin-resolution-experiment.ps1 -Record -EffectLoaded
    .\tools\plugin-resolution-experiment.ps1 -Undo

.NOTES
    Tracked as technical-debt.md 7.6. When the run is done, close that entry with the observed
    outcome and its consequence from the audit's table.
#>
[CmdletBinding(DefaultParameterSetName = 'Status')]
param(
    [Parameter(ParameterSetName = 'Status')]  [switch] $Status,
    [Parameter(ParameterSetName = 'Setup')]   [switch] $Setup,
    [Parameter(ParameterSetName = 'Record')]  [switch] $Record,
    [Parameter(ParameterSetName = 'Undo')]    [switch] $Undo,

    [Parameter(ParameterSetName = 'Setup')]   [string] $PluginName,
    [Parameter(ParameterSetName = 'Setup')]   [switch] $SkipBackupCheck,

    [Parameter(ParameterSetName = 'Record')]  [switch] $EffectLoaded,
    [Parameter(ParameterSetName = 'Record')]  [switch] $EffectDropped
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Locating things. Deliberately mirrors Core's SettingsLocator: glob the package
# family, resolve LocalAppData rather than composing %LOCALAPPDATA%, and never
# look at %APPDATA% - that path is the decoy, and a tool that resolves by vendor
# folder silently protects nothing.
# See _docs/knowledge-base/gotchas/backup-succeeds-but-protects-nothing.md
# ---------------------------------------------------------------------------

$LocalAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$JournalPath = Join-Path $LocalAppData 'WaveLinkBackup\plugin-resolution-experiment.json'
$UserVst3 = Join-Path $LocalAppData 'Programs\Common\VST3'

function Get-SettingsPath {
    $packages = Join-Path $LocalAppData 'Packages'
    if (-not (Test-Path $packages)) { throw "No Packages directory at $packages." }

    $found = @(
        Get-ChildItem -Path $packages -Directory -Filter 'Elgato.WaveLink_*' -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName 'LocalState\Settings.json' } |
            Where-Object { Test-Path $_ }
    )

    # Never guess. Picking one silently would experiment on the wrong installation.
    if ($found.Count -eq 0) {
        throw 'Wave Link not found: no Elgato.WaveLink_* package holds a Settings.json.'
    }
    if ($found.Count -gt 1) {
        throw "Multiple Wave Link packages found; resolve by hand: $($found -join ', ')"
    }
    return $found[0]
}

function Read-Settings([string] $Path) {
    # FileShare.ReadWrite | FileShare.Delete, the same share mode as Core's FileSystem.OpenShared.
    # ReadAllText takes an exclusive-enough handle that it throws "used by another process" the
    # moment Wave Link is open - which is always, on the rig this experiment runs on. ReadWrite
    # permits Wave Link's existing handle; Delete tolerates the file being replaced underneath
    # us, which is what its atomic-save does. SourceGuardTests enforces this rule in C#; the
    # same reasoning applies here.
    $stream = [IO.FileStream]::new(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally {
        $stream.Dispose()
    }

    # A stray BOM makes ConvertFrom-Json fail obscurely.
    return $text.TrimStart([char]0xFEFF) | ConvertFrom-Json
}

# Every plug-in on every input channel that carries a non-empty FilePath, flattened.
function Get-OnChannelPlugins([object] $Settings) {
    $inputs = $Settings.MixerConfiguration.InputSettings

    foreach ($property in $inputs.PSObject.Properties) {
        $channel = $property.Value

        $hasConfigs = $channel.PSObject.Properties.Name -contains 'AudioPluginConfigurations'
        if (-not $hasConfigs) { continue }

        $configs = $channel.AudioPluginConfigurations
        if ($null -eq $configs) { continue }

        foreach ($plugin in $configs) {
            if ([string]::IsNullOrWhiteSpace($plugin.FilePath)) { continue }

            [pscustomobject]@{
                Channel  = $property.Name
                Name     = $plugin.Name
                PluginId = $plugin.PluginId
                FilePath = $plugin.FilePath
                Exists   = Test-Path -LiteralPath $plugin.FilePath
            }
        }
    }
}

function Get-Journal {
    if (-not (Test-Path $JournalPath)) { return $null }
    return Get-Content -LiteralPath $JournalPath -Raw | ConvertFrom-Json
}

function Save-Journal([object] $Journal) {
    $dir = Split-Path -Parent $JournalPath
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $Journal | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $JournalPath -Encoding utf8
}

# The app process is called Elgato.WaveLink, NOT WaveLink. The name is already in Core -
# WaveLinkProcess.ProcessNames - and this script originally guessed 'WaveLink' instead of reading
# it. The guess cost a run: -Setup's "quit Wave Link first" guard never fired, so it renamed a
# plug-in bundle underneath a live Wave Link that had been up for fourteen hours. Nothing broke,
# but the experiment was void - the answer turns on Wave Link RESCANNING after the rename, and no
# restart had happened. A guard that cannot fire is worse than no guard: it reads as a safety
# check in the transcript.
#
# ONLY THE APP BLOCKS. Core's WaveLinkProcess lists WavelinkSEService alongside it, because a
# RESTORE has to stop both - the service holds settings too. This script is not a restore. The
# service is a Windows service that is up essentially all the time, so blocking on it would make
# -Setup and -Undo refuse forever, and the file this experiment moves is a VST3 bundle held by the
# app. The service is reported by -Status and ignored by the guards, deliberately.
$WaveLinkAppProcessName = 'Elgato.WaveLink'
$WaveLinkServiceProcessName = 'WavelinkSEService'

function Get-RunningWaveLinkProcesses {
    $found = @(
        Get-Process -Name $WaveLinkAppProcessName -ErrorAction SilentlyContinue | Where-Object { $_ }
    )

    # `return $found` UNROLLS a one-element array back to the bare object, so with only
    # WavelinkSEService up this handed back a Process rather than an array and every caller's
    # .Count threw under Set-StrictMode. The leading comma wraps it so the array survives.
    # Two processes hid this: it only misbehaves when exactly one of them is running.
    return , $found
}

function Test-WaveLinkRunning {
    return (Get-RunningWaveLinkProcesses).Count -gt 0
}

function Write-Section([string] $Text) {
    Write-Host ''
    Write-Host $Text -ForegroundColor Cyan
    Write-Host ('-' * $Text.Length) -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# Status
# ---------------------------------------------------------------------------

function Invoke-Status {
    $settingsPath = Get-SettingsPath
    $settings = Read-Settings $settingsPath
    $plugins = @(Get-OnChannelPlugins $settings)
    $journal = Get-Journal

    Write-Section 'Install'
    Write-Host "  Settings.json    $settingsPath"
    Write-Host "  User VST3        $UserVst3"
    if (-not (Test-Path $UserVst3)) { Write-Host '                   (does not exist yet)' -ForegroundColor DarkGray }
    $running = Get-RunningWaveLinkProcesses
    if ($running.Count -gt 0) {
        $names = ($running | ForEach-Object ProcessName | Sort-Object -Unique) -join ', '
        Write-Host "  Wave Link        RUNNING ($names)" -ForegroundColor Yellow
    } else {
        Write-Host "  Wave Link        not running (app process: $WaveLinkAppProcessName)"
    }

    $service = @(Get-Process -Name $WaveLinkServiceProcessName -ErrorAction SilentlyContinue)
    $serviceState = $service.Count -gt 0 ? 'running' : 'not running'
    Write-Host "  Wave Link svc    $serviceState ($WaveLinkServiceProcessName) - not a blocker here"

    Write-Section "On-channel plug-ins ($($plugins.Count))"
    if ($plugins.Count -eq 0) {
        Write-Host '  None. This experiment needs a channel with a third-party plug-in on it.' -ForegroundColor Yellow
    } else {
        $plugins | Format-Table Channel, Name, PluginId, Exists, FilePath -AutoSize |
            Out-String | Write-Host
    }

    Write-Section 'Experiment state'
    if (-not $journal) {
        Write-Host '  Not set up. Run -Setup to begin.' -ForegroundColor Green
        return
    }

    Write-Host "  Plug-in          $($journal.Name)  ($($journal.PluginId)) on $($journal.Channel)"
    Write-Host "  Started          $($journal.StartedAt)"
    Write-Host "  Original path    $($journal.OriginalPath)"
    Write-Host "  Renamed to       $($journal.RenamedPath)"
    if (Test-Path -LiteralPath $journal.RenamedPath) {
        Write-Host '                   present'
    } else {
        Write-Host '                   MISSING' -ForegroundColor Red
    }
    Write-Host "  User-folder copy $($journal.UserCopyPath)"
    if (Test-Path -LiteralPath $journal.UserCopyPath) {
        Write-Host '                   present'
    } else {
        Write-Host '                   MISSING' -ForegroundColor Red
    }
    Write-Host ''
    Write-Host '  The install is MID-EXPERIMENT. Run -Undo to put it back.' -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

function Invoke-Setup {
    if (Get-Journal) {
        throw "A journal already exists at $JournalPath. The install is mid-experiment - run -Undo first."
    }

    if (Test-WaveLinkRunning) {
        $names = (Get-RunningWaveLinkProcesses | ForEach-Object ProcessName | Sort-Object -Unique) -join ', '
        throw ("Wave Link is running ($names). Quit it first: it holds plug-in files open, it " +
               'rewrites Settings.json on exit, and - the reason this experiment exists - the ' +
               'answer turns on it RESCANNING after the rename, which only a restart does.')
    }

    $settingsPath = Get-SettingsPath
    $settings = Read-Settings $settingsPath
    $candidates = @(Get-OnChannelPlugins $settings | Where-Object { $_.Exists })

    if ($candidates.Count -eq 0) {
        throw 'No on-channel plug-in has a FilePath that resolves. Nothing to move.'
    }

    if ($PluginName) {
        $chosen = $candidates | Where-Object { $_.Name -like "*$PluginName*" } | Select-Object -First 1
        if (-not $chosen) {
            throw "No on-channel plug-in matches '$PluginName'. Run -Status to see the list."
        }
    } else {
        $chosen = $candidates[0]
    }

    # Step 1 of the audit's protocol. The tool exists for this, and this is the moment it is for.
    if (-not $SkipBackupCheck) {
        Write-Section 'Backup'
        Write-Host '  This experiment is reversible, but it is not free: it edits a live install.'
        Write-Host '  Take a snapshot first:'
        Write-Host ''
        Write-Host '      wlbackup backup' -ForegroundColor Green
        Write-Host ''
        $answer = Read-Host '  Has a backup been taken? [y/N]'
        if ($answer -notmatch '^(y|yes)$') {
            Write-Host '  Stopping. Take the backup, then run -Setup again.' -ForegroundColor Yellow
            return
        }
    }

    $originalPath = $chosen.FilePath
    $renamedPath = "$originalPath.bak"
    $userCopyPath = Join-Path $UserVst3 (Split-Path -Leaf $originalPath)

    if (Test-Path -LiteralPath $renamedPath) {
        throw "$renamedPath already exists. Move it aside by hand - this script will not overwrite it."
    }
    if (Test-Path -LiteralPath $userCopyPath) {
        throw "$userCopyPath already exists. Move it aside by hand - this script will not overwrite it."
    }

    Write-Section 'Applying'
    Write-Host "  Plug-in    $($chosen.Name)  ($($chosen.PluginId)) on $($chosen.Channel)"

    if (-not (Test-Path $UserVst3)) {
        New-Item -ItemType Directory -Path $UserVst3 -Force | Out-Null
        Write-Host "  Created    $UserVst3"
    }

    # Copy BEFORE renaming, so a failure here leaves the install untouched.
    # A .vst3 is usually a bundle directory on disk, not a file - copy it as a tree.
    $isBundle = (Get-Item -LiteralPath $originalPath) -is [IO.DirectoryInfo]
    if ($isBundle) {
        Copy-Item -LiteralPath $originalPath -Destination $userCopyPath -Recurse -Force
    } else {
        Copy-Item -LiteralPath $originalPath -Destination $userCopyPath -Force
    }
    Write-Host "  Copied to  $userCopyPath"

    # Rename, never delete. This is the step that makes the recorded path stop resolving,
    # and the one -Undo reverses.
    Rename-Item -LiteralPath $originalPath -NewName (Split-Path -Leaf $renamedPath)
    Write-Host "  Renamed    $originalPath"
    Write-Host "         ->  $renamedPath"

    Save-Journal ([pscustomobject]@{
        StartedAt       = (Get-Date).ToString('o')
        SettingsPath    = $settingsPath
        Channel         = $chosen.Channel
        Name            = $chosen.Name
        PluginId        = $chosen.PluginId
        OriginalPath    = $originalPath
        RenamedPath     = $renamedPath
        UserCopyPath    = $userCopyPath
        WasBundle       = $isBundle
        FilePathAtSetup = $chosen.FilePath
    })

    Write-Section 'Next'
    Write-Host '  1. Start Wave Link and let the plug-in scan finish.'
    Write-Host "  2. Look at the '$($chosen.Channel)' channel: does '$($chosen.Name)' still load?"
    Write-Host '  3. Quit Wave Link, so it flushes Settings.json.'
    Write-Host '  4. Run one of:'
    Write-Host ''
    Write-Host '        .\tools\plugin-resolution-experiment.ps1 -Record -EffectLoaded' -ForegroundColor Green
    Write-Host '        .\tools\plugin-resolution-experiment.ps1 -Record -EffectDropped' -ForegroundColor Green
    Write-Host ''
    Write-Host '  Then -Undo, whatever the answer.' -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# Record
# ---------------------------------------------------------------------------

function Invoke-Record {
    $journal = Get-Journal
    if (-not $journal) { throw 'No journal. Run -Setup first.' }

    if ($EffectLoaded -and $EffectDropped) {
        throw 'Pass one of -EffectLoaded or -EffectDropped, not both.'
    }
    if (-not $EffectLoaded -and -not $EffectDropped) {
        throw 'Say what you saw: pass -EffectLoaded or -EffectDropped.'
    }

    if (Test-WaveLinkRunning) {
        Write-Host ('Wave Link is still running. It writes Settings.json on exit, so the FilePath ' +
                    'below may be stale. Quit it and re-run for a reliable read.') -ForegroundColor Yellow
    }

    $settings = Read-Settings $journal.SettingsPath
    $entry = Get-OnChannelPlugins $settings |
        Where-Object { $_.PluginId -eq $journal.PluginId } |
        Select-Object -First 1

    Write-Section 'Observed'
    Write-Host "  Plug-in           $($journal.Name)  ($($journal.PluginId))"
    if ($EffectLoaded) {
        Write-Host '  Effect on channel LOADED'
    } else {
        Write-Host '  Effect on channel DROPPED'
    }
    Write-Host "  FilePath at setup $($journal.FilePathAtSetup)"

    if (-not $entry) {
        Write-Host '  FilePath now      (the entry is gone from Settings.json entirely)' -ForegroundColor Yellow
        $rewritten = $false
        $filePathAfter = $null
    } else {
        Write-Host "  FilePath now      $($entry.FilePath)"
        $rewritten = $entry.FilePath -ne $journal.FilePathAtSetup
        $filePathAfter = $entry.FilePath
    }

    Write-Section 'What it means'
    if ($EffectDropped) {
        Write-Host '  Wave Link resolves by FilePath.' -ForegroundColor Red
        Write-Host '  Consequence: the user-level folder is NOT a viable fallback for tier 4.'
        Write-Host '  Restoring a plug-in elsewhere would silently break the channel.'
        $verdict = 'resolves-by-filepath'
    } elseif ($rewritten) {
        Write-Host '  Wave Link resolves by PluginId, then repairs the path.' -ForegroundColor Green
        Write-Host '  Consequence: the user folder is a VIABLE fallback destination for tier 4.'
        $verdict = 'resolves-by-pluginid-repairs-path'
    } else {
        Write-Host '  Wave Link resolves by PluginId; the path is advisory.' -ForegroundColor Green
        Write-Host '  Consequence: viable, but the settings keep a stale path - worth noting'
        Write-Host '  before relying on it.'
        $verdict = 'resolves-by-pluginid-path-advisory'
    }

    $journal | Add-Member -NotePropertyName Verdict -NotePropertyValue $verdict -Force
    $journal | Add-Member -NotePropertyName EffectLoaded -NotePropertyValue ([bool] $EffectLoaded) -Force
    $journal | Add-Member -NotePropertyName FilePathAfter -NotePropertyValue $filePathAfter -Force
    $journal | Add-Member -NotePropertyName RecordedAt -NotePropertyValue ((Get-Date).ToString('o')) -Force
    Save-Journal $journal

    Write-Section 'Next'
    Write-Host "  Recorded in $JournalPath"
    Write-Host '  Run -Undo to put the install back, then close technical-debt.md 7.6 with the'
    Write-Host '  verdict above and its consequence from the audit table.'
}

# ---------------------------------------------------------------------------
# Undo
# ---------------------------------------------------------------------------

function Invoke-Undo {
    $journal = Get-Journal
    if (-not $journal) {
        Write-Host 'No journal - nothing to undo.' -ForegroundColor Green
        return
    }

    if (Test-WaveLinkRunning) {
        $names = (Get-RunningWaveLinkProcesses | ForEach-Object ProcessName | Sort-Object -Unique) -join ', '
        throw "Wave Link is running ($names). Quit it first, so the plug-in files are not held open."
    }

    Write-Section 'Reversing'

    # Order matters: put the original back BEFORE removing the copy, so a failure part-way
    # never leaves the machine with neither.
    if (Test-Path -LiteralPath $journal.RenamedPath) {
        if (Test-Path -LiteralPath $journal.OriginalPath) {
            throw ("Both $($journal.OriginalPath) and $($journal.RenamedPath) exist. Resolve by " +
                   'hand; this script will not choose which is authoritative.')
        }
        Rename-Item -LiteralPath $journal.RenamedPath -NewName (Split-Path -Leaf $journal.OriginalPath)
        Write-Host "  Restored   $($journal.OriginalPath)"
    } else {
        Write-Host "  Already back or never renamed: $($journal.RenamedPath) is not there." -ForegroundColor DarkGray
    }

    if (Test-Path -LiteralPath $journal.UserCopyPath) {
        if ($journal.WasBundle) {
            Remove-Item -LiteralPath $journal.UserCopyPath -Recurse -Force
        } else {
            Remove-Item -LiteralPath $journal.UserCopyPath -Force
        }
        Write-Host "  Removed    $($journal.UserCopyPath)"
    } else {
        Write-Host '  No user-folder copy to remove.' -ForegroundColor DarkGray
    }

    # Retire rather than delete: the verdict recorded by -Record lives in here, and it is the
    # whole point of having run the thing.
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $done = Join-Path (Split-Path -Parent $JournalPath) "plugin-resolution-experiment.$stamp.done.json"
    Move-Item -LiteralPath $JournalPath -Destination $done
    Write-Host "  Journal    -> $done"

    Write-Section 'Next'
    Write-Host '  Start Wave Link and confirm the channel is as it was before the experiment.'
    if ($journal.PSObject.Properties.Name -contains 'Verdict') {
        Write-Host "  Verdict recorded earlier: $($journal.Verdict)" -ForegroundColor Green
    }
}

switch ($PSCmdlet.ParameterSetName) {
    'Setup'  { Invoke-Setup }
    'Record' { Invoke-Record }
    'Undo'   { Invoke-Undo }
    default  { Invoke-Status }
}
