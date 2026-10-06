# Security

## Reporting

Once hosted on GitHub, the repository owner should enable private vulnerability reporting in Settings → Code security. Use the repository's Security → Advisories → Report a vulnerability form when available. If it is unavailable, contact the repository owner privately using a contact method they publish. No maintained private reporting endpoint exists until the owner configures one. Do not publish exploit details or personal file paths in a public issue.

Only the latest release is intended to receive fixes. This project has no promised response SLA.

## Boundaries and limitations

The desktop app refuses to run elevated. The helper accepts a single known operation ID and launches only fixed Windows system executables with fixed arguments. It does not accept file paths, scripts or arbitrary shell commands from the caller. Read-only analysis and user-cache recycling use ordinary user privileges.

Rules are embedded in the application, restricted to relative roots under LocalAppData, and validated. Scanning excludes reparse points, protected attributes, hard links and files that cannot be opened safely. Recycling creates an exclusive journal first and verifies identity and timestamps both before the Shell call and in its pre-delete callback. Parent directory leases inhibit ancestor replacement. If Windows proposes permanent deletion instead of recycling, the callback vetoes it.

The last-moment file check and Shell deletion are not one atomic filesystem operation. A malicious process already running as the same Windows user may still race file replacement. This app is not a security boundary against that process. User-writable application installations also cannot establish a trust boundary against malicious code with the same user's access; only approve elevation for an installation you trust.

Recycling does not guarantee application-level recovery and does not free occupied space immediately. Advanced servicing and download-cache operations make permanent changes through Windows tools. Hibernation operations change power behavior. There is no automatic restore point, scheduled cleanup, automatic Recycle Bin emptying, or system-wide permanent file deletion.

Local JSON reports and journals contain file paths. The app sends no telemetry and has no app-update network client. Preferences and journals survive uninstall so recovery records remain available.

Binaries currently have no code-signing certificate. SHA-256 checksums detect accidental changes but are only trustworthy when acquired from a trusted release source. CI never accepts or distributes a signing secret in this repository.
