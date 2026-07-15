# Publishes AccentHold to %LOCALAPPDATA%\Programs\AccentHold, registers it to start
# with Windows (per-user, no admin required) and launches it.
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\AccentHold'
$exe  = Join-Path $dest 'AccentHold.exe'

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

Get-Process AccentHold -ErrorAction SilentlyContinue | Stop-Process -Force
& $dotnet publish (Join-Path $repo 'src\AccentHold') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $dest
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué ($LASTEXITCODE)" }

Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'AccentHold' -Value ('"{0}"' -f $exe)
Start-Process $exe
Write-Host "AccentHold installé dans $dest et démarré (lancement automatique avec Windows activé)."
