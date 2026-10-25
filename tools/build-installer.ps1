# Builds publish\installer\Remindr-Installer.exe and its .sha256 file (Remindr ships as the installer only).
# Layout of the installer: [Remindr.Setup.exe stub][payload.zip][16-byte footer = int64 stub length + "RMDRPAY1"].
# Usage: powershell -File tools\build-installer.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root    = Split-Path -Parent $PSScriptRoot
$appProj = Join-Path $root 'src\Remindr\Remindr.csproj'
$stubProj= Join-Path $root 'installer\Remindr.Setup\Remindr.Setup.csproj'
$out     = Join-Path $root 'publish'
$appOut  = Join-Path $out 'app'
$stubOut = Join-Path $out 'stub'
$instOut = Join-Path $out 'installer'

$version = ([xml](Get-Content $appProj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in the app csproj.' }
Write-Host "Remindr $version"

foreach ($d in $appOut, $stubOut, $instOut) { if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory $d | Out-Null }

# 1. App: self-contained single file, win-x64, compressed.
dotnet publish $appProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o $appOut
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
$exe = Join-Path $appOut 'Remindr.exe'
if (-not (Test-Path $exe)) { throw 'Remindr.exe missing from the publish output.' }

# 2. Payload files: exe + licence + notices (LICENSE only once it exists).
$stage = Join-Path $out 'payload'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
Copy-Item $exe $stage
foreach ($f in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
    $p = Join-Path $root $f
    if (Test-Path $p) { Copy-Item $p $stage } else { Write-Warning "$f not found, not included." }
}

# 3. Payload zip. The exe is already compressed, so it uses Fastest.
$payloadZip = Join-Path $out 'payload.zip'
if (Test-Path $payloadZip) { Remove-Item $payloadZip }
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $payloadZip, [System.IO.Compression.CompressionLevel]::Fastest, $false)

# 4. Stub (same version as the app).
dotnet build $stubProj -c Release -p:Version=$version -p:FileVersion="$version.0" -p:InformationalVersion=$version -o $stubOut
if ($LASTEXITCODE -ne 0) { throw 'Setup stub build failed.' }
$stub = Join-Path $stubOut 'Remindr.Setup.exe'
if (-not (Test-Path $stub)) { throw 'Remindr.Setup.exe missing.' }
if (Test-Path (Join-Path $stubOut 'Remindr.Setup.Core.dll')) { throw 'Core must be compiled into the stub, not shipped as a side dll.' }

# 5. Append payload + footer.
$installer = Join-Path $instOut 'Remindr-Installer.exe'
Copy-Item $stub $installer
$stubLen = (Get-Item $stub).Length
$fs = [System.IO.File]::Open($installer, 'Append', 'Write')
try {
    $zipBytes = [System.IO.File]::ReadAllBytes($payloadZip)
    $fs.Write($zipBytes, 0, $zipBytes.Length)
    $footer = New-Object byte[] 16
    [BitConverter]::GetBytes([int64]$stubLen).CopyTo($footer, 0)
    [System.Text.Encoding]::ASCII.GetBytes('RMDRPAY1').CopyTo($footer, 8)
    $fs.Write($footer, 0, 16)
} finally { $fs.Dispose() }

# 6. Checksum for the in-app updater ("<hash>  Remindr-Installer.exe", sha256sum format).
$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$installer.sha256", "$hash  Remindr-Installer.exe`n")

Write-Host ''
Get-ChildItem $instOut | ForEach-Object { '{0,-40} {1,10:N1} MB' -f $_.Name, ($_.Length / 1MB) }
Write-Host ('stub {0:N0} bytes; payload {1:N1} MB' -f $stubLen, ((Get-Item $payloadZip).Length / 1MB))
