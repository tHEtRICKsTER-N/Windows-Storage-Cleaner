[CmdletBinding()]
param([string]$CompilerPath, [string]$OutputDirectory, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root "dist/v$version" }
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $root 'work/release-build' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
if (Test-Path -LiteralPath $OutputDirectory) { throw "Output already exists: $OutputDirectory. Review it before removing it or choose another -OutputDirectory." }
& (Join-Path $PSScriptRoot 'test.ps1')
if (-not $CompilerPath) { $CompilerPath = & (Join-Path $PSScriptRoot 'get-inno.ps1') }
$CompilerPath = [IO.Path]::GetFullPath($CompilerPath)
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'Inno Setup compiler not found.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$payload = Join-Path $OutputDirectory 'portable'
New-Item -ItemType Directory -Path $payload | Out-Null
foreach ($project in @('Cleaner.App','Cleaner.Maintenance','Cleaner.Cli')) {
    $csproj = Join-Path $root "src/$project/$project.csproj"
    & dotnet restore $csproj -r win-x64 --artifacts-path $ArtifactsPath --source https://api.nuget.org/v3/index.json -p:SelfContained=true
    if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
    & dotnet publish $csproj -c Release -r win-x64 --self-contained true --no-restore --artifacts-path $ArtifactsPath --output $payload -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
foreach ($name in @('LICENSE','README.md','CHANGELOG.md','SECURITY.md','THIRD_PARTY_NOTICES.md','CONTRIBUTING.md','CODE_OF_CONDUCT.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $payload }
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination (Join-Path $payload 'docs') -Recurse
$noticeFolder = Join-Path $payload 'third-party'
New-Item -ItemType Directory -Path $noticeFolder | Out-Null
Copy-Item -LiteralPath (Join-Path (Split-Path $CompilerPath -Parent) 'license.txt') -Destination (Join-Path $noticeFolder 'Inno-Setup-LICENSE.txt')
$assets = Get-Content -LiteralPath (Join-Path $ArtifactsPath 'obj/Cleaner.App/project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$runtimeConfig = Get-Content -LiteralPath (Join-Path $payload 'WindowsStorageCleaner.runtimeconfig.json') -Raw | ConvertFrom-Json
$frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App') })
if ($frameworks.Count -ne 2) { throw 'Expected the .NET and Windows Desktop runtimes.' }
foreach ($framework in $frameworks) {
        $packageName = $framework.name+'.Runtime.win-x64'; $packageVersion = $framework.version; $found = $false
        foreach ($packageRoot in $packageRoots) {
            $folder = Join-Path $packageRoot ($packageName.ToLowerInvariant()+'/'+$packageVersion)
            if (Test-Path -LiteralPath $folder) {
                foreach ($file in Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' }) {
                    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $noticeFolder ($packageName+'-'+$file.Name))
                    $found = $true
                }
                break
            }
        }
        if (-not $found) { throw "Runtime license missing: $packageName" }
}
if (-not (Test-Path -LiteralPath (Join-Path $noticeFolder 'Microsoft.NETCore.App.Runtime.win-x64-THIRD-PARTY-NOTICES.TXT'))) { throw 'Runtime third-party notices missing.' }
# Setup includes the same complete payload as the portable package.
& $CompilerPath (Join-Path $root 'installer/WindowsStorageCleaner.iss') "/DAppVersion=$version" "/DSourceRoot=$root" "/DPayloadDir=$payload" "/DReleaseDir=$OutputDirectory"
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $OutputDirectory "WindowsStorageCleaner-$version-win-x64.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$setup = Join-Path $OutputDirectory "WindowsStorageCleaner-$version-Setup-x64.exe"
$checksums = foreach ($file in @($setup,$zip)) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($file) }
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
Write-Host "Release packages are ready in $OutputDirectory"
