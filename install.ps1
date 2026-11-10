# 貼貼 Tietie Windows 版安裝程式
# 用法：打開 PowerShell，貼上這一行
#   irm https://raw.githubusercontent.com/KLWSharpe/tietie/main/install.ps1 | iex

& {
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$repo = 'KLWSharpe/tietie'
$url = if ($env:TIETIE_ZIP_URL) { $env:TIETIE_ZIP_URL } else { "https://github.com/$repo/releases/latest/download/Tietie-Windows.zip" }
$dest = if ($env:TIETIE_INSTALL_DIR) { $env:TIETIE_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\Tietie' }
$tmp = Join-Path $env:TEMP ('tietie-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($admin) { Write-Host '提醒：這個 PowerShell 是用系統管理員身分開的。一般開啟 PowerShell 就可以，不需要系統管理員。' }

Write-Host '==> 下載貼貼'
if ($env:TIETIE_ZIP_PATH) {
    Copy-Item $env:TIETIE_ZIP_PATH (Join-Path $tmp 'Tietie.zip')
} else {
    Invoke-WebRequest -Uri $url -OutFile (Join-Path $tmp 'Tietie.zip') -UseBasicParsing
}
Expand-Archive -Path (Join-Path $tmp 'Tietie.zip') -DestinationPath (Join-Path $tmp 'new') -Force
$exe = Join-Path $tmp 'new\Tietie.exe'
if (-not (Test-Path $exe)) { throw '下載的檔案不完整，請再試一次。' }

Write-Host '==> 安裝'
# 關掉正在執行的舊版
$old = Get-Process -Name Tietie -ErrorAction SilentlyContinue
if ($old) {
    $old | Stop-Process -Force -ErrorAction SilentlyContinue
    $old | Wait-Process -Timeout 8 -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $dest -Force | Out-Null
$copied = $false
for ($i = 0; $i -lt 10 -and -not $copied; $i++) {
    try { Copy-Item $exe (Join-Path $dest 'Tietie.exe') -Force; $copied = $true }
    catch { Start-Sleep -Milliseconds 700 }
}
if (-not $copied) { throw '舊版的貼貼還在執行，請在工作列的貼貼圖示上按右鍵選「結束貼貼」，再貼一次安裝指令。' }
Unblock-File (Join-Path $dest 'Tietie.exe') -ErrorAction SilentlyContinue

# 開始功能表捷徑，之後搜尋「貼貼」就找得到
# 捷徑先用英文檔名存（有些語言的 Windows 不能直接存中文檔名），再改成中文名
try {
    $programs = [Environment]::GetFolderPath('Programs')
    $ascii = Join-Path $programs 'Tietie.lnk'
    $final = Join-Path $programs '貼貼 Tietie.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($ascii)
    $link.TargetPath = Join-Path $dest 'Tietie.exe'
    $link.WorkingDirectory = $dest
    $link.Save()
    if (Test-Path -LiteralPath $final) { Remove-Item -LiteralPath $final -Force }
    try { Rename-Item -LiteralPath $ascii -NewName '貼貼 Tietie.lnk' } catch { }
} catch {
    Write-Host '（開始功能表的捷徑沒有建立成功，貼貼本身已經裝好）'
}

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue

if (-not $env:TIETIE_NO_OPEN) { Start-Process (Join-Path $dest 'Tietie.exe') }
Write-Host '==> 完成。貼貼在螢幕右下角的工作列，按 Ctrl+Shift+V 叫出清單。'
Write-Host '    看不到圖示的話，點工作列上的「^」箭頭。'
}
