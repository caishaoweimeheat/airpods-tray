param([switch]$Test)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputDirectory = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputPath = Join-Path $outputDirectory 'AirPods4Tray.exe'
$manifestPath = Join-Path $PSScriptRoot 'app.manifest'
$iconPath = Join-Path $PSScriptRoot 'AirPods4Tray.ico'
$sourceFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName
$commonArgs = @('/nologo', '/nowarn:1668', '/platform:x64', '/optimize+', '/codepage:65001', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xml.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', "/win32manifest:$manifestPath")
& $compilerPath @commonArgs '/target:winexe' "/win32icon:$iconPath" "/out:$outputPath" @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $outputPath"
if ($Test) {
    $testExe = Join-Path $outputDirectory 'AirPodsTray.Tests.exe'
    $testSource = Join-Path $PSScriptRoot 'tests\Tests.cs'
    & $compilerPath @commonArgs '/target:exe' '/main:AirPodsQuickConnect.Tests.TestProgram' "/out:$testExe" @sourceFiles $testSource
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExe (Join-Path $outputDirectory 'test-results')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
