[CmdletBinding()]
param([string]$ApplicationPath)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repository 'artifacts\tests\window'
New-Item -ItemType Directory -Path $output -Force | Out-Null
if (-not $ApplicationPath) {
    $ApplicationPath = Join-Path $repository 'src\CodexUsageMonitor\bin\x64\Release\CodexUsageMonitor.exe'
}
$application = (Resolve-Path -LiteralPath $ApplicationPath).Path
Copy-Item -LiteralPath $application -Destination $output -Force
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $output 'WindowInteractionTests.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x64', "/out:$executable", "/r:$application")
$referenceRoot = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0'
foreach ($assembly in @('WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.Xaml')) {
    $arguments += '/r:' + (Join-Path $referenceRoot "$assembly.dll")
}
$arguments += Join-Path $repository 'tests\WindowInteractionTests.cs'
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Window regression checks did not compile.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Window regression checks failed.' }
