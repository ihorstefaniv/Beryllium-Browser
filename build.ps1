# Builds Beryll Browser with the C# compiler built into Windows (.NET Framework 4.x) - no SDK / Visual Studio needed.
#   powershell -ExecutionPolicy Bypass -File build.ps1
# Output: dist\Beryll\  (Beryll.exe + WebView2 SDK files)  and  dist\Beryll-portable.zip
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc = "$fw\csc.exe"

# ---------------------------------------------------------------- icon: periodic-table tile "Be" (4, 9.012)
$ico = Join-Path $root 'res\beryll.ico'
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
    $pngs = @()
    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
        $g.Clear([System.Drawing.Color]::Transparent)
        $m = [single][Math]::Max(0.5, $s * 0.04); $w = [single]($s - 2 * $m); $r = [single]($s * 0.2); $d = 2 * $r
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($m, $m, $d, $d, 180, 90); $path.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
        $path.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90); $path.AddArc($m, $m + $w - $d, $d, $d, 90, 90); $path.CloseFigure()
        $rect = New-Object System.Drawing.RectangleF $m, $m, $w, $w
        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 58, 206, 172)), ([System.Drawing.Color]::FromArgb(255, 16, 110, 104)), 45.0
        $g.FillPath($grad, $path)
        # soft highlight like a gemstone facet
        $hl = New-Object System.Drawing.Drawing2D.GraphicsPath
        $hl.AddPolygon([System.Drawing.PointF[]]@((New-Object System.Drawing.PointF $m, $m), (New-Object System.Drawing.PointF ($m + $w * 0.62), $m), (New-Object System.Drawing.PointF $m, ($m + $w * 0.62))))
        $reg = New-Object System.Drawing.Region $path; $reg.Intersect($hl)
        $g.FillRegion((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(36, 255, 255, 255))), $reg)
        $white = [System.Drawing.Brushes]::White
        $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
        if ($s -ge 40) {
            $fBig = New-Object System.Drawing.Font 'Segoe UI Semibold', ([single]($s * 0.40)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
            $fSm = New-Object System.Drawing.Font 'Segoe UI', ([single]($s * 0.13)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
            $g.DrawString('Be', $fBig, $white, (New-Object System.Drawing.RectangleF 0, ([single]($s * 0.04)), $s, $s), $sf)
            $g.DrawString('4', $fSm, $white, [single]($s * 0.12), [single]($s * 0.09))
            $sf2 = New-Object System.Drawing.StringFormat; $sf2.Alignment = 'Center'
            $g.DrawString('9.012', $fSm, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(225, 255, 255, 255))), (New-Object System.Drawing.RectangleF 0, ([single]($s * 0.72)), $s, ([single]($s * 0.2))), $sf2)
        } else {
            $fBig = New-Object System.Drawing.Font 'Segoe UI Semibold', ([single]($s * 0.56)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
            $g.DrawString('Be', $fBig, $white, (New-Object System.Drawing.RectangleF 0, ([single]($s * 0.03)), $s, $s), $sf)
        }
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngs += , $ms.ToArray(); $bmp.Dispose()
    }
    $out = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $out
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $b = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset); $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Flush(); [IO.File]::WriteAllBytes($ico, $out.ToArray())
    Write-Host "Generated $ico"
}

# ---------------------------------------------------------------- update signing key (RSA-3072)
# PRIVATE key stays in keys\ (git-ignored; back it up offline!). Only the public key is compiled in (src\UpdateKey.cs).
$keyDir = Join-Path $root 'keys'
$privFile = Join-Path $keyDir 'update_private.xml'
function New-Rsa { $p = New-Object Security.Cryptography.CspParameters 24; $r = New-Object Security.Cryptography.RSACryptoServiceProvider 3072, $p; $r.PersistKeyInCsp = $false; $r }
if (-not (Test-Path $privFile)) {
    New-Item -ItemType Directory -Force $keyDir | Out-Null
    $tmpRsa = New-Rsa; [IO.File]::WriteAllText($privFile, $tmpRsa.ToXmlString($true)); $tmpRsa.Dispose()
    Write-Host "Generated update signing key: $privFile  (BACK IT UP, never publish it)"
}
$rsa = New-Rsa; $rsa.FromXmlString([IO.File]::ReadAllText($privFile))
$pub = $rsa.ToXmlString($false)
[IO.File]::WriteAllText((Join-Path $root 'src\UpdateKey.cs'),
    "// Generated by build.ps1: public key for verifying signed updates. Do not edit.`r`nnamespace Beryll { static class UpdateKey { public const string PublicKeyXml = `"$pub`"; } }`r`n",
    (New-Object Text.UTF8Encoding $true))

# ---------------------------------------------------------------- compile
$dist = Join-Path $root 'dist\Beryll'
New-Item -ItemType Directory -Force $dist | Out-Null
$target = Join-Path $dist 'Beryll.exe'
if (Test-Path $target) { try { Remove-Item $target -ErrorAction Stop } catch { Remove-Item "$target.old" -ErrorAction SilentlyContinue; [IO.File]::Move($target, "$target.old") } }
$lib = Join-Path $root 'lib'
$srcs = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /nowarn:1607 `
    "/out:$target" "/win32icon:$ico" "/win32manifest:$root\res\app.manifest" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    "/r:$lib\Microsoft.Web.WebView2.Core.dll" "/r:$lib\Microsoft.Web.WebView2.WinForms.dll" $srcs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

Copy-Item "$lib\Microsoft.Web.WebView2.Core.dll", "$lib\Microsoft.Web.WebView2.WinForms.dll", "$lib\WebView2-LICENSE.txt" $dist -Force
Copy-Item "$lib\runtimes" $dist -Recurse -Force

$zip = Join-Path $root 'dist\Beryll-portable.zip'
Remove-Item $zip -ErrorAction SilentlyContinue
$stage = Join-Path (Join-Path $env:TEMP 'beryll-stage') 'Beryll'
Remove-Item (Split-Path $stage) -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Get-ChildItem $dist -Exclude *.old, Data, portable | Copy-Item -Destination $stage -Recurse -Force
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
Remove-Item (Split-Path $stage) -Recurse -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------- signed release files for the updater (download\)
$hash = [Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($zip))
[IO.File]::WriteAllBytes("$zip.sig", $rsa.SignHash($hash, [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256')))
$rsa.Dispose()
$ver = (Get-Item $target).VersionInfo.FileVersion
$dl = Join-Path $root 'download'
New-Item -ItemType Directory -Force $dl | Out-Null
Copy-Item $zip, "$zip.sig" $dl -Force
$notes = Join-Path $root 'res\release-notes.txt'
$body = if (Test-Path $notes) { [IO.File]::ReadAllText($notes) } else { '' }
[IO.File]::WriteAllText((Join-Path $dl 'latest.txt'), "$ver`n$body", (New-Object Text.UTF8Encoding $false))
Get-Item $target, $zip, "$zip.sig" | Select-Object Name, Length
