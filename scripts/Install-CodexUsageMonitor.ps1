#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$SourceExecutable,
    [string]$InstallDirectory,
    [switch]$EnableStartup,
    [switch]$ForceStartupReplace,
    [switch]$Start,

    [ValidateRange(1, 60)]
    [int]$ShutdownTimeoutSeconds = 10
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Package.Common.ps1')

Assert-CumWindowsX64

if ([string]::IsNullOrWhiteSpace($SourceExecutable))
{
    $SourceExecutable = Join-Path `
        (Split-Path -Parent $PSScriptRoot) `
        'release\CodexUsageMonitor.exe'
}
$source = Resolve-CumFullPath $SourceExecutable
if (-not (Test-Path -LiteralPath $source -PathType Leaf))
{
    throw "Release executable was not found: $source"
}

if ([string]::IsNullOrWhiteSpace($InstallDirectory))
{
    $InstallDirectory = Get-CumDefaultInstallDirectory
}
$installRoot = Assert-CumInstallTargetOwnership $InstallDirectory
$destination = Assert-CumSafeMutationPath (
    Join-Path $installRoot 'CodexUsageMonitor.exe')
$existingMarker = Read-CumInstallMarker $installRoot
$markerStartupEntryWasReplaced = $false
$markerPreviousStartupCommand = $null
if ($null -ne $existingMarker -and
    $existingMarker.PSObject.Properties.Match(
        'StartupEntryWasReplaced').Count -gt 0 -and
    [bool]$existingMarker.StartupEntryWasReplaced)
{
    $markerStartupEntryWasReplaced = $true
    if ($existingMarker.PSObject.Properties.Match(
        'PreviousStartupCommand').Count -gt 0)
    {
        $markerPreviousStartupCommand = [string](
            $existingMarker.PreviousStartupCommand)
    }
}
if ($ForceStartupReplace -and -not $EnableStartup)
{
    throw '-ForceStartupReplace requires -EnableStartup.'
}
if ($EnableStartup)
{
    Get-CumStartupReplacementPlan `
        -Executable $destination `
        -ForceStartupReplace:$ForceStartupReplace | Out-Null
}

$sourceHash = Get-CumSha256 $source
$hashManifest = Join-Path (Split-Path -Parent $source) 'SHA256SUMS.txt'
$hashVerified = $false
if (Test-Path -LiteralPath $hashManifest -PathType Leaf)
{
    $expectedHash = Get-CumExpectedReleaseHash $hashManifest
    if ($sourceHash -ne $expectedHash)
    {
        throw (
            'Release SHA-256 mismatch. Expected ' +
            $expectedHash +
            ', found ' +
            $sourceHash +
            '.')
    }
    $hashVerified = $true
}
else
{
    Write-Warning (
        'SHA256SUMS.txt was not found beside the release executable. ' +
        'The install can continue, but release provenance was not verified.')
}

$rollbackRoot = Assert-CumSafeMutationPath (Get-CumRollbackDirectory)
$rollbackExecutable = Assert-CumSafeMutationPath (
    Join-Path $rollbackRoot 'CodexUsageMonitor.previous.exe')
$rollbackMetadata = Assert-CumSafeMutationPath (
    Join-Path $rollbackRoot 'rollback.json')
$hadPreviousVersion = Test-Path -LiteralPath $destination -PathType Leaf
$previousVersion = $null
$previousHash = $null
$stoppedProcessIds = @()
$installed = $false
$started = $false
$startupEntryReplaced = $markerStartupEntryWasReplaced
$previousStartupCommand = $markerPreviousStartupCommand

if ($PSCmdlet.ShouldProcess(
    $destination,
    'Install or upgrade Codex Usage Monitor'))
{
    $installRoot = Assert-CumInstallTargetOwnership $installRoot
    $destination = Assert-CumSafeMutationPath (
        Join-Path $installRoot 'CodexUsageMonitor.exe')
    $rollbackRoot = Assert-CumSafeMutationPath $rollbackRoot
    $rollbackExecutable = Assert-CumSafeMutationPath $rollbackExecutable
    $rollbackMetadata = Assert-CumSafeMutationPath $rollbackMetadata

    $controlExecutable = if ($hadPreviousVersion)
    {
        $destination
    }
    else
    {
        $source
    }
    $stoppedProcessIds = @(
        Stop-CumMonitorGracefully `
            -ControlExecutable $controlExecutable `
            -TimeoutSeconds $ShutdownTimeoutSeconds)

    if ($hadPreviousVersion)
    {
        $previousVersion = Get-CumFileVersion $destination
        $previousHash = Get-CumSha256 $destination
        Copy-CumFileAtomically `
            -Source $destination `
            -Destination $rollbackExecutable

        $metadata = [ordered]@{
            FormatVersion = 1
            PreviousVersion = $previousVersion
            PreviousSha256 = $previousHash
            SavedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        }
        $metadata |
            ConvertTo-Json -Depth 3 |
            ForEach-Object {
                Assert-CumSafeMutationPath $rollbackMetadata | Out-Null
                $_
            } |
            Set-Content `
                -LiteralPath $rollbackMetadata `
                -Encoding UTF8
    }

    Copy-CumFileAtomically `
        -Source $source `
        -Destination $destination
    if ((Get-CumSha256 $destination) -ne $sourceHash)
    {
        throw 'The installed executable failed its final SHA-256 check.'
    }
    Write-CumInstallMarker `
        -InstallDirectory $installRoot `
        -Sha256 $sourceHash `
        -StartupEntryWasReplaced $markerStartupEntryWasReplaced `
        -PreviousStartupCommand $markerPreviousStartupCommand | Out-Null
    $installed = $true

    if ($EnableStartup)
    {
        $startupChange = Enable-CumStartup `
            -Executable $destination `
            -ForceStartupReplace:$ForceStartupReplace `
            -Confirm:$false
        if ($startupChange.ReplacedExisting)
        {
            try
            {
                Write-CumInstallMarker `
                    -InstallDirectory $installRoot `
                    -Sha256 $sourceHash `
                    -StartupEntryWasReplaced $true `
                    -PreviousStartupCommand $startupChange.PreviousCommand |
                    Out-Null
                $startupEntryReplaced = $true
                $previousStartupCommand = $startupChange.PreviousCommand
            }
            catch
            {
                if ($startupChange.Changed)
                {
                    Remove-CumMatchingStartup `
                        -Executable $destination `
                        -RestorePrevious `
                        -PreviousCommand $startupChange.PreviousCommand `
                        -Confirm:$false | Out-Null
                }
                throw
            }
        }
    }

    if ($Start)
    {
        Start-Process -FilePath $destination | Out-Null
        $started = $true
    }
}

$startupCommand = Get-CumStartupValue
$expectedStartupCommand = Get-CumStartupCommand $destination
[pscustomobject]@{
    Installed = $installed
    ExecutablePath = $destination
    Version = if ($installed)
    {
        Get-CumFileVersion $destination
    }
    else
    {
        Get-CumFileVersion $source
    }
    Sha256 = $sourceHash
    InstallMarker = Join-Path `
        $installRoot `
        '.codex-usage-monitor-install.json'
    ReleaseHashVerified = $hashVerified
    HadPreviousVersion = $hadPreviousVersion
    RollbackExecutable = if ($hadPreviousVersion)
    {
        $rollbackExecutable
    }
    else
    {
        $null
    }
    StoppedProcessIds = $stoppedProcessIds
    StartWithWindows = [string]::Equals(
        [string]$startupCommand,
        $expectedStartupCommand,
        [StringComparison]::OrdinalIgnoreCase)
    StartupEntryReplaced = $startupEntryReplaced
    PreviousStartupCommandPreserved = (
        $startupEntryReplaced -and
        -not [string]::IsNullOrWhiteSpace($previousStartupCommand))
    Started = $started
}
