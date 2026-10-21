# SPDX-License-Identifier: MIT
<#
.SYNOPSIS
    Erzeugt alle Bilder der Doku neu: Screenshots der echten App und das Banner.

.DESCRIPTION
    1. Baut Lupe (Release) und rendert mit „Lupe.exe --screenshots“ jede Seite hell und dunkel, englisch und deutsch
       (Demo-Kamera und Beispielverlauf, geht auch ohne sichtbaren Desktop).
    2. Rendert banner.html mit Chrome über playwright-core (npm install in diesem Ordner).

.EXAMPLE
    cd tools\screenshots; npm install; .\make-assets.ps1
#>
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$shots = Join-Path $repo 'docs\assets\screenshots'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("lupe-assets-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $shots, $tmp | Out-Null

try {
    dotnet build (Join-Path $repo 'src\Lupe\Lupe.csproj') -c Release -nologo -v q
    $exe = Join-Path $repo 'src\Lupe\bin\Release\net10.0-windows10.0.19041.0\Lupe.exe'

    # 1. Screenshots – englisch für die Doku, deutsch zusätzlich in de/; Dateinamen der App (deutsch) auf englische
    #    Doku-Namen abbilden
    $names = @{ kamera = 'camera'; bildschirm = 'screen'; auswahl = 'selection'; treffer = 'result'; datei = 'file';
                verlauf = 'history'; einstellungen = 'settings'; info = 'about' }
    foreach ($lang in 'en', 'de') {
        $out = Join-Path $tmp $lang
        $target = if ($lang -eq 'en') { $shots } else { Join-Path $shots $lang }
        New-Item -ItemType Directory -Force $out, $target | Out-Null
        $env:LUPE_LANGUAGE = $lang
        & $exe --screenshots $out | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Screenshots ($lang) fehlgeschlagen ($LASTEXITCODE)" }
        foreach ($file in Get-ChildItem $out -Filter *.png) {
            if ($file.BaseName -notmatch '^(?<page>.+)-(?<theme>hell|dunkel)$') { continue }
            $page = $names[$Matches.page]; if (-not $page) { continue }
            $theme = if ($Matches.theme -eq 'hell') { 'light' } else { 'dark' }
            Copy-Item $file.FullName (Join-Path $target "$page-$theme.png") -Force
        }
    }
    Remove-Item Env:LUPE_LANGUAGE

    # 2. Banner (nutzt file-light.png aus Schritt 1)
    node (Join-Path $PSScriptRoot 'banner.js')
    if ($LASTEXITCODE -ne 0) { throw "Banner fehlgeschlagen ($LASTEXITCODE)" }
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
