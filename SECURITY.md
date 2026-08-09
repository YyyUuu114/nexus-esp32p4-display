# 安全政策 / Security Policy

[![简体中文](https://img.shields.io/badge/语言-简体中文-00d9ff)](#简体中文)
[![English](https://img.shields.io/badge/Language-English-6dff39)](#english)

## 简体中文

### 支持版本

仅 `firmware-stable` 与 `desktop-stable` 的最新正式版本接收安全修复。开发分支仅用于预发布验证，不提供稳定性、安全性或响应时限承诺。

### 项目性质与风险声明

本项目由个人开发者以学习、研究和技术交流为目的公开分享。它不是经过独立安全审计或认证的商业产品，也不适用于安全关键、生命支持、工业控制或其他要求故障保障的场景。

源代码、固件、可执行文件、接线与供电说明以及更新机制均按“现状”提供，不作任何明示或默示的安全性、可靠性、适销性、特定用途适用性或不侵权保证。使用者应在操作前自行审查、备份和验证，并自行承担刷写固件、接线、供电、驱动、管理员权限、自动启动、联网更新和第三方依赖带来的风险，包括但不限于漏洞利用、数据丢失、系统异常、设备失效以及软件或硬件损坏。

在适用法律允许的最大范围内，维护者不对使用或无法使用本项目所产生的任何直接、间接、附带、特殊或后果性损失承担责任。本声明不排除或限制适用法律规定不得排除或限制的责任，并与 [Apache License 2.0](LICENSE) 第 7、8 条一并适用。

### 漏洞报告

请通过 GitHub Security Advisory 的私密报告功能提交潜在漏洞。报告应包含受影响版本、复现条件、实际影响与最小复现材料。请勿在公开 Issue、日志或截图中提交访问令牌、用户名、主机名、串口设备实例路径、硬件序列号或其他可识别信息。

公开披露前应为维护者保留合理的确认与修复窗口。依赖组件漏洞应同时注明上游项目与受影响版本。

## English

### Supported versions

Only the latest stable releases on `firmware-stable` and `desktop-stable` receive security fixes. Development branches are intended solely for prerelease validation and carry no commitment regarding stability, security, or response time.

### Project scope and risk notice

This project is publicly shared by an individual developer for learning, research, and technical exchange. It is not an independently security-audited or certified commercial product and is not intended for safety-critical, life-support, industrial-control, or other fault-guaranteed environments.

The source code, firmware, executables, wiring and power instructions, and update mechanisms are provided on an “AS IS” basis, without express or implied warranties of security, reliability, merchantability, fitness for a particular purpose, or non-infringement. Users are responsible for reviewing, backing up, and validating their environment before use and assume the risks associated with firmware flashing, wiring, power delivery, drivers, administrator privileges, automatic startup, network updates, and third-party dependencies, including vulnerability exposure, data loss, system malfunction, device failure, and software or hardware damage.

To the maximum extent permitted by applicable law, the maintainer is not liable for direct, indirect, incidental, special, or consequential loss arising from use of, or inability to use, this project. This notice does not exclude or limit liability that cannot lawfully be excluded or limited and applies together with Sections 7 and 8 of the [Apache License 2.0](LICENSE).

### Reporting vulnerabilities

Use GitHub Security Advisories to report potential vulnerabilities privately. Include the affected version, reproduction conditions, observed impact, and a minimal reproducer. Do not place access tokens, user names, host names, serial-device instance paths, hardware serial numbers, or other identifying information in public issues, logs, or screenshots.

Allow a reasonable confirmation and remediation period before public disclosure. For dependency vulnerabilities, identify the upstream project and affected version.
