param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$executablePath = Join-Path $PSScriptRoot 'bin\AirPods4Tray.exe'
if (-not (Test-Path -LiteralPath $executablePath)) { throw 'Build the application first.' }
$distPath = Join-Path $PSScriptRoot 'dist'
$stagePath = Join-Path $distPath 'package'
New-Item -ItemType Directory -Path $stagePath -Force | Out-Null
Copy-Item -LiteralPath $executablePath -Destination (Join-Path $stagePath 'AirPods4Tray.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QUICKSTART.txt') -Destination (Join-Path $stagePath 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $stagePath 'LICENSE') -Force
$releasePath = Join-Path $distPath 'AirPodsTray-0.2.0-beta-win-x64.zip'
$payloadPaths = 'AirPods4Tray.exe','README.txt','LICENSE' | ForEach-Object { Join-Path $stagePath $_ }
Compress-Archive -LiteralPath $payloadPaths -DestinationPath $releasePath -Force
Get-FileHash -LiteralPath $releasePath -Algorithm SHA256 | Format-List
Write-Output "Release: $releasePath"
