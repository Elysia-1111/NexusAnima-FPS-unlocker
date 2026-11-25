# v1.2 · 修复游戏内帧率解锁失效

**崩坏：因缘精灵 FPS 解锁工具** · 开发者：**MissPinkElf**

## 更新内容

- 修复启动游戏一段时间后解锁停止、帧率可能回落的问题。
- 改善退出工具时的帧率恢复，并增加工具异常退出后的自动恢复。
- 保留 **1–240 FPS 任意整数**、游戏路径自动识别、自动收起至托盘及右键退出功能。

本次已在游戏中连续验证 3 分钟：维持 240 FPS 上限，结束后成功恢复到原来的 60 FPS。实际帧数仍受电脑性能和游戏画面影响。

## 下载与使用

1. 在本页下方 **Assets** 中下载 **`NexusAnimaFPS-MissPinkElf-v1.2.zip`** 并完整解压。
2. 正常退出游戏和旧版工具，将新版文件覆盖到原工具文件夹。
3. 运行 **`NexusAnimaFPS.exe`**，输入 `1–240` 之间的整数（默认 `240`），点击 **启动游戏**，并在 Windows 提示时允许管理员运行。

> 详细路径设置与常见问题请参阅仓库主页的 [README](https://github.com/Elysia-1111/NexusAnima-FPS-unlocker/blob/main/README.md)。

## 兼容性提醒

- **适配版本**：仅支持《崩坏：因缘精灵》Windows 版 **0.75.0（本次适配的安装版本）**。游戏更新或使用其他安装包可能会提示“版本不匹配”。
- **运行依赖**：已在 **Windows 11（64 位）** 验证，需要 **.NET Framework 4.8.1**（缺失可前往 [微软官网下载 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net481)）。
