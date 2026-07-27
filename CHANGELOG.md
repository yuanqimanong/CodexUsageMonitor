# Changelog

## 1.1.0

- Replaced the wide compact card with a small direction-aware semicircle: `30×52` DIPs on
  the left/right edges and `52×30` DIPs on the top/bottom edges.
- Added deterministic corner handling so one primary edge owns compact layout and drag state.
- Added direct switching between all four dock directions without retaining the previous
  orthogonal edge.
- Added double-click expansion while preserving drag-away restore.
- Kept `100`, `--`, and `∞` fully visible in every compact orientation; the tooltip retains
  the complete percentage text.

## 1.0.0

- Added the Windows x64 floating Codex usage monitor and notification-area controls.
- Added Cyberpunk, Glass, and Minimal themes.
- Added four-edge snapping, automatic compact mode, and drag-away restore.
- Improved compact percentage layout for `100%`, `--%`, and `∞`.
- Added the Codex-linked usage gauge application and plugin icons.
- Added portable Codex executable discovery and portable MSBuild discovery.
- Bounded app-server stdout/stderr processing, display-field sizes, and pending UI updates.
- Added per-user installation, rollback, uninstall, startup, privacy, and release documentation.
- Excluded PDB, `bin`, `obj`, local logs, settings, probes, schemas, and account-linked data from
  public packaging.
