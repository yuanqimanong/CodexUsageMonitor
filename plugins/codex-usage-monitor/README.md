# Codex Usage Monitor plugin

A small Windows desktop panel for Codex quota, response speed and Token usage.

## Get started

Use the complete plugin ZIP from the project's GitHub Releases. It includes the desktop app. You need Windows 10 or 11 x64 and Codex Desktop. Sign in to Codex to view your remaining quota.

After installing or updating the plugin, start a new Codex chat and ask it to start Codex Usage Monitor. You can also ask it to check the running status, stop the monitor, or turn start-with-Windows on or off.

## Use the panel

Move the pointer over the tab at the screen edge to expand it. Choose **会话** to see running sessions as a single scrolling list of cards. Each card shows the session name, model, status, latest measured Token/s and cumulative input, cached input, output and reasoning output.

The **额度** tab shows remaining quota and reset times. Click `◇` to keep the panel open. Drag the header to move it, or use the tray menu to change its theme, toggle edge snapping or exit. All themes use opaque backgrounds for readability.

Cards refresh every three seconds while visible. Completed, interrupted, deleted, archived and background review sessions are filtered out. Sessions with no new records for five minutes are hidden until their logs update again. A long-running tool or reasoning step may therefore disappear temporarily.

Speed updates after completed model calls and uses client-observed timing. Missing measurements appear as `—`. Cached input is included in input, and reasoning output is included in output.

## Local data

The panel reads `CODEX_HOME`, or the current user's `.codex` folder by default. Use **数据目录** to choose a folder containing `state_*.sqlite` and `sessions`. The current session catalog is opened read-only through Windows' built-in SQLite. The panel follows recent JSONL logs for active sessions. Compressed records are not supported yet.

Session names and metrics stay on your computer. The parser passes over log lines containing messages and tool results without keeping their contents. Quota comes from the locally installed `codex app-server`, which uses Codex's normal connection. Check session names before sharing screenshots.

## Update or remove

Install the latest complete plugin package and start a new Codex chat. To remove the plugin, use Codex's plugin management screen. Exit the desktop panel from its tray menu when you are finished.
