# Release 0.2.0 validation

Validated locally on Windows x64 on 2026-10-06 using .NET SDK 10.0.401. Self-contained packages bundle runtime 10.0.12.

- Release solution build: zero warnings and errors.
- 24 fixture tests passed, including file identity changes, path escapes, junctions, hard links, locks, age/exclusion enforcement, journals, history, duplicate analysis and fixed maintenance command boundaries.
- One generated 12-byte fixture was recycled through Windows Shell; no real user cache was cleaned.
- Eight GUI control checks passed: category selection/deselection, individual selection, mixed state, preview cleanup guard, theme distinction, category filter and preview maintenance guard.
- Four themes rendered at 1120 × 800. Paper, Midnight, Evergreen, Amethyst and the storage/tools pages were visually reviewed.
- Packaged CLI duplicate analysis, invalid-size rejection and refusal to overwrite an existing report passed using generated files.
- Setup compiled with Inno Setup 6.7.3. Installation to a unique workspace folder, repeat installation, installed GUI/CLI launch and uninstall passed with shortcuts disabled.
- Runtime and installer-engine license notices are included. SHA-256 package checksums were generated.

Advanced Windows maintenance operations were not run against the user's Windows installation. Test these operations, cross-version upgrades, shortcuts, accessibility, high-DPI layouts and Windows edition compatibility in disposable VMs before a public release. Native ARM64 is not covered.

GitHub CI/release workflows are configured but have not run on a hosted repository. Release binaries are unsigned; no signing or SmartScreen reputation validation was performed.
