[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repository 'artifacts\tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $output 'SessionMetricsTests.exe'
$sources = @(
    'tests\SessionMetricsTests.cs'
    'src\CodexUsageMonitor\SessionMetrics.cs'
    'src\CodexUsageMonitor\LocalSessionReader.cs'
    'src\CodexUsageMonitor\LocalThreadCatalog.cs'
    'src\CodexUsageMonitor\JsonValue.cs'
) | ForEach-Object { Join-Path $repository $_ }
& $compiler /nologo /target:exe /r:System.Web.Extensions.dll /out:$executable $sources
if ($LASTEXITCODE -ne 0) { throw 'Regression checks did not compile.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
