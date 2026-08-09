#include "telemetry.h"

#include <math.h>
#include <string.h>

#include "cJSON.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"

static const char *TAG = "nexus_telemetry";
static SemaphoreHandle_t s_lock;
static nexus_telemetry_t s_latest;

static float optional_number(const cJSON *root, const char *name)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(root, name);
    if (!cJSON_IsNumber(item) || !isfinite(item->valuedouble)) {
        return NAN;
    }
    return (float)item->valuedouble;
}

static float clamp_optional(float value, float minimum, float maximum)
{
    if (!isfinite(value)) {
        return NAN;
    }
    if (value < minimum) {
        return minimum;
    }
    if (value > maximum) {
        return maximum;
    }
    return value;
}

static float non_negative_optional(float value)
{
    if (!isfinite(value) || value < 0.0f) {
        return NAN;
    }
    return value;
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

bool telemetry_parse_and_store(const char *json, size_t length)
{
    if (json == NULL || length == 0) {
        return false;
    }

    cJSON *root = cJSON_ParseWithLength(json, length);
    if (root == NULL) {
        return false;
    }

    const cJSON *version = cJSON_GetObjectItemCaseSensitive(root, "v");
    const cJSON *sequence = cJSON_GetObjectItemCaseSensitive(root, "seq");
    if (!cJSON_IsNumber(version) || version->valueint != NEXUS_PROTOCOL_VERSION ||
        !cJSON_IsNumber(sequence) || sequence->valuedouble < 0.0) {
        cJSON_Delete(root);
        return false;
    }

    nexus_telemetry_t next = {
        .valid = true,
        .seq = (uint32_t)sequence->valuedouble,
        .cpu_load = clamp_optional(optional_number(root, "cpu_load"), 0.0f, 100.0f),
        .cpu_temp = non_negative_optional(optional_number(root, "cpu_temp")),
        .cpu_power = non_negative_optional(optional_number(root, "cpu_power")),
        .gpu_load = clamp_optional(optional_number(root, "gpu_load"), 0.0f, 100.0f),
        .gpu_temp = non_negative_optional(optional_number(root, "gpu_temp")),
        .gpu_power = non_negative_optional(optional_number(root, "gpu_power")),
        .session_energy_kwh = non_negative_optional(optional_number(root, "session_energy_kwh")),
        .memory_used_gb = non_negative_optional(optional_number(root, "memory_used_gb")),
        .memory_total_gb = non_negative_optional(optional_number(root, "memory_total_gb")),
        .net_down_mbps = non_negative_optional(optional_number(root, "net_down_mbps")),
        .net_up_mbps = non_negative_optional(optional_number(root, "net_up_mbps")),
        .fan_rpm = non_negative_optional(optional_number(root, "fan_rpm")),
        .received_us = esp_timer_get_time(),
    };

    const cJSON *host = cJSON_GetObjectItemCaseSensitive(root, "host");
    const cJSON *clock = cJSON_GetObjectItemCaseSensitive(root, "clock");
    const cJSON *date = cJSON_GetObjectItemCaseSensitive(root, "date");
    strlcpy(next.host, cJSON_IsString(host) ? host->valuestring : "DESKTOP", sizeof(next.host));
    strlcpy(next.clock, cJSON_IsString(clock) ? clock->valuestring : "--:--", sizeof(next.clock));
    strlcpy(next.date, cJSON_IsString(date) ? date->valuestring : "----/--/--", sizeof(next.date));

    cJSON_Delete(root);

    if (xSemaphoreTake(s_lock, pdMS_TO_TICKS(50)) != pdTRUE) {
        ESP_LOGW(TAG, "Telemetry mutex timeout");
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
