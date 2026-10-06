# Contributing

Use Windows, Git and the .NET 10 SDK. Run `scripts/test.ps1` before opening a pull request. GUI changes should include screenshots of Paper and Midnight at the minimum window size.

Keep cleanup rules narrow. Each rule must describe its contents, age threshold and user impact, and include fixture tests for its path boundary. Prefer supported Windows APIs over folder deletion. Do not introduce registry cleaners, forced unlocking, permanent fallback deletion, arbitrary elevated commands, telemetry or network-dependent cleanup.

Tests create uniquely named fixtures under `work/fixtures`; they must never use the user's real cache directories. The optional `scripts/test.ps1 -ShellIntegration` test recycles one generated 12-byte fixture through Windows Shell. It requires a normal interactive Windows user environment.

For maintenance changes, provide official Windows documentation, the exact fixed arguments, failure handling and an explanation of reversibility. System-changing commands should be manually tested in a disposable Windows VM; the automated suite verifies the command boundaries without executing those operations.

Use focused pull requests with a clear description of the problem, final behavior and validation. Discuss broad feature changes in an issue first. Contributions are accepted under the project's MIT license.

All changes to protected `main` use pull requests. Windows CI's `test` check must pass against the latest main commit, and review conversations must be resolved. Squash or rebase merge to preserve linear history. Force pushes and branch deletion are blocked, including administrator changes. Reviews are welcome; an external approval is not mandatory while the project has a solo maintainer.
