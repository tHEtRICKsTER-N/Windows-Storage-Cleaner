# Release process

## Local packaging

Use Windows x64, the .NET 10 SDK, PowerShell and an internet connection for official runtime packs. Ordinary builds/tests use no external package feeds.

1. Run `scripts/test.ps1`. Optional `-ShellIntegration` recycles one generated fixture only.
2. Run `scripts/get-inno.ps1`. It downloads pinned Inno Setup 6.7.3, checks its SHA-256 and Authenticode signature, and extracts a portable compiler under `work/inno`.
3. Run `scripts/release.ps1`. Output goes to `dist/v<version>`. Use `-CompilerPath C:\path\ISCC.exe` for an existing compiler.
4. Review the portable ZIP, Setup EXE and `SHA256SUMS.txt`. Test installation and uninstall in a disposable Windows environment. Uninstall keeps preferences and recycling journals.
5. Verify advanced operations, cross-version upgrades and Windows edition compatibility in disposable VMs. System-maintenance commands are not executed by automated tests.

Release scripts fail rather than overwrite an existing version's output. Review obsolete output before removing it, or choose a distinct `-OutputDirectory`. The app version is read from `Directory.Build.props`.

Self-contained .NET must be rebuilt regularly with updated runtime packs. Setup installs per user, registers an uninstaller, and offers an optional desktop shortcut. It adds no startup task or machine-wide runtime.

## GitHub releases

The project is hosted at [tHEtRICKsTER-N/Windows-Storage-Cleaner](https://github.com/tHEtRICKsTER-N/Windows-Storage-Cleaner). Download published packages from [Releases](https://github.com/tHEtRICKsTER-N/Windows-Storage-Cleaner/releases).

For a new version:

1. Update `Directory.Build.props`, changelog, documentation and version labels in a feature branch.
2. Open a pull request, resolve review conversations, and wait for Windows CI's `test` check against the current `main`.
3. Squash or rebase merge the PR. Pull the resulting main commit locally.
4. Tag that exact tested commit. Substitute the new project version in this example:

```powershell
git checkout main
git pull --ff-only origin main
git tag -a v0.2.0 -m "Windows Storage Cleaner v0.2.0"
git push origin v0.2.0
```

A tag must match the project version exactly and must not replace an existing release tag. The release workflow builds/tests, compiles setup, and prepares a **draft** with setup, portable ZIP and checksums. Its contents-write permission is limited to the release job.

Review the draft's assets, checksums, licenses, changelog, version and installer behavior before publishing it. Describe binaries as unsigned until a separately configured signing certificate is available. Never commit signing certificates or secrets.

All changes to protected `main` use pull requests with required CI and resolved conversations, including administrator changes. No external approval is mandatory while the project has a solo maintainer. Force pushes and branch deletion are blocked; merges preserve linear history.

Do not commit `dist/`, `work/`, personal reports or signing keys. Source and original artwork are MIT licensed; retain the bundled runtime and installer-engine notices when redistributing packages.
