# NEXUS ESP32-P4 Display Firmware

[![简体中文](https://img.shields.io/badge/语言-简体中文-00d9ff)](README.md)
[![English](https://img.shields.io/badge/Language-English-6dff39)](README.en.md)

[![Development](https://img.shields.io/badge/development-v1.2.1-f3b61f)](release.json)
[![Protocol](https://img.shields.io/badge/protocol-1.1-6dff39)](COMPATIBILITY.md)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

NEXUS 将 ESP32-P4-Function-EV-Board 与 1024 × 600 MIPI-DSI 显示套件配置为 Windows 主机遥测副屏。固件通过 ESP32-P4 原生 USB Serial/JTAG 接口接收逐行 JSON，在 LVGL 中显示 CPU、GPU、内存、网络、风扇、温度、功耗和本次桌面应用运行期间的累计能耗。

本分支为固件开发分支，当前版本为 **1.2.1**，不作为正式部署基线。正式固件位于 [`firmware-stable`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/firmware-stable)，对应的 Windows 开发分支位于 [`desktop-dev`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/desktop-dev)。

v1.2.1 仅建立下一发布线的开发通道，线协议保持 1.1，更新分类为 `single-endpoint`。它可继续与桌面端 v1.1.0 通信，不要求同步更新桌面端。

## 运行特性

- 1 Hz 主机遥测刷新，曲线保留最近 60 个样本。
- 3.5 秒无合法数据进入 `WAIT`，15 秒无合法数据关闭背光并暂停数据区刷新；收到下一帧后立即恢复。
- 电脑端串口断开不清零会话能耗；桌面应用重启时重新累计。
- 未使用 Wi-Fi/Bluetooth 时保持板载 ESP32-C6 复位，并关闭未使用的扬声器功放使能。
- 传感器缺失使用 `null` 表示，界面显示 `--`，不以零值替代未知数据。
- USB 连接使用 ESP32-P4 原生 USB Serial/JTAG，不依赖 CH340 驱动，也不依赖固定 COM 号。

## 硬件连接

固件针对 ESP32-P4-Function-EV-Board v1.4/v1.5 与 EK79007 1024 × 600 显示套件配置。断电后按下表连接：

| LCD 转接板 | ESP32-P4-Function-EV-Board |
| --- | --- |
| J3 | MIPI-DSI 连接器，排线反向插接 |
| J6 `RST_LCD` | J1 `GPIO27` |
| J6 `PWM` | J1 `GPIO26` |
| J6 `5V` / `GND` | J1 `5V` / `GND`，或使用 LCD 转接板 J1 USB 独立供电 |

详细机械安装、供电边界和检查步骤见 [docs/HARDWARE.md](docs/HARDWARE.md)。连接依据为乐鑫官方 [ESP32-P4-Function-EV-Board v1.4 用户指南](https://docs.espressif.com/projects/esp-dev-kits/en/latest/esp32p4/esp32-p4-function-ev-board/user_guide_v1.4.html)。

## 构建

要求：ESP-IDF 5.5.x、Python 与 ESP-IDF Component Manager。依赖锁文件固定板级 BSP 5.2.3 和 LVGL 9.5.0。

```powershell
idf.py set-target esp32p4
idf.py build
```

烧录命令与预编译二进制偏移见 [docs/FLASHING.md](docs/FLASHING.md)。正式发布资产可从 [GitHub Releases](https://github.com/YyyUuu114/nexus-esp32p4-display/releases) 下载。

## 版本与兼容性

产品版本采用 `MAJOR.LINE.CHANNEL`：正式版 `CHANNEL=0`，开发版 `CHANNEL=1..9`。通信协议另以 `WIRE_MAJOR.WIRE_REVISION` 管理。是否必须同步更新另一端由每次发布的 `release.json` 明确声明，不能仅根据产品版本号推断。

完整判定规则见 [COMPATIBILITY.md](COMPATIBILITY.md)，机器可读规则见 [compatibility.json](compatibility.json)，当前发布元数据见 [release.json](release.json)。v1.1.0 是首个成对基线，首次部署必须同时使用固件 v1.1.0 与桌面端 v1.1.0。

## 许可与安全

本项目自有源代码采用 Apache License 2.0。字体、BSP、LVGL 等依赖的许可与固定版本见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本项目是个人开发者的学习与研究分享，不提供安全性或软硬件损坏保证；完整风险声明和私密报告方式见 [SECURITY.md](SECURITY.md)。
