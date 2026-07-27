# Codex Usage Monitor plugin

This Codex plugin controls the bundled Windows desktop companion. It does not add an account,
network service, browser extension, or authentication flow.

## Requirements

- Windows 10 or Windows 11, x64
- Codex Desktop installed and signed in for the current Windows user
- `assets/CodexUsageMonitor.exe`, `assets/icon.png`, and `assets/logo.png` present in the package

## Bundled operations

The scripts resolve all files relative to the plugin directory. They do not contain a user name,
an installation-version hash, or an absolute path copied from a build computer.

```powershell
.\scripts\Start-CodexUsageMonitor.ps1
.\scripts\Get-CodexUsageMonitorStatus.ps1
.\scripts\Set-CodexUsageMonitorStartup.ps1 -Enabled $true
.\scripts\Stop-CodexUsageMonitor.ps1
```

`Get-CodexUsageMonitorStatus.ps1` returns `ProcessIds` as an array. Use `-AsJson` for
machine-readable output.

Enabling start-with-Windows refuses to overwrite an existing `CodexUsageMonitor` Run value that
points somewhere else. Intentional takeover and restoration are package-install operations; use the
repository installer with `-ForceStartupReplace` instead of overwriting the registry value manually.

## Privacy boundary

The companion asks the locally installed `codex app-server` for rate-limit windows under the
current Codex Desktop session. The plugin must never read or export cookies, OAuth tokens, API
keys, browser storage, or another Windows user's profile. See the repository-level
`docs/PRIVACY.md` for the complete boundary.

## Packaging

The release plugin contains the optimized EXE and public image assets only. Build directories,
settings, logs, account data, and `.pdb` symbol files are not part of the plugin archive.
