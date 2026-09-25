# INSTALL-FOR-AI.md — 供 AI Agent 执行的安装指南

本文档面向 AI Agent（如 ZCode / Claude / Codex 等）。当用户要求"安装 zcode-screenshot 插件"时，请按本指南执行。

## 插件简介

- **名称**：`zcode-screenshot`
- **来源**：https://github.com/lebrontheg0at/zcode-screenshot
- **功能**：全局热键（默认 Ctrl+Alt+A）或 `/screenshot` 命令触发框选截图，截图完成后图片自动粘贴进 ZCode 输入框；支持自定义热键；监听器空闲自动退出。
- **要求**：Windows（依赖 PowerShell 与 .NET 编译的截图监听器）。
- **当前版本**：见 `.zcode-plugin/plugin.json` 中的 `version` 字段。

## 安装方式

三选一。**方式 A 最简单，优先使用。**

### 方式 A：通过插件市场安装（推荐）

仓库根目录带有 `marketplace.json`，可直接作为市场源添加。

**Agent 操作步骤：**

1. 打开 ZCode 客户端的 **设置 → 插件市场（Plugin Marketplace）**。
2. 点击 **添加（Add）→ 添加插件市场（Add Plugin Marketplace）**。
3. 源地址粘贴：`https://github.com/lebrontheg0at/zcode-screenshot`
4. 点击添加，等待市场刷新成功。
5. 在市场的插件列表中找到 **zcode-screenshot**，核对版本号与描述，点击 **安装（Install）**。
6. 若插件默认未启用，在 **设置 → 插件（Settings → Plugins）** 中确认其处于启用状态。
7. **重启 ZCode 或新开会话**（插件的 SessionStart 钩子需要在新会话中预热截图监听器）。

如果客户端 UI 无法自动化操作，把上述步骤转述给用户，让用户手动完成第 2–5 步，Agent 继续执行后续验证。

### 方式 B：手动放置到 local-plugins 目录

适合无法访问 UI 或想跳过市场机制的场景。

**Agent 操作步骤（Windows）：**

```bash
git clone https://github.com/lebrontheg0at/zcode-screenshot.git "%USERPROFILE%\.zcode\local-plugins\zcode-screenshot"
```

注意：若 `%USERPROFILE%\.zcode\local-plugins\zcode-screenshot` 已存在，先备份或改用方式 A，不要直接覆盖用户现有目录。

放置后重启 ZCode 或新开会话。

### 方式 C：克隆到任意目录后作为本地市场添加

```bash
git clone https://github.com/lebrontheg0at/zcode-screenshot.git "D:\somewhere\zcode-screenshot"
```

然后在 ZCode 的 **插件市场 → 添加 → 添加插件市场** 中粘贴该本地目录路径（目录根需含 `marketplace.json`），再从市场列表安装。

## 安装后验证

按顺序执行，任何一步失败参考下方"故障排查"：

1. **清单存在**：确认插件目录下有 `.zcode-plugin/plugin.json`，且 `name` 为 `zcode-screenshot`。
2. **触发截图**：在 ZCode 对话中发送 `/screenshot`，或运行：

   ```bash
   powershell -NoProfile -ExecutionPolicy Bypass -File "${CLAUDE_PLUGIN_ROOT}/scripts/capture.ps1"
   ```

   预期：屏幕出现半透明遮罩，拖出矩形松手即完成截图，图片自动粘贴回 ZCode 输入框。按 ESC 取消属正常行为（输出"截图超时或被取消"）。
3. **数据目录生成**：确认 `%USERPROFILE%\.zcode\screenshot\` 下生成了 `config.json`、`shots\`、`latest.txt`。
4. **热键可用**：按下全局热键（默认 Ctrl+Alt+A）应同样触发框选截图。

## 自定义

- **改热键**：运行 `capture.ps1 热键 Ctrl+Alt+S`（任意组合键），或直接编辑 `%USERPROFILE%\.zcode\screenshot\config.json`。
- **改空闲退出时间**：编辑 `config.json` 的 `idleMinutes`（默认 30 分钟）。
- **查看监听器状态**：运行 `capture.ps1 状态`。

## 故障排查

| 症状 | 处理 |
|---|---|
| `/screenshot` 命令不存在 | 插件未安装或未启用；检查"设置 → 插件"，并确认安装源正确 |
| 触发后无反应 | 运行 `capture.ps1 状态` 查看监听器；确认是 Windows 系统且 PowerShell 可用 |
| 输出"编译失败" | 首次运行需要 .NET 编译器；把完整输出反馈给用户排查 |
| 截图了但没填入输入框 | 检查 `config.json` 中 `autoInsert` 是否为 true；截图路径可在 `%USERPROFILE%\.zcode\screenshot\latest.txt` 中找到，可手动拖入对话 |
| 脚本路径找不到 | 插件根目录以实际安装缓存为准（`${CLAUDE_PLUGIN_ROOT}`）；local-plugins 安装则为 `%USERPROFILE%\.zcode\local-plugins\zcode-screenshot` |

## 卸载

- 方式 A/C 安装的：**设置 → 插件** 中卸载；市场源可在市场设置中移除。
- 方式 B 安装的：删除 `%USERPROFILE%\.zcode\local-plugins\zcode-screenshot` 目录，并删除数据目录 `%USERPROFILE%\.zcode\screenshot\`。
