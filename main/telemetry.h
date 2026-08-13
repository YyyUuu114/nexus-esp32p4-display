#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "version.h"

#define NEXUS_STALE_AFTER_MS 3500
#define NEXUS_STANDBY_AFTER_MS 15000
#define NEXUS_HOST_MAX 15
#define NEXUS_CLOCK_MAX 5
#define NEXUS_DATE_MAX 10

typedef struct {
    bool valid;
    uint32_t seq;
    char host[NEXUS_HOST_MAX + 1];
    char clock[NEXUS_CLOCK_MAX + 1];
    char date[NEXUS_DATE_MAX + 1];
    float cpu_load;
    float cpu_temp;
    float cpu_power;
    float gpu_load;
    float gpu_temp;
    float gpu_power;
    float session_energy_kwh;
    float memory_used_gb;
    float memory_total_gb;
    float net_down_mbps;
    float net_up_mbps;
    float fan_rpm;
    int64_t received_us;
} nexus_telemetry_t;

void telemetry_init(void);
void telemetry_begin_session(void);
bool telemetry_parse_and_store(const char *json, size_t length, const char *expected_session_nonce);
void telemetry_get_snapshot(nexus_telemetry_t *out);
bool telemetry_snapshot_is_live(const nexus_telemetry_t *snapshot, int64_t now_us);
