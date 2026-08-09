# Third-Party Notices

| 组件 | 固定版本 | 许可证 | 官方来源 | 用途 |
| --- | --- | --- | --- | --- |
| LibreHardwareMonitor | 0.9.6 | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6 | 硬件传感器读取 |
| Microsoft .NET Runtime | 10.x | MIT；包含第三方声明 | https://github.com/dotnet/runtime | 自包含 Windows 运行时 |
| System.IO.Ports | 与 .NET 10 运行时匹配 | MIT | https://github.com/dotnet/runtime | 串口通信 |
| System.Management | 与 .NET 10 运行时匹配 | MIT | https://github.com/dotnet/runtime | Windows PnP 设备发现 |

`tools/restore-lhm.ps1` 仅从 LibreHardwareMonitor 官方 v0.9.6 Release 下载 `LibreHardwareMonitor.NET.10.zip`，要求 SHA-256 为 `29739C4959B01B348FDDAD87664066634BCFD4F46E9BF41E4E916C318BCFDB99`。恢复后的 `vendor` 目录不进入 Git 历史。

LibreHardwareMonitor 的 MPL-2.0 文本位于 `licenses/LibreHardwareMonitor-MPL-2.0.txt`。Microsoft .NET 的许可与第三方声明见官方 [.NET license information](https://github.com/dotnet/core/blob/main/license-information.md)。发布包包含本文件与相关许可证，不改变上游组件的许可条款。
