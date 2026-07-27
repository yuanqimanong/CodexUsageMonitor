#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$InstallDirectory,
    [switch]$Start,

    [ValidateRange(1, 60)]
    [int]$ShutdownTimeoutSeconds = 10
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Package.Common.ps1')

Assert-CumWindowsX64

if ([string]::IsNullOrWhiteSpace($InstallDirectory))
{
    $InstallDirectory = Get-CumDefaultInstallDirectory
}
$installRoot = Assert-CumInstallDirectory $InstallDirectory
$installMarker = Read-CumInstallMarker $installRoot
if ($null -eq $installMarker)
{
    throw (
        'The selected directory does not contain a valid Codex Usage Monitor ' +
        'install marker. It was left unchanged.')
}
$markerStartupEntryWasReplaced = $false
$markerPreviousStartupCommand = $null
if ($installMarker.PSObject.Properties.Match(
    'StartupEntryWasReplaced').Count -gt 0 -and
    [bool]$installMarker.StartupEntryWasReplaced)
{
    $markerStartupEntryWasReplaced = $true
    if ($installMarker.PSObject.Properties.Match(
        'PreviousStartupCommand').Count -gt 0)
    {
        $markerPreviousStartupCommand = [string](
            $installMarker.PreviousStartupCommand)
    }
}
$destination = Assert-CumSafeMutationPath (
    Join-Path $installRoot 'CodexUsageMonitor.exe')
if (-not (Test-Path -LiteralPath $destination -PathType Leaf))
{
    throw "The installed executable was not found: $destination"
}

$rollbackRoot = Assert-CumSafeMutationPath (Get-CumRollbackDirectory)
$rollbackExecutable = Assert-CumSafeMutationPath (
    Join-Path $rollbackRoot 'CodexUsageMonitor.previous.exe')
$rollbackMetadata = Assert-CumSafeMutationPath (
    Join-Path $rollbackRoot 'rollback.json')
if (-not (Test-Path -LiteralPath $rollbackExecutable -PathType Leaf))
{
    throw 'No previous Codex Usage Monitor version is available for rollback.'
}

$rollbackHash = Get-CumSha256 $rollbackExecutable
if (Test-Path -LiteralPath $rollbackMetadata -PathType Leaf)
{
    Assert-CumSafeMutationPath $rollbackMetadata | Out-Null
    try
    {
        $metadata = Get-Content `
            -Raw `
            -LiteralPath $rollbackMetadata |
            ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace(
            [string]$metadata.PreviousSha256) -and
            $rollbackHash -ne
                ([string]$metadata.PreviousSha256).ToUpperInvariant())
        {
            throw 'The rollback executable does not match rollback.json.'
        }
    }
    catch
    {
        throw "Rollback metadata verification failed: $($_.Exception.Message)"
    }
}
else
{
    Write-Warning (
        'rollback.json is missing. The rollback file will be hash-checked ' +
        'during copy, but its release provenance cannot be confirmed.')
}

$currentVersion = Get-CumFileVersion $destination
$currentHash = Get-CumSha256 $destination
$targetVersion = Get-CumFileVersion $rollbackExecutable
$stoppedProcessIds = @()
$rolledBack = $false
$started = $false

if ($PSCmdlet.ShouldProcess(
    $destination,
    "Rollback Codex Usage Monitor to version $targetVersion"))
{
    $installRoot = Assert-CumInstallDirectory $installRoot
    if ($null -eq (Read-CumInstallMarker $installRoot))
    {
        throw 'The install marker changed before rollback.'
    }
    $destination = Assert-CumSafeMutationPath $destination
    $rollbackRoot = Assert-CumSafeMutationPath $rollbackRoot
    $rollbackExecutable = Assert-CumSafeMutationPath $rollbackExecutable
    $rollbackMetadata = Assert-CumSafeMutationPath $rollbackMetadata

    $stoppedProcessIds = @(
        Stop-CumMonitorGracefully `
            -ControlExecutable $destination `
            -TimeoutSeconds $ShutdownTimeoutSeconds)

    $swapExecutable = Assert-CumSafeMutationPath (
        Join-Path `
            $rollbackRoot `
            'CodexUsageMonitor.pre-rollback.exe')
    Copy-CumFileAtomically `
        -Source $destination `
        -Destination $swapExecutable
    Copy-CumFileAtomically `
        -Source $rollbackExecutable `
        -Destination $destination
    if ((Get-CumSha256 $destination) -ne $rollbackHash)
    {
        throw 'The restored executable failed its final SHA-256 check.'
    }
    Write-CumInstallMarker `
        -InstallDirectory $installRoot `
        -Sha256 $rollbackHash `
        -StartupEntryWasReplaced $markerStartupEntryWasReplaced `
        -PreviousStartupCommand $markerPreviousStartupCommand | Out-Null

    # Preserve the replaced version so a second rollback can undo this action.
    Copy-CumFileAtomically `
        -Source $swapExecutable `
        -Destination $rollbackExecutable
    Assert-CumSafeMutationPath $swapExecutable | Out-Null
    Remove-Item -LiteralPath $swapExecutable -Force

    $metadata = [ordered]@{
        FormatVersion = 1
        PreviousVersion = $currentVersion
        PreviousSha256 = $currentHash
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

    $rolledBack = $true
    if ($Start)
    {
        Start-Process -FilePath $destination | Out-Null
        $started = $true
    }
}

[pscustomobject]@{
    RolledBack = $rolledBack
    ExecutablePath = $destination
    PreviousInstalledVersion = $currentVersion
    RestoredVersion = $targetVersion
    RestoredSha256 = $rollbackHash
    UndoRollbackExecutable = $rollbackExecutable
    StoppedProcessIds = $stoppedProcessIds
    Started = $started
}
