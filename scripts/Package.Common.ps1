# Shared helpers for the per-user package scripts.

Set-StrictMode -Version 2.0

function Assert-CumWindowsX64
{
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)
    {
        throw 'Codex Usage Monitor can only be installed on Windows.'
    }

    if (-not [Environment]::Is64BitOperatingSystem)
    {
        throw 'Codex Usage Monitor requires 64-bit Windows.'
    }
}

function Get-CumLocalAppData
{
    $path = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($path))
    {
        throw 'The current user LocalAppData directory could not be resolved.'
    }

    return [IO.Path]::GetFullPath($path)
}

function Get-CumDefaultInstallDirectory
{
    return Join-Path (Get-CumLocalAppData) 'Programs\CodexUsageMonitor'
}

function Get-CumSettingsDirectory
{
    return Join-Path (Get-CumLocalAppData) 'CodexUsageMonitor'
}

function Get-CumRollbackDirectory
{
    return Join-Path (Get-CumSettingsDirectory) 'rollback'
}

function Resolve-CumFullPath
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path))
    {
        throw 'A non-empty path is required.'
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($Path.Trim())
    if ([IO.Path]::IsPathRooted($expanded))
    {
        return [IO.Path]::GetFullPath($expanded)
    }

    return [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $expanded))
}

function Test-CumForbiddenWindowsPathPrefix
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $candidate = $Path.Trim()
    return (
        $candidate.StartsWith('\\', [StringComparison]::Ordinal) -or
        $candidate.StartsWith('//', [StringComparison]::Ordinal) -or
        $candidate.StartsWith('\??\', [StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith('/??/', [StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith(
            '\Device\',
            [StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith(
            '/Device/',
            [StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith(
            '\GLOBAL??\',
            [StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith(
            '\DosDevices\',
            [StringComparison]::OrdinalIgnoreCase))
}

function Assert-CumSafeMutationPath
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $expanded = [Environment]::ExpandEnvironmentVariables($Path.Trim())
    if (Test-CumForbiddenWindowsPathPrefix $expanded)
    {
        throw (
            'UNC paths and Windows device namespace paths cannot be used ' +
            'for Codex Usage Monitor file changes.')
    }

    $fullPath = Resolve-CumFullPath $expanded
    if (Test-CumForbiddenWindowsPathPrefix $fullPath)
    {
        throw (
            'UNC paths and Windows device namespace paths cannot be used ' +
            'for Codex Usage Monitor file changes.')
    }

    $root = [IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrWhiteSpace($root) -or
        $root -notmatch '^[A-Za-z]:\\$')
    {
        throw 'A local drive path is required for file changes.'
    }

    $current = $root
    $rootItem = Get-Item -LiteralPath $root -Force -ErrorAction Stop
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
    {
        throw "The path contains a reparse point and will not be modified: $root"
    }

    $relativePath = $fullPath.Substring($root.Length)
    $components = @(
        $relativePath.Split(
            [char[]]@(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar),
            [StringSplitOptions]::RemoveEmptyEntries))
    foreach ($component in $components)
    {
        $current = Join-Path $current $component
        if (-not (Test-Path -LiteralPath $current))
        {
            break
        }

        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
        {
            throw (
                'The path contains a symbolic link, junction, mount point, ' +
                "or other reparse point and will not be modified: $current")
        }
    }

    return $fullPath
}

function Assert-CumInstallDirectory
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = Assert-CumSafeMutationPath $Path
    $trimmed = $fullPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $root = [IO.Path]::GetPathRoot($trimmed)
    if ([string]::Equals(
        $trimmed,
        $root.TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar),
        [StringComparison]::OrdinalIgnoreCase))
    {
        throw 'A drive root cannot be used as the install directory.'
    }

    $leaf = Split-Path -Leaf $trimmed
    if (-not [string]::Equals(
        $leaf,
        'CodexUsageMonitor',
        [StringComparison]::OrdinalIgnoreCase))
    {
        throw (
            'For safe install and removal, the install directory must end with ' +
            'CodexUsageMonitor.')
    }

    return Assert-CumSafeMutationPath $trimmed
}

function Assert-CumNotReparsePoint
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return Assert-CumSafeMutationPath $Path
}

function Test-CumDirectoryEmpty
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = Assert-CumSafeMutationPath $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container))
    {
        return $true
    }

    return @(
        Get-ChildItem `
            -LiteralPath $fullPath `
            -Force `
            -ErrorAction Stop |
            Select-Object -First 1).Count -eq 0
}

function Assert-CumInstallTargetOwnership
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallDirectory
    )

    $installRoot = Assert-CumInstallDirectory $InstallDirectory
    if (-not (Test-Path -LiteralPath $installRoot))
    {
        return $installRoot
    }

    if (-not (Test-Path -LiteralPath $installRoot -PathType Container))
    {
        throw "The install path exists but is not a directory: $installRoot"
    }

    if (Test-CumInstallMarker $installRoot)
    {
        return $installRoot
    }

    if (-not (Test-CumDirectoryEmpty $installRoot))
    {
        throw (
            'The install directory is not empty and does not contain a valid ' +
            'Codex Usage Monitor install marker. It was left unchanged.')
    }

    return $installRoot
}

function Get-CumStartupCommand
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable
    )

    return '"' + (Resolve-CumFullPath $Executable) + '" --background'
}

function Get-CumStartupValue
{
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    try
    {
        return [string](Get-ItemProperty `
            -LiteralPath $runKey `
            -Name 'CodexUsageMonitor' `
            -ErrorAction Stop).CodexUsageMonitor
    }
    catch
    {
        return $null
    }
}

function Get-CumStartupReplacementPlan
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [switch]$ForceStartupReplace
    )

    $expected = Get-CumStartupCommand $Executable
    $current = Get-CumStartupValue
    $matches = [string]::Equals(
        [string]$current,
        $expected,
        [StringComparison]::OrdinalIgnoreCase)
    $hasDifferentValue = (
        -not [string]::IsNullOrWhiteSpace([string]$current) -and
        -not $matches)
    if ($hasDifferentValue -and -not $ForceStartupReplace)
    {
        throw (
            'The current user already has a CodexUsageMonitor startup entry ' +
            'that points to a different executable. It was left unchanged. ' +
            'Use -ForceStartupReplace only if that replacement is intended.')
    }

    return [pscustomobject]@{
        CurrentCommand = $current
        ExpectedCommand = $expected
        AlreadyMatches = $matches
        ReplacesExisting = $hasDifferentValue
    }
}

function Enable-CumStartup
{
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [switch]$ForceStartupReplace
    )

    $initialPlan = Get-CumStartupReplacementPlan `
        -Executable $Executable `
        -ForceStartupReplace:$ForceStartupReplace
    if ($initialPlan.AlreadyMatches)
    {
        return [pscustomobject]@{
            Changed = $false
            ReplacedExisting = $false
            PreviousCommand = $null
            ExpectedCommand = $initialPlan.ExpectedCommand
        }
    }

    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $action = if ($initialPlan.ReplacesExisting)
    {
        'Replace the current user CodexUsageMonitor startup entry'
    }
    else
    {
        'Enable Codex Usage Monitor at sign-in'
    }
    if (-not $PSCmdlet.ShouldProcess(
        "$runKey\CodexUsageMonitor",
        $action))
    {
        return [pscustomobject]@{
            Changed = $false
            ReplacedExisting = $initialPlan.ReplacesExisting
            PreviousCommand = $initialPlan.CurrentCommand
            ExpectedCommand = $initialPlan.ExpectedCommand
        }
    }

    if (-not (Test-Path -LiteralPath $runKey))
    {
        New-Item `
            -Path $runKey `
            -Force `
            -ErrorAction Stop | Out-Null
    }

    # Re-read immediately before mutation. A different process may have
    # created or changed the value after the first ownership check.
    $plan = Get-CumStartupReplacementPlan `
        -Executable $Executable `
        -ForceStartupReplace:$ForceStartupReplace
    if ($plan.AlreadyMatches)
    {
        return [pscustomobject]@{
            Changed = $false
            ReplacedExisting = $false
            PreviousCommand = $null
            ExpectedCommand = $plan.ExpectedCommand
        }
    }

    try
    {
        Set-ItemProperty `
            -LiteralPath $runKey `
            -Name 'CodexUsageMonitor' `
            -Value $plan.ExpectedCommand `
            -ErrorAction Stop

        $updated = Get-CumStartupValue
        if (-not [string]::Equals(
            [string]$updated,
            $plan.ExpectedCommand,
            [StringComparison]::OrdinalIgnoreCase))
        {
            throw (
                'The CodexUsageMonitor startup entry changed concurrently ' +
                'and could not be verified.')
        }
    }
    catch
    {
        $currentAfterFailure = Get-CumStartupValue
        $stillOwnsAttemptedValue = [string]::Equals(
            [string]$currentAfterFailure,
            $plan.ExpectedCommand,
            [StringComparison]::OrdinalIgnoreCase)
        if ($stillOwnsAttemptedValue -and $plan.ReplacesExisting)
        {
            Set-ItemProperty `
                -LiteralPath $runKey `
                -Name 'CodexUsageMonitor' `
                -Value $plan.CurrentCommand `
                -ErrorAction SilentlyContinue
        }
        elseif ($stillOwnsAttemptedValue)
        {
            Remove-ItemProperty `
                -LiteralPath $runKey `
                -Name 'CodexUsageMonitor' `
                -ErrorAction SilentlyContinue
        }
        throw
    }

    return [pscustomobject]@{
        Changed = $true
        ReplacedExisting = $plan.ReplacesExisting
        PreviousCommand = if ($plan.ReplacesExisting)
        {
            $plan.CurrentCommand
        }
        else
        {
            $null
        }
        ExpectedCommand = $plan.ExpectedCommand
    }
}

function Remove-CumMatchingStartup
{
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [switch]$RestorePrevious,

        [AllowNull()]
        [string]$PreviousCommand
    )

    $current = Get-CumStartupValue
    $expected = Get-CumStartupCommand $Executable
    if ([string]::IsNullOrWhiteSpace($current))
    {
        return $false
    }

    if (-not [string]::Equals(
        $current,
        $expected,
        [StringComparison]::OrdinalIgnoreCase))
    {
        Write-Warning (
            'The CodexUsageMonitor startup entry points to a different ' +
            'executable and was left unchanged.')
        return $false
    }

    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $action = if ($RestorePrevious)
    {
        'Restore the startup entry that existed before forced replacement'
    }
    else
    {
        'Remove the matching Codex Usage Monitor startup entry'
    }
    if (-not $PSCmdlet.ShouldProcess(
        "$runKey\CodexUsageMonitor",
        $action))
    {
        return $false
    }

    $current = Get-CumStartupValue
    if (-not [string]::Equals(
        [string]$current,
        $expected,
        [StringComparison]::OrdinalIgnoreCase))
    {
        Write-Warning (
            'The CodexUsageMonitor startup entry changed before removal and ' +
            'was left unchanged.')
        return $false
    }

    if ($RestorePrevious)
    {
        if ([string]::IsNullOrWhiteSpace($PreviousCommand))
        {
            throw (
                'The install marker requests startup restoration but does not ' +
                'contain the previous command.')
        }
        Set-ItemProperty `
            -LiteralPath $runKey `
            -Name 'CodexUsageMonitor' `
            -Value $PreviousCommand `
            -ErrorAction Stop
        if (-not [string]::Equals(
            [string](Get-CumStartupValue),
            $PreviousCommand,
            [StringComparison]::Ordinal))
        {
            throw 'The previous startup command could not be restored.'
        }
    }
    else
    {
        Remove-ItemProperty `
            -LiteralPath $runKey `
            -Name 'CodexUsageMonitor' `
            -ErrorAction SilentlyContinue
        if (-not [string]::IsNullOrWhiteSpace(
            [string](Get-CumStartupValue)))
        {
            throw (
                'The CodexUsageMonitor startup entry changed concurrently ' +
                'and was not removed.')
        }
    }
    return $true
}

function Stop-CumMonitorGracefully
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ControlExecutable,

        [ValidateRange(1, 60)]
        [int]$TimeoutSeconds = 10
    )

    $running = @(Get-Process `
        -Name 'CodexUsageMonitor' `
        -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0)
    {
        return @()
    }

    $controlPath = Resolve-CumFullPath $ControlExecutable
    if (-not (Test-Path -LiteralPath $controlPath -PathType Leaf))
    {
        throw (
            'A running monitor was found, but no trusted executable is ' +
            'available to request a safe shutdown.')
    }

    $process = Start-Process `
        -FilePath $controlPath `
        -ArgumentList '--shutdown' `
        -WindowStyle Hidden `
        -PassThru
    $process.WaitForExit()

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do
    {
        Start-Sleep -Milliseconds 100
        $remaining = @(Get-Process `
            -Name 'CodexUsageMonitor' `
            -ErrorAction SilentlyContinue)
    }
    while ($remaining.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)

    if ($remaining.Count -gt 0)
    {
        $ids = @($remaining | ForEach-Object { $_.Id })
        throw (
            'Codex Usage Monitor did not exit within ' +
            $TimeoutSeconds +
            ' seconds. Remaining process IDs: ' +
            ($ids -join ', '))
    }

    return @($running | ForEach-Object { $_.Id })
}

function Get-CumSha256
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return (Get-FileHash `
        -LiteralPath $Path `
        -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-CumExpectedReleaseHash
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath
    )

    $hashes = @()
    foreach ($line in Get-Content -LiteralPath $ManifestPath)
    {
        if ($line -match (
            '^\s*([0-9A-Fa-f]{64})\s+\*?' +
            'CodexUsageMonitor\.exe\s*$'))
        {
            $hashes += $Matches[1].ToUpperInvariant()
        }
    }

    $hashes = @($hashes | Select-Object -Unique)
    if ($hashes.Count -ne 1)
    {
        throw (
            'SHA256SUMS.txt must contain exactly one unambiguous hash for ' +
            'CodexUsageMonitor.exe.')
    }

    return $hashes[0]
}

function Copy-CumFileAtomically
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $sourcePath = Resolve-CumFullPath $Source
    $destinationPath = Assert-CumSafeMutationPath $Destination
    $destinationDirectory = Split-Path -Parent $destinationPath
    if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container))
    {
        Assert-CumSafeMutationPath $destinationDirectory | Out-Null
        New-Item `
            -ItemType Directory `
            -Path $destinationDirectory `
            -Force | Out-Null
    }
    Assert-CumSafeMutationPath $destinationDirectory | Out-Null

    $temporaryPath = Join-Path `
        $destinationDirectory `
        ('.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $replacementBackupPath = Join-Path `
        $destinationDirectory `
        ('.' + [Guid]::NewGuid().ToString('N') + '.bak')
    try
    {
        Assert-CumSafeMutationPath $temporaryPath | Out-Null
        Copy-Item `
            -LiteralPath $sourcePath `
            -Destination $temporaryPath `
            -Force
        if ((Get-CumSha256 $temporaryPath) -ne (Get-CumSha256 $sourcePath))
        {
            throw 'The copied executable failed its post-copy SHA-256 check.'
        }

        if (Test-Path -LiteralPath $destinationPath -PathType Leaf)
        {
            Assert-CumSafeMutationPath $temporaryPath | Out-Null
            Assert-CumSafeMutationPath $destinationPath | Out-Null
            Assert-CumSafeMutationPath $replacementBackupPath | Out-Null
            [IO.File]::Replace(
                $temporaryPath,
                $destinationPath,
                $replacementBackupPath)
        }
        else
        {
            Assert-CumSafeMutationPath $temporaryPath | Out-Null
            Assert-CumSafeMutationPath $destinationPath | Out-Null
            [IO.File]::Move($temporaryPath, $destinationPath)
        }
    }
    finally
    {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf)
        {
            Assert-CumSafeMutationPath $temporaryPath | Out-Null
            Remove-Item -LiteralPath $temporaryPath -Force
        }
        if (Test-Path -LiteralPath $replacementBackupPath -PathType Leaf)
        {
            Assert-CumSafeMutationPath $replacementBackupPath | Out-Null
            Remove-Item -LiteralPath $replacementBackupPath -Force
        }
    }
}

function Get-CumFileVersion
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    try
    {
        return [Diagnostics.FileVersionInfo]::GetVersionInfo(
            (Resolve-CumFullPath $Path)).FileVersion
    }
    catch
    {
        return $null
    }
}

function Write-CumInstallMarker
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Sha256,

        [bool]$StartupEntryWasReplaced = $false,

        [AllowNull()]
        [string]$PreviousStartupCommand
    )

    $installRoot = Assert-CumInstallDirectory $InstallDirectory
    if ($Sha256 -notmatch '^[0-9A-Fa-f]{64}$')
    {
        throw 'A valid executable SHA-256 is required for the install marker.'
    }
    if ($StartupEntryWasReplaced -and
        [string]::IsNullOrWhiteSpace($PreviousStartupCommand))
    {
        throw (
            'A previous startup command is required when recording a forced ' +
            'startup replacement.')
    }
    $markerPath = Join-Path `
        $installRoot `
        '.codex-usage-monitor-install.json'
    $markerAlreadyExists = Test-Path -LiteralPath $markerPath
    if ($markerAlreadyExists)
    {
        if ($null -eq (Read-CumInstallMarker $installRoot))
        {
            throw (
                'The install marker changed or became invalid before it could ' +
                'be updated.')
        }
    }

    $markerJson = [ordered]@{
        FormatVersion = 2
        Product = 'Codex Usage Monitor'
        Executable = 'CodexUsageMonitor.exe'
        InstallDirectory = $installRoot
        Sha256 = $Sha256.ToUpperInvariant()
        InstalledAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        StartupEntryWasReplaced = $StartupEntryWasReplaced
        PreviousStartupCommand = if ($StartupEntryWasReplaced)
        {
            $PreviousStartupCommand
        }
        else
        {
            $null
        }
    } |
        ConvertTo-Json -Depth 3

    Assert-CumSafeMutationPath $markerPath | Out-Null
    if ($markerAlreadyExists -and
        $null -eq (Read-CumInstallMarker $installRoot))
    {
        throw 'The install marker changed immediately before it was updated.'
    }
    Set-Content `
        -LiteralPath $markerPath `
        -Value $markerJson `
        -Encoding UTF8

    return $markerPath
}

function Read-CumInstallMarker
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallDirectory
    )

    $installRoot = Assert-CumSafeMutationPath $InstallDirectory
    $markerPath = Join-Path `
        $installRoot `
        '.codex-usage-monitor-install.json'
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf))
    {
        return $null
    }

    Assert-CumSafeMutationPath $markerPath | Out-Null
    try
    {
        $markerItem = Get-Item `
            -LiteralPath $markerPath `
            -Force `
            -ErrorAction Stop
        if ($markerItem.PSIsContainer -or $markerItem.Length -gt 65536)
        {
            return $null
        }

        $marker = Get-Content `
            -Raw `
            -LiteralPath $markerPath |
            ConvertFrom-Json
        $formatVersion = [int]$marker.FormatVersion
        $basicFieldsAreValid = (
            $formatVersion -eq 2 -and
            [string]::Equals(
                [string]$marker.Product,
                'Codex Usage Monitor',
                [StringComparison]::Ordinal) -and
            [string]::Equals(
                [string]$marker.Executable,
                'CodexUsageMonitor.exe',
                [StringComparison]::OrdinalIgnoreCase))
        if (-not $basicFieldsAreValid)
        {
            return $null
        }

        if (-not [string]::Equals(
                [string]$marker.InstallDirectory,
                $installRoot,
                [StringComparison]::OrdinalIgnoreCase))
        {
            return $null
        }

        if ([string]$marker.Sha256 -notmatch '^[0-9A-Fa-f]{64}$')
        {
            return $null
        }

        $installedAt = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParseExact(
            [string]$marker.InstalledAtUtc,
            'O',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None,
            [ref]$installedAt))
        {
            return $null
        }

        $startupProperty = $marker.PSObject.Properties.Match(
            'StartupEntryWasReplaced')
        if ($startupProperty.Count -ne 1 -or
            -not ($marker.StartupEntryWasReplaced -is [bool]))
        {
            return $null
        }

        $previousCommandProperty = $marker.PSObject.Properties.Match(
            'PreviousStartupCommand')
        if ($previousCommandProperty.Count -ne 1)
        {
            return $null
        }

        $startupWasReplaced = [bool]$marker.StartupEntryWasReplaced
        $previousCommand = [string]$marker.PreviousStartupCommand
        if (($startupWasReplaced -and
                ([string]::IsNullOrWhiteSpace($previousCommand) -or
                 $previousCommand.Length -gt 32767)) -or
            (-not $startupWasReplaced -and
                -not [string]::IsNullOrWhiteSpace($previousCommand)))
        {
            return $null
        }

        return $marker
    }
    catch
    {
        return $null
    }
}

function Test-CumInstallMarker
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallDirectory
    )

    return $null -ne (Read-CumInstallMarker $InstallDirectory)
}
