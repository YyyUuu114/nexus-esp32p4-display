#pragma once

#include <stdbool.h>

#include "cJSON.h"

/**
 * @brief Check that every property name in a JSON object appears exactly once.
 *
 * Duplicate members are rejected because otherwise different JSON parsers can
 * select different values for security-critical fields such as protocol and
 * session identifiers.
 */
bool nexus_json_object_has_unique_keys(const cJSON *object);
