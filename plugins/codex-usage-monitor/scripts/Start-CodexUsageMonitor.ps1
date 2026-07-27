[CmdletBinding()]
param(
    [switch]$Background,
    [switch]$PassThru
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$pluginRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $pluginRoot 'assets\CodexUsageMonitor.exe'

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "CodexUsageMonitor.exe is missing from the plugin assets directory: $executable"
}

if ($Background) {
    $process = Start-Process `
        -FilePath $executable `
        -ArgumentList '--background' `
        -PassThru
}
else {
    $process = Start-Process -FilePath $executable -PassThru
}

if ($PassThru) {
    $process
    return
}

[pscustomobject]@{
    Started = $true
    Background = [bool]$Background
    ProcessId = $process.Id
    ExecutablePath = $executable
}
