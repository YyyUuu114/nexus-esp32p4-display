# NEXUS Display for Windows

[![简体中文](https://img.shields.io/badge/语言-简体中文-00d9ff)](README.md)
[![English](https://img.shields.io/badge/Language-English-6dff39)](README.en.md)

[![Development](https://img.shields.io/badge/development-v2.1.3-f3b61f)](release.json)
[![Protocol](https://img.shields.io/badge/protocol-2.0-6dff39)](COMPATIBILITY.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078d4)](docs/BUILD.md)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

NEXUS Display 是 ESP32-P4 主机状态副屏的 Windows 托盘采集器。应用以 1 Hz 读取 CPU、GPU、内存、网络、风扇、温度和功耗，通过 ESP32-P4 原生 USB Serial/JTAG 发送遥测，并累计本次进程运行期间的可用 CPU 与全部 GPU 能耗估算。

本分支是桌面端开发分支，当前版本为 **2.1.3**。它基于 v2.1.1 的协议 2.0 配对基线，将硬件采样、GPU 驱动查询和串口发送相互隔离，避免 CUDA 高负载拖停通信，分类为 `single-endpoint`；兼容固件 v2.1.1，无需更新固件。正式桌面端仍位于 [`desktop-stable`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/desktop-stable)。

## 解压后双击

1. 完整解压发布 ZIP。
2. 双击 `NEXUS Display\01-启动程序.cmd` 或 `Nexus Display.exe`。
3. 接受一次 UAC 后，程序安装到 `C:\Program Files\NEXUS Display\current` 并从受保护位置运行。
4. 需要开机自启时双击 `02-启用开机自启.cmd`；取消时双击 `03-取消开机自启.cmd`。安装不会自动启用自启。

发布包自带 .NET 运行时，目标电脑无需安装 .NET SDK。读取部分硬件传感器需要管理员权限。稳定生产环境应继续使用 [v1.1.0 正式 Release](https://github.com/YyyUuu114/nexus-esp32p4-display/releases/tag/v1.1.0)，直至协议 2.x 完成稳定验证。

## 连接与驱动

- 应用从 USB VID `303A`、PID `1001` 的 Espressif USB Serial/JTAG 设备解析动态 COM 号。
- 每次打开候选端口都执行协议 2.0 随机 nonce 双向握手，并检查产品标识、协议和双端版本范围；不再使用“系统只有一个串口”的回退。
- 运行通信走 ESP32-P4 原生 USB，不经过 CH340，因此无需 CH340 驱动。只有用户另接 CH340 转串口模块时才需要对应驱动。
- 换电脑、USB 插口或 COM 号不会影响识别；协议不兼容或非 NEXUS 串口会被关闭。

完整算法见 [docs/SERIAL_DISCOVERY.md](docs/SERIAL_DISCOVERY.md)，线协议见 [PROTOCOL.md](PROTOCOL.md)。

## 性能、日志与能耗

- 传感器和网络计数器每秒更新一次；串口发送使用独立的高优先级后台线程，断开时每 3 秒扫描一次候选设备，无忙等。
- GPU 驱动查询采用单任务隔离且不允许重叠；查询延迟不会阻塞 CPU、内存、网络采样或串口心跳，超过 5 秒的 GPU 数据不会继续冒充实时值。
- Windows 串口发送队列连续 3 秒无法排空时自动关闭并重新握手，避免仅托盘显示“正常”而设备端不再收包。
- 主 GPU 在进程启动时按稳定规则选定，避免显示值和能耗源随瞬时负载跳换。
- 能耗对 CPU 和所有 GPU 的有效非负功耗做梯形积分；断板不清零，应用重启才清零。
- 日志使用短单行记录，单文件 256 KiB，保留两份历史并清理 7 天前文件。
- 除用户主动检查更新外，程序不访问互联网。

## 签名与原子更新

“检查更新”只读取源码中固定的官方 HTTPS 地址。清单载荷必须通过内置 ECDSA P-256 公钥验签，下载地址必须属于本仓库 GitHub Release，ZIP 还需匹配签名载荷中的 SHA-256。更新辅助程序位于受保护目录，安全解压后以 `incoming → current` 和 `current → rollback` 交换；新版不能持续启动时恢复上一版。

该签名链提供项目级更新身份，不等同于 Windows 商业 Authenticode 信誉。发布脚本支持在维护者提供代码签名证书时附加 Authenticode 签名；当前开发资产若未签名，Windows 仍可能显示“未知发布者”。详见 [docs/UPDATE_FORMAT.md](docs/UPDATE_FORMAT.md) 与 [SECURITY.md](SECURITY.md)。

## 从源码构建

要求 .NET 10 SDK 和 PowerShell 7.4+：

```powershell
./tools/restore-lhm.ps1
dotnet build ./NexusDisplayAgent.csproj -c Release
dotnet run --project ./tests/NexusDisplay.Tests.csproj -c Release
./tools/build-release.ps1 -SigningKeyPath D:\secure\nexus-update-key.pem
```

私钥必须位于仓库外且限制访问；任何私钥都不得提交或打包。依赖与完整步骤见 [docs/BUILD.md](docs/BUILD.md)。

## 许可与安全

本项目自有源代码采用 Apache License 2.0。第三方来源与许可证见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本项目是个人开发者的学习与研究分享，不提供安全性或软硬件损坏保证；完整中英文风险声明和私密报告方式见 [SECURITY.md](SECURITY.md)。
