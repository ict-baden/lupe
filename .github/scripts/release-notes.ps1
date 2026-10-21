# SPDX-License-Identifier: MIT
<#
.SYNOPSIS
    Gibt den Release-Text (englisch, Markdown) für „latest“ oder ein Tag-Release aus.

.DESCRIPTION
    Für GitHub Actions (braucht GITHUB_REPOSITORY und GITHUB_SHA). Der Text steht in einer eigenen Datei statt im
    Workflow: Typografische Anführungszeichen beendeten dort in PowerShell den Text (ParserError im Release-Job).
    Die Dateinamen müssen zu denen im Workflow und in packaging/*/package.sh passen; release-latest.ps1 prüft mit
    -Expected, dass es sie gibt.
    Tag-Releases bekommen den Abschnitt „## [Version]“ aus CHANGELOG.md (englisch) statt einer erzeugten Liste der
    Pull Requests; fehlt er, bricht das Skript ab. Links in die Doku zeigen dort auf den Stand des Tags.

.EXAMPLE
    ./.github/scripts/release-notes.ps1 -Tag latest -Version 0.1.1-ci.40 -Signed $false -Notarized $false
    ./.github/scripts/release-notes.ps1 -Tag v0.1.1 -Version 0.1.1 -Suffix -0.1.1
#>
param(
    [Parameter(Mandatory)] [string] $Tag,
    [Parameter(Mandatory)] [string] $Version,
    # Dateisuffix der Tag-Releases ("-1.2.3"), bei „latest“ leer
    [string] $Suffix = '',
    [bool] $Signed = $false,
    [bool] $Notarized = $false,
    [string] $Changelog = (Join-Path $PSScriptRoot '../../CHANGELOG.md')
)
$ErrorActionPreference = 'Stop'
$repo = $env:GITHUB_REPOSITORY
$base = "https://github.com/$repo/releases/download/$Tag"
$ref = if ($Tag -eq 'latest') { 'main' } else { $Tag }
$docs = "https://github.com/$repo/blob/$ref/docs"
$deb = if ($Suffix) { "lupe_${Version}_amd64.deb" } else { 'lupe_amd64.deb' }
$windowsSigned = if ($Signed) { 'Windows files are signed.' } else { 'Windows files are not signed yet: SmartScreen may warn on first start (*More info* › *Run anyway*).' }
$macNote = if ($Notarized) { 'signed and notarized' } else { "not notarized yet: on first start, choose *Open Anyway* under System Settings › Privacy & Security, see [Installation]($docs/installation.md#macos)" }

$lines = @()
if ($Tag -eq 'latest') {
    $built = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm')
    $lines += "Automatic build of main ($($env:GITHUB_SHA.Substring(0, 7))), version $Version, built $built UTC. " +
              'The links stay the same and always point to the newest build. For a fixed version, use the release marked *Latest*.'
}
else {
    $lines += "Lupe $Version – reads QR codes and barcodes from the webcam, a screen region, files or the clipboard."
    $lines += ''
    # Abschnitt der Version aus dem CHANGELOG, ohne die Überschrift „## [1.2.3] – Datum“, bis zum nächsten „## “
    $section = @()
    $inside = $false
    foreach ($line in Get-Content -LiteralPath $Changelog -Encoding utf8) {
        if ($line -match '^## ') {
            if ($inside) { break }
            $inside = $line -match "^## \[$([regex]::Escape($Version))\]"
            continue
        }
        if ($inside) { $section += $line }
    }
    $text = ($section -join "`n").Trim()
    # Keine typografischen Anführungszeichen in PowerShell-Texten: Sie beenden den Text wie ein gerades "
    if (-not $text) { throw "CHANGELOG.md hat keinen Abschnitt '## [$Version]' mit Inhalt - vor dem Tag eintragen." }
    $lines += $text
    $lines += ''
    $lines += "All versions: [CHANGELOG](https://github.com/$repo/blob/$Tag/CHANGELOG.md)"
    $lines += ''
    $lines += '### Downloads'
}
$lines += ''
$lines += $windowsSigned
$lines += ''
$lines += '**Windows** (10 1809 or later, x64)'
$lines += ''
$lines += "- **[Lupe$Suffix-x64.msi]($base/Lupe$Suffix-x64.msi)**: installer for all users (Intune/GPO)"
$lines += "- **[Lupe.exe]($base/Lupe.exe)**: single file, runs without installation and without the .NET runtime"
$lines += "- **[Lupe$Suffix-win-x64-framework.zip]($base/Lupe$Suffix-win-x64-framework.zip)**: small variant, needs the .NET 10 runtime"
$lines += ''
$lines += '**Linux** (X11 and Wayland)'
$lines += ''
$lines += "- **[$deb]($base/$deb)**: package for Debian/Ubuntu (x64)"
$lines += "- **[Lupe$Suffix-linux-x64.tar.gz]($base/Lupe$Suffix-linux-x64.tar.gz)** / **[Lupe$Suffix-linux-arm64.tar.gz]($base/Lupe$Suffix-linux-arm64.tar.gz)**: " +
          'unpack, run `lupe/install.sh` (glibc 2.31 or later: Ubuntu 20.04, Debian 11)'
$lines += ''
$lines += "**macOS** (12 or later; $macNote)"
$lines += ''
$lines += "- **[Lupe$Suffix-macos-arm64.zip]($base/Lupe$Suffix-macos-arm64.zip)**: Apple Silicon"
$lines += "- **[Lupe$Suffix-macos-x64.zip]($base/Lupe$Suffix-macos-x64.zip)**: Intel"
$lines += ''
$lines += "[SHA256SUMS.txt]($base/SHA256SUMS.txt): checksums of all files · " +
          "[Lupe$Suffix-sbom.cdx.json]($base/Lupe$Suffix-sbom.cdx.json): software bill of materials (CycloneDX) · " +
          "[Installation]($docs/installation.md)"
$lines -join "`n"
