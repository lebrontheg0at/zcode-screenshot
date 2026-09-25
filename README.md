# zcode-screenshot

一个 [ZCode](https://zcode.ai) 插件：全局热键或 `/screenshot` 命令触发框选截图，图片自动粘贴进对话。会话启动时预热监听器，空闲时自动退出。

## 功能

- 🖼️ **框选截图**：通过全局热键或 `/screenshot` 命令触发，截图路径自动填入 ZCode 输入框，补充文字后发送即可
- ⌨️ **自定义热键**：支持修改全局截图快捷键
- 🔄 **自动预热**：会话启动时预热监听器，空闲自动退出，兼顾响应速度与资源占用

## 目录结构

```
zcode-screenshot/
├── .zcode-plugin/   # 插件清单 (plugin.json)
├── commands/        # 斜杠命令
├── hooks/           # 生命周期钩子
├── scripts/         # 脚本
└── skills/          # 技能
```

## 安装

将本插件目录添加为本地插件（local-plugins），或放入自定义 marketplace 后在 ZCode 中安装。

## 使用

- 在 ZCode 对话中输入 `/screenshot`，或使用全局热键触发框选截图
- 截图完成后路径会自动填入输入框，补充说明后发送

## 许可证

[MIT](LICENSE)
