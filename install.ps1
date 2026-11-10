# 貼貼 Tietie Windows 版安裝程式
# 用法：打開 PowerShell，貼上這一行
#   irm https://raw.githubusercontent.com/KLWSharpe/tietie/main/install.ps1 | iex

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$repo = 'KLWSharpe/tietie'
$url = if ($env:TIETIE_ZIP_URL) { $env:TIETIE_ZIP_URL } else { "https://github.com/$repo/releases/latest/download/Tietie-Windows.zip" }
$dest = if ($env:TIETIE_INSTALL_DIR) { $env:TIETIE_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\Tietie' }
$tmp = Join-Path $env:TEMP ('tietie-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

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
Get-Process -Name Tietie -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
New-Item -ItemType Directory -Path $dest -Force | Out-Null
Copy-Item $exe (Join-Path $dest 'Tietie.exe') -Force
Unblock-File (Join-Path $dest 'Tietie.exe') -ErrorAction SilentlyContinue

# 開始功能表捷徑，之後搜尋「貼貼」就找得到
try {
    $programs = [Environment]::GetFolderPath('Programs')
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut((Join-Path $programs '貼貼 Tietie.lnk'))
    $link.TargetPath = Join-Path $dest 'Tietie.exe'
    $link.Description = '貼貼 Tietie 剪貼簿小工具'
    $link.Save()
} catch { }

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue

if (-not $env:TIETIE_NO_OPEN) { Start-Process (Join-Path $dest 'Tietie.exe') }
Write-Host '==> 完成。貼貼在螢幕右下角的工作列，按 Ctrl+Shift+V 叫出清單。'
Write-Host '    看不到圖示的話，點工作列上的「^」箭頭。'
