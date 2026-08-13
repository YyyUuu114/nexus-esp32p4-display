#include "dashboard_ui.h"
#include "serial_transport.h"
#include "telemetry.h"
#include "version.h"

#include "bsp/esp-bsp.h"
#include "driver/gpio.h"
#include "esp_check.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char *TAG = "nexus";

/* ESP32-P4-Function-EV-Board v1.4/v1.5: P4 GPIO54 drives the on-board
 * ESP32-C6 EN/RST line. This project does not use Wi-Fi/Bluetooth, so keep the
 * co-processor in reset. GPIO53 is the unused speaker power-amplifier enable. */
#define WIFI_COPROCESSOR_RESET_GPIO GPIO_NUM_54
#define ACTIVE_BRIGHTNESS_PERCENT 88
#define DISPLAY_RETRY_MS 2000

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

static lv_display_t *start_display(void)
{
    lv_display_t *display = bsp_display_start();
    if (display == NULL) {
        ESP_LOGE(TAG, "Display initialization failed");
        return NULL;
    }

    if (!bsp_display_lock(1000)) {
        ESP_LOGE(TAG, "Could not lock display during UI initialization");
        bsp_display_stop(display);
        return NULL;
    }
    dashboard_ui_init(display);
    bsp_display_unlock();

    const esp_err_t brightness_result = bsp_display_brightness_set(ACTIVE_BRIGHTNESS_PERCENT);
    if (brightness_result != ESP_OK) {
        ESP_LOGW(TAG, "Could not set display brightness: %s",
                 esp_err_to_name(brightness_result));
    }
    ESP_LOGI(TAG, "Display state: ACTIVE");
    return display;
}

static void stop_display(lv_display_t *display)
{
    if (bsp_display_lock(1000)) {
        dashboard_ui_deinit();
        bsp_display_unlock();
    } else {
        ESP_LOGW(TAG, "Could not lock display before standby");
    }

    const esp_err_t backlight_result = bsp_display_backlight_off();
    if (backlight_result != ESP_OK) {
        ESP_LOGW(TAG, "Could not disable backlight: %s", esp_err_to_name(backlight_result));
    }
    vTaskDelay(pdMS_TO_TICKS(20));
    bsp_display_stop(display);
    ESP_LOGI(TAG, "Display state: STANDBY (MIPI-DSI, touch, LVGL and backlight stopped)");
}

static void display_manager_task(void *argument)
{
    (void)argument;
    const int64_t started_us = esp_timer_get_time();
    int64_t next_start_attempt_us = 0;
    lv_display_t *display = NULL;

    while (true) {
        nexus_telemetry_t snapshot = {0};
        telemetry_get_snapshot(&snapshot);
        const int64_t now_us = esp_timer_get_time();
        const int64_t last_activity_us = snapshot.received_us > 0
                                             ? snapshot.received_us
                                             : started_us;
        const bool standby = now_us - last_activity_us >=
                             ((int64_t)NEXUS_STANDBY_AFTER_MS * 1000);

        if (display != NULL && standby) {
            stop_display(display);
            display = NULL;
        } else if (display == NULL && !standby && now_us >= next_start_attempt_us) {
            display = start_display();
            next_start_attempt_us = now_us + ((int64_t)DISPLAY_RETRY_MS * 1000);
        }

        vTaskDelay(pdMS_TO_TICKS(250));
    }
}

void app_main(void)
{
    ESP_LOGI(TAG, "Starting NEXUS firmware %s (protocol %s)",
             NEXUS_FIRMWARE_VERSION, NEXUS_PROTOCOL_VERSION_STRING);
    disable_unused_board_peripherals();
    telemetry_init();
    ESP_ERROR_CHECK(serial_transport_start());

    const BaseType_t created = xTaskCreate(display_manager_task, "nexus_display", 8192,
                                           NULL, 4, NULL);
    if (created != pdPASS) {
        ESP_LOGE(TAG, "Display manager task creation failed");
        return;
    }
    ESP_LOGI(TAG, "NEXUS is ready");
}
