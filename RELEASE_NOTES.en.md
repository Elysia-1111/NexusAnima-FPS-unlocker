# v1.2 · Fix for FPS unlocking stopping during gameplay

**Honkai: Nexus Anima FPS Unlocker** · Developer: **MissPinkElf**

## What's changed

- Fixed an issue where FPS unlocking stopped some time after launch and the frame rate could fall back.
- Improved frame-rate restoration when closing the tool and added automatic restoration after an unexpected tool exit.
- Retained support for **any whole-number limit from 1–240 FPS**, automatic game-path detection, automatic hiding in the system tray, and right-click Exit.

Verified in a continuous three-minute game session: the 240 FPS limit remained active, then the original 60 FPS limit was restored successfully. Actual FPS still depends on PC performance and the scene being rendered.

## Download and use

1. Download **`NexusAnimaFPS-MissPinkElf-v1.2.zip`** from **Assets** below and extract the entire archive.
2. Exit both the game and the old tool normally, then replace the files in the tool's existing folder.
3. Run **`NexusAnimaFPS.exe`**, enter a whole number from `1–240` (default: `240`), click **启动游戏 (Launch game)**, and allow administrator permission when prompted.

> For detailed game path setup and frequently asked questions, see the [README](https://github.com/Elysia-1111/NexusAnima-FPS-unlocker/blob/main/README.en.md) on the repository's home page.

## Compatibility notes

- **Supported version:** Only the Windows version of Honkai: Nexus Anima **0.75.0 (the installation package supported by this release)**. Updating the game or using another installation package may cause a “Version mismatch” message.
- **Runtime requirement:** Verified on **64-bit Windows 11**. Requires **.NET Framework 4.8.1**. If it is missing, [download the Runtime from Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net481).
