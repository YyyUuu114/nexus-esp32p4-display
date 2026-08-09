# Contributing

## Branch scope

- `firmware-stable`：已验证的 ESP32-P4 正式发布快照。
- `firmware-dev`：ESP32-P4 预发布开发快照。
- `desktop-stable`：已验证的 Windows 正式发布快照。
- `desktop-dev`：Windows 预发布开发快照。

变更应以对应开发分支为目标。正式分支仅接收通过发布检查的提升提交或经过验证的紧急修复。

## Required declaration

每个 Pull Request 必须声明端点、产品版本、发布通道、`updateClass`、线协议变化、是否需要更新另一端，以及验证证据。协议相关变更必须同步更新兼容规则、协议、发布元数据、更新清单和变更日志。

## Quality requirements

- 代码必须在 .NET 10 SDK 中以 `TreatWarningsAsErrors=true` 构建。
- 注释应说明约束、边界或系统行为，不记录讨论过程。
- 文档不得包含本机绝对路径、真实主机名、固定 COM 号、设备标识或凭据。
- 第三方依赖必须使用官方来源、固定版本和校验值，并记录许可证。
- 发布 ZIP 必须从隔离快照构建，提供 SHA-256，并与发布提交对应。
