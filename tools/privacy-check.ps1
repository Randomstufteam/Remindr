# Fails if anything that could hold personal data is about to be published.
# Run before every commit/release:  powershell -File tools\privacy-check.ps1
# Optional: list extra private words (real names, profile names) one per line in .privacy-terms
# at the repo root. That file is git-ignored so the words themselves are never published.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $problems = New-Object System.Collections.Generic.List[string]

    # Files that would be published: tracked + untracked-not-ignored when git exists, else everything outside build output.
    $files = @()
    if ((Get-Command git -ErrorAction SilentlyContinue) -and (Test-Path .git)) {
        $files = git ls-files --cached --others --exclude-standard
    } else {
        $files = Get-ChildItem -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj|publish|\.git|\.vs)\\' } |
            ForEach-Object { $_.FullName.Substring($root.Length + 1) }
    }

    $badExt = '.db', '.db-wal', '.db-shm', '.sqlite', '.sqlite3', '.log'
    foreach ($f in $files) {
        if ($badExt -contains [IO.Path]::GetExtension($f).ToLowerInvariant()) { $problems.Add("data/log file: $f") }
        if ($f -match '(^|[\\/])(avatars|l10n-check|remindr-backup[^\\/]*)([\\/]|$)') { $problems.Add("private folder: $f") }
        if ($f -match '\.(png|jpe?g|gif)$' -and $f -notmatch '^(src[\\/]Remindr[\\/]Assets|installer[\\/]Remindr\.Setup[\\/]Assets|docs[\\/](screenshots|hero|tour))') {
            $problems.Add("image outside approved folders (check it shows no real data): $f")
        }
    }

    # Text that points at this PC or a real person.
    $terms = @('C:\Users\' + $env:USERNAME, '\Users\' + $env:USERNAME + '\', $env:COMPUTERNAME)
    if (Test-Path .privacy-terms) { $terms += Get-Content .privacy-terms | Where-Object { $_.Trim() } }
    $textExt = '.cs', '.xaml', '.md', '.yml', '.yaml', '.ps1', '.csproj', '.resx', '.csv', '.json', '.txt', '.sln', '.props', '.svg', '.iss'
    foreach ($f in $files) {
        if ($textExt -notcontains [IO.Path]::GetExtension($f).ToLowerInvariant() -or -not (Test-Path $f)) { continue }
        $text = [IO.File]::ReadAllText((Join-Path $root $f))
        foreach ($t in $terms) {
            if ($t -and $text.IndexOf($t, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $problems.Add("'$t' found in $f") }
        }
    }

    if ($problems.Count) {
        Write-Host "Privacy check FAILED:" -ForegroundColor Red
        $problems | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    Write-Host "Privacy check passed ($($files.Count) files)." -ForegroundColor Green
} finally { Pop-Location }
