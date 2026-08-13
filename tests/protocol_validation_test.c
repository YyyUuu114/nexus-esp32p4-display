#include <math.h>
#include <stdio.h>
#include <stdlib.h>

#include "protocol_validation.h"

static void require_true(bool condition, const char *description)
{
    if (!condition) {
        fprintf(stderr, "FAIL: %s\n", description);
        exit(EXIT_FAILURE);
    }
}

static void require_false(bool condition, const char *description)
{
    require_true(!condition, description);
}

int main(void)
{
    nexus_product_version_t version;
    require_true(nexus_parse_product_version("2.1.1", &version), "canonical development version");
    require_true(version.major == 2U && version.line == 1U && version.channel == 1U,
                 "parsed version components");
    require_true(nexus_parse_product_version("2.1.0", &version), "canonical stable version");
    require_false(nexus_parse_product_version("2.01.1", &version), "leading zero rejected");
    require_false(nexus_parse_product_version("2.1.10", &version), "invalid channel rejected");
    require_false(nexus_parse_product_version("2.1.1x", &version), "trailing version text rejected");
    require_false(nexus_parse_product_version("4294967296.1.1", &version), "overflow rejected");

    require_true(nexus_version_in_supported_range("2.1.1", "2.1.1", 3U), "range lower bound");
    require_true(nexus_version_in_supported_range("2.9.9", "2.1.1", 3U), "range interior");
    require_true(nexus_version_in_supported_range("2.1.0", "2.1.1", 3U),
                 "stable channel follows development channels");
    require_false(nexus_version_in_supported_range("3.0.0", "2.1.1", 3U), "exclusive major");

    require_true(nexus_nonce_is_valid("0123456789abcdefABCDEF0123456789"), "32 hex nonce");
    require_false(nexus_nonce_is_valid("0123456789abcdef"), "short nonce rejected");
    require_false(nexus_nonce_is_valid("0123456789abcdefABCDEF012345678g"), "non-hex nonce rejected");

    require_true(nexus_number_is_uint32(0.0), "zero uint32");
    require_true(nexus_number_is_uint32(4294967295.0), "maximum uint32");
    require_false(nexus_number_is_uint32(1.5), "fractional uint32 rejected");
    require_false(nexus_number_is_uint32(-1.0), "negative uint32 rejected");
    require_false(nexus_number_is_uint32(NAN), "NaN uint32 rejected");

    require_true(nexus_text_is_printable_ascii("DESKTOP", 15U), "printable host");
    require_false(nexus_text_is_printable_ascii("", 15U), "empty host rejected");
    require_false(nexus_text_is_printable_ascii("DESKTOP-WITH-A-LONG-NAME", 15U),
                  "long host rejected");
    require_false(nexus_text_is_printable_ascii("LINE\nBREAK", 15U), "control character rejected");

    require_true(nexus_clock_is_valid("23:59"), "valid clock");
    require_false(nexus_clock_is_valid("24:00"), "invalid hour rejected");
    require_false(nexus_clock_is_valid("23:60"), "invalid minute rejected");
    require_true(nexus_date_is_valid("2028-02-29"), "leap day");
    require_false(nexus_date_is_valid("2027-02-29"), "non-leap day rejected");
    require_false(nexus_date_is_valid("2026-04-31"), "invalid month day rejected");

    puts("Protocol validation tests passed.");
    return EXIT_SUCCESS;
}
