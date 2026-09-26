---
description: 框选截图，路径自动填入输入框，补充文字回车后带进对话（支持自定义全局热键）
argument-hint: "[热键 Ctrl+Alt+S | 状态]"
---

用户请求截图工具操作。脚本位于本插件目录下的 `scripts/capture.ps1`（插件根目录可用环境变量 `${CLAUDE_PLUGIN_ROOT}` 表示；若未设置，默认位于 `%USERPROFILE%\.zcode\local-plugins\zcode-screenshot\scripts\capture.ps1`）。用 Bash 运行对应命令（参数 `$ARGUMENTS` 直接追加到脚本名后）：

1. 默认（无参数）：触发框选截图
   `powershell -NoProfile -ExecutionPolicy Bypass -File "${CLAUDE_PLUGIN_ROOT}/scripts/capture.ps1" $ARGUMENTS`
   - 截图完成后工具会自动把**图片本体**（非文字路径）粘贴回 ZCode 输入框，和拖图进对话框的效果一致。**此时不要主动读图**，只简短回复"截图已填入输入框，补充你想说的话后回车发送"。图片随用户消息直接进来，无需 Read。
   - 输出"截图超时或被取消" → 告知用户框选时按 ESC 可取消，重试即可。
   - 输出"编译失败" → 把完整输出贴给用户排查。
2. 参数以"热键"开头（如 `热键 Ctrl+Alt+S`）：运行同一脚本，输出"已把截图快捷键改为 X"后向用户确认。
3. 参数为"状态"：运行同一脚本，把监听器与热键状态报告给用户。
4. 参数为"校准"：先提醒用户"请把光标点进 ZCode 输入框"，确认后再运行同一脚本；输出"校准完成"即可。校准记录输入框粘贴位置，之后截图不再依赖键盘焦点。

用户也可以随时直接按全局热键（默认 Ctrl+Alt+A）框选截图，行为与命令触发完全一致（路径自动填入输入框）。热键截图也会记录到 `%USERPROFILE%\.zcode\screenshot\latest.txt`。
