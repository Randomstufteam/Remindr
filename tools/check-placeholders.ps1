# Lists any {{PLACEHOLDER}} tokens left in release files. Exits 1 if any remain, 0 if none.
$root = Split-Path -Parent $PSScriptRoot
$files = @(
    'src\Remindr\RepoInfo.cs',
    'LICENSE',
    'README.md',
    'CHANGELOG.md',
    'THIRD-PARTY-NOTICES.md',
    '.github\ISSUE_TEMPLATE\config.yml',
    '.github\ISSUE_TEMPLATE\bug_report.yml',
    '.github\ISSUE_TEMPLATE\feature_request.yml'
)
$found = 0
foreach ($f in $files) {
    $path = Join-Path $root $f
    if (-not (Test-Path $path)) { continue }   # files from later slices may not exist yet
    $hits = Select-String -Path $path -Pattern '\{\{[A-Z_]+\}\}'
    foreach ($h in $hits) {
        Write-Host ("{0}:{1}: {2}" -f $f, $h.LineNumber, $h.Line.Trim())
        $found++
    }
}
if ($found -gt 0) { Write-Host "$found placeholder(s) left."; exit 1 }
Write-Host "No placeholders left."
exit 0
