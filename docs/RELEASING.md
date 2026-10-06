# Release process

## Local packaging

Use Windows x64, the .NET 10 SDK, PowerShell and an internet connection for official runtime packs. Ordinary builds/tests use no external package feeds.

1. Run `scripts/test.ps1`. Optional `-ShellIntegration` recycles one generated fixture only.
2. Run `scripts/get-inno.ps1`. It downloads pinned Inno Setup 6.7.3, checks its SHA-256 and Authenticode signature, and extracts a portable compiler under `work/inno`.
3. Run `scripts/release.ps1`. Output goes to `dist/v0.2.0`. Use `-CompilerPath C:\path\ISCC.exe` for an existing compiler.
4. Review the portable ZIP, Setup EXE and `SHA256SUMS.txt`. Test installation, upgrade and uninstall in a disposable Windows VM before public release. Uninstall keeps preferences and recycling journals.
5. Verify screenshots and advanced operations in the VM. System-maintenance commands are not executed by automated tests.

Release scripts fail rather than overwrite an existing version's output. Remove an obsolete output manually after reviewing it, or build with a distinct `-OutputDirectory`. The app version is read from `Directory.Build.props`.

Self-contained .NET must be rebuilt regularly with updated runtime packs. The scripts do not install a machine-wide runtime or add startup tasks. Setup registers the per-user application and uninstaller and supports an optional desktop shortcut.

## Host on GitHub

Create an empty public GitHub repository under your own account. From this folder:

```powershell
git init -b main
git add .
git commit -m "Release Windows Storage Cleaner 0.2.0"
git remote add origin <YOUR_REPOSITORY_URL>
git push -u origin main
```

Use your own configured Git name and email. Do not commit `dist/`, `work/`, signing certificates or personal reports. Enable private vulnerability reporting and publish a private maintainer contact method.

The CI workflow builds/tests on Windows and checks the GUI in demo mode. The release workflow runs for semver tags and prepares a draft release with setup, portable and checksum assets. It uses GitHub's built-in token with contents-write permission limited to the release job.

```powershell
git tag v0.2.0
git push origin v0.2.0
```

Review the draft's assets, changelog, signatures and version before publishing. No signing certificate is configured; describe binaries as unsigned. Add signing through a separately configured protected environment if a certificate becomes available.

The app is MIT licensed. Keep bundled runtime and installer-engine license notices in redistributed packages.
