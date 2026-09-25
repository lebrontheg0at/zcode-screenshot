# ZCode 截图调度脚本：编译监听器（仅首次/源码更新时）、预热/懒启动、触发截图、等待结果
# 用法：
#   capture.ps1                触发框选截图，成功输出图片路径
#   capture.ps1 启动            预热：确保监听器在运行（SessionStart 钩子调用）
#   capture.ps1 热键 Ctrl+Alt+S  修改全局热键并热重载#   capture.ps1 状态            查看监听器与热键状态
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
