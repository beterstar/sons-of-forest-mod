#Requires -Version 5.1
<#
    Sons Of The Forest - Mod Picker
    ---------------------------------
    โปรแกรมเลือกติดตั้ง mod แบบติ๊กเลือก (ทำงานบน Windows PowerShell 5.1 ที่มีมากับ Windows)

    มาตรการความปลอดภัย
      1. ดาวน์โหลดเฉพาะจาก GitHub ทางการของ RedLoader และ sotf-mods.com เท่านั้น
      2. ทุกไฟล์ถูกล็อกค่า SHA256 ไว้ใน catalog.json — ถ้าไฟล์ไม่ตรง จะไม่ติดตั้ง
      3. สแกนไฟล์ด้วย Windows Defender ก่อนติดตั้ง (ถ้าเจอภัยคุกคาม จะยกเลิก)
      4. ตรวจทุกไฟล์ใน zip: ห้าม path หลุดออกนอกโฟลเดอร์เกม, ห้ามไฟล์ .exe/.bat/.ps1 ฯลฯ
      5. สำรองไฟล์เดิมก่อนเขียนทับ และบันทึกรายการไฟล์ที่ติดตั้ง เพื่อถอนออกได้ครบ
      6. ไม่ขอสิทธิ์ Administrator และไม่แตะไฟล์เซฟเกม
#>
param([string]$GamePath)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Root        = $PSScriptRoot
$CatalogPath = Join-Path $Root 'catalog.json'
$PackagesDir = Join-Path $Root 'packages'
$StateDir    = Join-Path $Root 'state'
$StatePath   = Join-Path $StateDir 'installed.json'
$BackupDir   = Join-Path $Root 'backup'
$LogDir      = Join-Path $Root 'logs'
$UserAgent   = 'V2N-SOTF-ModPicker/1.0 (personal use)'
$AllowedUrlPrefixes = @(
    'https://github.com/ToniMacaroni/RedLoader/releases/download/',
    'https://sotf-mods.com/mods/'
)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.IO.Compression, System.IO.Compression.FileSystem

# ---------------------------------------------------------------- Win32 (DPI + console)
try {
    Add-Type -Namespace V2N -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("kernel32.dll")] public static extern System.IntPtr GetConsoleWindow();
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
'@
    [void][V2N.Native]::SetProcessDPIAware()
} catch { }

# ---------------------------------------------------------------- helpers
function Get-Opt($obj, [string]$name, $default = $null) {
    if ($null -ne $obj -and $obj.PSObject.Properties[$name]) { return $obj.$name }
    return $default
}

function Write-Log([string]$msg) {
    $line = '[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $msg
    if ($script:LogBox) {
        $script:LogBox.AppendText($line + [Environment]::NewLine)
        [System.Windows.Forms.Application]::DoEvents()
    }
    if ($script:LogFile) { Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 }
}

function New-Dir([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
}

function Get-Sha256([string]$path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# ---------------------------------------------------------------- catalog / state
$Catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$Loader = $Catalog.loader
$ItemsById = @{}
foreach ($it in $Catalog.items) { $ItemsById[$it.id] = $it }

function Read-State {
    $state = @{ gamePath = $null; items = @{}; settings = @{} }
    if (-not (Test-Path -LiteralPath $StatePath)) { return $state }
    $obj = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $state.gamePath = Get-Opt $obj 'gamePath'
    $items = Get-Opt $obj 'items'
    if ($items) {
        foreach ($p in $items.PSObject.Properties) {
            $state.items[$p.Name] = @{
                name        = Get-Opt $p.Value 'name'
                sha256      = Get-Opt $p.Value 'sha256'
                installedAt = Get-Opt $p.Value 'installedAt'
                backup      = Get-Opt $p.Value 'backup'
                files       = @((Get-Opt $p.Value 'files' @()) | Where-Object { $_ })
            }
        }
    }
    $settings = Get-Opt $obj 'settings'
    if ($settings) { foreach ($p in $settings.PSObject.Properties) { $state.settings[$p.Name] = $p.Value } }
    return $state
}

function Save-State {
    New-Dir $StateDir
    $script:State.gamePath = $script:GameRoot
    $script:State | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $StatePath -Encoding UTF8
}

# ---------------------------------------------------------------- game folder
function Test-GameFolder([string]$path) {
    return ($path -and (Test-Path -LiteralPath (Join-Path $path $Catalog.game.exe)))
}

function Find-GameFolder {
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($GamePath) { $candidates.Add($GamePath) }
    if ($script:State.gamePath) { $candidates.Add($script:State.gamePath) }
    try {
        $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($m in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $lib = $m.Groups[1].Value -replace '\\\\', '\'
                $candidates.Add((Join-Path $lib 'steamapps\common\Sons Of The Forest'))
            }
        }
    } catch { }
    $candidates.Add('E:\SteamLibrary\steamapps\common\Sons Of The Forest')
    $candidates.Add('C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest')
    foreach ($c in $candidates) { if (Test-GameFolder $c) { return [IO.Path]::GetFullPath($c) } }
    return $null
}

function Test-GameRunning {
    return [bool](Get-Process -Name $Catalog.game.process -ErrorAction SilentlyContinue)
}

# ---------------------------------------------------------------- package verification
function Get-VerifiedPackage($item) {
    New-Dir $PackagesDir
    $zip = Join-Path $PackagesDir $item.file
    if (Test-Path -LiteralPath $zip) {
        if ((Get-Sha256 $zip) -eq $item.sha256) {
            Write-Log "  ✔ SHA256 ตรงกับที่ล็อกไว้: $($item.file)"
            return $zip
        }
        if (Get-Opt $item 'local' $false) {
            throw "แพ็กเกจ $($item.file) ไม่ตรง SHA256 ที่ล็อกไว้ — รัน ThaiLanguage\build.ps1 ใหม่ (ไม่ลบไฟล์ให้ เพราะ build ในเครื่องนี้)"
        }
        Write-Log "  ✖ ไฟล์ในเครื่องไม่ตรง SHA256 — ลบทิ้งแล้วดาวน์โหลดใหม่: $($item.file)"
        Remove-Item -LiteralPath $zip -Force
    }

    # mod ที่ build ในเครื่องนี้ (เช่น ภาษาไทย) ไม่มีที่ให้ดาวน์โหลด
    if (Get-Opt $item 'local' $false) {
        throw "ไม่พบแพ็กเกจ $($item.file) ในโฟลเดอร์ packages — รัน ThaiLanguage\build.ps1 เพื่อสร้างใหม่"
    }

    $allowed = $false
    foreach ($prefix in $AllowedUrlPrefixes) { if ($item.url.StartsWith($prefix)) { $allowed = $true } }
    if (-not $allowed) { throw "URL ไม่อยู่ในรายการแหล่งที่เชื่อถือได้: $($item.url)" }

    Write-Log "  ↓ กำลังดาวน์โหลด $($item.file) ($([Math]::Round($item.size / 1MB, 2)) MB) จาก $($item.url)"
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $tmp = "$zip.download"
    try {
        Invoke-WebRequest -Uri $item.url -OutFile $tmp -UseBasicParsing -UserAgent $UserAgent -MaximumRedirection 5
        $size = (Get-Item -LiteralPath $tmp).Length
        $hash = Get-Sha256 $tmp
        if ($size -ne $item.size -or $hash -ne $item.sha256) {
            throw "ไฟล์ที่ดาวน์โหลดไม่ตรงกับที่ตรวจสอบไว้ (SHA256: $hash) — ยกเลิกเพื่อความปลอดภัย"
        }
        Move-Item -LiteralPath $tmp -Destination $zip -Force
    } finally {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    }
    Write-Log "  ✔ ดาวน์โหลดเสร็จ และ SHA256 ตรงกับที่ล็อกไว้"
    return $zip
}

function Invoke-DefenderScan([string]$path) {
    $mp = Join-Path $env:ProgramFiles 'Windows Defender\MpCmdRun.exe'
    if (-not (Test-Path -LiteralPath $mp)) {
        Write-Log '  ! ไม่พบ Windows Defender — ข้ามการสแกน (ไฟล์ผ่านการตรวจ SHA256 แล้ว)'
        return
    }
    $p = Start-Process -FilePath $mp -ArgumentList @('-Scan', '-ScanType', '3', '-File', "`"$path`"", '-DisableRemediation') `
        -Wait -PassThru -WindowStyle Hidden
    switch ($p.ExitCode) {
        0 { Write-Log '  ✔ Windows Defender: ไม่พบภัยคุกคาม' }
        2 { throw "Windows Defender พบภัยคุกคามในไฟล์ $path — ยกเลิกการติดตั้ง" }
        default { Write-Log "  ! Windows Defender สแกนไม่สำเร็จ (code $($p.ExitCode)) — ใช้ผลตรวจ SHA256 แทน" }
    }
}

# ตรวจทุกไฟล์ใน zip ก่อนแตก — คืนค่ารายชื่อไฟล์ (ไม่รวมโฟลเดอร์)
function Test-ZipSafety([string]$zipPath, $item) {
    $names = New-Object System.Collections.Generic.List[string]
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($e in $zip.Entries) {
            $name = $e.FullName.Replace('\', '/')
            if ($name.EndsWith('/')) { continue }
            if ($name.StartsWith('/') -or $name -match '^[A-Za-z]:' -or $name -match '(^|/)\.\.(/|$)') {
                throw "พบ path อันตรายใน zip: $name"
            }
            $rootOk = $false
            foreach ($r in $item.roots) {
                if (($r.EndsWith('/') -and $name.StartsWith($r)) -or $name -eq $r) { $rootOk = $true }
            }
            if (-not $rootOk) { throw "พบไฟล์นอกโฟลเดอร์ที่อนุญาต: $name" }
            $ext = [IO.Path]::GetExtension($name).ToLowerInvariant()
            if ($Catalog.allowedExtensions -notcontains $ext) { throw "พบชนิดไฟล์ที่ไม่อนุญาต: $name" }
            $names.Add($name)
        }
    } finally { $zip.Dispose() }

    $expected = Get-Opt $item 'files'
    if ($expected) {
        $diff = Compare-Object -ReferenceObject @($expected) -DifferenceObject @($names)
        if ($diff) { throw "รายการไฟล์ใน zip ไม่ตรงกับ catalog: $($item.file)" }
    }
    Write-Log "  ✔ ตรวจไฟล์ใน zip ผ่าน ($($names.Count) ไฟล์ ไม่มีไฟล์อันตราย)"
    return $names
}

function Expand-Package([string]$zipPath, [string]$backupRoot) {
    $rootFull = [IO.Path]::GetFullPath($script:GameRoot).TrimEnd('\') + '\'
    $owned = @{}
    foreach ($rec in $script:State.items.Values) { foreach ($f in $rec.files) { $owned[$f.ToLowerInvariant()] = $true } }

    $files = New-Object System.Collections.Generic.List[string]
    $backedUp = $false
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($e in $zip.Entries) {
            $name = $e.FullName.Replace('\', '/')
            if ($name.EndsWith('/')) { continue }
            $rel = $name.Replace('/', '\')
            $dest = [IO.Path]::GetFullPath((Join-Path $script:GameRoot $rel))
            if (-not $dest.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) { throw "path หลุดออกนอกโฟลเดอร์เกม: $name" }

            if ((Test-Path -LiteralPath $dest) -and -not $owned.ContainsKey($rel.ToLowerInvariant())) {
                $bak = Join-Path $backupRoot $rel
                New-Dir (Split-Path -Parent $bak)
                Copy-Item -LiteralPath $dest -Destination $bak -Force
                $backedUp = $true
            }
            New-Dir (Split-Path -Parent $dest)
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true)
            $files.Add($rel)
        }
    } finally { $zip.Dispose() }
    if ($backedUp) { Write-Log "  ↺ สำรองไฟล์เดิมที่ถูกเขียนทับไว้ที่ $backupRoot" }
    return @{ files = $files.ToArray(); backup = $(if ($backedUp) { $backupRoot } else { $null }) }
}

function Remove-EmptyParents([string[]]$relFiles) {
    $rootFull = [IO.Path]::GetFullPath($script:GameRoot).TrimEnd('\')
    $dirs = $relFiles | ForEach-Object { Split-Path -Parent (Join-Path $rootFull $_) } | Sort-Object -Unique |
        Sort-Object -Property Length -Descending
    foreach ($d in $dirs) {
        $cur = $d
        while ($cur -and $cur.Length -gt $rootFull.Length -and (Test-Path -LiteralPath $cur)) {
            if (Get-ChildItem -LiteralPath $cur -Force | Select-Object -First 1) { break }
            Remove-Item -LiteralPath $cur -Force
            $cur = Split-Path -Parent $cur
        }
    }
}

# ---------------------------------------------------------------- install / remove
function Test-ItemInstalled($item) {
    if (-not $script:State.items.ContainsKey($item.id)) { return $false }
    $rec = $script:State.items[$item.id]
    if ($rec.sha256 -ne $item.sha256 -or $rec.files.Count -eq 0) { return $false }
    foreach ($f in $rec.files) { if (-not (Test-Path -LiteralPath (Join-Path $script:GameRoot $f))) { return $false } }
    return $true
}

function Install-Item($item) {
    Write-Log "▶ ติดตั้ง: $($item.name)  [$($item.mod)]"
    $zip = Get-VerifiedPackage $item
    if ($script:ChkScan.Checked) { Invoke-DefenderScan $zip }
    [void](Test-ZipSafety $zip $item)
    if ($script:State.items.ContainsKey($item.id)) { Remove-ItemFiles $item.id -Quiet }

    $backupRoot = Join-Path $BackupDir ((Get-Date -Format 'yyyyMMdd-HHmmss') + '\' + $item.id)
    $result = Expand-Package $zip $backupRoot
    $script:State.items[$item.id] = @{
        name        = $item.name
        sha256      = $item.sha256
        installedAt = (Get-Date).ToString('s')
        backup      = $result.backup
        files       = $result.files
    }
    Save-State
    Write-Log "  ✔ ติดตั้งเสร็จ ($($result.files.Count) ไฟล์)"
}

function Remove-ItemFiles([string]$id, [switch]$Quiet) {
    if (-not $script:State.items.ContainsKey($id)) { return }
    $rec = $script:State.items[$id]
    if (-not $Quiet) { Write-Log "▶ ถอนการติดตั้ง: $($rec.name)" }
    foreach ($f in $rec.files) {
        $p = Join-Path $script:GameRoot $f
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force }
    }
    if ($id -eq $Loader.id) {
        # โฟลเดอร์ _Redloader เป็นของ RedLoader ทั้งหมด (รวมไฟล์ที่มันสร้างตอนเปิดเกม) ลบทิ้งได้ทั้งโฟลเดอร์
        $rl = Join-Path $script:GameRoot '_Redloader'
        if ((Test-GameFolder $script:GameRoot) -and (Test-Path -LiteralPath $rl)) { Remove-Item -LiteralPath $rl -Recurse -Force }
    }
    Remove-EmptyParents $rec.files
    [void]$script:State.items.Remove($id)
    Save-State
    if (-not $Quiet) { Write-Log '  ✔ ลบไฟล์ของ mod นี้ออกแล้ว' }
}

# ---------------------------------------------------------------- settings: Kelvin HP / enable toggle
function Set-KelvinHp([int]$hp) {
    $ud = Join-Path $script:GameRoot 'UserData'
    if (-not (Test-Path -LiteralPath $ud)) { return $false }
    $cfgs = @(Get-ChildItem -LiteralPath $ud -Filter '*.cfg' -File | Where-Object {
        $t = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
        $t -match '(?m)^\s*Maxhealth\s*=' -and ($_.Name -like '*Kelvin*' -or $t -match '(?i)kelvin')
    })
    if ($cfgs.Count -eq 0) { return $false }
    foreach ($cfg in $cfgs) {
        $text = Get-Content -LiteralPath $cfg.FullName -Raw -Encoding UTF8
        $new = [regex]::Replace($text, '(?m)^(\s*Maxhealth\s*=\s*)([-+0-9.eE_]+)', [Text.RegularExpressions.MatchEvaluator]{
            param($m)
            if ($m.Groups[2].Value.Contains('.')) { $v = "$hp.0" } else { $v = "$hp" }
            $m.Groups[1].Value + $v
        })
        if ($new -ne $text) {
            $bak = Join-Path $BackupDir ('config\' + $cfg.Name + '.' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak')
            New-Dir (Split-Path -Parent $bak)
            Copy-Item -LiteralPath $cfg.FullName -Destination $bak
            [IO.File]::WriteAllText($cfg.FullName, $new, (New-Object Text.UTF8Encoding $false))
        }
        Write-Log "  ✔ ตั้งเลือดสูงสุดของ Kelvin = $hp ใน $($cfg.Name)"
    }
    return $true
}

function Get-DoorstopPath { return (Join-Path $script:GameRoot 'doorstop_config.ini') }

function Get-ModsEnabled {
    $ini = Get-DoorstopPath
    if (-not (Test-Path -LiteralPath $ini)) { return $null }
    return ((Get-Content -LiteralPath $ini -Raw) -match '(?m)^\s*enabled\s*=\s*true')
}

function Set-ModsEnabled([bool]$enabled) {
    $ini = Get-DoorstopPath
    $text = Get-Content -LiteralPath $ini -Raw
    $value = $(if ($enabled) { 'true' } else { 'false' })
    $text = [regex]::Replace($text, '(?m)^(\s*enabled\s*=\s*)\w+', ('${1}' + $value))
    [IO.File]::WriteAllText($ini, $text, (New-Object Text.UTF8Encoding $false))
}

# ---------------------------------------------------------------- GUI
function Px([double]$v) { return [int][Math]::Round($v * $script:Scale) }

function New-Label([string]$text, $font, $color, [int]$indent = 0) {
    $l = New-Object System.Windows.Forms.Label
    $l.Text = $text
    $l.AutoSize = $true
    if ($font) { $l.Font = $font }
    if ($color) { $l.ForeColor = $color }
    $l.Margin = New-Object System.Windows.Forms.Padding((Px $indent), 0, 0, (Px 2))
    return $l
}

function Update-Status {
    $installed = @($script:State.items.Keys | Where-Object { $_ -ne $Loader.id -and -not (Get-Opt $ItemsById[$_] 'hidden' $false) })
    $loaderOk = $script:GameRoot -and (Test-ItemInstalled $Loader)
    $enabled = $null
    if ($script:GameRoot) { $enabled = Get-ModsEnabled }
    $parts = @()
    if (-not $script:GameRoot) { $parts += 'ยังไม่พบโฟลเดอร์เกม — กด "เปลี่ยน..." เพื่อเลือก' }
    elseif ($loaderOk) { $parts += 'RedLoader: ติดตั้งแล้ว' } else { $parts += 'RedLoader: ยังไม่ติดตั้ง' }
    $parts += "mod ที่ติดตั้งอยู่: $($installed.Count) ตัว"
    if ($enabled -eq $false) { $parts += 'สถานะ mod: ปิดชั่วคราว (เกมเล่นแบบปกติ)' }
    elseif ($enabled -eq $true) { $parts += 'สถานะ mod: เปิดใช้งาน' }
    $script:LblStatus.Text = ($parts -join '   •   ')

    $script:BtnToggle.Enabled = ($null -ne $enabled)
    $script:BtnToggle.Text = $(if ($enabled -eq $false) { 'เปิด mod อีกครั้ง' } else { 'ปิด mod ชั่วคราว' })
    $script:BtnUninstall.Enabled = ($script:State.items.Count -gt 0)
}

function Set-Busy([bool]$busy) {
    $script:IsBusy = $busy
    $script:Form.UseWaitCursor = $busy
    foreach ($c in $script:BusyControls) { $c.Enabled = -not $busy }
    if (-not $busy) { Update-Status }
    [System.Windows.Forms.Application]::DoEvents()
}

function Show-Msg([string]$text, [string]$icon = 'Information', [string]$buttons = 'OK') {
    return [System.Windows.Forms.MessageBox]::Show($script:Form, $text, 'Mod Picker', $buttons, $icon)
}

function Get-SelectedIds {
    $ids = New-Object System.Collections.Generic.List[string]
    foreach ($it in $Catalog.items) {
        if (Get-Opt $it 'hidden' $false) { continue }
        if (-not $script:Checks[$it.id].Checked) { continue }
        foreach ($req in @(Get-Opt $it 'requires' @())) { if ($req -and -not $ids.Contains($req)) { $ids.Add($req) } }
        if (-not $ids.Contains($it.id)) { $ids.Add($it.id) }
    }
    return $ids.ToArray()
}

function Invoke-Apply {
    if (-not $script:GameRoot) { [void](Show-Msg 'กรุณาเลือกโฟลเดอร์เกมก่อน' 'Warning'); return }
    if (Test-GameRunning) { [void](Show-Msg 'กรุณาปิดเกม Sons Of The Forest ก่อนติดตั้ง' 'Warning'); return }

    $selected = @(Get-SelectedIds)
    $toRemove = @($script:State.items.Keys | Where-Object { $_ -ne $Loader.id -and $selected -notcontains $_ })
    $toInstall = @($selected | Where-Object { -not (Test-ItemInstalled $ItemsById[$_]) })
    $needLoader = $selected.Count -gt 0
    $installLoader = $needLoader -and -not (Test-ItemInstalled $Loader)
    $removeLoader = (-not $needLoader) -and $script:State.items.ContainsKey($Loader.id)

    $lines = @()
    if ($installLoader) { $lines += "  + $($Loader.name)" }
    foreach ($id in $toInstall) { $lines += "  + $($ItemsById[$id].name)" }
    foreach ($id in $toRemove) { $lines += "  - $($script:State.items[$id].name)" }
    if ($removeLoader) { $lines += "  - $($Loader.name)" }

    if ($lines.Count -gt 0) {
        $msg = "จะดำเนินการดังนี้ (+ ติดตั้ง, - ถอนออก):`r`n`r`n" + ($lines -join "`r`n") + "`r`n`r`nดำเนินการต่อ?"
        if ((Show-Msg $msg 'Question' 'YesNo') -ne 'Yes') { return }
    }

    Set-Busy $true
    try {
        New-Dir $LogDir
        $script:LogFile = Join-Path $LogDir ('install-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
        Write-Log "โฟลเดอร์เกม: $($script:GameRoot)"
        foreach ($id in $toRemove) { Remove-ItemFiles $id }
        if ($installLoader) { Install-Item $Loader }
        foreach ($id in ($toInstall | Sort-Object { -not [bool](Get-Opt $ItemsById[$_] 'hidden' $false) })) {
            Install-Item $ItemsById[$id]
        }
        if ($removeLoader) { Remove-ItemFiles $Loader.id }

        $kelvinNote = ''
        if ($selected -contains 'kelvin') {
            $hp = [int]$script:NumKelvinHp.Value
            $script:State.settings['kelvinHp'] = $hp
            Save-State
            if (-not (Set-KelvinHp $hp)) {
                $kelvinNote = "`r`n`r`nหมายเหตุ: ค่าเลือด Kelvin ($hp) จะตั้งได้หลังเปิดเกมครั้งแรก 1 ครั้ง" +
                    "`r`n→ เปิดเกม แล้วออก จากนั้นเปิดโปรแกรมนี้กด 'ติดตั้ง / อัปเดต' อีกครั้ง" +
                    "`r`n   (หรือปรับในเกมที่ Mods > KelvinCanAttack > Settings > Max Health)"
                Write-Log "  ! ยังไม่พบไฟล์ config ของ KelvinCanAttack — จะตั้งค่าเลือดได้หลังเปิดเกม 1 ครั้ง"
            }
        }

        if ($lines.Count -eq 0) {
            Write-Log 'ไม่มีการเปลี่ยนแปลง mod (ทุกอย่างตรงกับที่เลือกแล้ว)'
            [void](Show-Msg ("ไม่มีการเปลี่ยนแปลง mod — ทุกอย่างตรงกับที่เลือกแล้ว" + $kelvinNote))
        } elseif ($needLoader) {
            Write-Log '✔ เสร็จเรียบร้อย'
            [void](Show-Msg ("ติดตั้งเสร็จเรียบร้อย!`r`n`r`nเปิดเกมผ่าน Steam ได้เลย — ครั้งแรก RedLoader จะเตรียมไฟล์ประมาณ 1-5 นาที (มีหน้าจอ RedLoader ขึ้น) เป็นเรื่องปกติ ไม่ต้องปิด" + $kelvinNote))
        } else {
            Write-Log '✔ ถอน mod ทั้งหมดแล้ว เกมกลับเป็นแบบปกติ'
            [void](Show-Msg 'ถอน mod ทั้งหมดแล้ว เกมกลับเป็นแบบปกติ')
        }
    } catch {
        Write-Log "✖ ผิดพลาด: $($_.Exception.Message)"
        [void](Show-Msg ("เกิดข้อผิดพลาด — หยุดการติดตั้งเพื่อความปลอดภัย`r`n`r`n" + $_.Exception.Message) 'Error')
    } finally {
        Set-Busy $false
    }
}

function Invoke-UninstallAll {
    if (Test-GameRunning) { [void](Show-Msg 'กรุณาปิดเกม Sons Of The Forest ก่อน' 'Warning'); return }
    $msg = "ถอน RedLoader และ mod ทั้งหมดที่โปรแกรมนี้ติดตั้ง เพื่อให้เกมกลับเป็นแบบปกติ?`r`n`r`n(ไฟล์เซฟเกมไม่ถูกแตะ / การตั้งค่า mod ในโฟลเดอร์ UserData ยังเก็บไว้)"
    if ((Show-Msg $msg 'Question' 'YesNo') -ne 'Yes') { return }
    Set-Busy $true
    try {
        New-Dir $LogDir
        $script:LogFile = Join-Path $LogDir ('uninstall-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
        foreach ($id in @($script:State.items.Keys | Where-Object { $_ -ne $Loader.id })) { Remove-ItemFiles $id }
        Remove-ItemFiles $Loader.id
        foreach ($cb in $script:Checks.Values) { $cb.Checked = $false }
        Write-Log '✔ ถอนทั้งหมดแล้ว เกมกลับเป็นแบบปกติ'
        [void](Show-Msg 'ถอนทั้งหมดแล้ว เกมกลับเป็นแบบปกติ')
    } catch {
        Write-Log "✖ ผิดพลาด: $($_.Exception.Message)"
        [void](Show-Msg $_.Exception.Message 'Error')
    } finally { Set-Busy $false }
}

function Invoke-Toggle {
    if (Test-GameRunning) { [void](Show-Msg 'กรุณาปิดเกม Sons Of The Forest ก่อน' 'Warning'); return }
    $enabled = Get-ModsEnabled
    if ($null -eq $enabled) { return }
    Set-ModsEnabled (-not $enabled)
    if ($enabled) { Write-Log 'ปิด mod ชั่วคราวแล้ว — เกมจะเปิดแบบปกติ (ไฟล์ mod ยังอยู่)' }
    else { Write-Log 'เปิด mod อีกครั้งแล้ว' }
    Update-Status
}

function Set-GameRoot([string]$path) {
    $script:GameRoot = $path
    $script:TxtGame.Text = $(if ($path) { $path } else { '(ไม่พบ)' })
    Update-Status
}

function Build-Gui {
    [System.Windows.Forms.Application]::EnableVisualStyles()
    $g = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
    $script:Scale = [Math]::Max(1.0, $g.DpiX / 96.0)
    $g.Dispose()

    $families = [System.Drawing.FontFamily]::Families | ForEach-Object { $_.Name }
    $fontName = $(if ($families -contains 'Leelawadee UI') { 'Leelawadee UI' } else { 'Tahoma' })
    $fBase  = New-Object System.Drawing.Font($fontName, 10)
    $fSmall = New-Object System.Drawing.Font($fontName, 9)
    $fBold  = New-Object System.Drawing.Font($fontName, 10, [System.Drawing.FontStyle]::Bold)
    $fGroup = New-Object System.Drawing.Font($fontName, 11.5, [System.Drawing.FontStyle]::Bold)
    $fTitle = New-Object System.Drawing.Font($fontName, 14, [System.Drawing.FontStyle]::Bold)
    $cMuted = [System.Drawing.Color]::FromArgb(90, 90, 90)
    $cWarn  = [System.Drawing.Color]::FromArgb(176, 80, 0)
    $cOk    = [System.Drawing.Color]::FromArgb(0, 110, 60)
    $cGroup = [System.Drawing.Color]::FromArgb(25, 60, 120)

    $form = New-Object System.Windows.Forms.Form
    $script:Form = $form
    $form.Text = 'Sons Of The Forest — Mod Picker'
    $form.Font = $fBase
    $form.StartPosition = 'CenterScreen'
    $form.ClientSize = New-Object System.Drawing.Size((Px 1000), (Px 820))
    $form.MinimumSize = New-Object System.Drawing.Size((Px 720), (Px 600))
    $form.BackColor = [System.Drawing.Color]::FromArgb(245, 246, 248)

    $main = New-Object System.Windows.Forms.TableLayoutPanel
    $main.Dock = 'Fill'
    $main.Padding = New-Object System.Windows.Forms.Padding((Px 14))
    $main.ColumnCount = 1
    $main.RowCount = 6
    [void]$main.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle('Percent', 100)))
    foreach ($style in @('AutoSize', 'AutoSize', 'Percent', 'AutoSize', 'Absolute', 'AutoSize')) {
        $rs = New-Object System.Windows.Forms.RowStyle($style)
        if ($style -eq 'Percent') { $rs.Height = 100 }
        if ($style -eq 'Absolute') { $rs.Height = (Px 130) }
        [void]$main.RowStyles.Add($rs)
    }
    $form.Controls.Add($main)

    # --- header
    $header = New-Object System.Windows.Forms.FlowLayoutPanel
    $header.FlowDirection = 'TopDown'; $header.WrapContents = $false; $header.AutoSize = $true; $header.Dock = 'Fill'
    $script:HeaderLabels = New-Object System.Collections.Generic.List[object]
    $header.Controls.Add((New-Label 'เลือก mod ที่ต้องการ แล้วกด "ติดตั้ง / อัปเดต"' $fTitle $null))
    $safety = New-Label ('ปลอดภัย: ดาวน์โหลดเฉพาะจาก GitHub ทางการของ RedLoader และ sotf-mods.com  •  ล็อก SHA256 ทุกไฟล์  •  ' +
        'สแกนด้วย Windows Defender  •  สำรองไฟล์ก่อนเขียนทับ  •  ถอนออกได้ครบ') $fSmall $cOk
    $tipsText = 'ถ้าเล่น online กับเพื่อน: ทุกคนควรใช้ mod ชุดเดียวกัน หรือกด "ปิด mod ชั่วคราว" ก่อนเข้าห้อง  •  ถ้าเกมอัปเดตแล้วเปิดไม่ขึ้น ให้กด "ปิด mod ชั่วคราว"'
    $tips = New-Label $tipsText $fSmall $cMuted
    $header.Controls.Add($safety); $header.Controls.Add($tips)
    $script:HeaderLabels.Add($safety); $script:HeaderLabels.Add($tips)
    $main.Controls.Add($header, 0, 0)

    # --- game folder row + status
    $pathRow = New-Object System.Windows.Forms.TableLayoutPanel
    $pathRow.Dock = 'Fill'; $pathRow.AutoSize = $true; $pathRow.ColumnCount = 3; $pathRow.RowCount = 2
    $pathRow.Margin = New-Object System.Windows.Forms.Padding(0, (Px 8), 0, (Px 6))
    [void]$pathRow.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle('AutoSize')))
    [void]$pathRow.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle('Percent', 100)))
    [void]$pathRow.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle('AutoSize')))
    $lblPath = New-Label 'โฟลเดอร์เกม:' $fBold $null
    $lblPath.Anchor = 'Left'
    $script:TxtGame = New-Object System.Windows.Forms.TextBox
    $script:TxtGame.ReadOnly = $true; $script:TxtGame.Dock = 'Fill'
    $btnBrowse = New-Object System.Windows.Forms.Button
    $btnBrowse.Text = 'เปลี่ยน...'; $btnBrowse.AutoSize = $true
    $btnBrowse.Add_Click({
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = 'เลือกโฟลเดอร์ที่มีไฟล์ SonsOfTheForest.exe'
        if ($script:GameRoot) { $dlg.SelectedPath = $script:GameRoot }
        if ($dlg.ShowDialog($script:Form) -eq 'OK') {
            if (Test-GameFolder $dlg.SelectedPath) { Set-GameRoot ([IO.Path]::GetFullPath($dlg.SelectedPath)); Save-State }
            else { [void](Show-Msg 'ไม่พบ SonsOfTheForest.exe ในโฟลเดอร์นี้' 'Warning') }
        }
    })
    $pathRow.Controls.Add($lblPath, 0, 0); $pathRow.Controls.Add($script:TxtGame, 1, 0); $pathRow.Controls.Add($btnBrowse, 2, 0)
    $script:LblStatus = New-Label '' $fSmall $cGroup
    $script:LblStatus.Margin = New-Object System.Windows.Forms.Padding(0, (Px 6), 0, 0)
    $script:HeaderLabels.Add($script:LblStatus)
    $pathRow.Controls.Add($script:LblStatus, 0, 1); $pathRow.SetColumnSpan($script:LblStatus, 3)
    $main.Controls.Add($pathRow, 0, 1)

    # --- mod list
    $list = New-Object System.Windows.Forms.FlowLayoutPanel
    $script:List = $list
    $list.Dock = 'Fill'; $list.FlowDirection = 'TopDown'; $list.WrapContents = $false; $list.AutoScroll = $true
    $list.BackColor = [System.Drawing.Color]::White; $list.BorderStyle = 'FixedSingle'
    $list.Padding = New-Object System.Windows.Forms.Padding((Px 10), (Px 4), (Px 10), (Px 10))
    $main.Controls.Add($list, 0, 2)

    $tip = New-Object System.Windows.Forms.ToolTip
    $script:Checks = @{}
    $script:WrapLabels = New-Object System.Collections.Generic.List[object]
    $anyInstalled = @($script:State.items.Keys | Where-Object { $_ -ne $Loader.id }).Count -gt 0

    foreach ($group in $Catalog.groups) {
        $gl = New-Label $group $fGroup $cGroup
        $gl.Margin = New-Object System.Windows.Forms.Padding(0, (Px 12), 0, (Px 4))
        $list.Controls.Add($gl)
        foreach ($it in ($Catalog.items | Where-Object { (Get-Opt $_ 'group') -eq $group -and -not (Get-Opt $_ 'hidden' $false) })) {
            $cb = New-Object System.Windows.Forms.CheckBox
            $cb.Text = $it.name; $cb.Font = $fBold; $cb.AutoSize = $true
            $cb.Margin = New-Object System.Windows.Forms.Padding((Px 4), (Px 6), 0, 0)
            if ($anyInstalled) { $cb.Checked = $script:State.items.ContainsKey($it.id) } else { $cb.Checked = [bool](Get-Opt $it 'default' $false) }
            $tip.SetToolTip($cb, "$($it.mod)`r`n$($it.page)")
            $script:Checks[$it.id] = $cb
            $list.Controls.Add($cb)

            $mpText = $(if (Get-Opt $it 'mp' $false) { 'ใช้ใน multiplayer ได้' } else { 'แนะนำเล่นคนเดียว' })
            $desc = New-Label ($it.desc + "`r`n" + $it.mod + '   •   ' + $mpText) $fSmall $cMuted 26
            $script:WrapLabels.Add($desc); $list.Controls.Add($desc)

            $warn = Get-Opt $it 'warn'
            if ($warn) {
                $wl = New-Label ('คำเตือน: ' + $warn) $fSmall $cWarn 26
                $script:WrapLabels.Add($wl); $list.Controls.Add($wl)
            }

            if ((Get-Opt $it 'setting') -eq 'kelvinHp') {
                $cfg = $Catalog.settings.kelvinHp
                $row = New-Object System.Windows.Forms.FlowLayoutPanel
                $row.AutoSize = $true; $row.WrapContents = $false
                $row.Margin = New-Object System.Windows.Forms.Padding((Px 26), (Px 2), 0, (Px 2))
                $hl = New-Label ($cfg.label + ':') $fSmall $null
                $hl.Anchor = 'Left'; $hl.Margin = New-Object System.Windows.Forms.Padding(0, (Px 4), (Px 4), 0)
                $script:NumKelvinHp = New-Object System.Windows.Forms.NumericUpDown
                $script:NumKelvinHp.Minimum = $cfg.min; $script:NumKelvinHp.Maximum = $cfg.max
                $script:NumKelvinHp.Increment = 100; $script:NumKelvinHp.Width = (Px 100)
                $hpValue = $cfg.default
                if ($script:State.settings.ContainsKey('kelvinHp')) { $hpValue = $script:State.settings['kelvinHp'] }
                $script:NumKelvinHp.Value = [decimal]$hpValue
                $row.Controls.Add($hl); $row.Controls.Add($script:NumKelvinHp)
                $hint = New-Label '(ผู้สร้าง mod แนะนำอย่างน้อย 1000)' $fSmall $cMuted
                $hint.Margin = New-Object System.Windows.Forms.Padding((Px 6), (Px 4), 0, 0)
                $row.Controls.Add($hint)
                $list.Controls.Add($row)
            }

            $link = New-Object System.Windows.Forms.LinkLabel
            $link.Text = 'ดูหน้า mod'; $link.AutoSize = $true; $link.Font = $fSmall; $link.Tag = $it.page
            $link.Margin = New-Object System.Windows.Forms.Padding((Px 26), 0, 0, (Px 4))
            $link.Add_LinkClicked({
                param($s, $e)
                $target = [string]$s.Tag
                if ($target -notmatch '^https?://') { $target = Join-Path $script:Root $target }   # mod ในเครื่อง: เปิดโฟลเดอร์
                Start-Process $target
            })
            $list.Controls.Add($link)
        }
    }
    # handler ทำงานหลัง Build-Gui จบแล้ว จึงอ้างได้เฉพาะตัวแปร $script:
    $script:Relayout = {
        $w = [Math]::Max((Px 200), $script:List.ClientSize.Width - (Px 60))
        foreach ($l in $script:WrapLabels) { $l.MaximumSize = New-Object System.Drawing.Size($w, 0) }
        $hw = [Math]::Max((Px 200), $script:Form.ClientSize.Width - (Px 40))
        foreach ($l in $script:HeaderLabels) { $l.MaximumSize = New-Object System.Drawing.Size($hw, 0) }
    }
    $form.Add_Resize({ & $script:Relayout })

    # --- options
    $script:ChkScan = New-Object System.Windows.Forms.CheckBox
    $script:ChkScan.Text = 'สแกนไฟล์ด้วย Windows Defender ก่อนติดตั้ง (แนะนำ)'
    $script:ChkScan.Checked = $true; $script:ChkScan.AutoSize = $true
    $script:ChkScan.Margin = New-Object System.Windows.Forms.Padding(0, (Px 8), 0, (Px 4))
    $main.Controls.Add($script:ChkScan, 0, 3)

    # --- log
    $script:LogBox = New-Object System.Windows.Forms.TextBox
    $script:LogBox.Multiline = $true; $script:LogBox.ReadOnly = $true; $script:LogBox.ScrollBars = 'Vertical'
    $script:LogBox.Dock = 'Fill'; $script:LogBox.Font = $fSmall; $script:LogBox.BackColor = [System.Drawing.Color]::White
    $main.Controls.Add($script:LogBox, 0, 4)

    # --- buttons
    $buttons = New-Object System.Windows.Forms.FlowLayoutPanel
    $buttons.Dock = 'Fill'; $buttons.AutoSize = $true; $buttons.FlowDirection = 'RightToLeft'
    $buttons.Margin = New-Object System.Windows.Forms.Padding(0, (Px 8), 0, 0)
    function New-Btn([string]$text, [scriptblock]$onClick, [bool]$primary = $false) {
        $b = New-Object System.Windows.Forms.Button
        $b.Text = $text; $b.AutoSize = $true
        $b.Padding = New-Object System.Windows.Forms.Padding((Px 8), (Px 3), (Px 8), (Px 3))
        if ($primary) { $b.Font = $fBold; $b.BackColor = [System.Drawing.Color]::FromArgb(0, 120, 70); $b.ForeColor = [System.Drawing.Color]::White; $b.FlatStyle = 'Flat' }
        $b.Add_Click($onClick)
        $buttons.Controls.Add($b)
        return $b
    }
    $btnApply = New-Btn 'ติดตั้ง / อัปเดต ตามที่เลือก' { Invoke-Apply } $true
    $btnClose = New-Btn 'ปิด' { $script:Form.Close() }
    $script:BtnUninstall = New-Btn 'ถอนทั้งหมด (กลับเป็นเกมปกติ)' { Invoke-UninstallAll }
    $script:BtnToggle = New-Btn 'ปิด mod ชั่วคราว' { Invoke-Toggle }
    $btnRec = New-Btn 'เลือกตามที่แนะนำ' {
        foreach ($it in $Catalog.items) { if ($script:Checks.ContainsKey($it.id)) { $script:Checks[$it.id].Checked = [bool](Get-Opt $it 'default' $false) } }
    }
    $btnNone = New-Btn 'ไม่เลือกเลย' { foreach ($cb in $script:Checks.Values) { $cb.Checked = $false } }
    $btnPlay = New-Btn 'เปิดเกม (Steam)' { Start-Process ('steam://rungameid/' + $Catalog.game.steamAppId) }
    $main.Controls.Add($buttons, 0, 5)

    $script:BusyControls = @($btnApply, $btnClose, $script:BtnUninstall, $script:BtnToggle, $btnRec, $btnNone, $btnPlay, $btnBrowse, $list, $script:ChkScan)

    $form.Add_FormClosing({
        param($s, $e)
        if ($script:IsBusy) { $e.Cancel = $true }
    })
    $form.Add_Shown({
        & $script:Relayout
        Set-GameRoot (Find-GameFolder)
        $visible = @($Catalog.items | Where-Object { -not (Get-Opt $_ 'hidden' $false) }).Count
        Write-Log "พร้อมใช้งาน — มีให้เลือก $visible mod, แพ็กเกจที่ตรวจแล้วเก็บไว้ที่ $PackagesDir"
        Write-Log 'ติ๊กเลือก mod ที่ต้องการ (เอาติ๊กออก = ถอน mod นั้น) แล้วกด "ติดตั้ง / อัปเดต ตามที่เลือก"'
        if ($script:GameRoot) {
            if ($script:State.gamePath -ne $script:GameRoot) { Save-State }
        }
    })
    return $form
}

# ---------------------------------------------------------------- main
try {
    $script:State = Read-State
    $script:GameRoot = $null
    $form = Build-Gui
    try { [void][V2N.Native]::ShowWindow([V2N.Native]::GetConsoleWindow(), 6) } catch { }   # ย่อหน้าต่าง console
    [System.Windows.Forms.Application]::Run($form)
} catch {
    $err = $_ | Out-String
    New-Dir $LogDir
    Add-Content -LiteralPath (Join-Path $LogDir 'fatal.log') -Value ("[{0}] {1}" -f (Get-Date -Format 's'), $err) -Encoding UTF8
    try { [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Mod Picker — ผิดพลาด', 'OK', 'Error') } catch { }
    Write-Host $err
    exit 1
}
