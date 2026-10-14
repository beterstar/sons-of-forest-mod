#Requires -Version 5.1
<#
    Build ThaiLanguage mod -> ..\packages\thai-language-<version>.zip
    ใช้ตัวคอมไพล์ Roslyn ใน tools\roslyn + .NET runtime ที่ติดตั้งในเครื่อง
    อ้างอิง dll ของเกมจากโฟลเดอร์ _Redloader (ต้องเปิดเกมพร้อม RedLoader อย่างน้อย 1 ครั้งก่อน)
#>
param(
    [string]$GamePath = 'E:\SteamLibrary\steamapps\common\Sons Of The Forest',
    [string]$Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Rl = Join-Path $GamePath '_Redloader'
$Out = Join-Path $Here 'build'
$Stage = Join-Path $Out 'stage'
$Dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$Csc = Join-Path $Here 'tools\roslyn\csc.dll'

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $Stage 'Mods\ThaiLanguage') | Out-Null

# ---- compile
$refs = @()
$refs += Get-ChildItem "$Rl\dotnet\*.dll" | Where-Object {
    $_.Name -match '^(System\.|mscorlib|netstandard|Microsoft\.CSharp|Microsoft\.Win32\.)' -and $_.Name -ne 'System.IO.Compression.Native.dll'
} | ForEach-Object { $_.FullName }
$refs += 'RedLoader.dll', 'SonsSdk.dll', 'Il2CppInterop.Runtime.dll', 'Il2CppInterop.Common.dll', '0Harmony.dll' |
    ForEach-Object { Join-Path "$Rl\net6" $_ }
$refs += 'Il2Cppmscorlib.dll', 'Il2CppSystem.dll', 'Il2CppSystem.Core.dll', 'Unity.Localization.dll', 'Unity.TextMeshPro.dll',
    'Unity.ResourceManager.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.TextCoreFontEngineModule.dll',
    'UnityEngine.TextRenderingModule.dll', 'UnityEngine.UI.dll', 'UnityEngine.UIModule.dll', 'UnityEngine.ScreenCaptureModule.dll' |
    ForEach-Object { Join-Path "$Rl\Game" $_ }
foreach ($r in $refs) { if (-not (Test-Path -LiteralPath $r)) { throw "ไม่พบไฟล์อ้างอิง: $r (เปิดเกมพร้อม RedLoader 1 ครั้งก่อน)" } }

$dll = Join-Path $Stage 'Mods\ThaiLanguage.dll'
$rsp = @('/nologo', '/nostdlib+', '/target:library', '/optimize+', '/deterministic+', '/langversion:latest',
    '/nullable:disable', '/warn:4', "/out:`"$dll`"") + ($refs | ForEach-Object { "/r:`"$_`"" }) +
    (Get-ChildItem "$Here\src\*.cs" | ForEach-Object { "`"$($_.FullName)`"" })
$rspFile = Join-Path $Out 'build.rsp'
$rsp | Set-Content -Encoding UTF8 $rspFile
& $Dotnet $Csc "@$rspFile"
if ($LASTEXITCODE -ne 0) { throw "คอมไพล์ไม่ผ่าน" }

# ---- data files
$utf8 = New-Object System.Text.UTF8Encoding($false)
$modDir = Join-Path $Stage 'Mods\ThaiLanguage'
$manifest = [ordered]@{
    '$schema'   = 'https://raw.githubusercontent.com/ToniMacaroni/RedLoader/main/MetadataSchema.json'
    id          = 'ThaiLanguage'
    name        = 'Thai Language'
    author      = 'Claude (local build)'
    version     = $Version
    description = 'แปลเกม Sons Of The Forest เป็นภาษาไทย + ฟอนต์ไทย จัดวรรณยุกต์ และตัดคำ'
    gameVersion = '1.0.0'
    type        = 'Mod'
    url         = ''
}
[IO.File]::WriteAllText((Join-Path $modDir 'manifest.json'), ($manifest | ConvertTo-Json), $utf8)

# th.json: รวมไฟล์ใน translations\ และตรวจตัวแปร/แท็กเทียบกับข้อความอังกฤษของเกม
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py) {
    $env:PYTHONIOENCODING = 'utf-8'
    & $py.Source -I (Join-Path $Here 'tools\merge_translations.py') | Where-Object { $_ -notmatch '^WARN' } | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'คำแปลมีข้อผิดพลาด (ดูบรรทัด ERROR ด้านบน)' }
} else {
    Write-Warning 'ไม่พบ python — ใช้ th.json เดิมโดยไม่ตรวจ'
}
$th = Join-Path $Here 'th.json'
$null = [IO.File]::ReadAllText($th, $utf8) | ConvertFrom-Json
Copy-Item $th (Join-Path $modDir 'th.json')
Copy-Item (Join-Path $Here 'config.json') (Join-Path $modDir 'config.json')
Copy-Item (Join-Path $Here 'README-TH.txt') (Join-Path $modDir 'README-TH.txt')

# ---- zip (fixed timestamps so the same input gives the same SHA256)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$pkgDir = Join-Path (Split-Path $Here -Parent) 'packages'
New-Item -ItemType Directory -Force $pkgDir | Out-Null
$zipPath = Join-Path $pkgDir "thai-language-$Version.zip"
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
$size = (Get-Item $zipPath).Length
Write-Host "แพ็กเกจ: $zipPath ($size bytes) SHA256 $hash"

# ---- ลงทะเบียนใน catalog.json ของ ModPicker (ค่า SHA256 ใหม่ทุกครั้งที่ build)
if ($py) {
    & $py.Source -I (Join-Path $Here 'tools\update_catalog.py') $zipPath $Version | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'อัปเดต catalog.json ไม่สำเร็จ' }
} else {
    Write-Warning 'ไม่พบ python — อัปเดต sha256/size ของรายการ thai ใน catalog.json เองด้วยค่าด้านบน'
}
