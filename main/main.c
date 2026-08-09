#include "dashboard_ui.h"
#include "serial_transport.h"
#include "telemetry.h"
#include "version.h"

#include "bsp/esp-bsp.h"
#include "driver/gpio.h"
#include "esp_check.h"
#include "esp_log.h"

static const char *TAG = "nexus";

/* ESP32-P4-Function-EV-Board v1.4/v1.5: P4 GPIO54 drives the on-board
 * ESP32-C6 EN/RST line. This project does not use Wi-Fi/Bluetooth, so keep the
 * co-processor in reset. GPIO53 is the unused speaker power-amplifier enable. */
#define WIFI_COPROCESSOR_RESET_GPIO GPIO_NUM_54

static void disable_unused_board_peripherals(void)
{
    const gpio_config_t config = {
        .pin_bit_mask = (1ULL << WIFI_COPROCESSOR_RESET_GPIO) | (1ULL << BSP_POWER_AMP_IO),
        .mode = GPIO_MODE_OUTPUT,
        .pull_up_en = GPIO_PULLUP_DISABLE,
        .pull_down_en = GPIO_PULLDOWN_ENABLE,
        .intr_type = GPIO_INTR_DISABLE,
    };
    ESP_ERROR_CHECK(gpio_config(&config));
    ESP_ERROR_CHECK(gpio_set_level(WIFI_COPROCESSOR_RESET_GPIO, 0));
    ESP_ERROR_CHECK(gpio_set_level(BSP_POWER_AMP_IO, 0));
    ESP_LOGI(TAG, "Unused ESP32-C6 and speaker amplifier disabled");
}

void app_main(void)
{
    ESP_LOGI(TAG, "Starting NEXUS firmware %s (protocol %s)",
             NEXUS_FIRMWARE_VERSION, NEXUS_PROTOCOL_VERSION_STRING);
    disable_unused_board_peripherals();
    telemetry_init();

    lv_display_t *display = bsp_display_start();
    if (display == NULL) {
        ESP_LOGE(TAG, "Display initialization failed");
        return;
    }

    if (bsp_display_lock(0)) {
        dashboard_ui_init(display);
        bsp_display_unlock();
    }
    ESP_ERROR_CHECK(bsp_display_brightness_set(88));
    ESP_ERROR_CHECK(serial_transport_start());
    ESP_LOGI(TAG, "NEXUS is ready");
}
