#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef struct {
    uint32_t major;
    uint32_t line;
    uint32_t channel;
} nexus_product_version_t;

bool nexus_parse_product_version(const char *text, nexus_product_version_t *out);
int nexus_compare_product_versions(const nexus_product_version_t *left,
                                   const nexus_product_version_t *right);
bool nexus_version_in_supported_range(const char *text, const char *minimum,
                                      uint32_t maximum_exclusive_major);
bool nexus_nonce_is_valid(const char *nonce);
bool nexus_number_is_uint32(double value);
bool nexus_text_is_printable_ascii(const char *text, size_t maximum_length);
bool nexus_clock_is_valid(const char *text);
bool nexus_date_is_valid(const char *text);
