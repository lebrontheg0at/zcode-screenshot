# zcode-screenshot | zcode-screenshot 截图工具

[English](#english) | [中文](#中文)

---

<a id="english"></a>

## English

A [ZCode](https://zcode.ai) plugin that adds instant region screenshots to your AI conversations: press a global hotkey, drag a rectangle, and the image is **automatically pasted into the ZCode input box** — just add your question and hit Enter.

### Highlights

- 🖼️ **Region capture with auto-paste** — the screenshot is pasted as an image (not a file path) back into the window you captured from, ready to send
- ⌨️ **One global hotkey** — `Ctrl+Alt+A` works anywhere in Windows, even when ZCode is not focused
- 💬 **`/screenshot` command** — trigger capture from the conversation, change the hotkey, or check listener status
- 🪶 **Lightweight** — a tiny pure-Win32 listener is compiled on first use, starts lazily, and exits automatically when idle (default 30 min) or when ZCode exits
- 🛠️ **Zero runtime dependencies** — only Windows + .NET Framework 4 (preinstalled on all modern Windows)

### Requirements

| | |
|---|---|
| OS | Windows 10/11 |
| Runtime | .NET Framework 4.x (preinstalled), PowerShell 5+ |
| Host | ZCode desktop client (for auto-paste into the chat) |

### Installation

**Way 1 — Plugin Marketplace (recommended)**

The repository root contains a `marketplace.json`, so it can be added directly as a marketplace source:

1. Open ZCode → **Settings → Plugin Marketplace**.
2. Click **Add → Add Plugin Marketplace**.
3. Paste: `https://github.com/lebrontheg0at/zcode-screenshot`
4. Install **zcode-screenshot** from the marketplace list.
5. Restart ZCode (the plugin preheats its listener on session start).

**Way 2 — Manual install into `local-plugins`**

```bash
git clone https://github.com/lebrontheg0at/zcode-screenshot.git "%USERPROFILE%\.zcode\local-plugins\zcode-screenshot"
```

Then restart ZCode.

> 🤖 **Let your AI agent install it**: point the agent at [INSTALL-FOR-AI.md](INSTALL-FOR-AI.md) — it contains step-by-step instructions, verification checks, troubleshooting and uninstall steps written for automated execution.

### Usage

**Global hotkey (works anywhere in Windows, even when ZCode is not focused):**

| Hotkey | Behavior |
|---|---|
| `Ctrl + Alt + A` | Capture a screen region, then auto-paste the image into the current foreground window (usually the ZCode input box) |

After triggering: drag a rectangle over the region → release to capture; press `ESC` to cancel. The captured image is pasted into the previously focused window — add your text and press Enter to send.

**Slash command in the conversation:**

| Command | Effect |
|---|---|
| `/screenshot` | Trigger a region capture |
| `/screenshot 热键 Ctrl+Alt+S` | Change the global hotkey and hot-reload the listener |
| `/screenshot 状态` | Show listener status (running/PID or idle) and current hotkeys |

**Say it in natural language** — the bundled skill lets you just ask: *"take a screenshot"*, *"change the screenshot hotkey to Ctrl+Alt+S"*, *"is the listener running?"*

### Configuration

Everything lives in `%USERPROFILE%\.zcode\screenshot\config.json` (created on first run):

| Key | Default | Meaning |
|---|---|---|
| `hotkey` | `Ctrl+Alt+A` | Global capture hotkey |
| `idleMinutes` | `30` | Listener auto-exits after this many idle minutes |
| `autoInsert` | `true` | Auto-paste the captured image into the previous foreground window |

Hotkey names support `Ctrl`, `Alt`, `Shift`, `Win` + a letter, digit, or `F1`–`F24`. Editing `config.json` is picked up by the listener within ~200 ms (hot reload); you can also use `/screenshot 热键 …`.

### How it works

```
SessionStart hook ──► capture.ps1 启动 ──► compile capture.exe (first run only)
                                          └─► listener registers both global hotkeys
Hotkey / /screenshot ──► named-event trigger ──► fullscreen overlay, drag rectangle
                      ──► PNG saved to %USERPROFILE%\.zcode\screenshot\shots\
                      ──► image pasted back into the previously focused window
Idle for idleMinutes ──► listener exits (relaunched automatically on next use)
ZCode process gone for 60 s ──► listener exits
```

Single-instance is enforced by a mutex; a re-entry guard prevents stacked overlays; the overlay hides itself before the actual screen grab so it never appears in the picture.

### Repository layout

```
zcode-screenshot/
├── .zcode-plugin/plugin.json   # plugin manifest
├── marketplace.json            # makes this repo a one-click marketplace source
├── commands/screenshot.md      # /screenshot command
├── skills/screenshot/          # skill so agents can operate the tool
├── hooks/hooks.json            # SessionStart preheat
├── scripts/capture.ps1         # dispatcher: compile / start / trigger / hotkey / status
├── scripts/capture.cs          # listener source, pure Win32 + GDI+ (compiled on demand)
└── INSTALL-FOR-AI.md           # install guide written for AI agents
```

### License

[MIT](LICENSE)

---

<a id="中文"></a>

## 中文

一个为 [ZCode](https://zcode.ai) 打造的截图插件：按下全局热键，框选一块区域，截图就会**自动粘贴进 ZCode 输入框**——补充一句话、回车，图片就进了对话。

### 功能特性

- 🖼️ **框选截图自动粘贴**——截图以图片本体（而非文件路径）粘贴回截图前的窗口，即拍即发
- ⌨️ **全局热键**——`Ctrl+Alt+A`，系统级，ZCode 不在前台也能用
- 💬 **`/screenshot` 命令**——对话内触发截图、修改热键、查看监听器状态
- 🪶 **轻量**——监听器用纯 Win32 消息循环实现（无 WinForms），首次使用时自动编译，懒启动，空闲 30 分钟（可调）或 ZCode 退出后自动退出
- 🛠️ **零依赖**——只需 Windows + .NET Framework 4（现代 Windows 系统自带）

### 环境要求

| | |
|---|---|
| 操作系统 | Windows 10/11 |
| 运行时 | .NET Framework 4.x（系统自带）、PowerShell 5+ |
| 宿主 | ZCode 桌面客户端（用于自动粘贴进对话） |

### 安装

**方式一：插件市场（推荐）**

仓库根目录带有 `marketplace.json`，可直接作为市场源添加：

1. 打开 ZCode → **设置 → 插件市场**。
2. 点击 **添加 → 添加插件市场**。
3. 粘贴源地址：`https://github.com/lebrontheg0at/zcode-screenshot`
4. 在市场列表中安装 **zcode-screenshot**。
5. 重启 ZCode（插件会在会话启动时预热截图监听器）。

**方式二：手动放入 `local-plugins` 目录**

```bash
git clone https://github.com/lebrontheg0at/zcode-screenshot.git "%USERPROFILE%\.zcode\local-plugins\zcode-screenshot"
```

然后重启 ZCode。

> 🤖 **让 AI Agent 帮你装**：把 [INSTALL-FOR-AI.md](INSTALL-FOR-AI.md) 交给你的 Agent 即可——里面有为其编写的分步安装说明、安装后验证、故障排查与卸载步骤。

### 使用

**全局热键（系统级，ZCode 不在前台也能用）：**

| 热键 | 行为 |
|---|---|
| `Ctrl + Alt + A` | 框选截图，完成后图片自动粘贴回当前前台窗口（通常是 ZCode 输入框） |

触发后的操作：拖出矩形 → 松手完成截图；按 `ESC` 取消。截图完成后图片自动粘贴回之前的前台窗口，补充文字、回车即可发送。

**对话内命令：**

| 命令 | 效果 |
|---|---|
| `/screenshot` | 触发框选截图 |
| `/screenshot 热键 Ctrl+Alt+S` | 修改全局热键并热重载监听器，无需重启 |
| `/screenshot 状态` | 查看监听器状态（运行中/PID 或未运行）与当前热键 |

**自然语言也可以**——插件内置了技能，直接说"截个图"、"把截图快捷键改成 Ctrl+Alt+S"、"看下监听器状态"即可。

### 配置

所有配置保存在 `%USERPROFILE%\.zcode\screenshot\config.json`（首次运行自动创建）：

| 键 | 默认值 | 说明 |
|---|---|---|
| `hotkey` | `Ctrl+Alt+A` | 全局截图热键 |
| `idleMinutes` | `30` | 监听器空闲多少分钟后自动退出 |
| `autoInsert` | `true` | 截图完成后自动把图片粘贴回原前台窗口 |

热键格式支持 `Ctrl`、`Alt`、`Shift`、`Win` + 字母 / 数字 / `F1`–`F24`。直接编辑 `config.json` 后监听器约 200ms 内热重载生效，也可以用 `/screenshot 热键 …` 修改。

### 工作原理

```
SessionStart 钩子 ──► capture.ps1 启动 ──► 首次运行时编译 capture.exe
                                        └─► 监听器注册全局热键
热键 / /screenshot ──► 命名事件触发 ──► 全屏遮罩框选
                   ──► PNG 保存到 %USERPROFILE%\.zcode\screenshot\shots\
                   ──► 图片粘贴回之前的前台窗口
空闲超过 idleMinutes ──► 监听器自动退出（下次使用自动拉起）
ZCode 进程连续 60 秒不存在 ──► 监听器自动退出
```

互斥锁保证单实例；防重入保护避免叠加多层遮罩；真正截屏前遮罩会先隐藏自己，因此截图里永远不会出现遮罩。

### 目录结构

```
zcode-screenshot/
├── .zcode-plugin/plugin.json   # 插件清单
├── marketplace.json            # 让本仓库可作为市场源一键添加
├── commands/screenshot.md      # /screenshot 命令
├── skills/screenshot/          # 技能，供 Agent 操作截图工具
├── hooks/hooks.json            # SessionStart 预热
├── scripts/capture.ps1         # 调度脚本：编译/启动/触发/热键/状态
├── scripts/capture.cs          # 监听器源码，纯 Win32 + GDI+（按需编译）
└── INSTALL-FOR-AI.md           # 面向 AI Agent 的安装指南
```

### 许可证

[MIT](LICENSE)
