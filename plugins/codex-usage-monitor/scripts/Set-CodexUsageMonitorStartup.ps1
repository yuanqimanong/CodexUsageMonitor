[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [bool]$Enabled
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$pluginRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $pluginRoot 'assets\CodexUsageMonitor.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'CodexUsageMonitor'
$expectedCommand = '"' + $executable + '" --background'

function Get-CumPluginStartupValue
{
    try {
        return [string](Get-ItemProperty `
            -LiteralPath $runKey `
            -Name $runValueName `
            -ErrorAction Stop).$runValueName
    }
    catch {
        return $null
    }
}

function Assert-CumPluginStartupAvailable
{
    param(
        [AllowNull()]
        [string]$CurrentCommand
    )

    if (-not [string]::IsNullOrWhiteSpace($CurrentCommand) -and
        -not [string]::Equals(
            $CurrentCommand,
            $expectedCommand,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw (
            'The current user already has a CodexUsageMonitor startup entry ' +
            'that points to a different executable. It was left unchanged. ' +
            'Use the package installer with -ForceStartupReplace only when an ' +
            'intentional, recoverable replacement is required.')
    }
}

if ($Enabled -and -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Cannot enable startup because the executable is missing: $executable"
}

$currentCommand = Get-CumPluginStartupValue

if ($Enabled) {
    Assert-CumPluginStartupAvailable $currentCommand
}

if ($Enabled) {
    if ($PSCmdlet.ShouldProcess(
        "$runKey\$runValueName",
        'Enable Codex Usage Monitor at sign-in')) {
        if (-not (Test-Path -LiteralPath $runKey)) {
            New-Item `
                -Path $runKey `
                -Force `
                -ErrorAction Stop | Out-Null
        }

        # Re-read immediately before the write so a newly created conflicting
        # value is never silently replaced.
        $currentCommand = Get-CumPluginStartupValue
        Assert-CumPluginStartupAvailable $currentCommand
        if (-not [string]::Equals(
            [string]$currentCommand,
            $expectedCommand,
            [StringComparison]::OrdinalIgnoreCase)) {
            Set-ItemProperty `
                -LiteralPath $runKey `
                -Name $runValueName `
                -Value $expectedCommand `
                -ErrorAction Stop
        }
    }
}
elseif ([string]::IsNullOrWhiteSpace([string]$currentCommand)) {
    # Nothing to remove.
}
elseif ([string]::Equals(
    [string]$currentCommand,
    $expectedCommand,
    [StringComparison]::OrdinalIgnoreCase)) {
    if ($PSCmdlet.ShouldProcess(
        "$runKey\$runValueName",
        'Disable Codex Usage Monitor at sign-in')) {
        $currentCommand = Get-CumPluginStartupValue
        if ([string]::Equals(
            [string]$currentCommand,
            $expectedCommand,
            [StringComparison]::OrdinalIgnoreCase)) {
            Remove-ItemProperty `
                -LiteralPath $runKey `
                -Name $runValueName `
                -ErrorAction SilentlyContinue
        }
        elseif (-not [string]::IsNullOrWhiteSpace(
            [string]$currentCommand)) {
            Write-Warning (
                'The CodexUsageMonitor startup entry changed before removal ' +
                'and was left unchanged: ' +
                [string]$currentCommand)
        }
    }
}
else {
    Write-Warning (
        'The CodexUsageMonitor startup entry points to another executable and was left unchanged: ' +
        [string]$currentCommand)
}

$updatedCommand = Get-CumPluginStartupValue

[pscustomobject]@{
    Enabled = [string]::Equals(
        [string]$updatedCommand,
        $expectedCommand,
        [StringComparison]::OrdinalIgnoreCase)
    ExecutablePath = $executable
    StartupCommand = $updatedCommand
}
