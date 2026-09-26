# ZCode 截图调度脚本：编译监听器（仅首次/源码更新时）、预热/懒启动、触发截图、等待结果
# 用法：
#   capture.ps1                触发框选截图，成功输出图片路径
#   capture.ps1 启动            预热：确保监听器在运行（SessionStart 钩子调用）
#   capture.ps1 热键 Ctrl+Alt+S  修改全局热键并热重载
#   capture.ps1 状态            查看监听器与热键状态
#   capture.ps1 校准            记录输入框粘贴位置（先把光标点进 ZCode 输入框再运行）
$ErrorActionPreference = "Stop"

$base = Join-Path $env:USERPROFILE ".zcode\screenshot"
$shots = Join-Path $base "shots"
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$config = Join-Path $base "config.json"
if (-not (Test-Path $config)) {
  @{ hotkey = "Ctrl+Alt+A"; idleMinutes = 30; autoInsert = $true } | ConvertTo-Json | Set-Content -Encoding UTF8 $config
}

$cs = Join-Path $PSScriptRoot "capture.cs"
$exe = Join-Path $base "capture.exe"

# 编译：exe 缺失或源码更新时才重新编译（编译期间监听器须先退出，否则 exe 被占用）
if (-not (Test-Path $exe) -or (Get-Item $cs).LastWriteTime -gt (Get-Item $exe).LastWriteTime) {
  Stop-Process -Name "capture" -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 300
  $csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
  & $csc -nologo -target:winexe -out:$exe -r:System.Drawing.dll -r:System.dll $cs
  if ($LASTEXITCODE -ne 0) { Write-Output "编译失败"; exit 1 }
}

# 解析参数
$mode = "capture"
$hotkeyText = ""
$joined = if ($args) { ($args -join " ").Trim() } else { "" }
if ($joined -match "^(热键|hotkey)\s*(.*)$") { $mode = "hotkey"; $hotkeyText = $Matches[2].Trim() }
elseif ($joined -match "^(状态|status)$") { $mode = "status" }
elseif ($joined -match "^(启动|start)$") { $mode = "start" }
elseif ($joined -match "^(校准|calibrate)$") { $mode = "calibrate" }

if ($mode -eq "calibrate") {
  Add-Type -TypeDefinition @"
using System;using System.Runtime.InteropServices;
public struct PRECT{public int L,T,R,B;}
public struct PPOINT{public int X,Y;}
public class PCal{
  [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]public static extern bool GetCursorPos(out PPOINT p);
  [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out PRECT r);
  [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
}
"@
  $fg = [PCal]::GetForegroundWindow()
  if ($fg -eq [IntPtr]::Zero) { Write-Output "校准失败：找不到前台窗口"; exit 1 }
  $wr = New-Object PRECT
  [PCal]::GetWindowRect($fg, [ref]$wr) | Out-Null
  $winPid = 0
  [PCal]::GetWindowThreadProcessId($fg, [ref]$winPid) | Out-Null
  $proc = Get-Process -Id $winPid -ErrorAction SilentlyContinue
  if (-not $proc -or $proc.ProcessName -notmatch "zcode") { Write-Output "校准失败：请先把 ZCode 设为前台窗口"; exit 1 }
  $pt = New-Object PPOINT
  [PCal]::GetCursorPos([ref]$pt) | Out-Null
  if ($pt.X -lt $wr.L -or $pt.X -gt $wr.R -or $pt.Y -lt $wr.T -or $pt.Y -gt $wr.B) {
    Write-Output "校准失败：鼠标不在 ZCode 窗口内。请先用鼠标点一下输入框，保持鼠标不动，再运行校准"
    exit 1
  }
  $fx = [math]::Round(($pt.X - $wr.L) / ($wr.R - $wr.L), 4)
  $fy = [math]::Round($wr.B - $pt.Y, 0)
  $cfg = Get-Content $config -Raw | ConvertFrom-Json
  $cfg | Add-Member -NotePropertyName pasteClickX -NotePropertyValue $fx -Force
  $cfg | Add-Member -NotePropertyName pasteClickYFromBottom -NotePropertyValue $fy -Force
  $cfg | ConvertTo-Json | Set-Content -Encoding UTF8 $config
  Write-Output ("校准完成：粘贴点击位置已记录（横向 " + ($fx * 100) + "%，距窗口底部 " + $fy + "px）。以后截图会自动点进输入框粘贴。")
  exit 0
}

if ($mode -eq "hotkey") {
  if ($hotkeyText -eq "") { Write-Output "用法：/screenshot 热键 Ctrl+Alt+S"; exit 0 }
  $cfg = Get-Content $config -Raw | ConvertFrom-Json
  $cfg.hotkey = $hotkeyText
  $cfg | ConvertTo-Json | Set-Content -Encoding UTF8 $config
  # 通知运行中的监听器热重载热键
  try { [System.Threading.EventWaitHandle]::OpenExisting("Local\ZCodeShotReload").Set() | Out-Null } catch {}
  Write-Output "已把截图快捷键改为 $hotkeyText"
  exit 0
}

if ($mode -eq "status") {
  $p = Get-Process -Name "capture" -ErrorAction SilentlyContinue
  if ($p) { Write-Output ("监听器：运行中 (PID " + $p.Id + ")") } else { Write-Output "监听器：未运行（截图时自动拉起）" }
  $cfg = Get-Content $config -Raw | ConvertFrom-Json
  Write-Output ("热键：" + $cfg.hotkey)
  $hkErr = Join-Path $base "hotkey-error.log"
  if (Test-Path $hkErr) { Write-Output ("警告：" + (Get-Content $hkErr -Raw).Trim() + "（修复后该日志自动清除）") }
  exit 0
}

# ---- 确保监听器运行（"启动"模式到此为止；截图模式继续触发） ----
$running = Get-Process -Name "capture" -ErrorAction SilentlyContinue
if (-not $running) {
  Start-Process -FilePath $exe -WindowStyle Hidden
  # 等待监听器创建好事件句柄（比固定 sleep 更快也更可靠）；句柄尚未创建时视为未就绪，继续轮询
  $handleReady = $false
  $deadline = (Get-Date).AddSeconds(5)
  while ((Get-Date) -lt $deadline) {
    try { if ([System.Threading.EventWaitHandle]::OpenExisting("Local\ZCodeShotTrigger")) { $handleReady = $true; break } }
    catch [System.Threading.WaitHandleCannotBeOpenedException] { }
    catch { }
    Start-Sleep -Milliseconds 50
  }
  if (-not $handleReady) { Write-Output "监听器启动失败"; exit 1 }
}
if ($mode -eq "start") { Write-Output "截图监听器已就绪（Ctrl+Alt+A 框选截图，空闲或 ZCode 退出后自动退出）"; exit 0 }

# ---- 截图：触发 -> 等待 latest.txt 更新 ----
$latest = Join-Path $base "latest.txt"
$before = if (Test-Path $latest) { (Get-Item $latest).LastWriteTimeUtc } else { [DateTime]::MinValue }
[System.Threading.EventWaitHandle]::OpenExisting("Local\ZCodeShotTrigger").Set() | Out-Null

$timeout = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $timeout) {
  Start-Sleep -Milliseconds 100
  if (Test-Path $latest) {
    if ((Get-Item $latest).LastWriteTimeUtc -gt $before) {
      Write-Output (Get-Content $latest -Raw).Trim()
      exit 0
    }
  }
}
Write-Output "截图超时或被取消（框选时按 ESC 可取消）"
