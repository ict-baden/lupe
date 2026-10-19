<#
.SYNOPSIS
    Erzeugt alle Bilder der Doku neu: Screenshots der echten App und das Banner.

.DESCRIPTION
    1. Baut Lupe (Release) und rendert mit „Lupe.exe --screenshots“ jede Seite hell und dunkel
       (Demo-Kamera und Beispielverlauf, geht auch ohne sichtbaren Desktop).
    2. Rendert banner.html mit Chrome über playwright-core (npm install in diesem Ordner).

.EXAMPLE
    cd tools\screenshots; npm install; .\make-assets.ps1
#>
# SPDX-License-Identifier: MIT
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$shots = Join-Path $repo 'docs\assets\screenshots'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("lupe-assets-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $shots, $tmp | Out-Null

try {
    dotnet build (Join-Path $repo 'src\Lupe\Lupe.csproj') -c Release -nologo -v q
    $exe = Join-Path $repo 'src\Lupe\bin\Release\net10.0-windows10.0.19041.0\Lupe.exe'

    # 1. Screenshots – Dateinamen der App (deutsch) auf englische Doku-Namen abbilden
    & $exe --screenshots $tmp | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Screenshots fehlgeschlagen ($LASTEXITCODE)" }
    $names = @{ kamera = 'camera'; bildschirm = 'screen'; auswahl = 'selection'; treffer = 'result'; datei = 'file';
                verlauf = 'history'; einstellungen = 'settings'; info = 'about' }
    foreach ($file in Get-ChildItem $tmp -Filter *.png) {
        if ($file.BaseName -notmatch '^(?<page>.+)-(?<theme>hell|dunkel)$') { continue }
        $page = $names[$Matches.page]; if (-not $page) { continue }
        $theme = if ($Matches.theme -eq 'hell') { 'light' } else { 'dark' }
        Copy-Item $file.FullName (Join-Path $shots "$page-$theme.png") -Force
    }

    # 2. Banner (nutzt file-light.png aus Schritt 1)
    node (Join-Path $PSScriptRoot 'banner.js')
    if ($LASTEXITCODE -ne 0) { throw "Banner fehlgeschlagen ($LASTEXITCODE)" }
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
