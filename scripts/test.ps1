[CmdletBinding()]
param([switch]$ShellIntegration, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $root 'work/test-build' }
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null
& dotnet build (Join-Path $root 'WindowsStorageCleaner.slnx') -c Release --artifacts-path $ArtifactsPath --configfile (Join-Path $root 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$fixtureRoot = Join-Path $root 'work/fixtures'
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
$testArgs = @($fixtureRoot)
if ($ShellIntegration) { $testArgs += '--shell' }
& (Join-Path $ArtifactsPath 'bin/Cleaner.Tests/release/Cleaner.Tests.exe') @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Fixture tests failed.' }
$ui = Join-Path $ArtifactsPath 'ui'
New-Item -ItemType Directory -Force -Path $ui | Out-Null
foreach ($theme in @('Paper','Midnight','Evergreen','Amethyst')) {
    $image = Join-Path $ui ($theme.ToLowerInvariant()+'.png')
    & dotnet (Join-Path $ArtifactsPath 'bin/Cleaner.App/release/WindowsStorageCleaner.dll') --render-preview-small $image $theme
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $image)) { throw "UI rendering failed: $theme" }
}
$checks = Get-Content -LiteralPath (Join-Path $ui 'ui-checks.txt')
if ($checks.Count -lt 8 -or ($checks | Where-Object { $_ -notlike 'PASS *' })) { throw 'UI control checks failed.' }
& (Join-Path $ArtifactsPath 'bin/Cleaner.Cli/release/cleaner.exe') --help
if ($LASTEXITCODE -ne 0) { throw 'CLI smoke test failed.' }
Write-Host 'Build, fixture tests, four theme renders and UI control checks passed.'
