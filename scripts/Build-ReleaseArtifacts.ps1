#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [string]$OutputDirectory
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-NormalizedFullPath
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.Length -gt $root.Length)
    {
        $fullPath = $fullPath.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    }

    return $fullPath
}

function Test-PathInside
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Parent
    )

    $normalizedPath = Get-NormalizedFullPath $Path
    $normalizedParent = Get-NormalizedFullPath $Parent
    if ($normalizedPath.Equals(
        $normalizedParent,
        [System.StringComparison]::OrdinalIgnoreCase))
    {
        return $true
    }

    $parentPrefix = $normalizedParent +
        [System.IO.Path]::DirectorySeparatorChar
    return $normalizedPath.StartsWith(
        $parentPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-LocalOutputPath
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $normalizedPath = Get-NormalizedFullPath $Path
    $pathRoot = [System.IO.Path]::GetPathRoot($normalizedPath)
    if ($pathRoot -notmatch '^[A-Za-z]:\\$')
    {
        throw "The output path must be on a local Windows drive: $normalizedPath"
    }

    if (Test-PathInside -Path $normalizedPath -Parent $RepositoryRoot)
    {
        throw @"
Release output must be outside the repository so generated EXE, ZIP, and
debug files cannot become Git-tracked content: $normalizedPath
"@
    }

    $current = $pathRoot
    $relativePath = $normalizedPath.Substring($pathRoot.Length)
    foreach ($segment in @($relativePath -split '[\\/]'))
    {
        if ([string]::IsNullOrWhiteSpace($segment))
        {
            continue
        }

        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current))
        {
            break
        }

        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0)
        {
            throw "Release output cannot traverse a reparse point: $current"
        }

        if (-not $item.PSIsContainer)
        {
            throw "A release output path component is not a directory: $current"
        }
    }

    return $normalizedPath
}

function Copy-PluginTree
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination,

        [Parameter(Mandatory = $true)]
        [string]$SkipRelativePath
    )

    $sourceRoot = Get-NormalizedFullPath $Source
    $skipPath = $SkipRelativePath.Replace('/', '\')
    New-Item -ItemType Directory -Path $Destination | Out-Null

    $pending = New-Object System.Collections.Queue
    $pending.Enqueue(@($sourceRoot, $Destination))

    while ($pending.Count -gt 0)
    {
        $pair = $pending.Dequeue()
        $sourceDirectory = [string]$pair[0]
        $destinationDirectory = [string]$pair[1]

        foreach ($item in @(
            Get-ChildItem -LiteralPath $sourceDirectory -Force))
        {
            if (($item.Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -ne 0)
            {
                throw "The plugin source contains a reparse point: $($item.FullName)"
            }

            $relativePath = $item.FullName.Substring(
                $sourceRoot.Length).TrimStart([char[]]@('\', '/'))
            $destinationPath = Join-Path $Destination $relativePath

            if ($item.PSIsContainer)
            {
                New-Item -ItemType Directory -Path $destinationPath | Out-Null
                $pending.Enqueue(@($item.FullName, $destinationPath))
                continue
            }

            if ($relativePath.Replace('/', '\').Equals(
                $skipPath,
                [System.StringComparison]::OrdinalIgnoreCase))
            {
                continue
            }

            if ($item.Extension.Equals(
                '.pdb',
                [System.StringComparison]::OrdinalIgnoreCase) -or
                $item.Extension.Equals(
                '.zip',
                [System.StringComparison]::OrdinalIgnoreCase))
            {
                throw "The plugin source contains a forbidden release file: $relativePath"
            }

            [System.IO.File]::Copy(
                $item.FullName,
                $destinationPath,
                $false)
        }
    }
}

function Write-Sha256Manifest
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Destination,

        [Parameter(Mandatory = $true)]
        [string[]]$Files
    )

    $lines = foreach ($file in $Files)
    {
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).
            Hash.ToLowerInvariant()
        '{0} *{1}' -f $hash, [System.IO.Path]::GetFileName($file)
    }

    [System.IO.File]::WriteAllLines(
        $Destination,
        [string[]]$lines,
        [System.Text.Encoding]::ASCII)
}

function New-ZipFromDirectory
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (Test-Path -LiteralPath $Destination)
    {
        throw "Refusing to overwrite an existing archive: $Destination"
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $SourceDirectory,
        $Destination,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}

function Get-ZipFileEntries
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try
    {
        return @(
            $archive.Entries |
                Where-Object { -not $_.FullName.EndsWith('/') } |
                ForEach-Object { $_.FullName.Replace('\', '/') })
    }
    finally
    {
        $archive.Dispose()
    }
}

$repositoryRoot = Get-NormalizedFullPath (Split-Path -Parent $PSScriptRoot)
$buildScript = Join-Path $repositoryRoot 'src\CodexUsageMonitor\build.ps1'
$builtExecutable = Join-Path $repositoryRoot `
    'src\CodexUsageMonitor\bin\x64\Release\CodexUsageMonitor.exe'
$pluginSource = Join-Path $repositoryRoot 'plugins\codex-usage-monitor'

if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf))
{
    throw "The source build script was not found: $buildScript"
}
if (-not (Test-Path -LiteralPath $pluginSource -PathType Container))
{
    throw "The Codex plugin source was not found: $pluginSource"
}

if ($PSBoundParameters.ContainsKey('OutputDirectory') -and
    [string]::IsNullOrWhiteSpace($OutputDirectory))
{
    throw 'OutputDirectory cannot be empty when supplied.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        'CodexUsageMonitor-Releases'
}

$outputBase = Assert-LocalOutputPath `
    -Path $OutputDirectory `
    -RepositoryRoot $repositoryRoot
$releaseName = 'CodexUsageMonitor-{0}-{1}' -f `
    (Get-Date -Format 'yyyyMMdd-HHmmss'), `
    ([System.Guid]::NewGuid().ToString('N').Substring(0, 8))
$releaseRoot = Assert-LocalOutputPath `
    -Path (Join-Path $outputBase $releaseName) `
    -RepositoryRoot $repositoryRoot

if (-not $PSCmdlet.ShouldProcess(
    $releaseRoot,
    'Build x64 Release and create standalone and Codex plugin artifacts'))
{
    [pscustomobject]@{
        Status = 'WhatIf'
        OutputDirectory = $releaseRoot
        BuildScript = $buildScript
    }
    return
}

$null = Add-Type -AssemblyName System.IO.Compression.FileSystem `
    -PassThru

New-Item -ItemType Directory -Force -Path $outputBase | Out-Null
$outputBase = Assert-LocalOutputPath `
    -Path $outputBase `
    -RepositoryRoot $repositoryRoot
New-Item -ItemType Directory -Path $releaseRoot | Out-Null
$releaseRoot = Assert-LocalOutputPath `
    -Path $releaseRoot `
    -RepositoryRoot $repositoryRoot

$stagingRoot = Join-Path $releaseRoot '.staging'
$standaloneStage = Join-Path $stagingRoot 'standalone'
$pluginStage = Join-Path $stagingRoot 'codex-usage-monitor'
New-Item -ItemType Directory -Path $standaloneStage | Out-Null

$null = & $buildScript -Configuration Release
if (-not (Test-Path -LiteralPath $builtExecutable -PathType Leaf))
{
    throw "The build did not produce the expected executable: $builtExecutable"
}

$finalExecutable = Join-Path $releaseRoot 'CodexUsageMonitor.exe'
[System.IO.File]::Copy($builtExecutable, $finalExecutable, $false)

$standaloneExecutable = Join-Path $standaloneStage 'CodexUsageMonitor.exe'
[System.IO.File]::Copy($builtExecutable, $standaloneExecutable, $false)
$standaloneManifest = Join-Path $standaloneStage 'SHA256SUMS.txt'
Write-Sha256Manifest `
    -Destination $standaloneManifest `
    -Files @($standaloneExecutable)

Copy-PluginTree `
    -Source $pluginSource `
    -Destination $pluginStage `
    -SkipRelativePath 'assets\CodexUsageMonitor.exe'
$pluginExecutable = Join-Path $pluginStage `
    'assets\CodexUsageMonitor.exe'
$pluginAssetDirectory = Split-Path -Parent $pluginExecutable
if (-not (Test-Path -LiteralPath $pluginAssetDirectory -PathType Container))
{
    throw "The plugin source is missing its assets directory: $pluginAssetDirectory"
}
[System.IO.File]::Copy($builtExecutable, $pluginExecutable, $false)

$builtHash = (Get-FileHash `
    -LiteralPath $builtExecutable `
    -Algorithm SHA256).Hash
$pluginHash = (Get-FileHash `
    -LiteralPath $pluginExecutable `
    -Algorithm SHA256).Hash
if (-not $builtHash.Equals(
    $pluginHash,
    [System.StringComparison]::OrdinalIgnoreCase))
{
    throw 'The staged plugin executable does not match the new Release build.'
}

if (@(
    Get-ChildItem -LiteralPath $stagingRoot -Recurse -Force -File |
        Where-Object {
            $_.Extension.Equals(
                '.pdb',
                [System.StringComparison]::OrdinalIgnoreCase)
        }).Count -ne 0)
{
    throw 'A PDB file reached the release staging directory.'
}

$standaloneZip = Join-Path $releaseRoot `
    'CodexUsageMonitor-windows-x64.zip'
$pluginZip = Join-Path $releaseRoot `
    'codex-usage-monitor-plugin.zip'
New-ZipFromDirectory `
    -SourceDirectory $standaloneStage `
    -Destination $standaloneZip
New-ZipFromDirectory `
    -SourceDirectory $pluginStage `
    -Destination $pluginZip

$standaloneEntries = @(
    Get-ZipFileEntries $standaloneZip |
        Sort-Object)
$expectedStandaloneEntries = @(
    'CodexUsageMonitor.exe',
    'SHA256SUMS.txt'
) | Sort-Object
if (($standaloneEntries -join "`n") -ne
    ($expectedStandaloneEntries -join "`n"))
{
    throw 'The standalone archive contains an unexpected file set.'
}

$pluginEntries = @(Get-ZipFileEntries $pluginZip)
foreach ($requiredEntry in @(
    '.codex-plugin/plugin.json',
    'assets/CodexUsageMonitor.exe'
))
{
    if ($pluginEntries -notcontains $requiredEntry)
    {
        throw "The plugin archive is missing: $requiredEntry"
    }
}
if (@($pluginEntries | Where-Object {
    $_.EndsWith(
        '.pdb',
        [System.StringComparison]::OrdinalIgnoreCase)
}).Count -ne 0)
{
    throw 'The plugin archive contains a PDB file.'
}
$pluginExecutables = @($pluginEntries | Where-Object {
    $_.EndsWith(
        '.exe',
        [System.StringComparison]::OrdinalIgnoreCase)
})
if ($pluginExecutables.Count -ne 1 -or
    $pluginExecutables[0] -ne 'assets/CodexUsageMonitor.exe')
{
    throw 'The plugin archive contains an unexpected executable.'
}

$externalManifest = Join-Path $releaseRoot 'SHA256SUMS.txt'
Write-Sha256Manifest `
    -Destination $externalManifest `
    -Files @(
        $finalExecutable,
        $standaloneZip,
        $pluginZip
    )

[System.IO.Directory]::Delete($stagingRoot, $true)
if (Test-Path -LiteralPath $stagingRoot)
{
    throw "The temporary release staging directory was not removed: $stagingRoot"
}

$expectedReleaseFiles = @(
    'CodexUsageMonitor.exe',
    'CodexUsageMonitor-windows-x64.zip',
    'codex-usage-monitor-plugin.zip',
    'SHA256SUMS.txt'
) | Sort-Object
$actualReleaseFiles = @(
    Get-ChildItem -LiteralPath $releaseRoot -Force -File |
        ForEach-Object { $_.Name } |
        Sort-Object)
if (($actualReleaseFiles -join "`n") -ne
    ($expectedReleaseFiles -join "`n"))
{
    throw 'The release output contains an unexpected file set.'
}

[pscustomobject]@{
    Status = 'Complete'
    OutputDirectory = $releaseRoot
    ExecutableSha256 = $builtHash.ToLowerInvariant()
    ArtifactFiles = @(
        $finalExecutable,
        $externalManifest,
        $standaloneZip,
        $pluginZip
    )
}
