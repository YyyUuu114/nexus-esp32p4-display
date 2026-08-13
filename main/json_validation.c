#include "json_validation.h"

#include <string.h>

bool nexus_json_object_has_unique_keys(const cJSON *object)
{
    if (!cJSON_IsObject(object)) {
        return false;
    }

    for (const cJSON *left = object->child; left != NULL; left = left->next) {
        if (left->string == NULL) {
            return false;
        }
        for (const cJSON *right = left->next; right != NULL; right = right->next) {
            if (right->string == NULL || strcmp(left->string, right->string) == 0) {
                return false;
            }
        }
    }
    return true;
}
