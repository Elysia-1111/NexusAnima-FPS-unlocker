# Honkai: Nexus Anima FPS Unlocker

[简体中文](README.md)

PC games still locked to 60 FPS in 2026? A small tool for raising the frame-rate limit in the Windows version of **Honkai: Nexus Anima**, developed by **MissPinkElf**.

Set a custom limit of **1–240 FPS** (default: **240**). The tool automatically remembers your last frame rate and game path. After the game starts, it hides in the system tray and maintains your chosen frame rate in the background. Exiting the tool normally restores the game's original frame-rate limit, usually 60 FPS.

> **Before you begin (please read):**
>
> - **The game must be closed first:** Fully exit the game, then launch it through this tool. The tool cannot take over a game that is already running.
> - **Supported version:** Currently supports only the Windows version of Honkai: Nexus Anima **0.75.0 (the installation package for the third playtest)**. Updating the game or using a different installation package may cause a “Version mismatch” message. If this happens, wait for a compatible tool update.
> - **System requirements:** Verified on **64-bit Windows 11**; requires **.NET Framework 4.8.1**. If the tool shows an error or does not open, first [download .NET Framework 4.8.1 Runtime from Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net481) and install it. You do not need the Developer Pack.

## Quick start

1. **Download and extract**

   Open this repository's **[Releases](https://github.com/Elysia-1111/NexusAnima-FPS-unlocker/releases/latest)** page and download the latest attached package, **`NexusAnimaFPS-MissPinkElf-v1.2.zip`**. Extract the entire archive into a separate folder of your choice. It does not need to be inside the game folder.

2. **Launch the game**

   Run the extracted **`NexusAnimaFPS.exe`**, enter a target frame rate (a whole number from 1 to 240, such as `120`, `144` or `240`), and click **启动游戏 (Launch game)**.

   *If Windows asks for administrator permission, click “Yes.” The tool needs this permission to apply the frame-rate setting.*

3. **Keep the tool running in the background**

   After detecting that the game has started and applying the frame-rate setting, the tool automatically hides in the system tray, at the bottom right of the taskbar. Its icon may be inside the `^` hidden-icons menu. The disappearing window is normal; keep the tool running in the background.

## Select the game path manually

If the tool cannot automatically find the game:

1. Click the **gear icon** in the top-right corner of the main window.
2. Try **自动识别 (Auto-detect)** first. If the game is still not found, click **选择游戏文件 (Select game file)**.
3. Open the game's installation folder and select the game executable, **`NexusAnima.exe`**, usually inside a `games` or `NexusAnima Game` folder. **Do not** select a desktop shortcut, the game launcher, or this tool itself.
4. Once the status at the bottom confirms that the path is ready, close Settings. The path is saved automatically.

## System tray and exiting

| Action | Result |
| --- | --- |
| Successfully launch the game / Click Minimize | Hide the window in the system tray and continue maintaining the frame-rate setting in the background |
| Left-click the tray icon / Right-click → 打开工具 (Open tool) | Reopen the main window |
| Right-click the tray icon → 退出 (Exit) / Click `×` in the main window | Exit the tool and restore the game's original frame-rate limit without closing the game |

## Frequently asked questions

**Q: I set 240 FPS. Why isn't the game reaching it?**

The tool changes the frame-rate limit. Actual FPS depends on graphics card performance, graphics settings, V-Sync and any frame-rate limit set by the graphics driver. The number shown in the tool is your chosen target, not a live FPS reading.

**Q: Can I change the frame rate while the game is running?**

Not currently. The frame-rate field is locked while the game is running. To change it, first exit the game, then enter a new value and launch the game again.

**Q: What should I do if I see “游戏访问被拒绝” (Game access denied)?**

Make sure the game is fully closed. Reopen the tool and allow it to run as administrator when Windows asks for permission. If the error persists, hover over the status message at the bottom of the main window to see the specific reason.

**Q: What if the original frame-rate limit is not restored after I exit the tool?**

Exiting normally restores the original limit automatically. The tool also attempts to restore it after an unexpected exit. If the limit is still not restored, close the game normally and open it again to reset it.

## Updating and removing the tool

- **Update:** Exit the game first, then right-click the tray icon and exit the old tool. Extract the new ZIP into the existing folder and replace the old files. If you use a new folder, you will need to set the game path again. The tool does not update automatically.
- **Remove:** This is a portable app with no uninstaller required. Exit both the game and the tool normally, then delete the tool's entire folder.

## Reporting problems

If you encounter an error or a compatibility issue, please report it in this repository's **[Issues](https://github.com/Elysia-1111/NexusAnima-FPS-unlocker/issues)**. Include the **tool version, game version and Windows version**, along with a **screenshot of the error message**. Hide any personal information before sharing the screenshot.
