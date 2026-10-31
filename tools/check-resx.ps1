# Quick structural check for translation files, without building the app.
# Usage: powershell -File tools\check-resx.ps1 ca gl eu        (culture codes)
# Checks each src\Remindr\Resources\Strings.<code>.resx against the neutral Strings.resx:
# valid XML, every neutral key present (plural _Zero/_Two/_Few/_Many extras allowed), no empty values,
# and the same {n} placeholders as English. The full rules (plural coverage, English copies) live in dotnet test.
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Codes)

$ErrorActionPreference = 'Stop'
$res = Join-Path $PSScriptRoot '..\src\Remindr\Resources'

function Read-Resx([string]$path) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($path)
    $map = [ordered]@{}
    foreach ($d in $xml.SelectNodes('/root/data')) { $map[$d.GetAttribute('name')] = $d.SelectSingleNode('value').InnerText }
    $map
}

function Placeholders([string]$s) { ([regex]::Matches($s, '\{\d+(?:[^}]*)\}') | ForEach-Object { $_.Value -replace ':.*\}', '}' } | Sort-Object -Unique) -join ',' }

$neutral = Read-Resx (Join-Path $res 'Strings.resx')
$failed = 0
foreach ($code in $Codes) {
    $path = Join-Path $res "Strings.$code.resx"
    $problems = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path $path)) { Write-Host "$code  MISSING FILE"; $failed++; continue }
    try { $map = Read-Resx $path } catch { Write-Host "$code  BAD XML: $($_.Exception.Message)"; $failed++; continue }

    foreach ($k in $neutral.Keys) {
        if (-not $map.Contains($k)) { $problems.Add("missing $k"); continue }
        if ([string]::IsNullOrWhiteSpace($map[$k])) { $problems.Add("empty $k"); continue }
        $want = Placeholders $neutral[$k]; $got = Placeholders $map[$k]
        if ($want -ne $got) { $problems.Add("placeholders $k (en: $want, $code`: $got)") }
    }
    foreach ($k in $map.Keys) {
        if ($neutral.Contains($k)) { continue }
        $base = $k -replace '_(Zero|One|Two|Few|Many|Other)$', ''
        if ($base -ne $k -and ($neutral.Contains("$base`_Other"))) {
            $want = Placeholders $neutral["$base`_Other"]; $got = Placeholders $map[$k]
            if ($want -ne $got) { $problems.Add("placeholders $k (en: $want, $code`: $got)") }
            continue
        }
        $problems.Add("unknown key $k")
    }

    if ($problems.Count -eq 0) { Write-Host "$code  OK ($($map.Count) keys)" }
    else { $failed++; Write-Host "$code  $($problems.Count) problem(s):"; $problems | Select-Object -First 15 | ForEach-Object { Write-Host "   $_" } }
}
if ($failed -gt 0) { exit 1 }
