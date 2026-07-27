---
name: codex-usage-monitor
description: Control and diagnose the Codex Usage Monitor Windows desktop companion. Use when the user asks to start, stop, show, inspect, or troubleshoot the monitor; check whether it is running; enable or disable start-with-Windows; or locate its executable and local status. The monitor displays Codex rate-limit usage and reset times from the signed-in Codex Desktop account.
---

# Codex Usage Monitor

Use the bundled PowerShell scripts for all lifecycle operations. Do not rewrite process,
registry, or path-management logic unless the scripts themselves need repair.

## Resolve bundled scripts

The plugin scripts are two levels above this skill directory:

```powershell
$pluginRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$scripts = Join-Path $pluginRoot 'scripts'
```

When executing from Codex rather than inside this file, resolve the installed skill path first,
then use the same `..\..` relationship. Never assume the current working directory is the plugin
root.

## Operations

### Start or show

Run:

```powershell
& (Join-Path $scripts 'Start-CodexUsageMonitor.ps1')
```

Use `-Background` only when the user asks to start hidden in the notification area. Starting a
second instance safely asks the existing instance to show itself.

### Stop safely

Run:

```powershell
& (Join-Path $scripts 'Stop-CodexUsageMonitor.ps1')
```

This uses the application's `--shutdown` signal so its local `codex app-server` child and tray
resources can close cleanly. Do not use `Stop-Process` unless safe shutdown fails and the user
explicitly asks for a forced termination.

### Report status

Run:

```powershell
& (Join-Path $scripts 'Get-CodexUsageMonitorStatus.ps1')
```

Use `-AsJson` when another tool needs machine-readable output. Report `Installed`, `Running`,
`ProcessIds`, `ExecutablePath`, `RunningExecutablePath`, `Version`, and `StartWithWindows` to the
user. `ProcessIds` is always an array because diagnostics must remain correct even if a previous
instance is still exiting. If the bundled EXE is missing, explain that the package is incomplete
and recommend reinstalling it.

### Configure start-with-Windows

Enable:

```powershell
& (Join-Path $scripts 'Set-CodexUsageMonitorStartup.ps1') -Enabled $true
```

Disable:

```powershell
& (Join-Path $scripts 'Set-CodexUsageMonitorStartup.ps1') -Enabled $false
```

The script only changes the current user's `CodexUsageMonitor` Run entry. When disabling, it
leaves an entry pointing to a different executable untouched. Enabling also refuses to overwrite a
different entry. If the user explicitly wants a recoverable takeover, direct them to the
package-level installer and its `-ForceStartupReplace` option.

## Diagnostics and boundaries

- Treat displayed percentages as Codex rate-limit windows, not billing or personal spending.
- The executable uses the local authenticated `codex app-server` transport. Never request,
  extract, log, or copy account cookies, OAuth tokens, API keys, or browser storage.
- The monitor is Windows x64 only. Confirm that Codex Desktop is installed and signed in on the
  same Windows user account. Do not copy another user's Codex profile or authentication state.
- The bundled executable finds Codex through the current user's standard Codex Desktop location,
  then `PATH`. `CODEX_USAGE_MONITOR_CODEX_EXE` is an advanced per-machine override; only point it
  at a trusted `codex.exe`, and never write a developer machine's absolute path into the plugin.
- If data cannot be fetched, first report status, confirm Codex Desktop is installed and signed in,
  then restart the monitor. Preserve the application's error log when diagnosing.
- Settings and the optional error log live under `%LOCALAPPDATA%\CodexUsageMonitor`; do not delete
  them unless the user asks to reset or fully uninstall the application.
- A `.pdb` file is not required at runtime and is intentionally not distributed. Do not search for
  or copy one from a build machine into the plugin.
- Installation, upgrade, rollback, and removal are handled by the package-level scripts documented
  in the package `README.md`, not by ad-hoc file copies.
