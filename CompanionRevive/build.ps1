#Requires -Version 5.1
<#
    Build CompanionRevive mod -> ..\packages\companion-revive-<version>.zip  (และลงทะเบียนใน catalog.json)
    -Dev : คอมไพล์แล้วคัดลอกลงโฟลเดอร์เกมโดยตรง (ใช้ตอนพัฒนา/ทดสอบ)
#>
param(
    [string]$GamePath = 'E:\SteamLibrary\steamapps\common\Sons Of The Forest',
    [string]$Version = '1.0.0',
    [switch]$Dev
)
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Rl = Join-Path $GamePath '_Redloader'
$Out = Join-Path $Here 'build'
$Stage = Join-Path $Out 'stage'
$Dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$Csc = Join-Path (Split-Path $Here -Parent) 'ThaiLanguage\tools\roslyn\csc.dll'

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $Stage 'Mods\CompanionRevive') | Out-Null

$refs = @()
$refs += Get-ChildItem "$Rl\dotnet\*.dll" | Where-Object {
    $_.Name -match '^(System\.|mscorlib|netstandard|Microsoft\.CSharp|Microsoft\.Win32\.)' -and $_.Name -ne 'System.IO.Compression.Native.dll'
} | ForEach-Object { $_.FullName }
$refs += 'RedLoader.dll', 'SonsSdk.dll', 'Il2CppInterop.Runtime.dll', 'Il2CppInterop.Common.dll', '0Harmony.dll' |
    ForEach-Object { Join-Path "$Rl\net6" $_ }
$refs += Get-ChildItem "$Rl\Game\*.dll" | ForEach-Object { $_.FullName }

$dll = Join-Path $Stage 'Mods\CompanionRevive.dll'
$rsp = @('/nologo', '/nostdlib+', '/target:library', '/optimize+', '/deterministic+', '/langversion:latest',
    '/nullable:disable', '/warn:4', '/nowarn:CS1701,CS1702', "/out:`"$dll`"") + ($refs | ForEach-Object { "/r:`"$_`"" }) +
    (Get-ChildItem "$Here\src\*.cs" | ForEach-Object { "`"$($_.FullName)`"" })
$rspFile = Join-Path $Out 'build.rsp'
$rsp | Set-Content -Encoding UTF8 $rspFile
& $Dotnet $Csc "@$rspFile"
if ($LASTEXITCODE -ne 0) { throw 'คอมไพล์ไม่ผ่าน' }

$utf8 = New-Object System.Text.UTF8Encoding($false)
$modDir = Join-Path $Stage 'Mods\CompanionRevive'
$manifest = [ordered]@{
    '$schema'   = 'https://raw.githubusercontent.com/ToniMacaroni/RedLoader/main/MetadataSchema.json'
    id          = 'CompanionRevive'
    name        = 'Companion Revive'
    author      = 'Claude (local build)'
    version     = $Version
    description = 'ชุบชีวิตเวอร์จิเนีย/เคลวินในเซฟที่ตายไปแล้ว (เฉพาะโฮสต์)'
    gameVersion = '1.0.0'
    type        = 'Mod'
    url         = ''
}
[IO.File]::WriteAllText((Join-Path $modDir 'manifest.json'), ($manifest | ConvertTo-Json), $utf8)
foreach ($f in 'config.json', 'README-TH.txt') { if (Test-Path (Join-Path $Here $f)) { Copy-Item (Join-Path $Here $f) (Join-Path $modDir $f) } }

if ($Dev) {
    if (Get-Process SonsOfTheForest -ErrorAction SilentlyContinue) { throw 'ปิดเกมก่อน' }
    Copy-Item $dll (Join-Path $GamePath 'Mods\CompanionRevive.dll') -Force
    New-Item -ItemType Directory -Force (Join-Path $GamePath 'Mods\CompanionRevive') | Out-Null
    Get-ChildItem $modDir -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $GamePath "Mods\CompanionRevive\$($_.Name)") -Force }
    Write-Host 'คัดลอกลงเกมแล้ว (dev)'
    return
}

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$pkgDir = Join-Path (Split-Path $Here -Parent) 'packages'
$zipPath = Join-Path $pkgDir "companion-revive-$Version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
$files = Get-ChildItem $Stage -Recurse -File | Sort-Object FullName
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($Stage.Length + 1).Replace('\', '/')
        $e = $zip.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal)
        $e.LastWriteTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $s = $e.Open(); try { $b = [IO.File]::ReadAllBytes($f.FullName); $s.Write($b, 0, $b.Length) } finally { $s.Dispose() }
    }
} finally { $zip.Dispose() }
$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "แพ็กเกจ: $zipPath ($((Get-Item $zipPath).Length) bytes) SHA256 $hash"
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py) {
    & $py.Source -I (Join-Path $Here 'tools\update_catalog.py') $zipPath $Version | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'อัปเดต catalog.json ไม่สำเร็จ' }
}
