#include "telemetry.h"

#include <math.h>
#include <string.h>

#include "cJSON.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "json_validation.h"
#include "protocol_validation.h"

static const char *TAG = "nexus_telemetry";
static SemaphoreHandle_t s_lock;
static nexus_telemetry_t s_latest;

static bool read_optional_number(const cJSON *root, const char *name,
                                 float minimum, float maximum, float *out)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(root, name);
    if (item == NULL || cJSON_IsNull(item)) {
        *out = NAN;
        return true;
    }
    if (!cJSON_IsNumber(item) || !isfinite(item->valuedouble) ||
        item->valuedouble < minimum || item->valuedouble > maximum) {
        return false;
    }
    *out = (float)item->valuedouble;
    return true;
}

static bool copy_optional_text(const cJSON *root, const char *name, const char *fallback,
                               char *out, size_t out_size, bool (*validator)(const char *))
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(root, name);
    if (item == NULL || cJSON_IsNull(item)) {
        strlcpy(out, fallback, out_size);
        return true;
    }
    if (!cJSON_IsString(item) || item->valuestring == NULL || !validator(item->valuestring)) {
        return false;
    }
    strlcpy(out, item->valuestring, out_size);
    return true;
}

static bool host_is_valid(const char *text)
{
    return nexus_text_is_printable_ascii(text, NEXUS_HOST_MAX);
}

void telemetry_init(void)
{
    s_lock = xSemaphoreCreateMutex();
    configASSERT(s_lock != NULL);

    memset(&s_latest, 0, sizeof(s_latest));
    s_latest.cpu_load = NAN;
    s_latest.cpu_temp = NAN;
    s_latest.cpu_power = NAN;
    s_latest.gpu_load = NAN;
    s_latest.gpu_temp = NAN;
    s_latest.gpu_power = NAN;
    s_latest.session_energy_kwh = NAN;
    s_latest.memory_used_gb = NAN;
    s_latest.memory_total_gb = NAN;
    s_latest.net_down_mbps = NAN;
    s_latest.net_up_mbps = NAN;
    s_latest.fan_rpm = NAN;
    strlcpy(s_latest.host, "DESKTOP", sizeof(s_latest.host));
    strlcpy(s_latest.clock, "--:--", sizeof(s_latest.clock));
    strlcpy(s_latest.date, "----/--/--", sizeof(s_latest.date));
}

void telemetry_begin_session(void)
{
    if (xSemaphoreTake(s_lock, pdMS_TO_TICKS(50)) != pdTRUE) {
        ESP_LOGW(TAG, "Telemetry mutex timeout while starting session");
        return;
    }
    s_latest.valid = false;
    s_latest.received_us = esp_timer_get_time();
    xSemaphoreGive(s_lock);
}

bool telemetry_parse_and_store(const char *json, size_t length, const char *expected_session_nonce)
{
    if (json == NULL || length == 0 || length > 1024 ||
        memchr(json, '\0', length) != NULL ||
        !nexus_nonce_is_valid(expected_session_nonce)) {
        return false;
    }

    const char *parse_end = NULL;
    cJSON *root = cJSON_ParseWithLengthOpts(json, length + 1, &parse_end, true);
    if (root == NULL || !cJSON_IsObject(root) || parse_end != json + length ||
        !nexus_json_object_has_unique_keys(root)) {
        cJSON_Delete(root);
        return false;
    }

    const cJSON *version = cJSON_GetObjectItemCaseSensitive(root, "v");
    const cJSON *sequence = cJSON_GetObjectItemCaseSensitive(root, "seq");
    const cJSON *session_nonce = cJSON_GetObjectItemCaseSensitive(root, "session_nonce");
    if (!cJSON_IsNumber(version) || !nexus_number_is_uint32(version->valuedouble) ||
        (uint32_t)version->valuedouble != NEXUS_PROTOCOL_VERSION ||
        !cJSON_IsNumber(sequence) || !nexus_number_is_uint32(sequence->valuedouble) ||
        !cJSON_IsString(session_nonce) || session_nonce->valuestring == NULL ||
        strcmp(session_nonce->valuestring, expected_session_nonce) != 0) {
        cJSON_Delete(root);
        return false;
    }

    nexus_telemetry_t next = {
        .valid = true,
        .seq = (uint32_t)sequence->valuedouble,
        .received_us = esp_timer_get_time(),
    };

    const bool values_valid =
        read_optional_number(root, "cpu_load", 0.0f, 100.0f, &next.cpu_load) &&
        read_optional_number(root, "cpu_temp", 0.0f, 250.0f, &next.cpu_temp) &&
        read_optional_number(root, "cpu_power", 0.0f, 5000.0f, &next.cpu_power) &&
        read_optional_number(root, "gpu_load", 0.0f, 100.0f, &next.gpu_load) &&
        read_optional_number(root, "gpu_temp", 0.0f, 250.0f, &next.gpu_temp) &&
        read_optional_number(root, "gpu_power", 0.0f, 5000.0f, &next.gpu_power) &&
        read_optional_number(root, "session_energy_kwh", 0.0f, 1000000.0f,
                             &next.session_energy_kwh) &&
        read_optional_number(root, "memory_used_gb", 0.0f, 65536.0f, &next.memory_used_gb) &&
        read_optional_number(root, "memory_total_gb", 0.0f, 65536.0f, &next.memory_total_gb) &&
        read_optional_number(root, "net_down_mbps", 0.0f, 10000000.0f, &next.net_down_mbps) &&
        read_optional_number(root, "net_up_mbps", 0.0f, 10000000.0f, &next.net_up_mbps) &&
        read_optional_number(root, "fan_rpm", 0.0f, 1000000.0f, &next.fan_rpm) &&
        copy_optional_text(root, "host", "DESKTOP", next.host, sizeof(next.host), host_is_valid) &&
        copy_optional_text(root, "clock", "--:--", next.clock, sizeof(next.clock),
                           nexus_clock_is_valid) &&
        copy_optional_text(root, "date", "----/--/--", next.date, sizeof(next.date),
                           nexus_date_is_valid);

    if (!values_valid ||
        (isfinite(next.memory_used_gb) && isfinite(next.memory_total_gb) &&
         next.memory_used_gb > next.memory_total_gb)) {
        cJSON_Delete(root);
        return false;
    }

    cJSON_Delete(root);

    if (xSemaphoreTake(s_lock, pdMS_TO_TICKS(50)) != pdTRUE) {
        ESP_LOGW(TAG, "Telemetry mutex timeout");
        return false;
    }
    if (s_latest.valid && next.seq <= s_latest.seq) {
        xSemaphoreGive(s_lock);
        return false;
    }
    s_latest = next;
    xSemaphoreGive(s_lock);
    return true;
}

void telemetry_get_snapshot(nexus_telemetry_t *out)
{
    if (out == NULL) {
        return;
    }
    if (xSemaphoreTake(s_lock, pdMS_TO_TICKS(50)) == pdTRUE) {
        *out = s_latest;
        xSemaphoreGive(s_lock);
    }
}

bool telemetry_snapshot_is_live(const nexus_telemetry_t *snapshot, int64_t now_us)
{
    if (snapshot == NULL || !snapshot->valid) {
        return false;
    }
    return (now_us - snapshot->received_us) <= ((int64_t)NEXUS_STALE_AFTER_MS * 1000);
}
