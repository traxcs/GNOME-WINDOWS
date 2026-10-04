
param(
    [ValidateSet('win-x64', 'win-arm64')] [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet -or -not (& $dotnet --list-sdks | Select-String '^8\.|^9\.|^1\d\.')) {
    $local = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path $local) { $dotnet = $local; $env:DOTNET_ROOT = Split-Path $local }
    else { throw '.NET 8 SDK not found. Install it from https://dot.net or with: winget install Microsoft.DotNet.SDK.8' }
}

$env:DOTNET_ROOT = Split-Path $dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }

if (-not $SkipTests) {
    Write-Host '== Unit tests' -ForegroundColor Cyan
    & $dotnet test "$root\tests\GnomeWin.Tests\GnomeWin.Tests.csproj" -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed' }
}

Write-Host "== Publish ($Runtime, self-contained single file)" -ForegroundColor Cyan
$publish = Join-Path $root "artifacts\publish\$Runtime"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
& $dotnet publish "$root\src\GnomeWin\GnomeWin.csproj" -c $Configuration -r $Runtime -p:Platform=$platform `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false -p:PublishReadyToRun=true -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

Write-Host '== Package' -ForegroundColor Cyan
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item "$publish\GnomeWin.exe" "$dist\GnomeWin-Setup.exe" -Force

$portable = Join-Path $root 'artifacts\portable'
if (Test-Path $portable) { Remove-Item -Recurse -Force $portable }
New-Item -ItemType Directory -Force $portable | Out-Null
Copy-Item "$publish\GnomeWin.exe" $portable
Copy-Item "$root\tools\Restore-Taskbar.cmd" $portable
Copy-Item "$root\README.md" $portable
$zip = Join-Path $dist 'GnomeWin-Portable.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$portable\*" -DestinationPath $zip

Get-ChildItem $dist | ForEach-Object { '{0,-28} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
Write-Host 'Done.' -ForegroundColor Green
