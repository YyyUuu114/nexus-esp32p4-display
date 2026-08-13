# NEXUS USB 遥测协议 2.0

本文定义 ESP32-P4 固件与 Windows 桌面端之间的规范性线协议。关键词“必须”“不得”“应”用于描述互操作要求。

## 1. 传输层

运行中的固件通过 ESP32-P4 原生 USB Serial/JTAG 枚举为 Windows 虚拟串口。桌面端以 `115200 8N1` 打开串口用于 API 兼容；USB CDC 实际吞吐不由物理 UART 波特率决定。该运行时链路不经过 CH340，也不依赖固定 COM 号。

每条消息必须是单个 UTF-8 JSON 对象，以 LF (`0x0A`) 结束；LF 前允许一个 CR (`0x0D`)。不含终止符的消息最大为 1024 字节。对象后除行结束符外不得包含尾随字符，消息内部不得包含 NUL。

## 2. 双向握手

桌面端打开候选串口后生成 16 字节密码学随机数，并以 32 个十六进制字符编码为 nonce。它必须先发送：

```json
{"type":"hello","product":"NEXUS_DESKTOP","desktop_version":"2.1.1","protocol_major":2,"protocol_revision":0,"minimum_firmware_version":"2.1.1","maximum_firmware_major_exclusive":3,"nonce":"0123456789abcdef0123456789abcdef"}
```

固件只有在产品标识、产品版本范围、协议版本和 nonce 均合法时才返回：

```json
{"type":"ready","product":"NEXUS_ESP32P4_DISPLAY","firmware_version":"2.1.1","protocol_major":2,"protocol_revision":0,"minimum_desktop_version":"2.1.1","maximum_desktop_major_exclusive":3,"nonce":"0123456789abcdef0123456789abcdef"}
```

桌面端必须逐项验证响应，并要求返回 nonce 与本次挑战完全一致。超时、非 JSON 响应、`error` 响应、产品不符、范围不交集或 nonce 不符均表示该端口不是受支持的 NEXUS 设备；桌面端必须关闭端口并继续扫描。不得以“仅有一个串口”“端口曾经成功”或“能够打开”为替代判断。

固件在握手成功前不得接受遥测。新的 `hello` 会替换现有会话；后续遥测必须携带该会话 nonce。

## 3. 遥测帧

桌面端在握手成功后立即发送一帧，此后默认每 1000 ms 发送一帧：

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
| `v` | integer | 协议主版本，必须为 `2` | 必需 |
| `seq` | integer | `0..4294967295`；进程内单调递增，重启可归零 | 必需 |
| `session_nonce` | string | 当前握手的 32 字符十六进制 nonce | 必需 |
| `host` | string | 1 至 15 个可打印 ASCII 字符 | 可选 |
| `date` | string | 有效本地日期 `yyyy-MM-dd` | 可选 |
| `clock` | string | 有效本地时间 `HH:mm` | 可选 |
| `cpu_load`, `gpu_load` | number/null | `0..100` % | 可选 |
| `cpu_temp`, `gpu_temp` | number/null | `0..250` °C | 可选 |
| `cpu_power`, `gpu_power` | number/null | `0..5000` W | 可选 |
| `session_energy_kwh` | number/null | `0..1000000` kWh | 可选 |
| `memory_used_gb`, `memory_total_gb` | number/null | `0..65536` GB，且 used 不得大于 total | 可选 |
| `net_down_mbps`, `net_up_mbps` | number/null | `0..10000000` Mbit/s | 可选 |
| `fan_rpm` | number/null | `0..1000000` RPM | 可选 |

未知或不可访问的传感器必须发送 JSON `null` 或省略对应可选字段，不得用 `0` 表示未知。接收端必须忽略未知字段。出现重复字段、错误类型、非有限数、越界值、非整数 `v`/`seq`、非递增 `seq`、错误 nonce 或尾随内容时，固件丢弃整帧并保留上一份有效状态。

`session_energy_kwh` 是桌面应用对当前进程生命周期内有效 CPU 与 GPU 功耗样本进行积分的估算值。串口或开发板断开不清零；桌面应用进程重启时清零。它不是整机交流输入电量，也不能替代计费电表。

## 4. 状态与低功耗

- 连续 3.5 秒未收到合法遥测帧时，界面显示 `WAIT`。
- 连续 15 秒没有握手或合法遥测活动时，固件关闭背光并停止 LVGL、触摸与 MIPI-DSI 显示子系统；USB Serial/JTAG 接收任务保持运行。
- 新握手会唤醒显示，第一帧合法遥测使状态进入 `LIVE`。
- 固件允许动态降频，但不得启用会中断原生 USB 可连接性的自动 light sleep。

版本与升级分类见 [COMPATIBILITY.md](COMPATIBILITY.md)。协议 2.0 改变了身份确认和会话语义，不能与协议 1.x 混用。
