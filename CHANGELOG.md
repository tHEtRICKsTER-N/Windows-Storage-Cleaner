# Changelog

## 0.2.0 — 2026-10-06

- Rebuilt the desktop interface with five navigation pages, searchable cache/file lists and category filters.
- Added Paper, Midnight, Evergreen, Amethyst and Windows System appearance; persistent preferences.
- Expanded to 21 cache rules covering browsers, graphics, development caches, Explorer and archived diagnostics.
- Added stricter age thresholds and folder exclusions, applied again at cleanup time.
- Added read-only large-file and SHA-256 duplicate-content analysis with cancellation and limits.
- Added local cache-cleanup history and JSON storage-analysis export.
- Added a separate UAC maintenance helper for fixed DISM, Delivery Optimization and hibernation operations.
- Expanded read-only CLI with age/exclusion options and folder analysis.
- Added self-contained x64 portable packaging, per-user Inno Setup installer, checksums, Windows CI and draft release workflow.
- Added MIT licensing metadata, contribution/security policies and GitHub issue templates.

## 0.1.0 — 2026-10-06

- Initial Windows WPF cache review app, read-only CLI, curated user/graphics rules.
- Conservative file identity validation, parent directory leases and Windows Recycle Bin-only handling.
- Per-file journals, cancellation, selection confirmation and fixture-based tests.
