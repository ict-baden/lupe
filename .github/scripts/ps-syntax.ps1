# SPDX-License-Identifier: MIT
<#
.SYNOPSIS
    Prüft die Syntax aller PowerShell-Skripte (unter -Root) und aller PowerShell-Schritte der Workflows.
.DESCRIPTION
    Eingabe: JSON aus ps-syntax.py (Name und Code je Schritt). ${{ … }}-Ausdrücke werden vor dem Parsen ersetzt.
    Neben Parserfehlern meldet es Texte, die mit einem typografischen Anführungszeichen beginnen oder enden: PowerShell
    liest „ “ ” (und ‚ ‘ ’) als Anführungszeichen. "Release „latest“ ist da" ist deshalb kein Syntaxfehler, sondern
    still drei Teile – und mit anderen Zeichen dahinter ein ParserError, der erst im Release-Job auffiel.
    Exitcode 0 ohne Fehler, sonst 1.
#>
# -Root: Ordner mit Skripten, mehrere mit Komma (pwsh -File reicht keine Liste weiter)
param([Parameter(Mandatory)][string]$Blocks, [string]$Root = '.github')
# Unlesbare Eingabe ist ein Fehler, kein „0 Fehler“
$ErrorActionPreference = 'Stop'
$quotes = [char[]]@(0x201C, 0x201D, 0x201E, 0x2018, 0x2019, 0x201A)

# Alle Tokens, auch die in $( … ) innerhalb eines Textes (NestedTokens)
function Get-AllTokens($tokens) {
    foreach ($t in $tokens) {
        $t
        if ($t.PSObject.Properties['NestedTokens'] -and $t.NestedTokens) { Get-AllTokens $t.NestedTokens }
    }
}

# Je Fehler ein Paar (Zeile, Meldung): Parserfehler und Texte, die ein typografisches Anführungszeichen begrenzt
function Test-Code($tokens, $errors) {
    foreach ($e in $errors) { , @($e.Extent.StartLineNumber, $e.Message) }
    foreach ($t in Get-AllTokens $tokens) {
        if ($t.Kind -notin 'StringExpandable', 'StringLiteral' -or $t.Text.Length -lt 2) { continue }
        if ($quotes -contains $t.Text[0] -or $quotes -contains $t.Text[$t.Text.Length - 1]) {
            , @($t.Extent.StartLineNumber, "typografisches Anführungszeichen begrenzt den Text $($t.Text) - gerade Anführungszeichen oder ein anderes Zeichen nehmen")
        }
    }
}

$fail = 0
$items = @(Get-Content $Blocks -Raw | ConvertFrom-Json)
if ($items.Count -eq 0) { Write-Host '::error::Keine PowerShell-Schritte gefunden - ps-syntax.py prüfen'; exit 1 }
foreach ($b in $items) {
    $code = $b.code -replace '(?s)\$\{\{.*?\}\}', 'X'
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
    foreach ($m in Test-Code $tokens $errors) { $fail++; Write-Host "::error::$($b.name), Zeile $($m[0]) des Schritts: $($m[1])" }
}
$files = @(Get-ChildItem -Recurse -Path ($Root -split ',') -Filter *.ps1)
foreach ($f in $files) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$tokens, [ref]$errors)
    $path = [IO.Path]::GetRelativePath((Get-Location).Path, $f.FullName)
    foreach ($m in Test-Code $tokens $errors) { $fail++; Write-Host "::error file=$path,line=$($m[0])::$($m[1])" }
}
Write-Host "PowerShell-Syntax: $($items.Count) Workflow-Schritte, $($files.Count) Skripte, $fail Fehler"
exit [int]($fail -gt 0)
