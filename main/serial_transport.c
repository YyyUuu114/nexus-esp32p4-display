#include "serial_transport.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "driver/usb_serial_jtag.h"
#include "esp_check.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "telemetry.h"
#include "version.h"

#define NEXUS_LINE_MAX 1024

static const char *TAG = "nexus_serial";

static void serial_task(void *argument)
{
    (void)argument;
    uint8_t chunk[128];
    char line[NEXUS_LINE_MAX + 1];
    size_t used = 0;
    bool overflowed = false;

    const char ready[] =
        "NEXUS_READY fw=" NEXUS_FIRMWARE_VERSION
        " proto=" NEXUS_PROTOCOL_VERSION_STRING
        " transport=USB_SERIAL_JTAG\r\n";
    usb_serial_jtag_write_bytes(ready, sizeof(ready) - 1, pdMS_TO_TICKS(100));

    while (true) {
        const int count = usb_serial_jtag_read_bytes(chunk, sizeof(chunk), pdMS_TO_TICKS(250));
        for (int index = 0; index < count; ++index) {
            const char byte = (char)chunk[index];
            if (byte == '\r') {
                continue;
            }
            if (byte == '\n') {
                if (!overflowed && used > 0) {
                    line[used] = '\0';
                    if (!telemetry_parse_and_store(line, used)) {
                        ESP_LOGW(TAG, "Dropped invalid telemetry frame (%u bytes)", (unsigned)used);
                    }
                } else if (overflowed) {
                    ESP_LOGW(TAG, "Dropped oversized telemetry frame");
                }
                used = 0;
                overflowed = false;
                continue;
            }
            if (overflowed) {
                continue;
            }
            if (used >= NEXUS_LINE_MAX) {
                overflowed = true;
                continue;
            }
            line[used++] = byte;
        }
    }
}

esp_err_t serial_transport_start(void)
{
    usb_serial_jtag_driver_config_t config = {
        .tx_buffer_size = 1024,
        .rx_buffer_size = 4096,
    };

    ESP_RETURN_ON_ERROR(usb_serial_jtag_driver_install(&config), TAG, "USB Serial/JTAG driver install failed");

    BaseType_t created = xTaskCreate(serial_task, "nexus_serial", 4096, NULL, 5, NULL);
    if (created != pdPASS) {
        usb_serial_jtag_driver_uninstall();
        return ESP_ERR_NO_MEM;
    }
    return ESP_OK;
}
