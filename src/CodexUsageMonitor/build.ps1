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

& $msbuild $project /t:Restore /p:Configuration=$Configuration /p:Platform=x64 /nologo /verbosity:minimal | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "NuGet restore failed with exit code $LASTEXITCODE."
}

$assetsPath = Join-Path $projectRoot 'obj\project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$referencePackage = $assets.libraries.PSObject.Properties |
    Where-Object { $_.Name -like 'Microsoft.NETFramework.ReferenceAssemblies.net40/*' } |
    Select-Object -First 1
if ($null -eq $referencePackage) {
    throw 'The restored assets do not contain the .NET Framework 4.0 reference package.'
}

$referenceRoot = $null
foreach ($packageFolder in $assets.packageFolders.PSObject.Properties.Name) {
    $candidate = Join-Path (Join-Path $packageFolder $referencePackage.Value.path) 'build'
    if (Test-Path -LiteralPath (Join-Path $candidate '.NETFramework\v4.0\mscorlib.dll') -PathType Leaf) {
        $referenceRoot = $candidate
        break
    }
}
if (-not $referenceRoot) {
    throw 'The restored .NET Framework 4.0 reference assemblies could not be located.'
}

# WPF temporary projects do not inherit the main project's generated NuGet imports.
# A global property gives them the same reference assemblies on clean build agents.
& $msbuild $project /t:Rebuild /p:Configuration=$Configuration /p:Platform=x64 "/p:TargetFrameworkRootPath=$referenceRoot" /nologo /verbosity:minimal | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

$output = Join-Path $projectRoot "bin\x64\$Configuration\CodexUsageMonitor.exe"
if (-not (Test-Path -LiteralPath $output)) {
    throw "Build completed without the expected executable: $output"
}

Get-Item -LiteralPath $output |
    Select-Object FullName, Length, LastWriteTime
