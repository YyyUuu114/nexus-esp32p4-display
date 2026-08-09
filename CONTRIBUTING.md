# Contributing

## Branch scope

- `firmware-stable`：已验证的 ESP32-P4 正式发布快照。
- `firmware-dev`：ESP32-P4 预发布开发快照。
- `desktop-stable`：已验证的 Windows 正式发布快照。
- `desktop-dev`：Windows 预发布开发快照。

变更应以对应开发分支为目标。正式分支仅接收通过发布检查的提升提交或经过验证的紧急修复。

## Required change declaration

每个 Pull Request 必须声明：端点、产品版本、发布通道、`updateClass`、线协议变化、是否需要更新另一端，以及验证证据。协议相关变更必须同步更新 `COMPATIBILITY.md`、`compatibility.json`、`release.json`、`PROTOCOL.md` 和 `CHANGELOG.md`。

## Quality requirements

- 代码必须在目标工具链中无警告构建；固件启用 `-Wall -Wextra -Werror`。
- 注释应说明约束、边界或硬件依据，不记录讨论过程。
- 文档不得包含本机绝对路径、真实主机名、固定 COM 号、设备标识或凭据。
- 只能引用上游官方文档、官方仓库或规范；第三方代码、字体与二进制必须记录版本、来源和许可证。
- 发布二进制必须提供 SHA-256，并与相同提交的源代码对应。
