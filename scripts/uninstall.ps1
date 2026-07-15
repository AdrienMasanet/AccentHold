# Stops AccentHold, removes the startup entry and deletes the installed files.
$ErrorActionPreference = 'Stop'

$dest = Join-Path $env:LOCALAPPDATA 'Programs\AccentHold'

Get-Process AccentHold -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'AccentHold' -ErrorAction SilentlyContinue
if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
Write-Host 'AccentHold désinstallé.'
