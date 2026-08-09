# NEXUS USB 遥测协议 1.1

ESP32-P4 原生 USB Serial/JTAG 在 Windows 中枚举为虚拟串口。主机以 `115200 8N1` 打开串口用于 API 兼容；USB CDC 数据吞吐不由物理 UART 波特率决定。

每帧为一个 UTF-8 JSON 对象，以 LF (`0x0A`) 结束。包含终止符前的帧长不得超过 1024 字节。桌面端默认每 1000 ms 发送一帧。

```json
{
  "v": 1,
  "seq": 42,
  "ts_ms": 1786283280000,
  "host": "WORKSTATION",
  "date": "2026-08-09",
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
| `v` | integer | 线协议主版本，当前为 `1` | 必需 |
| `seq` | integer | 非负、单调递增，允许进程重启后归零 | 必需 |
| `ts_ms` | integer | Unix 时间戳，毫秒；固件 1.1 可忽略 | 可选 |
| `host` | string | 固件最多显示 15 个 ASCII 字符 | 可选 |
| `date` | string | 本地日期 `yyyy-MM-dd` | 可选 |
| `clock` | string | 本地时间 `HH:mm` | 可选 |
| `cpu_load`, `gpu_load` | number/null | 百分比，`0..100` | 可选 |
| `cpu_temp`, `gpu_temp` | number/null | 摄氏度，非负 | 可选 |
| `cpu_power`, `gpu_power` | number/null | 瓦，非负 | 可选 |
| `session_energy_kwh` | number/null | 千瓦时，非负 | 可选 |
| `memory_used_gb`, `memory_total_gb` | number/null | GB，非负 | 可选 |
| `net_down_mbps`, `net_up_mbps` | number/null | Mbit/s，非负 | 可选 |
| `fan_rpm` | number/null | RPM，非负 | 可选 |

传感器未知、不可访问或读数无效时必须发送 JSON `null`，不得以数值 `0` 代替未知状态。接收端必须忽略未知字段，并为缺失的可选字段采用未知状态。

`session_energy_kwh` 由桌面应用对当前进程生命周期内可用的 CPU 与 GPU 功耗读数积分得到。开发板或串口断开不清零；桌面应用进程重启时清零。该值不是外接功率计测得的整机交流输入能耗。
