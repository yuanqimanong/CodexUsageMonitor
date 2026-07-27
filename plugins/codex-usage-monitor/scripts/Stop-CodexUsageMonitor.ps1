[CmdletBinding()]
param(
    [ValidateRange(1, 60)]
    [int]$TimeoutSeconds = 10
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$pluginRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $pluginRoot 'assets\CodexUsageMonitor.exe'
$processName = 'CodexUsageMonitor'

$before = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
if ($before.Count -eq 0) {
    [pscustomobject]@{
        Stopped = $true
        WasRunning = $false
        RemainingProcessIds = @()
    }
    return
}

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Cannot request a safe shutdown because the bundled executable is missing: $executable"
}

& $executable --shutdown

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    Start-Sleep -Milliseconds 100
    $remaining = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
} while ($remaining.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)

if ($remaining.Count -gt 0) {
    $ids = @($remaining | ForEach-Object { $_.Id })
    throw "Codex Usage Monitor did not exit within $TimeoutSeconds seconds. Remaining process IDs: $($ids -join ', ')"
}

[pscustomobject]@{
    Stopped = $true
    WasRunning = $true
    RemainingProcessIds = @()
}
