# Builds dist\VoiceDrop-Setup-<version>.exe  (requires .NET SDK and Inno Setup 6)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

Remove-Item -Recurse -Force dist\app -ErrorAction SilentlyContinue
dotnet publish src\VoiceDrop -c Release -r win-x64 --self-contained true -p:DebugType=none -o dist\app
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# the app is win-x64 only: drop whisper runtimes for other platforms
foreach ($rid in 'win-arm64', 'win-x86', 'linux-x64', 'linux-arm64', 'osx-arm64', 'osx-x64') {
    Get-ChildItem dist\app\runtimes -Directory -Recurse -Filter $rid | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install with: winget install JRSoftware.InnoSetup' }
& $iscc installer\VoiceDrop.iss
if ($LASTEXITCODE -ne 0) { throw 'installer build failed' }
Get-Item dist\VoiceDrop-Setup-*.exe
