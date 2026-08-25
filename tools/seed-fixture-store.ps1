<#
.SYNOPSIS
    Builds a throwaway snapshot store holding the rigs item 5 of the by-eye checklist needs.

.DESCRIPTION
    technical-debt.md 8.2's last open tail is a look, not a commit: the INPUTS verdict on a
    five-input row and on a collapsed rig, the same cell at nine and twelve channels, and the
    details dialog's routing matrix in light and in a real high-contrast scheme.

    Getting to those states by hand means adding and removing channels in Wave Link and taking a
    snapshot after each - half an hour of rig surgery on a live install to look at four things.
    This writes the snapshots directly instead. They are synthetic but structurally real: the
    manifests carry the fields the row reads, the settings payloads carry the MixerConfiguration
    the details dialog parses, and every sha256 in a manifest is the true hash of the file beside
    it, so SnapshotGuard verifies them rather than flagging the lot as damaged.

    NOTHING here touches the real store. The fixture store is a separate directory, and the
    script refuses to write into %LOCALAPPDATA%\WaveLinkBackup. Point the app at it through
    Settings, do the looks, point it back.

.PARAMETER Path
    Where to build the store. Defaults to a folder under TEMP.

.PARAMETER Force
    Overwrite an existing fixture store at that path.

.EXAMPLE
    .\tools\seed-fixture-store.ps1
    # then: app -> Settings -> change the backup folder to the path it prints
    #       do the looks, tick item 5, change the folder back

.NOTES
    The snapshots this writes are NOT restorable onto a real machine - the endpoint ids are
    invented, so restoring one would describe channels no device on this rig has. They exist to
    be looked at. Delete the folder when the sitting is done.
#>
[CmdletBinding()]
param(
    [string] $Path,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$LocalAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$RealStore = Join-Path $LocalAppData 'WaveLinkBackup'

if (-not $Path) { $Path = Join-Path ([IO.Path]::GetTempPath()) 'wlbackup-fixture-store' }
$Path = [IO.Path]::GetFullPath($Path)

# The one refusal that matters. A fixture snapshot in the real store would look exactly like a
# real one in the list, and the user would find out by restoring it.
$normalisedReal = $RealStore.TrimEnd('\')
$normalisedPath = $Path.TrimEnd('\')
if ($normalisedPath -eq $normalisedReal -or $normalisedPath.StartsWith("$normalisedReal\", 'OrdinalIgnoreCase')) {
    throw "Refusing to seed inside the real store ($RealStore). Pass -Path somewhere else."
}

if (Test-Path $Path) {
    if (-not $Force) {
        throw "$Path already exists. Pass -Force to rebuild it, or delete it first."
    }
    Remove-Item -LiteralPath $Path -Recurse -Force
}
New-Item -ItemType Directory -Path $Path -Force | Out-Null

# ---------------------------------------------------------------------------
# Building a settings payload
#
# Only the shape ConfigurationDetail actually parses, which is why this is
# hand-built rather than copied from a real file: a real Settings.json carries
# device serials and a username in every absolute path, and this is a file that
# gets committed to nobody-knows-where. See technical-debt.md 6.
# ---------------------------------------------------------------------------

$MixNames = @('Stream', 'Local', 'Chat')

function New-Effect([string] $Name, [string] $Vendor, [bool] $Bypassed) {
    return [ordered]@{
        Name        = $Name
        Vendor      = $Vendor
        Category    = 'Fx'
        BypassState = $Bypassed
        # A plausible shared-folder path, with no username in it.
        FilePath    = "C:\Program Files\Common Files\VST3\$Vendor\$Name.vst3"
        PluginId    = ('{0:x8}' -f ($Name.GetHashCode() -band 0x7FFFFFFF))
    }
}

function New-Settings {
    param(
        [string[]] $InputNames,
        [int] $EffectsPerChannel = 0,
        [switch] $Generic
    )

    $inputs = [ordered]@{}
    $index = 0

    foreach ($name in $InputNames) {
        $index++

        # A Core Audio endpoint id is the real key. Invented here, and deliberately not
        # serial-shaped: nothing in a fixture should look like it came off a device.
        $endpointId = 'FIXTURE-RIG\\PCM_IN_{0:D2}_C_00_SD1' -f $index

        # A collapsed rig routes to fewer mixes - Wave Link's fallback state, and the thing the
        # amber verdict is about.
        $mixIds = $Generic ? @($MixNames[0]) : @($MixNames | Select-Object -First (($index % 3) + 1))

        $effects = @()
        for ($e = 1; $e -le $EffectsPerChannel; $e++) {
            $effects += New-Effect "Effect $e" 'FixtureAudio' ($e % 4 -eq 0)
        }

        $inputs[$endpointId] = [ordered]@{
            InputName                 = $name
            WaveDeviceType            = $Generic ? 'Unknown' : 'Microphone'
            IsHiddenFromMixes         = $false
            MixerIds                  = $mixIds
            AudioPluginConfigurations = $effects
        }
    }

    $mixSettings = [ordered]@{}
    foreach ($mix in $MixNames) {
        $mixSettings[$mix] = [ordered]@{
            Name          = $mix
            IsMuted       = $false
            OutputDevices = @(
                [ordered]@{ Name = "$mix Output"; FriendlyName = "$mix Output (Fixture Device)" }
            )
        }
    }

    return [ordered]@{
        MixerConfiguration = [ordered]@{
            InputSettings            = $inputs
            MixSettings              = $mixSettings
            MainOutputDeviceSettings = [ordered]@{
                Name         = 'Main Output'
                FriendlyName = 'Main Output (Fixture Device)'
            }
        }
    }
}

function Get-Sha256Hex([byte[]] $Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function New-Snapshot {
    param(
        [string] $DisplayName,
        [string] $Notes,
        [string[]] $InputNames,
        [int] $EffectsPerChannel = 0,
        [switch] $Generic,
        [datetime] $CreatedUtc
    )

    $settings = New-Settings -InputNames $InputNames -EffectsPerChannel $EffectsPerChannel -Generic:$Generic
    $json = $settings | ConvertTo-Json -Depth 12
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)

    $sha = Get-Sha256Hex $bytes
    $stamp = $CreatedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HHmm")
    $id = "$stamp-$($sha.Substring(0, 6))"

    $dir = Join-Path $Path $id
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $dir 'settings.json'), $bytes)

    $effectChannels = $EffectsPerChannel -gt 0 ? $InputNames.Count : 0

    # Field names and casing are ManifestSerializer's, exactly - this file is read by the same
    # parser the app uses, so a typo here reads as a damaged snapshot rather than a bad fixture.
    $manifest = [ordered]@{
        schemaVersion      = 1
        displayName        = $DisplayName
        notes              = $Notes
        createdUtc         = $CreatedUtc.ToUniversalTime().ToString('O')
        trigger            = 'manual'
        settingsSha256     = $sha
        waveLinkVersion    = '3.3.0.4108'
        inputCount         = $InputNames.Count
        inputNames         = $InputNames
        effectCount        = $EffectsPerChannel * $InputNames.Count
        effectChannelCount = $effectChannels
        hasDuplicateKeys   = $false
        tiers              = @('settings')
        files              = [ordered]@{
            'settings.json' = [ordered]@{ sha256 = $sha; sizeBytes = $bytes.Length }
        }
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllBytes(
        (Join-Path $dir 'manifest.json'),
        [Text.UTF8Encoding]::new($false).GetBytes($manifestJson))

    Write-Host ("  {0,-28} {1,2} inputs  {2}" -f $DisplayName, $InputNames.Count, $id)
}

# ---------------------------------------------------------------------------
# The rigs item 5 asks for.
#
# The verdict is InputCount >= 5, not a comparison against the previous snapshot
# (SnapshotRowViewModel.InputsVerdict), so the collapsed row is a genuinely short
# rig rather than an ordering trick.
# ---------------------------------------------------------------------------

$base = [datetime]::new(2026, 8, 20, 9, 0, 0, [DateTimeKind]::Utc)

Write-Host ''
Write-Host "Seeding fixture store: $Path" -ForegroundColor Cyan
Write-Host ('-' * 60) -ForegroundColor DarkGray

New-Snapshot -DisplayName 'Five inputs, all named' `
    -Notes 'The design rig. Verdict should read Complete / 5 INPUTS - ALL NAMED, in the ok colour.' `
    -InputNames @('Wave Mic 1', 'Voice', 'Browser', 'Game', 'System') `
    -CreatedUtc $base

New-Snapshot -DisplayName 'Collapsed rig, two inputs' `
    -Notes 'Verdict should read Only part of your setup / 2 INPUTS - UNNAMED, amber, warning triangle.' `
    -InputNames @('Input 1', 'Input 2') -Generic `
    -CreatedUtc $base.AddHours(1)

New-Snapshot -DisplayName 'Nine channels' `
    -Notes 'Four-character labels. The cell should read LESS crowded than the old strip did.' `
    -InputNames @('Wave Mic 1', 'Voice', 'Browser', 'Game', 'System', 'Music', 'Alerts', 'Chat', 'Capture') `
    -CreatedUtc $base.AddHours(2)

New-Snapshot -DisplayName 'Twelve channels' `
    -Notes 'Three-character labels, past the point where the old strip dropped them entirely.' `
    -InputNames @('Wave Mic 1', 'Voice', 'Browser', 'Game', 'System', 'Music',
                  'Alerts', 'Chat', 'Capture', 'Aux 1', 'Aux 2', 'Monitor') `
    -CreatedUtc $base.AddHours(3)

New-Snapshot -DisplayName 'Long effect chains' `
    -Notes 'Six effects on each of five channels: the details dialog should hit its 720px cap and scroll.' `
    -InputNames @('Wave Mic 1', 'Voice', 'Browser', 'Game', 'System') `
    -EffectsPerChannel 6 `
    -CreatedUtc $base.AddHours(4)

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Write-Host ''
Write-Host 'Next:' -ForegroundColor Cyan
Write-Host '  1. Open the app, Settings, change the backup folder to:'
Write-Host "       $Path" -ForegroundColor Yellow
Write-Host '  2. Work item 5 of operations/design/screen-1-by-eye-checklist.md:'
Write-Host '       - the verdict on the five-input row, and on the collapsed row'
Write-Host '       - the verdict at nine and twelve channels'
Write-Host '       - the details dialog matrix on "Long effect chains", light and high-contrast'
Write-Host '  3. Change the backup folder back to your real store.'
Write-Host "  4. Remove-Item -Recurse -Force '$Path'"
Write-Host ''
Write-Host '  These snapshots are for looking at, not restoring: the endpoint ids are invented.' -ForegroundColor DarkGray
