# NEXUS USB 遥测协议 2.0

ESP32-P4 原生 USB Serial/JTAG 在 Windows 中枚举为虚拟串口。桌面端以 `115200 8N1` 打开串口用于 API 兼容；运行链路不经过 CH340，也不依赖固定 COM 号。

每条消息必须是单个 UTF-8 JSON 对象，以 LF (`0x0A`) 结束；LF 前允许一个 CR。消息最大 1024 字节，对象后不得有尾随数据，消息内部不得有 NUL。

## 双向握手

桌面端对 USB VID `303A`、PID `1001` 的候选端口生成 16 字节密码学随机数，以 32 个十六进制字符发送挑战：

```json
{"type":"hello","product":"NEXUS_DESKTOP","desktop_version":"2.1.2","protocol_major":2,"protocol_revision":0,"minimum_firmware_version":"2.1.1","maximum_firmware_major_exclusive":3,"nonce":"0123456789abcdef0123456789abcdef"}
```

固件兼容时返回：

```json
{"type":"ready","product":"NEXUS_ESP32P4_DISPLAY","firmware_version":"2.1.1","protocol_major":2,"protocol_revision":0,"minimum_desktop_version":"2.1.1","maximum_desktop_major_exclusive":3,"nonce":"0123456789abcdef0123456789abcdef"}
```

桌面端必须验证产品标识、协议、双端版本范围和原样返回的 nonce。任何失败均关闭该端口；不得以唯一串口、可打开端口或历史 COM 号替代握手。固件在握手成功前不得接受遥测。

## 遥测帧

桌面端握手后立即发送首帧，此后默认每 1000 ms 发送：

```json
{
  "v": 2,
  "seq": 42,
  "session_nonce": "0123456789abcdef0123456789abcdef",
  "host": "WORKSTATION",
  "date": "2026-08-13",
  "clock": "21:48",
  "cpu_load": 37.0,
  "cpu_temp": 58.0,
  "cpu_power": 62.0,
  "gpu_load": 72.0,
  "gpu_temp": 68.0,
  "gpu_power": 186.0,
  "session_energy_kwh": 0.037412,
  "memory_used_gb": 12.4,
  "memory_total_gb": 32.0,
  "net_down_mbps": 48.2,
  "net_up_mbps": 6.8,
  "fan_rpm": 1240
}
```

| 字段 | 类型 | 单位/约束 | 必需性 |
| --- | --- | --- | --- |
| `v` | integer | 必须为 `2` | 必需 |
| `seq` | integer | `0..4294967295`；进程内单调递增 | 必需 |
| `session_nonce` | string | 当前会话的 32 字符十六进制 nonce | 必需 |
| `host` | string | 1 至 15 个可打印 ASCII 字符 | 可选 |
| `date` / `clock` | string | `yyyy-MM-dd` / `HH:mm` | 可选 |
| `cpu_load`, `gpu_load` | number/null | `0..100` % | 可选 |
| `cpu_temp`, `gpu_temp` | number/null | `0..250` °C | 可选 |
| `cpu_power`, `gpu_power` | number/null | `0..5000` W | 可选 |
| `session_energy_kwh` | number/null | `0..1000000` kWh | 可选 |
| `memory_used_gb`, `memory_total_gb` | number/null | `0..65536` GB，used 不大于 total | 可选 |
| `net_down_mbps`, `net_up_mbps` | number/null | `0..10000000` Mbit/s | 可选 |
| `fan_rpm` | number/null | `0..1000000` RPM | 可选 |

未知传感器发送 `null` 或省略，不得以零表示未知。固件忽略未知字段；错误类型、非有限数、越界值、非整数 `v`/`seq`、错误 nonce 或尾随内容会导致整帧被丢弃。

`session_energy_kwh` 对本次桌面进程生命周期内有效 CPU 功耗与所有 GPU 功耗样本做梯形积分。负数、非有限数、异常大值和超过 10 秒的挂起间隔不参与积分；数值只增不减。开发板断开不清零，应用重启时清零。该值不是整机交流输入电量。

状态、低功耗和版本升级规则见固件分支 [PROTOCOL.md](https://github.com/YyyUuu114/nexus-esp32p4-display/blob/firmware-dev/PROTOCOL.md) 与 [COMPATIBILITY.md](COMPATIBILITY.md)。
