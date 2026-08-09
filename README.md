# NEXUS Display for Windows

[![Release](https://img.shields.io/badge/release-v1.1.0-00d9ff)](https://github.com/YyyUuu114/nexus-esp32p4-display/releases/tag/v1.1.0)
[![Protocol](https://img.shields.io/badge/protocol-1.1-6dff39)](COMPATIBILITY.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078d4)](docs/BUILD.md)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

NEXUS Display 是 ESP32-P4 主机状态副屏的 Windows 托盘采集器。应用读取 CPU、GPU、内存、网络、风扇、温度和功耗，以 1 Hz 通过 ESP32-P4 原生 USB Serial/JTAG 发送遥测，并累计本次应用运行期间的 CPU 与 GPU 可用功耗。

本分支为桌面端正式发布分支，当前版本为 **1.1.0**。配套固件源代码位于 [`firmware-stable`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/firmware-stable)；开发分支分别为 [`desktop-dev`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/desktop-dev) 与 [`firmware-dev`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/firmware-dev)。

## 最短使用路径

1. 从 [v1.1.0 Release](https://github.com/YyyUuu114/nexus-esp32p4-display/releases/tag/v1.1.0) 下载 `NEXUS-Display-Windows-x64.zip`。
2. 完整解压 ZIP；不要直接在压缩软件内运行。
3. 双击 `NEXUS Display\01-启动程序.cmd` 或 `Nexus Display.exe`。
4. 需要登录后自动运行时，双击 `02-启用开机自启.cmd`；取消时双击 `03-取消开机自启.cmd`。

发布包自带 .NET 运行时，不要求目标电脑安装 .NET SDK。读取部分主板传感器需要管理员权限，因此手动启动时会出现 Windows UAC 提示。

## 连接与驱动

- 应用优先通过 USB VID `303A`、PID `1001` 识别 Espressif USB Serial/JTAG 设备，然后读取其当前 COM 号。
- COM 号由 Windows 动态分配，应用不写死端口号；换电脑、换 USB 插口或重新枚举不影响自动发现。
- 该数据通道是 ESP32-P4 原生 USB，不使用 CH340，因此无需 CH340 驱动。仅在用户自行改用外置 CH340 转串口模块时才需要相应驱动。
- 当系统只存在一个串口时，应用可将其作为兼容性回退；存在多个非目标串口时不会任意连接。

完整选择算法和限制见 [docs/SERIAL_DISCOVERY.md](docs/SERIAL_DISCOVERY.md)。

## 资源开销设计

- 传感器和网络计数器每秒更新一次，不进行高频轮询。
- 串口断开时每 3 秒扫描一次；异常重连采用 2.5 秒退避。
- 除用户主动检查更新外，应用不建立互联网连接。
- 日志为短单行格式，单文件达到 256 KiB 自动轮转，仅保留两份历史文件，并删除 14 天以前的日志。
- 应用使用单实例互斥量，重复双击不会创建多个采集进程。

设计边界见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

## 更新与版本兼容

托盘菜单的“检查更新”读取 HTTPS 清单、验证发布通道和 SHA-256，再执行覆盖与重启。正式版只读取 `stable` 清单；开发版只读取 `development` 清单，从而满足正式版 `.0` 与开发版 `.1..9` 的自定义版本规则。

产品与线协议规则见 [COMPATIBILITY.md](COMPATIBILITY.md)。当前 v1.1.0 是首个成对基线，首次部署应同时使用桌面端 v1.1.0 与固件 v1.1.0。破坏性协同更新会在更新确认框明确提示所需固件版本。

## 从源码构建

要求 .NET 10 SDK 和 PowerShell 7.4 或更高版本：

```powershell
./tools/restore-lhm.ps1
./tools/build-release.ps1
```

依赖恢复脚本只下载 LibreHardwareMonitor 官方 v0.9.6 资产并校验固定 SHA-256。完整说明见 [docs/BUILD.md](docs/BUILD.md)。

## 许可与安全

本项目自有源代码采用 Apache License 2.0。LibreHardwareMonitor、.NET 与其他依赖的版本、来源和许可证见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。安全问题请按 [SECURITY.md](SECURITY.md) 私密报告。
