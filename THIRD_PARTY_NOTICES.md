# Third-Party Notices

本仓库不提交 ESP-IDF Component Manager 生成的 `managed_components` 目录。构建时按 `dependencies.lock` 和 `main/idf_component.yml` 获取以下上游组件。

| 组件 | 固定版本/范围 | 许可证 | 官方来源 | 用途 |
| --- | --- | --- | --- | --- |
| Espressif ESP-IDF | `>=5.5.0,<6.0.0`；v1.1.0 已验证 5.5.2 | Apache-2.0 | https://github.com/espressif/esp-idf | 工具链、驱动与运行时 |
| ESP32-P4 Function EV Board BSP | 5.2.3 | Apache-2.0 | https://github.com/espressif/esp-bsp | MIPI-DSI、背光与板级初始化 |
| LVGL | 9.5.0 | MIT | https://github.com/lvgl/lvgl | 嵌入式图形界面 |
| Montserrat Bold | 随仓库保留的字体文件 | SIL Open Font License 1.1 | https://github.com/JulietaUla/Montserrat | 数值字形生成 |
| Phosphor Icons Web | 2.1.2 | MIT | https://github.com/phosphor-icons/web | 图标字形生成 |

生成后的 LVGL 字体 C 文件保留字体来源和生成参数。Montserrat 许可证位于 `fonts/OFL-Montserrat.txt`，Phosphor 许可证位于 `fonts/LICENSE-Phosphor.txt`。上游组件可能包含其自身的传递依赖；相应许可证随 Component Manager 下载内容提供。
