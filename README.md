# Desktop Dynamic Island · 桌面灵动岛

Native Windows performance monitoring in a compact desktop widget.

用轻量胶囊与展开面板查看电脑负载，并在持续越界时提醒。

**技术栈 / Stack:** C# · .NET 8 · WPF · Win32 · NVIDIA NVML

## 能做什么

- CPU、RAM、GPU、VRAM 与 GPU 温度，可信数据不可用时显示占位。
- 胶囊展开、拖拽定位、托盘控制、全屏和远程桌面场景抑制。
- 持续时间、恢复与冷却共同控制告警，避免重复弹出。

## 安装与运行

Windows 与 .NET 8 SDK。

```powershell
dotnet build DesktopDynamicIsland.sln -c Release
./src/DesktopDynamicIsland/bin/Release/net8.0-windows/DesktopDynamicIsland.exe
```

NVIDIA 指标需要可用的 NVML。用户设置首次启动时生成，不随源码公开。开机启动由托盘菜单手动开启；项目不会在构建时修改启动项。

## 验证

```powershell
dotnet test DesktopDynamicIsland.sln -c Release
```

硬件读数、DPI、多显示器、全屏与托盘交互仍需在目标 Windows 机器实际验证。

命令结果与验收范围见 [VALIDATION.md](VALIDATION.md)。这些检查不能替代目标机器上的原生桌面验收。

## 发布范围

[公开源码说明](PUBLICATION.md) · [全部作品](https://github.com/YUANYZ-cell)
