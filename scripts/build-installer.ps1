# Publishes AccentHold (self-contained, no runtime prerequisite) and builds the Inno Setup installer into out\.
# Requires Inno Setup 6 (ISCC.exe); install with: winget install JRSoftware.InnoSetup
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repo 'src\AccentHold\AccentHold.csproj'
$version = ((([xml](Get-Content $csproj)).Project.PropertyGroup.Version) | Where-Object { $_ } | Select-Object -First 1)
$publish = Join-Path $repo 'out\publish'

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = Get-ChildItem "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", `
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' -ErrorAction SilentlyContinue |
        Select-Object -First 1 -Expand FullName
}
if (-not $iscc) { throw 'ISCC.exe not found. Install Inno Setup 6 (winget install JRSoftware.InnoSetup).' }

# Folder publish on purpose: WPF is incompatible with single-file (native libs fail to load).
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet publish (Join-Path $repo 'src\AccentHold') -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }

& $iscc "/DAppVersion=$version" "/DPublishDir=$publish" (Join-Path $repo 'installer\AccentHold.iss')
if ($LASTEXITCODE) { throw "ISCC failed ($LASTEXITCODE)" }
Write-Host "Built out\AccentHold-Setup-$version.exe"
