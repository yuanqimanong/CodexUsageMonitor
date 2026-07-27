#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$InstallDirectory,
    [switch]$RemoveSettings,

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
$destination = Assert-CumSafeMutationPath (
    Join-Path $installRoot 'CodexUsageMonitor.exe')
$settingsRoot = Resolve-CumFullPath (Get-CumSettingsDirectory)
$expectedSettingsRoot = Resolve-CumFullPath (
    Join-Path (Get-CumLocalAppData) 'CodexUsageMonitor')
if (-not [string]::Equals(
    $settingsRoot,
    $expectedSettingsRoot,
    [StringComparison]::OrdinalIgnoreCase))
{
    throw 'The settings directory safety check failed.'
}

$stoppedProcessIds = @()
$startupEntryRemoved = $false
$startupEntryRestored = $false
$programRemoved = $false
$installDirectoryRemoved = $false
$programFilesRemoved = @()
$settingsRemoved = $false
$settingsFilesRemoved = @()
$installMarker = $null
$restorePreviousStartup = $false
$previousStartupCommand = $null

if (Test-Path -LiteralPath $installRoot -PathType Container)
{
    $installMarker = Read-CumInstallMarker $installRoot
    if ($null -eq $installMarker)
    {
        throw (
            'The selected directory does not contain a valid Codex Usage ' +
            'Monitor install marker. It was left unchanged.')
    }
    if ($installMarker.PSObject.Properties.Match(
        'StartupEntryWasReplaced').Count -gt 0 -and
        [bool]$installMarker.StartupEntryWasReplaced)
    {
        $restorePreviousStartup = $true
        if ($installMarker.PSObject.Properties.Match(
            'PreviousStartupCommand').Count -gt 0)
        {
            $previousStartupCommand = [string](
                $installMarker.PreviousStartupCommand)
        }
    }

    if ($PSCmdlet.ShouldProcess(
        $installRoot,
        'Stop and remove Codex Usage Monitor'))
    {
        $installRoot = Assert-CumInstallDirectory $installRoot
        if ($null -eq (Read-CumInstallMarker $installRoot))
        {
            throw 'The install marker changed before removal.'
        }
        $destination = Assert-CumSafeMutationPath $destination

        if (Test-Path -LiteralPath $destination -PathType Leaf)
        {
            $stoppedProcessIds = @(
                Stop-CumMonitorGracefully `
                    -ControlExecutable $destination `
                    -TimeoutSeconds $ShutdownTimeoutSeconds)
        }

        $startupChanged = Remove-CumMatchingStartup `
            -Executable $destination `
            -RestorePrevious:$restorePreviousStartup `
            -PreviousCommand $previousStartupCommand `
            -Confirm:$false
        $startupEntryRestored = (
            $startupChanged -and $restorePreviousStartup)
        $startupEntryRemoved = (
            $startupChanged -and -not $restorePreviousStartup)

        $ownedProgramFiles = @(
            $destination,
            (Join-Path `
                $installRoot `
                '.codex-usage-monitor-install.json'))
        foreach ($ownedFile in $ownedProgramFiles)
        {
            if (Test-Path -LiteralPath $ownedFile -PathType Leaf)
            {
                Assert-CumSafeMutationPath $ownedFile | Out-Null
                Remove-Item -LiteralPath $ownedFile -Force
                $programFilesRemoved += $ownedFile
            }
        }

        if (Test-CumDirectoryEmpty $installRoot)
        {
            Assert-CumSafeMutationPath $installRoot | Out-Null
            Remove-Item -LiteralPath $installRoot -Force
            $installDirectoryRemoved = $true
        }
        else
        {
            Write-Warning (
                'The install directory contains files that are not owned by ' +
                'this package. Those files and the directory were preserved.')
        }

        $programRemoved = (
            -not (Test-Path -LiteralPath $destination) -and
            -not (Test-Path `
                -LiteralPath (
                    Join-Path `
                        $installRoot `
                        '.codex-usage-monitor-install.json')))
    }
}
elseif ($PSCmdlet.ShouldProcess(
    'HKCU Run entry for the selected installation',
    'Remove matching Codex Usage Monitor startup entry'))
{
    $startupEntryRemoved = Remove-CumMatchingStartup `
        -Executable $destination `
        -Confirm:$false
}

if ($RemoveSettings -and
    (Test-Path -LiteralPath $settingsRoot -PathType Container) -and
    $PSCmdlet.ShouldProcess(
        $settingsRoot,
        'Remove Codex Usage Monitor settings, error log, and rollback files'))
{
    $settingsRoot = Assert-CumSafeMutationPath $settingsRoot
    $rollbackRoot = Assert-CumSafeMutationPath (
        Join-Path $settingsRoot 'rollback')
    $ownedSettingsFiles = @(
        (Join-Path $settingsRoot 'settings.json'),
        (Join-Path $settingsRoot 'last-error.txt'),
        (Join-Path $rollbackRoot 'CodexUsageMonitor.previous.exe'),
        (Join-Path $rollbackRoot 'CodexUsageMonitor.pre-rollback.exe'),
        (Join-Path $rollbackRoot 'rollback.json'))
    foreach ($ownedFile in $ownedSettingsFiles)
    {
        if (Test-Path -LiteralPath $ownedFile -PathType Leaf)
        {
            Assert-CumSafeMutationPath $ownedFile | Out-Null
            Remove-Item -LiteralPath $ownedFile -Force
            $settingsFilesRemoved += $ownedFile
        }
    }

    if (Test-Path -LiteralPath $rollbackRoot -PathType Container)
    {
        if (Test-CumDirectoryEmpty $rollbackRoot)
        {
            Assert-CumSafeMutationPath $rollbackRoot | Out-Null
            Remove-Item -LiteralPath $rollbackRoot -Force
        }
        else
        {
            Write-Warning (
                'The rollback directory contains unrecognized files and was ' +
                'preserved.')
        }
    }

    if (Test-CumDirectoryEmpty $settingsRoot)
    {
        Assert-CumSafeMutationPath $settingsRoot | Out-Null
        Remove-Item -LiteralPath $settingsRoot -Force
        $settingsRemoved = $true
    }
    else
    {
        Write-Warning (
            'The settings directory contains unrecognized files and was ' +
            'preserved.')
    }
}

[pscustomobject]@{
    ProgramRemoved = $programRemoved
    InstallDirectory = $installRoot
    InstallDirectoryRemoved = $installDirectoryRemoved
    ProgramFilesRemoved = $programFilesRemoved
    StartupEntryRemoved = $startupEntryRemoved
    StartupEntryRestored = $startupEntryRestored
    SettingsRemoved = $settingsRemoved
    SettingsFilesRemoved = $settingsFilesRemoved
    SettingsDirectory = $settingsRoot
    StoppedProcessIds = $stoppedProcessIds
}
