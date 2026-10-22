# SPDX-License-Identifier: MIT
<#
.SYNOPSIS
    Prüft die Syntax aller PowerShell-Skripte unter .github/ und aller PowerShell-Schritte der Workflows.
.DESCRIPTION
    Eingabe: JSON aus ps-syntax.py (Name und Code je Schritt). ${{ … }}-Ausdrücke werden vor dem Parsen ersetzt.
    Neben Parserfehlern meldet es Texte, die mit einem typografischen Anführungszeichen beginnen oder enden: PowerShell
    liest „ “ ” (und ‚ ‘ ’) als Anführungszeichen. "Release „latest“ ist da" ist deshalb kein Syntaxfehler, sondern
    still drei Teile – und mit anderen Zeichen dahinter ein ParserError, der erst im Release-Job auffiel.
    Exitcode = Anzahl der Fehler.
#>
param([Parameter(Mandatory)][string]$Blocks, [string]$Root = '.github')
# Unlesbare Eingabe ist ein Fehler, kein „0 Fehler“
$ErrorActionPreference = 'Stop'
$quotes = [char[]]@(0x201C, 0x201D, 0x201E, 0x2018, 0x2019, 0x201A)

# Gibt je Fehler eine Zeile aus: Parserfehler und Texte, die ein typografisches Anführungszeichen begrenzt
function Test-Code([string] $where, $tokens, $errors) {
    foreach ($e in $errors) { "${where}, Zeile $($e.Extent.StartLineNumber): $($e.Message)" }
    foreach ($t in $tokens) {
        if ($t.Kind -notin 'StringExpandable', 'StringLiteral' -or $t.Text.Length -lt 2) { continue }
        if ($quotes -contains $t.Text[0] -or $quotes -contains $t.Text[$t.Text.Length - 1]) {
            "${where}, Zeile $($t.Extent.StartLineNumber): typografisches Anführungszeichen begrenzt den Text $($t.Text) - gerade Anführungszeichen oder ein anderes Zeichen nehmen"
        }
    }
}

$fail = 0
$items = @(Get-Content $Blocks -Raw | ConvertFrom-Json)
foreach ($b in $items) {
    $code = $b.code -replace '\$\{\{[^}]*\}\}', 'X'
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
    foreach ($m in Test-Code $b.name $tokens $errors) { $fail++; Write-Host "::error::$m" }
}
$files = @(Get-ChildItem -Recurse -Path $Root -Filter *.ps1)
foreach ($f in $files) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$tokens, [ref]$errors)
    foreach ($m in Test-Code $f.FullName $tokens $errors) { $fail++; Write-Host "::error file=$($f.FullName)::$m" }
}
Write-Host "PowerShell-Syntax: $($items.Count) Workflow-Schritte, $($files.Count) Skripte, $fail Fehler"
exit $fail
