#include "dashboard_ui.h"

#include <math.h>
#include <stdint.h>
#include <stdio.h>

#include "bsp/esp-bsp.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "telemetry.h"

LV_FONT_DECLARE(lv_font_nexus_digits_80);
LV_FONT_DECLARE(lv_font_nexus_icons_44);

#define ICON_FAN "\xEE\xA7\xB2"

#define SCREEN_W 1024
#define SCREEN_H 600
#define HEADER_H 61
#define SUMMARY_Y 484
#define COLOR_BG lv_color_hex(0x0b1117)
#define COLOR_SURFACE lv_color_hex(0x10171e)
#define COLOR_LINE lv_color_hex(0x37414a)
#define COLOR_TEXT lv_color_hex(0xf5f7f9)
#define COLOR_MUTED lv_color_hex(0x8e98a4)
#define COLOR_CPU lv_color_hex(0x12bcf1)
#define COLOR_GPU lv_color_hex(0x83dc13)
#define COLOR_WARN lv_color_hex(0xf0a43b)

typedef struct {
    lv_obj_t *arc;
    lv_obj_t *load;
    lv_obj_t *temperature;
    lv_obj_t *power;
    lv_obj_t *chart;
    lv_chart_series_t *series;
    lv_color_t accent;
} metric_view_t;

static lv_obj_t *s_host;
static lv_obj_t *s_energy;
static lv_obj_t *s_date;
static lv_obj_t *s_clock;
static lv_obj_t *s_live_dot;
static lv_obj_t *s_connection;
static lv_obj_t *s_memory;
static lv_obj_t *s_network_down;
static lv_obj_t *s_network_up;
static lv_obj_t *s_fan;
static metric_view_t s_cpu;
static metric_view_t s_gpu;
static lv_timer_t *s_refresh_timer;
static uint32_t s_last_history_seq = UINT32_MAX;

static lv_obj_t *make_box(lv_obj_t *parent, int x, int y, int width, int height, lv_color_t color, lv_opa_t opacity)
{
    lv_obj_t *object = lv_obj_create(parent);
    lv_obj_remove_style_all(object);
    lv_obj_set_pos(object, x, y);
    lv_obj_set_size(object, width, height);
    lv_obj_set_style_bg_color(object, color, 0);
    lv_obj_set_style_bg_opa(object, opacity, 0);
    lv_obj_remove_flag(object, LV_OBJ_FLAG_SCROLLABLE);
    return object;
}

static lv_obj_t *make_label(lv_obj_t *parent, const char *text, int x, int y, int width, int height,
                            const lv_font_t *font, lv_color_t color, lv_text_align_t align)
{
    lv_obj_t *label = lv_label_create(parent);
    lv_obj_remove_style_all(label);
    lv_obj_set_pos(label, x, y);
    lv_obj_set_size(label, width, height);
    lv_obj_set_style_text_font(label, font, 0);
    lv_obj_set_style_text_color(label, color, 0);
    lv_obj_set_style_text_align(label, align, 0);
    lv_obj_set_style_text_letter_space(label, 0, 0);
    lv_label_set_long_mode(label, LV_LABEL_LONG_CLIP);
    lv_label_set_text(label, text);
    return label;
}

static void make_line(lv_obj_t *parent, int x, int y, int width, int height)
{
    make_box(parent, x, y, width, height, COLOR_LINE, LV_OPA_COVER);
}

static lv_obj_t *make_chart(lv_obj_t *parent, int x, int y, lv_color_t accent, lv_chart_series_t **series)
{
    lv_obj_t *chart = lv_chart_create(parent);
    lv_obj_remove_style_all(chart);
    lv_obj_set_pos(chart, x, y);
    lv_obj_set_size(chart, 405, 104);
    lv_obj_set_style_bg_opa(chart, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(chart, 0, 0);
    lv_obj_set_style_pad_all(chart, 0, LV_PART_MAIN);
    lv_obj_set_style_line_color(chart, lv_color_hex(0x46505a), LV_PART_MAIN);
    lv_obj_set_style_line_width(chart, 1, LV_PART_MAIN);
    lv_obj_set_style_line_dash_width(chart, 4, LV_PART_MAIN);
    lv_obj_set_style_line_dash_gap(chart, 5, LV_PART_MAIN);
    lv_obj_set_style_line_width(chart, 3, LV_PART_ITEMS);
    lv_obj_set_style_line_opa(chart, LV_OPA_COVER, LV_PART_ITEMS);
    lv_obj_set_style_line_rounded(chart, true, LV_PART_ITEMS);
    lv_obj_set_style_size(chart, 0, 0, LV_PART_INDICATOR);
    lv_chart_set_type(chart, LV_CHART_TYPE_LINE);
    lv_chart_set_update_mode(chart, LV_CHART_UPDATE_MODE_SHIFT);
    lv_chart_set_point_count(chart, 60);
    lv_chart_set_range(chart, LV_CHART_AXIS_PRIMARY_Y, 0, 100);
    lv_chart_set_div_line_count(chart, 3, 0);
    *series = lv_chart_add_series(chart, accent, LV_CHART_AXIS_PRIMARY_Y);
    lv_chart_set_all_value(chart, *series, LV_CHART_POINT_NONE);
    return chart;
}

static void make_metric_panel(lv_obj_t *root, int base_x, const char *title, lv_color_t accent, metric_view_t *view)
{
    make_label(root, title, base_x + 33, HEADER_H + 27, 120, 48, &lv_font_montserrat_36, accent, LV_TEXT_ALIGN_LEFT);

    view->arc = lv_arc_create(root);
    lv_obj_remove_style(view->arc, NULL, LV_PART_KNOB);
    lv_obj_remove_flag(view->arc, LV_OBJ_FLAG_CLICKABLE);
    lv_obj_set_pos(view->arc, base_x + 43, HEADER_H + 61);
    /* LVGL arcs are circular: use a square box so its object and visual centers match. */
    lv_obj_set_size(view->arc, 235, 235);
    lv_arc_set_range(view->arc, 0, 100);
    lv_arc_set_bg_angles(view->arc, 138, 42);
    lv_arc_set_value(view->arc, 0);
    lv_obj_set_style_arc_width(view->arc, 18, LV_PART_MAIN);
    lv_obj_set_style_arc_color(view->arc, lv_color_hex(0x323a43), LV_PART_MAIN);
    lv_obj_set_style_arc_rounded(view->arc, true, LV_PART_MAIN);
    lv_obj_set_style_arc_width(view->arc, 18, LV_PART_INDICATOR);
    lv_obj_set_style_arc_color(view->arc, accent, LV_PART_INDICATOR);
    lv_obj_set_style_arc_rounded(view->arc, true, LV_PART_INDICATOR);

    /* The visual arc center is base_x + 160.5. */
    view->load = make_label(root, "--", base_x + 41, HEADER_H + 132, 240, 96,
                            &lv_font_nexus_digits_80, COLOR_TEXT, LV_TEXT_ALIGN_CENTER);
    make_label(root, "%", base_x + 204, HEADER_H + 187, 46, 44, &lv_font_montserrat_36, accent, LV_TEXT_ALIGN_LEFT);

    make_line(root, base_x + 381, HEADER_H + 68, 1, 165);
    make_label(root, "TEMP", base_x + 399, HEADER_H + 72, 96, 20,
               &lv_font_montserrat_14, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);
    view->temperature = make_label(root, "--", base_x + 391, HEADER_H + 91, 76, 54,
                                   &lv_font_montserrat_40, accent, LV_TEXT_ALIGN_RIGHT);
    lv_obj_t *degree = make_box(root, base_x + 472, HEADER_H + 103, 7, 7, COLOR_MUTED, LV_OPA_TRANSP);
    lv_obj_set_style_border_width(degree, 2, 0);
    lv_obj_set_style_border_color(degree, COLOR_MUTED, 0);
    lv_obj_set_style_radius(degree, LV_RADIUS_CIRCLE, 0);
    make_label(root, "C", base_x + 480, HEADER_H + 109, 18, 26,
               &lv_font_montserrat_20, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);

    make_label(root, "POWER", base_x + 399, HEADER_H + 151, 96, 20,
               &lv_font_montserrat_14, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);
    view->power = make_label(root, "--", base_x + 391, HEADER_H + 169, 76, 54,
                             &lv_font_montserrat_40, accent, LV_TEXT_ALIGN_RIGHT);
    make_label(root, "W", base_x + 474, HEADER_H + 187, 24, 26,
               &lv_font_montserrat_20, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);

    /* Leave a visible gap between the gauge endpoints and the chart's top axis. */
    view->chart = make_chart(root, base_x + 72, HEADER_H + 270, accent, &view->series);

    /* Anchor labels to the chart object itself so grid, curve, and axes share coordinates. */
    lv_obj_t *axis_100 = make_label(root, "100%", 0, 0, 47, 20,
                                    &lv_font_montserrat_14, COLOR_MUTED, LV_TEXT_ALIGN_RIGHT);
    lv_obj_align_to(axis_100, view->chart, LV_ALIGN_OUT_LEFT_TOP, -6, -10);
    lv_obj_t *axis_50 = make_label(root, "50%", 0, 0, 47, 20,
                                   &lv_font_montserrat_14, COLOR_MUTED, LV_TEXT_ALIGN_RIGHT);
    lv_obj_align_to(axis_50, view->chart, LV_ALIGN_OUT_LEFT_MID, -6, 0);
    lv_obj_t *axis_0 = make_label(root, "0%", 0, 0, 47, 20,
                                  &lv_font_montserrat_14, COLOR_MUTED, LV_TEXT_ALIGN_RIGHT);
    lv_obj_align_to(axis_0, view->chart, LV_ALIGN_OUT_LEFT_BOTTOM, -6, 10);

    lv_obj_t *time_60 = make_label(root, "60s", 0, 0, 42, 24,
                                   &lv_font_montserrat_16, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);
    lv_obj_align_to(time_60, view->chart, LV_ALIGN_OUT_BOTTOM_LEFT, 0, 16);
    lv_obj_t *time_0 = make_label(root, "0s", 0, 0, 35, 24,
                                  &lv_font_montserrat_16, COLOR_MUTED, LV_TEXT_ALIGN_RIGHT);
    lv_obj_align_to(time_0, view->chart, LV_ALIGN_OUT_BOTTOM_RIGHT, 0, 16);
    view->accent = accent;
}

static void update_number(lv_obj_t *label, float value, unsigned decimals)
{
    if (!isfinite(value)) {
        lv_label_set_text(label, "--");
    } else if (decimals == 0) {
        lv_label_set_text_fmt(label, "%.0f", value);
    } else {
        lv_label_set_text_fmt(label, "%.1f", value);
    }
}

static void update_network_number(lv_obj_t *label, float value)
{
    if (!isfinite(value)) {
        lv_label_set_text(label, "--");
    } else if (value >= 100.0f) {
        lv_label_set_text_fmt(label, "%.0f", value);
    } else {
        lv_label_set_text_fmt(label, "%.1f", value);
    }
}

static void update_energy_number(lv_obj_t *label, float value)
{
    if (!isfinite(value)) {
        lv_label_set_text(label, "--");
    } else if (value < 10.0f) {
        lv_label_set_text_fmt(label, "%.3f", value);
    } else if (value < 100.0f) {
        lv_label_set_text_fmt(label, "%.2f", value);
    } else if (value < 1000.0f) {
        lv_label_set_text_fmt(label, "%.1f", value);
    } else {
        lv_label_set_text_fmt(label, "%.0f", value);
    }
}

static void update_metric(metric_view_t *view, float load, float temperature, float power)
{
    const int value = isfinite(load) ? (int)lroundf(load) : 0;
    lv_arc_set_value(view->arc, value);
    update_number(view->load, load, 0);
    update_number(view->temperature, temperature, 0);
    update_number(view->power, power, 0);
}

static void refresh_timer(lv_timer_t *timer)
{
    (void)timer;
    nexus_telemetry_t data = {0};
    telemetry_get_snapshot(&data);
    const int64_t now_us = esp_timer_get_time();
    const bool live = telemetry_snapshot_is_live(&data, now_us);

    lv_label_set_text(s_host, data.host[0] != '\0' ? data.host : "DESKTOP");
    update_energy_number(s_energy, data.session_energy_kwh);
    lv_label_set_text(s_date, data.date[0] != '\0' ? data.date : "----/--/--");
    lv_label_set_text(s_clock, data.clock[0] != '\0' ? data.clock : "--:--");
    lv_label_set_text(s_connection, live ? "LIVE" : "WAIT");
    const lv_color_t status_color = live ? COLOR_GPU : (data.valid ? COLOR_WARN : COLOR_MUTED);
    lv_obj_set_style_bg_color(s_live_dot, status_color, 0);
    lv_obj_set_style_text_color(s_connection, status_color, 0);

    update_metric(&s_cpu, data.cpu_load, data.cpu_temp, data.cpu_power);
    update_metric(&s_gpu, data.gpu_load, data.gpu_temp, data.gpu_power);

    if (isfinite(data.memory_used_gb) && isfinite(data.memory_total_gb)) {
        lv_label_set_text_fmt(s_memory, "%.1f / %.0f", data.memory_used_gb, data.memory_total_gb);
    } else {
        lv_label_set_text(s_memory, "-- / --");
    }
    update_network_number(s_network_down, data.net_down_mbps);
    update_network_number(s_network_up, data.net_up_mbps);
    update_number(s_fan, data.fan_rpm, 0);

    if (data.valid && data.seq != s_last_history_seq) {
        lv_chart_set_next_value(s_cpu.chart, s_cpu.series,
                                isfinite(data.cpu_load) ? (int32_t)lroundf(data.cpu_load) : LV_CHART_POINT_NONE);
        lv_chart_set_next_value(s_gpu.chart, s_gpu.series,
                                isfinite(data.gpu_load) ? (int32_t)lroundf(data.gpu_load) : LV_CHART_POINT_NONE);
        s_last_history_seq = data.seq;
    }
}

static void make_summary_band(lv_obj_t *root)
{
    make_box(root, 0, SUMMARY_Y, SCREEN_W, SCREEN_H - SUMMARY_Y, lv_color_hex(0x080d12), LV_OPA_70);
    make_line(root, 0, SUMMARY_Y, SCREEN_W, 1);
    make_line(root, 330, SUMMARY_Y + 22, 1, 74);
    make_line(root, 704, SUMMARY_Y + 22, 1, 74);

    make_label(root, LV_SYMBOL_DRIVE, 29, SUMMARY_Y + 30, 58, 55, &lv_font_montserrat_36, COLOR_CPU, LV_TEXT_ALIGN_CENTER);
    make_label(root, "MEMORY", 104, SUMMARY_Y + 26, 150, 28, &lv_font_montserrat_18, COLOR_CPU, LV_TEXT_ALIGN_LEFT);
    s_memory = make_label(root, "-- / --", 103, SUMMARY_Y + 57, 180, 45, &lv_font_montserrat_36, COLOR_TEXT, LV_TEXT_ALIGN_LEFT);
    make_label(root, "GB", 277, SUMMARY_Y + 72, 38, 25, &lv_font_montserrat_16, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);

    make_label(root, LV_SYMBOL_WIFI, 351, SUMMARY_Y + 31, 60, 53, &lv_font_montserrat_36, COLOR_GPU, LV_TEXT_ALIGN_CENTER);
    make_label(root, "NETWORK", 425, SUMMARY_Y + 26, 170, 28, &lv_font_montserrat_18, COLOR_GPU, LV_TEXT_ALIGN_LEFT);
    make_label(root, LV_SYMBOL_DOWN, 424, SUMMARY_Y + 66, 30, 31, &lv_font_montserrat_28, COLOR_GPU, LV_TEXT_ALIGN_LEFT);
    s_network_down = make_label(root, "--", 454, SUMMARY_Y + 57, 82, 45, &lv_font_montserrat_36, COLOR_TEXT, LV_TEXT_ALIGN_LEFT);
    make_label(root, LV_SYMBOL_UP, 535, SUMMARY_Y + 66, 30, 31, &lv_font_montserrat_28, COLOR_GPU, LV_TEXT_ALIGN_LEFT);
    s_network_up = make_label(root, "--", 566, SUMMARY_Y + 57, 77, 45, &lv_font_montserrat_36, COLOR_TEXT, LV_TEXT_ALIGN_LEFT);
    make_label(root, "Mbps", 646, SUMMARY_Y + 76, 50, 22, &lv_font_montserrat_14, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);

    make_label(root, ICON_FAN, 726, SUMMARY_Y + 25, 62, 62, &lv_font_nexus_icons_44, COLOR_CPU, LV_TEXT_ALIGN_CENTER);
    make_label(root, "FANS", 805, SUMMARY_Y + 26, 110, 28, &lv_font_montserrat_18, COLOR_CPU, LV_TEXT_ALIGN_LEFT);
    s_fan = make_label(root, "--", 803, SUMMARY_Y + 56, 132, 47, &lv_font_montserrat_40, COLOR_TEXT, LV_TEXT_ALIGN_LEFT);
    make_label(root, "RPM", 934, SUMMARY_Y + 76, 55, 22, &lv_font_montserrat_16, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);
}

void dashboard_ui_init(lv_display_t *display)
{
    s_last_history_seq = UINT32_MAX;
    lv_display_set_default(display);
    lv_obj_t *root = lv_display_get_screen_active(display);
    lv_obj_clean(root);
    lv_obj_remove_style_all(root);
    lv_obj_set_size(root, SCREEN_W, SCREEN_H);
    lv_obj_set_style_bg_color(root, COLOR_BG, 0);
    lv_obj_set_style_bg_opa(root, LV_OPA_COVER, 0);
    lv_obj_remove_flag(root, LV_OBJ_FLAG_SCROLLABLE);

    make_box(root, 0, 0, SCREEN_W, HEADER_H, lv_color_hex(0x080d12), LV_OPA_70);
    make_line(root, 0, HEADER_H - 1, SCREEN_W, 1);
    make_label(root, "NEXUS", 22, 15, 132, 40, &lv_font_montserrat_32, COLOR_TEXT, LV_TEXT_ALIGN_LEFT);
    make_line(root, 163, 19, 1, 28);
    s_host = make_label(root, "DESKTOP", 186, 18, 250, 36, &lv_font_montserrat_24, lv_color_hex(0xaeb5bd), LV_TEXT_ALIGN_LEFT);

    make_line(root, 448, 19, 1, 28);
    make_label(root, "ENERGY", 464, 9, 86, 20, &lv_font_montserrat_14, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);
    s_energy = make_label(root, "0.000", 462, 27, 105, 29, &lv_font_montserrat_20, COLOR_CPU, LV_TEXT_ALIGN_RIGHT);
    make_label(root, "kWh", 575, 31, 44, 22, &lv_font_montserrat_14, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);
    make_line(root, 638, 19, 1, 28);

    s_live_dot = make_box(root, 670, 26, 10, 10, COLOR_MUTED, LV_OPA_COVER);
    lv_obj_set_style_radius(s_live_dot, LV_RADIUS_CIRCLE, 0);
    s_connection = make_label(root, "WAIT", 689, 18, 86, 36, &lv_font_montserrat_24, COLOR_MUTED, LV_TEXT_ALIGN_LEFT);
    make_line(root, 786, 19, 1, 28);
    s_date = make_label(root, "----/--/--", 798, 20, 112, 30, &lv_font_montserrat_18,
                        lv_color_hex(0xaeb5bd), LV_TEXT_ALIGN_RIGHT);
    make_line(root, 918, 19, 1, 28);
    s_clock = make_label(root, "--:--", 928, 16, 92, 38, &lv_font_montserrat_28, COLOR_TEXT, LV_TEXT_ALIGN_RIGHT);

    make_line(root, 511, HEADER_H, 1, SUMMARY_Y - HEADER_H);
    make_metric_panel(root, 0, "CPU", COLOR_CPU, &s_cpu);
    make_metric_panel(root, 512, "GPU", COLOR_GPU, &s_gpu);
    make_summary_band(root);

    s_refresh_timer = lv_timer_create(refresh_timer, 250, NULL);
    refresh_timer(NULL);
}

void dashboard_ui_deinit(void)
{
    if (s_refresh_timer != NULL) {
        lv_timer_delete(s_refresh_timer);
        s_refresh_timer = NULL;
    }
}
