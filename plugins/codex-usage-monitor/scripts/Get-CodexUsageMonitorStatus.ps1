[CmdletBinding()]
param(
    [switch]$AsJson
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$pluginRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $pluginRoot 'assets\CodexUsageMonitor.exe'
$installed = Test-Path -LiteralPath $executable -PathType Leaf
$processes = @(Get-Process -Name 'CodexUsageMonitor' -ErrorAction SilentlyContinue)
$running = $processes.Count -gt 0
$processIds = @($processes | ForEach-Object { $_.Id })
$runningPath = $null

if ($running) {
    try {
        $runningPath = $processes[0].Path
    }
    catch {
        try {
            $runningPath = $processes[0].MainModule.FileName
        }
        catch {
            $runningPath = $null
        }
    }
}

$version = $null
if ($installed) {
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        $executable).FileVersion
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'CodexUsageMonitor'
$startupCommand = $null
try {
    $startupCommand = (Get-ItemProperty `
        -LiteralPath $runKey `
        -Name $runValueName `
        -ErrorAction Stop).$runValueName
}
catch {
    $startupCommand = $null
}

$expectedStartupCommand = '"' + $executable + '" --background'
$startWithWindows = $installed -and [string]::Equals(
    [string]$startupCommand,
    $expectedStartupCommand,
    [StringComparison]::OrdinalIgnoreCase)

$status = [pscustomobject]@{
    Installed = $installed
    Running = $running
    ProcessIds = $processIds
    ExecutablePath = $executable
    RunningExecutablePath = $runningPath
    Version = $version
    StartWithWindows = $startWithWindows
    StartupCommand = $startupCommand
    SettingsPath = Join-Path $env:LOCALAPPDATA 'CodexUsageMonitor\settings.json'
    ErrorLogPath = Join-Path $env:LOCALAPPDATA 'CodexUsageMonitor\last-error.txt'
}

if ($AsJson) {
    $status | ConvertTo-Json -Depth 3
}
else {
    $status
}
