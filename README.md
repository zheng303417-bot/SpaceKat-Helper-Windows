# SpaceKat 小助手

SpaceKat Mini II 和 Mini II Lite 的 Windows 上位机。支持 USB 和蓝牙连接，可查看六轴输入、测试四个物理按键，并将设备映射为鼠标或虚拟 Xbox 手柄。

## 功能

- **设备测试**。实时显示 X、Y、Z、Rx、Ry、Rz 和四键状态，通过三维立方体观察平移与旋转。蓝牙设备上报电量时，顶部会显示百分比。
- **鼠标映射**。X/Y 移动光标，Z 控制滚轮，上滚和下滚速度可独立设置。四个物理键可映射为单击、双击、侧键、滚轮或左键锁定。
- **摇杆模拟**。任选两轴控制右摇杆，Z 控制 LT/RT，四个物理键可分别映射为 A、B、X、Y。支持本页预览及虚拟手柄输出。
- **设置**。可管理当前 Windows 账号的自启动、主题色和配置。导入配置前会备份当前设置。

## 使用便携包

在 [Releases](https://github.com/zheng303417-bot/SpaceKat-Helper-Windows/releases) 下载 Windows 便携包。解压后运行「启动上位机.cmd」或同目录的 `SpaceKat小助手-1.16.exe`。首次运行会载入内置配置。无需设备时，可以运行「演示模式.cmd」查看界面。

适用 Windows 10/11 和 .NET Framework 4.8。系统虚拟手柄输出需要电脑上已有可用的 ViGEmBus。左侧「停止并释放」与 `Ctrl+Alt+F8` 可停止当前模拟。

## 从源码构建

在 Windows PowerShell 中进入仓库目录，运行下面的命令。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

程序生成在仓库根目录。构建脚本使用 Windows 自带的 .NET Framework 编译器，并嵌入图标和 `src/Defaults` 内的默认配置。

完整操作和排查步骤见[使用说明](使用说明.md)。虚拟手柄客户端的版本、校验值与许可见[依赖说明](native/SOURCES.md)。硬件资料见[SpaceKat Mini II 项目](https://github.com/Cityforest2000/SpaceKat-V3-Mini2)。
