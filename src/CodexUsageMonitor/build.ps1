[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$assetRoot = Join-Path $projectRoot 'Assets'
$iconGenerator = Join-Path $assetRoot 'Generate-Icon.ps1'
$project = Join-Path $projectRoot 'CodexUsageMonitor.csproj'

function Resolve-MSBuild {
    $pathCommand = Get-Command 'MSBuild.exe' -ErrorAction SilentlyContinue
    if ($null -ne $pathCommand) {
        return $pathCommand.Source
    }

    $programFilesX86 = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFilesX86)
    $vswhere = Join-Path $programFilesX86 `
        'Microsoft Visual Studio\Installer\vswhere.exe'

    if (Test-Path -LiteralPath $vswhere) {
        $installations = @(
            & $vswhere `
                -products '*' `
                -requires Microsoft.Component.MSBuild `
                -property installationPath `
                -latest
        )

        foreach ($installation in $installations) {
            if ([string]::IsNullOrWhiteSpace($installation)) {
                continue
            }

            foreach ($relativePath in @(
                'MSBuild\Current\Bin\MSBuild.exe',
                'MSBuild\15.0\Bin\MSBuild.exe'
            )) {
                $candidate = Join-Path $installation.Trim() $relativePath
                if (Test-Path -LiteralPath $candidate) {
                    return $candidate
                }
            }
        }
    }

    throw @'
MSBuild was not found. Install Visual Studio Build Tools 2019 or newer
with the .NET desktop build tools and .NET Framework 4 targeting pack,
then run this script again.
'@
}

$msbuild = Resolve-MSBuild

New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null
& $iconGenerator

& $msbuild $project /restore /t:Rebuild /p:Configuration=$Configuration /p:Platform=x64 /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

$output = Join-Path $projectRoot "bin\x64\$Configuration\CodexUsageMonitor.exe"
if (-not (Test-Path -LiteralPath $output)) {
    throw "Build completed without the expected executable: $output"
}

Get-Item -LiteralPath $output |
    Select-Object FullName, Length, LastWriteTime
