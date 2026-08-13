#include "protocol_validation.h"

#include <ctype.h>
#include <math.h>
#include <string.h>

static bool parse_version_component(const char **cursor, char terminator, uint32_t *out)
{
    const char *position = *cursor;
    if (!isdigit((unsigned char)*position)) {
        return false;
    }
    if (*position == '0' && isdigit((unsigned char)position[1])) {
        return false;
    }

    uint32_t value = 0;
    do {
        const uint32_t digit = (uint32_t)(*position - '0');
        if (value > (UINT32_MAX - digit) / 10U) {
            return false;
        }
        value = value * 10U + digit;
        ++position;
    } while (isdigit((unsigned char)*position));

    if (*position != terminator) {
        return false;
    }
    *out = value;
    *cursor = terminator == '\0' ? position : position + 1;
    return true;
}

bool nexus_parse_product_version(const char *text, nexus_product_version_t *out)
{
    if (text == NULL || out == NULL) {
        return false;
    }

    const char *cursor = text;
    if (!parse_version_component(&cursor, '.', &out->major) ||
        !parse_version_component(&cursor, '.', &out->line) ||
        !parse_version_component(&cursor, '\0', &out->channel) ||
        out->channel > 9U) {
        return false;
    }
    return true;
}

int nexus_compare_product_versions(const nexus_product_version_t *left,
                                   const nexus_product_version_t *right)
{
    if (left->major != right->major) {
        return left->major < right->major ? -1 : 1;
    }
    if (left->line != right->line) {
        return left->line < right->line ? -1 : 1;
    }
    if (left->channel != right->channel) {
        if (left->channel == 0U) {
            return 1;
        }
        if (right->channel == 0U) {
            return -1;
        }
        return left->channel < right->channel ? -1 : 1;
    }
    return 0;
}

bool nexus_version_in_supported_range(const char *text, const char *minimum,
                                      uint32_t maximum_exclusive_major)
{
    nexus_product_version_t actual;
    nexus_product_version_t lower;
    if (!nexus_parse_product_version(text, &actual) ||
        !nexus_parse_product_version(minimum, &lower)) {
        return false;
    }
    return nexus_compare_product_versions(&actual, &lower) >= 0 &&
           actual.major < maximum_exclusive_major;
}

bool nexus_nonce_is_valid(const char *nonce)
{
    if (nonce == NULL || strlen(nonce) != 32) {
        return false;
    }
    for (size_t index = 0; index < 32; ++index) {
        if (!isxdigit((unsigned char)nonce[index])) {
            return false;
        }
    }
    return true;
}

bool nexus_number_is_uint32(double value)
{
    return isfinite(value) && value >= 0.0 && value <= (double)UINT32_MAX &&
           floor(value) == value;
}

bool nexus_text_is_printable_ascii(const char *text, size_t maximum_length)
{
    if (text == NULL) {
        return false;
    }
    const size_t length = strlen(text);
    if (length == 0 || length > maximum_length) {
        return false;
    }
    for (size_t index = 0; index < length; ++index) {
        const unsigned char value = (unsigned char)text[index];
        if (value < 0x20 || value > 0x7e) {
            return false;
        }
    }
    return true;
}

bool nexus_clock_is_valid(const char *text)
{
    if (text == NULL || strlen(text) != 5 || text[2] != ':' ||
        !isdigit((unsigned char)text[0]) || !isdigit((unsigned char)text[1]) ||
        !isdigit((unsigned char)text[3]) || !isdigit((unsigned char)text[4])) {
        return false;
    }
    const unsigned hour = (unsigned)(text[0] - '0') * 10U + (unsigned)(text[1] - '0');
    const unsigned minute = (unsigned)(text[3] - '0') * 10U + (unsigned)(text[4] - '0');
    return hour < 24U && minute < 60U;
}

bool nexus_date_is_valid(const char *text)
{
    if (text == NULL || strlen(text) != 10 || text[4] != '-' || text[7] != '-') {
        return false;
    }
    for (size_t index = 0; index < 10; ++index) {
        if (index == 4 || index == 7) {
            continue;
        }
        if (!isdigit((unsigned char)text[index])) {
            return false;
        }
    }
    const unsigned year = (unsigned)(text[0] - '0') * 1000U +
                          (unsigned)(text[1] - '0') * 100U +
                          (unsigned)(text[2] - '0') * 10U +
                          (unsigned)(text[3] - '0');
    const unsigned month = (unsigned)(text[5] - '0') * 10U + (unsigned)(text[6] - '0');
    const unsigned day = (unsigned)(text[8] - '0') * 10U + (unsigned)(text[9] - '0');
    static const unsigned days_per_month[] = {
        31U, 28U, 31U, 30U, 31U, 30U, 31U, 31U, 30U, 31U, 30U, 31U,
    };
    if (year == 0U || month < 1U || month > 12U) {
        return false;
    }
    unsigned maximum_day = days_per_month[month - 1U];
    const bool leap_year = (year % 4U == 0U && year % 100U != 0U) || year % 400U == 0U;
    if (month == 2U && leap_year) {
        maximum_day = 29U;
    }
    return day >= 1U && day <= maximum_day;
}
