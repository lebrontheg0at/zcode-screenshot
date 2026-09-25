---
name: screenshot
description: 截图工具操作指南：触发框选截图（路径自动填入 ZCode 输入框，用户补充文字后发送）、自定义全局热键、查看监听器状态、排查截图故障。当用户要求截图、修改截图快捷键、或截图功能出问题时使用。
---

# ZCode 截图工具

- 调度脚本（固定）：`%USERPROFILE%\.zcode\local-plugins\zcode-screenshot\scripts\capture.ps1`
- 数据目录：`%USERPROFILE%\.zcode\screenshot\`（`config.json` 热键/空闲/autoInsert 配置、`shots\` 截图、`latest.txt` 最近一次截图路径）
- 监听器 `capture.exe` 由脚本按需编译并懒启动：只在需要时运行，空闲（默认 30 分钟，`config.json` 的 `idleMinutes` 可调）自动退出。

## 核心行为：截图自动填入输入框

截图完成后，工具自动把**图片本体**粘贴回截图前的前台窗口（通常是 ZCode 输入框），效果等同拖图进对话框，**由用户补充文字后按回车发送**。因此：

- 通过 `/screenshot` 或"截个图"触发后，**不要主动 Read 图片**，简短确认"已填入输入框，补充文字后回车"即可。
- 用户消息里直接附带截图（图片输入），直接看图分析即可，无需 Read。
- 用户说"看下我刚才的截图"但消息里没带图时，读 `%USERPROFILE%\.zcode\screenshot\latest.txt` 里的路径。

## 触发截图

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Saber\.zcode\local-plugins\zcode-screenshot\scripts\capture.ps1"
```

框选画面为半透明遮罩，拖出矩形松手即完成，按 ESC 取消（取消时不粘贴）。

## 自定义热键（对话里用户说"把截图快捷键改成 X"时）

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Saber\.zcode\local-plugins\zcode-screenshot\scripts\capture.ps1" 热键 Ctrl+Alt+S
```

支持 Ctrl/Alt/Shift/Win 组合 字母/数字/F1~F12。脚本会更新 config.json 并通知运行中的监听器热重载。

## 查看状态 / 配置

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Saber\.zcode\local-plugins\zcode-screenshot\scripts\capture.ps1" 状态
```

`config.json` 可调项：`hotkey`（热键）、`idleMinutes`（空闲自动退出时间）、`autoInsert`（截图后自动粘贴路径进输入框，改 false 则只落盘不粘贴）。

## 故障排查

- 截图超时：用户未完成框选或按了 ESC，重试即可。
- 热键无效：`状态` 里确认监听器是否运行；热键可能被其他程序占用，换个组合键。
- 路径粘贴到了别的窗口：粘贴目标是截图前的前台窗口，截图前先确保 ZCode 是活动窗口；或把 `autoInsert` 设为 false。
- 编译失败：需要 .NET Framework v4 的 csc.exe（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`），源码在插件 `scripts\capture.cs`。
