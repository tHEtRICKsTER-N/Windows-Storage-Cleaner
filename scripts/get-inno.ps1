[CmdletBinding()]
param([string]$ToolsDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $ToolsDirectory) { $ToolsDirectory = Join-Path $root 'work/inno' }
$ToolsDirectory = [IO.Path]::GetFullPath($ToolsDirectory)
$compiler = Join-Path $ToolsDirectory 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) { return $compiler }
New-Item -ItemType Directory -Force -Path $ToolsDirectory | Out-Null
$download = Join-Path $ToolsDirectory 'innosetup-6.7.3.exe'
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$expected = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $download
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Inno Setup download checksum mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Pyrsys B.V.') { throw 'Inno Setup publisher signature is invalid.' }
$args = @('/PORTABLE=1','/VERYSILENT','/CURRENTUSER','/NOICONS','/TASKS=','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$ToolsDirectory+'"'))
$process = Start-Process -FilePath $download -ArgumentList $args -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw 'Could not extract the portable compiler.' }
return $compiler
