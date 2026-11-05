# Builds Beryll Legacy (Windows 7 / 8 / 8.1, 32-bit engine: runs on 32- and 64-bit Windows).
#   powershell -ExecutionPolicy Bypass -File legacy\build-legacy.ps1
# Output: dist\Beryll-Legacy\  and  dist\Beryll-Legacy-x86.zip
#
# Engine and uBlock Origin versions are pinned below together with their SHA-256: the build stops if a
# downloaded file differs (a swapped / damaged release never gets packed). To update: change version + hash.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$EngineTag  = 'v150-r2.02'
$EngineZip  = 'supermium_150_32_nonsetup.zip'
$EngineSha  = '00677d17795044f490c4bc83afa49ff5e66626793432c544dd91efff91af2bc5'
$UboVersion = '1.75.4'
$UboSha     = '26ad8697a7b19e1ef5f4c0750165dd585cbc7707c72dbc3f3a0bdbcd712f3576'

$here  = $PSScriptRoot
$root  = Split-Path $here
$cache = Join-Path $here '.cache'
$out   = Join-Path $root 'dist\Beryll-Legacy'
New-Item -ItemType Directory -Force $cache | Out-Null

function Get-Pinned($url, $file, $sha) {
    $path = Join-Path $cache $file
    if (-not (Test-Path $path)) {
        Write-Host "Downloading $url"
        $tmp = "$path.part"
        Invoke-WebRequest $url -OutFile $tmp -UseBasicParsing
        Move-Item $tmp $path -Force
    }
    $h = (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()
    if ($sha -and $h -ne $sha) { Remove-Item $path; throw "SHA-256 mismatch for $file`n  expected $sha`n  got      $h" }
    if (-not $sha) { Write-Host "  $file sha256 = $h  (pin it in build-legacy.ps1)" }
    $path
}

# ------------------------------------------------------------------ clean output
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out, "$out\engine", "$out\ext" | Out-Null

# ------------------------------------------------------------------ engine (Supermium, BSD licence like Chromium)
$ez = Get-Pinned "https://github.com/win32ss/supermium/releases/download/$EngineTag/$EngineZip" $EngineZip $EngineSha
$ex = Join-Path $cache "engine-$EngineTag-x86"
if (-not (Test-Path "$ex\Supermium\chrome.exe")) {
    if (Test-Path $ex) { Remove-Item $ex -Recurse -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($ez, $ex)
}
Copy-Item "$ex\Supermium\*" "$out\engine" -Recurse -Force
# not needed in our package: Supermium's own launch scripts and uninstaller
Get-ChildItem "$out\engine" -Filter *.cmd | Remove-Item -Force
Remove-Item "$out\engine\uninstall.exe" -Force -ErrorAction SilentlyContinue
# interface languages: Ukrainian + English only (saves ~40 MB on disk and download)
$loc = Get-ChildItem "$out\engine" -Directory | Where-Object { Test-Path "$($_.FullName)\Locales" } | ForEach-Object { "$($_.FullName)\Locales" }
foreach ($l in $loc) {
    Get-ChildItem $l -Filter *.pak | Where-Object { $_.BaseName -notmatch '^(uk|en-US|en-GB)(_|$)' } | Remove-Item -Force
}

# ------------------------------------------------------------------ uBlock Origin (GPLv3; only a "key" is added to its manifest)
$uz = Get-Pinned "https://github.com/gorhill/uBlock/releases/download/$UboVersion/uBlock0_$UboVersion.chromium.zip" "uBlock0_$UboVersion.chromium.zip" $UboSha
$ux = Join-Path $cache "ublock-$UboVersion"
if (-not (Test-Path $ux)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($uz, $ux)
}
$uman = Get-ChildItem $ux -Recurse -Filter manifest.json | Select-Object -First 1
Copy-Item $uman.DirectoryName "$out\ext\ublock" -Recurse -Force
# Fixed public key = fixed extension ID (pkoblbgcjbbjbhkhcjfopladehdejjef). Without it the ID of an unpacked extension
# depends on the folder path, and moving portable Beryll elsewhere would lose uBlock's settings and filter lists.
$uKey = 'MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAtcs69v5Io/JfYB4ihtGeku7linXbfxbjU0kNWLSfI8TqfWFXxE5un/m6FiOkY3pT0OYP6vHRe3dRgqH9BOLHR2ZuTh0y7U9XZSFI04J4rtFhJFEdATDRq7LPkhNYLRGCL7THPfWRHnDPNtdydcD5qjR4YiDNo223rZoDpR1edxsfKGcYEzI6nvD/wEhvsyFgUoAO1mKb6NcUbnmNUxfx9ezAacbU19iTbhPz4DKhdCK1glDynSFFJRSYDXFtJPOqyPaDdL8eqNJ7c3XCovM9FLvX4Z/P7SGZ6szJCOSq6IWV8PlX7ouUa2pacuhtKiqvhC9IOY6w76Y9PUXTJlwnnwIDAQAB'
$um = "$out\ext\ublock\manifest.json"
$utext = [IO.File]::ReadAllText($um)
if ($utext -notmatch '"key"\s*:') {
    $i = $utext.IndexOf('{')
    [IO.File]::WriteAllText($um, $utext.Substring(0, $i + 1) + "`n  `"key`": `"$uKey`"," + $utext.Substring($i + 1), (New-Object Text.UTF8Encoding $false))
}

# ------------------------------------------------------------------ Beryll extension (+ icons from the app icon)
Copy-Item "$here\ext\beryll" "$out\ext\beryll" -Recurse -Force
$ico = Join-Path $root 'res\beryll.ico'
# the .ico frames are stored as PNG: copy the 48 and 128 px frames out of the icon directory as they are
$ib = [IO.File]::ReadAllBytes($ico)
$count = [BitConverter]::ToUInt16($ib, 4)
for ($i = 0; $i -lt $count; $i++) {
    $e = 6 + 16 * $i
    $w = if ($ib[$e] -eq 0) { 256 } else { $ib[$e] }
    if ($w -ne 48 -and $w -ne 128) { continue }
    $len = [BitConverter]::ToUInt32($ib, $e + 8); $off = [BitConverter]::ToUInt32($ib, $e + 12)
    $png = New-Object byte[] $len; [Array]::Copy($ib, $off, $png, 0, $len)
    [IO.File]::WriteAllBytes("$out\ext\beryll\icon$w.png", $png)
}

# ------------------------------------------------------------------ shell: compiled against .NET 2.0 (runs on a clean Windows 7)
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
# .NET 2.0 reference assemblies (official Microsoft NuGet package): no need to enable .NET 3.5 on the build PC
$rp = Get-Pinned 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net20/1.0.3/microsoft.netframework.referenceassemblies.net20.1.0.3.nupkg' `
    'refasm-net20-1.0.3.nupkg' '7f74997fec338dde70f5a8c9fd09aa6fe21286731e7b133cc133d644e4e3c5e8'
$rx = Join-Path $cache 'refasm-net20'
if (-not (Test-Path "$rx\build\.NETFramework\v2.0\mscorlib.dll")) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($rp, $rx)
}
$v2 = "$rx\build\.NETFramework\v2.0"
foreach ($d in 'mscorlib', 'System', 'System.Drawing', 'System.Windows.Forms') {
    $sig = Get-AuthenticodeSignature "$v2\$d.dll"
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw "$d.dll: not a valid Microsoft signature ($($sig.Status))" }
}
& $csc /nologo /noconfig /nostdlib+ /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$out\Beryll.exe" "/win32icon:$ico" "/win32manifest:$here\shell\legacy.manifest" `
    "/r:$v2\mscorlib.dll" "/r:$v2\System.dll" "/r:$v2\System.Drawing.dll" "/r:$v2\System.Windows.Forms.dll" `
    "$here\shell\BeryllLegacy.cs"
if ($LASTEXITCODE -ne 0) { throw 'Shell build failed' }
# run on whichever runtime the PC has: CLR 4 (Windows 8+, updated 7) or CLR 2 (.NET 3.5, built into Windows 7)
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0"/>
    <supportedRuntime version="v2.0.50727"/>
  </startup>
</configuration>
'@ | Set-Content "$out\Beryll.exe.config" -Encoding UTF8

Copy-Item "$here\README-Legacy.txt" "$out\README.txt"   # ASCII name: the Windows 7 zip folder garbles non-ASCII file names -Force
Copy-Item "$root\LICENSE" "$out\LICENSE.txt" -Force

# ------------------------------------------------------------------ zip
$zip = Join-Path $root 'dist\Beryll-Legacy-x86.zip'
Remove-Item $zip -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$mb = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
$disk = [Math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 0)
Write-Host "OK: $zip  ($mb MB zip, $disk MB unpacked)"
