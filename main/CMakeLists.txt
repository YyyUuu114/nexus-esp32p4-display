idf_component_register(
    SRCS
        "main.c"
        "dashboard_ui.c"
        "serial_transport.c"
        "telemetry.c"
        "protocol_validation.c"
        "json_validation.c"
        "fonts/lv_font_nexus_digits_80.c"
        "fonts/lv_font_nexus_icons_44.c"
    INCLUDE_DIRS "."
    REQUIRES
        esp32_p4_function_ev_board
        esp_driver_gpio
        esp_driver_usb_serial_jtag
        esp_timer
        json
)

target_compile_options(${COMPONENT_LIB} PRIVATE -Wall -Wextra -Werror)
