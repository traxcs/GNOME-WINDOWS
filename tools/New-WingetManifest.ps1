param(
    [Parameter(Mandatory)] [string]$InstallerUrl,
    [string]$Version,
    [string]$Installer,
    [string]$Publisher = 'GnomeWin',
    [string]$PackageUrl = '',
    [ValidateSet('x64', 'arm64')] [string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Installer) { $Installer = Join-Path $here '..\dist\GnomeWin-Setup.exe' }
if (-not (Test-Path $Installer)) { throw "Installer not found: $Installer (run build.ps1 first)" }
if (-not $Version) { $Version = (Get-Item $Installer).VersionInfo.ProductVersion.Split('+')[0] }
$hash = (Get-FileHash $Installer -Algorithm SHA256).Hash
$id = 'GnomeWin.GnomeWin'
$dir = Join-Path $here "..\packaging\winget\manifests\g\GnomeWin\GnomeWin\$Version"
New-Item -ItemType Directory -Force $dir | Out-Null
$schema = '1.6.0'
$utf8 = New-Object System.Text.UTF8Encoding $false

[IO.File]::WriteAllText("$dir\$id.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: fr-FR
ManifestType: version
ManifestVersion: $schema
"@, $utf8)

[IO.File]::WriteAllText("$dir\$id.installer.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
MinimumOSVersion: 10.0.19041.0
InstallerType: exe
Scope: user
InstallModes:
  - interactive
  - silent
  - silentWithProgress
InstallerSwitches:
  Silent: --install --quiet --startup
  SilentWithProgress: --install --quiet --startup
  Interactive: --install
UpgradeBehavior: install
AppsAndFeaturesEntries:
  - DisplayName: GnomeWin
    Publisher: GnomeWin
    ProductCode: GnomeWin
    DisplayVersion: $Version
Installers:
  - Architecture: $Architecture
    InstallerUrl: $InstallerUrl
    InstallerSha256: $hash
ManifestType: installer
ManifestVersion: $schema
"@, $utf8)

[IO.File]::WriteAllText("$dir\$id.locale.fr-FR.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: fr-FR
Publisher: $Publisher
PackageName: GnomeWin
$(if ($PackageUrl) { "PackageUrl: $PackageUrl" })
License: MIT
ShortDescription: L'expérience GNOME Shell / Ubuntu pour Windows 11 (vue d'ensemble, dock, espaces de travail).
Description: |-
  Shell de bureau réversible : vue d'ensemble avec aperçus en direct, Ubuntu Dock, espaces de travail
  dynamiques (bureaux virtuels Windows), grille d'applications, recherche, barre supérieure et réglages
  rapides. Aucune modification système ; la barre des tâches Windows est toujours restaurée.
Tags:
  - gnome
  - ubuntu
  - dock
  - shell
  - virtual-desktops
  - workspaces
ManifestType: defaultLocale
ManifestVersion: $schema
"@, $utf8)

Write-Host "Manifests written to $((Resolve-Path $dir).Path)"
Write-Host "SHA256: $hash"
