#Requires -Version 5.1
<#
    ติดตั้ง/อัปเดต mod ที่ build ในเครื่อง (รายการที่มี "local": true ใน catalog.json) ลงเกมโดยไม่ต้องเปิด ModPicker
    ทำแบบเดียวกับปุ่ม "ติดตั้ง / อัปเดต" ของ ModPicker: ตรวจ SHA256, ตรวจไฟล์ใน zip, ลบไฟล์เวอร์ชันเก่า, แตกไฟล์,
    แล้วบันทึกรายการไฟล์ลง state\installed.json เพื่อให้ ModPicker ถอนออกได้ครบ
#>
param(
    [Parameter(Mandatory)][string]$Id,
    [string]$GamePath
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$StatePath = Join-Path $Root 'state\installed.json'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$catalog = Get-Content -LiteralPath (Join-Path $Root 'catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$item = $catalog.items | Where-Object { $_.id -eq $Id }
if (-not $item) { throw "ไม่พบ mod '$Id' ใน catalog.json" }
if (-not $item.local) { throw "mod '$Id' ไม่ใช่ mod ที่ build ในเครื่อง — ใช้ ModPicker ติดตั้ง" }

$obj = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $GamePath) { $GamePath = $obj.gamePath }
if (-not (Test-Path -LiteralPath (Join-Path $GamePath $catalog.game.exe))) { throw "ไม่พบเกมที่ $GamePath" }
if (Get-Process -Name $catalog.game.process -ErrorAction SilentlyContinue) { throw 'กรุณาปิดเกมก่อน' }

$zipPath = Join-Path $Root "packages\$($item.file)"
if ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { throw "SHA256 ของ $($item.file) ไม่ตรง catalog" }

# state เดิม (รูปแบบเดียวกับ Read-State ของ ModPicker)
$state = @{ gamePath = $obj.gamePath; items = @{}; settings = @{} }
foreach ($p in $obj.items.PSObject.Properties) {
    $state.items[$p.Name] = @{ name = $p.Value.name; sha256 = $p.Value.sha256; installedAt = $p.Value.installedAt
        backup = $p.Value.backup; files = @($p.Value.files | Where-Object { $_ }) }
}
if ($obj.settings) { foreach ($p in $obj.settings.PSObject.Properties) { $state.settings[$p.Name] = $p.Value } }

# ลบไฟล์เวอร์ชันเก่าของ mod นี้
if ($state.items.ContainsKey($Id)) {
    foreach ($f in $state.items[$Id].files) {
        $full = Join-Path $GamePath $f
        if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Force }
    }
}

$rootFull = [IO.Path]::GetFullPath($GamePath).TrimEnd('\') + '\'
$files = New-Object System.Collections.Generic.List[string]
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($e in $zip.Entries) {
        $name = $e.FullName.Replace('\', '/')
        if ($name.EndsWith('/')) { continue }
        $ok = $false; foreach ($r in $item.roots) { if ($name.StartsWith($r)) { $ok = $true } }
        if (-not $ok) { throw "ไฟล์นอกโฟลเดอร์ที่อนุญาต: $name" }
        if ($catalog.allowedExtensions -notcontains [IO.Path]::GetExtension($name).ToLowerInvariant()) { throw "ชนิดไฟล์ไม่อนุญาต: $name" }
        $rel = $name.Replace('/', '\')
        $dest = [IO.Path]::GetFullPath((Join-Path $GamePath $rel))
        if (-not $dest.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) { throw "path หลุดนอกโฟลเดอร์เกม: $name" }
        New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true)
        $files.Add($rel)
    }
} finally { $zip.Dispose() }

$state.items[$Id] = @{ name = $item.name; sha256 = $item.sha256; installedAt = (Get-Date).ToString('s'); backup = $null; files = $files.ToArray() }
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $StatePath -Encoding UTF8
Write-Host "ติดตั้ง $($item.name) แล้ว ($($files.Count) ไฟล์)"
