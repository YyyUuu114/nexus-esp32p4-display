#include "serial_transport.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "cJSON.h"
#include "driver/usb_serial_jtag.h"
#include "esp_check.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "json_validation.h"
#include "protocol_validation.h"
#include "telemetry.h"
#include "version.h"

#define NEXUS_LINE_MAX 1024

static const char *TAG = "nexus_serial";

typedef enum {
    CONTROL_NOT_HELLO,
    CONTROL_ACCEPTED,
    CONTROL_REJECTED,
} control_result_t;

static void write_response(const char *response)
{
    const size_t length = strlen(response);
    size_t written = 0;
    while (written < length) {
        const int count = usb_serial_jtag_write_bytes(response + written, length - written,
                                                       pdMS_TO_TICKS(100));
        if (count <= 0) {
            ESP_LOGW(TAG, "USB response write timed out");
            return;
        }
        written += (size_t)count;
    }
}

static bool json_uint32_equals(const cJSON *root, const char *name, uint32_t expected)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(root, name);
    return cJSON_IsNumber(item) && nexus_number_is_uint32(item->valuedouble) &&
           (uint32_t)item->valuedouble == expected;
}

static control_result_t process_hello(const char *json, size_t length, char active_nonce[33])
{
    const char *parse_end = NULL;
    cJSON *root = cJSON_ParseWithLengthOpts(json, length + 1, &parse_end, true);
    if (root == NULL || !cJSON_IsObject(root) || parse_end != json + length ||
        !nexus_json_object_has_unique_keys(root)) {
        cJSON_Delete(root);
        return CONTROL_NOT_HELLO;
    }

    const cJSON *type = cJSON_GetObjectItemCaseSensitive(root, "type");
    if (!cJSON_IsString(type) || strcmp(type->valuestring, "hello") != 0) {
        cJSON_Delete(root);
        return CONTROL_NOT_HELLO;
    }

    const cJSON *product = cJSON_GetObjectItemCaseSensitive(root, "product");
    const cJSON *desktop_version = cJSON_GetObjectItemCaseSensitive(root, "desktop_version");
    const cJSON *minimum_firmware = cJSON_GetObjectItemCaseSensitive(root, "minimum_firmware_version");
    const cJSON *maximum_firmware_major =
        cJSON_GetObjectItemCaseSensitive(root, "maximum_firmware_major_exclusive");
    const cJSON *nonce = cJSON_GetObjectItemCaseSensitive(root, "nonce");

    nexus_product_version_t firmware_version;
    const bool nonce_valid = cJSON_IsString(nonce) && nexus_nonce_is_valid(nonce->valuestring);
    const bool maximum_valid = cJSON_IsNumber(maximum_firmware_major) &&
                               nexus_number_is_uint32(maximum_firmware_major->valuedouble) &&
                               maximum_firmware_major->valuedouble > 0.0;
    const bool compatible =
        cJSON_IsString(product) && strcmp(product->valuestring, "NEXUS_DESKTOP") == 0 &&
        cJSON_IsString(desktop_version) &&
        nexus_version_in_supported_range(desktop_version->valuestring,
                                         NEXUS_MIN_DESKTOP_VERSION,
                                         NEXUS_MAX_DESKTOP_MAJOR) &&
        json_uint32_equals(root, "protocol_major", NEXUS_PROTOCOL_VERSION) &&
        json_uint32_equals(root, "protocol_revision", NEXUS_PROTOCOL_REVISION) &&
        cJSON_IsString(minimum_firmware) && maximum_valid &&
        nexus_parse_product_version(NEXUS_FIRMWARE_VERSION, &firmware_version) &&
        nexus_version_in_supported_range(NEXUS_FIRMWARE_VERSION,
                                         minimum_firmware->valuestring,
                                         (uint32_t)maximum_firmware_major->valuedouble) &&
        nonce_valid;

    char response[512];
    if (compatible) {
        strlcpy(active_nonce, nonce->valuestring, 33);
        telemetry_begin_session();
        snprintf(response, sizeof(response),
                 "{\"type\":\"ready\",\"product\":\"%s\",\"firmware_version\":\"%s\","
                 "\"protocol_major\":%u,\"protocol_revision\":%u,"
                 "\"minimum_desktop_version\":\"%s\","
                 "\"maximum_desktop_major_exclusive\":%u,\"nonce\":\"%s\"}\n",
                 NEXUS_PRODUCT_ID, NEXUS_FIRMWARE_VERSION,
                 NEXUS_PROTOCOL_VERSION, NEXUS_PROTOCOL_REVISION,
                 NEXUS_MIN_DESKTOP_VERSION, NEXUS_MAX_DESKTOP_MAJOR, active_nonce);
        write_response(response);
        ESP_LOGI(TAG, "Authenticated desktop session %s", desktop_version->valuestring);
        cJSON_Delete(root);
        return CONTROL_ACCEPTED;
    }

    active_nonce[0] = '\0';
    snprintf(response, sizeof(response),
             "{\"type\":\"error\",\"product\":\"%s\",\"firmware_version\":\"%s\","
             "\"protocol_major\":%u,\"protocol_revision\":%u,"
             "\"code\":\"incompatible\",\"nonce\":\"%s\"}\n",
             NEXUS_PRODUCT_ID, NEXUS_FIRMWARE_VERSION,
             NEXUS_PROTOCOL_VERSION, NEXUS_PROTOCOL_REVISION,
             nonce_valid ? nonce->valuestring : "");
    write_response(response);
    ESP_LOGW(TAG, "Rejected incompatible desktop handshake");
    cJSON_Delete(root);
    return CONTROL_REJECTED;
}

static void serial_task(void *argument)
{
    (void)argument;
    uint8_t chunk[128];
    char line[NEXUS_LINE_MAX + 1];
    char active_nonce[33] = {0};
    size_t used = 0;
    bool overflowed = false;

    while (true) {
        const int count = usb_serial_jtag_read_bytes(chunk, sizeof(chunk), pdMS_TO_TICKS(250));
        for (int index = 0; index < count; ++index) {
            const char byte = (char)chunk[index];
            if (byte == '\n') {
                if (!overflowed && used > 0) {
                    size_t frame_length = used;
                    if (line[frame_length - 1] == '\r') {
                        --frame_length;
                    }
                    line[frame_length] = '\0';
                    if (frame_length == 0 || memchr(line, '\r', frame_length) != NULL) {
                        ESP_LOGW(TAG, "Dropped frame with invalid line ending");
                    } else {
                        const control_result_t control =
                            process_hello(line, frame_length, active_nonce);
                        if (control == CONTROL_NOT_HELLO &&
                            !telemetry_parse_and_store(line, frame_length, active_nonce)) {
                            ESP_LOGW(TAG, "Dropped invalid telemetry frame (%u bytes)",
                                     (unsigned)frame_length);
                        }
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
