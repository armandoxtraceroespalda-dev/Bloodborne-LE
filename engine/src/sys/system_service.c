// SPDX-License-Identifier: GPL-3.0-or-later
#include "sys.h"
#include "runtime.h"
#include <SDL3/SDL.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

enum {
    SYSTEM_ERROR_PARAMETER = (int32_t)0x80A10003,
    SYSTEM_ERROR_NO_EVENT = (int32_t)0x80A10004,
};

enum {
    PARAM_LANGUAGE = 1,
    PARAM_DATE_FORMAT = 2,
    PARAM_TIME_FORMAT = 3,
    PARAM_TIME_ZONE = 4,
    PARAM_SUMMER_TIME = 5,
    PARAM_GAME_PARENTAL_LEVEL = 7,
    PARAM_ENTER_BUTTON = 1000,
};

static int32_t time_zone_minutes(void) {
    time_t now = time(NULL);
    struct tm local;
#ifdef _WIN32
    if (localtime_s(&local, &now)) return 0;
    return (int32_t)((_mkgmtime(&local) - now) / 60);
#else
    if (!localtime_r(&now, &local)) return 0;
    return (int32_t)(local.tm_gmtoff / 60);
#endif
}

static SYS_ABI int32_t system_param_get_int(int32_t id, int32_t *value) {
    if (!value) return SYSTEM_ERROR_PARAMETER;
    switch (id) {
    case PARAM_LANGUAGE: {
        const char *language = getenv("BB_LANGUAGE");
        *value = language ? atoi(language) : 1;
        return 0;
    }
    case PARAM_DATE_FORMAT:
    case PARAM_TIME_FORMAT:
    case PARAM_ENTER_BUTTON:
        *value = 1;
        return 0;
    case PARAM_TIME_ZONE:
        *value = time_zone_minutes();
        return 0;
    case PARAM_SUMMER_TIME:
    case PARAM_GAME_PARENTAL_LEVEL:
        *value = 0;
        return 0;
    }
    fprintf(stderr, "Runtime: unsupported system parameter %d\n", id);
    return SYSTEM_ERROR_PARAMETER;
}

static SYS_ABI int32_t system_get_status(void *status) {
    if (!status) return SYSTEM_ERROR_PARAMETER;
    memset(status, 0, 12);
    return 0;
}

static SYS_ABI int32_t system_receive_event(void *event) {
    (void)event;
    return SYSTEM_ERROR_NO_EVENT;
}

static SYS_ABI int32_t system_hide_splash_screen(void) {
    puts("Runtime: SystemService: splash screen hidden");
    return 0;
}

/* Quit to Desktop (scripts/quit_menu.py): the patched menus call the browser with these
 * addresses. "bb:q" (title menu) closes the game; "bb:p" (System menu, in the world) marks the
 * Exit Game that follows, and "bb:d" (its confirmation dialog) answers 1 to show the Quit to
 * Desktop text and arms the quit. The game then saves and leaves the world, and closes as soon
 * as it starts the intro logos (a menu/logo_ file opens) with no save data mounted, or when the
 * title screen is built ("bb:t", with the intro skipped). An unanswered dialog disarms it after
 * two minutes. */
static int quit_pending, quit_waiting;
static time_t quit_armed;
static HostMutex quit_lock=HOST_MUTEX_INIT;
static void quit_game(void) {
    puts("Runtime: Quit to Desktop");
    SDL_Event quit={.type=SDL_EVENT_QUIT};
    SDL_PushEvent(&quit);
}
static int quit_ready(void) { return quit_armed && time(NULL)-quit_armed<120; }
void runtime_quit_on_open(const char *guest) {
    if (!guest || !strstr(guest,"/menu/logo_")) return;
    host_lock(&quit_lock);
    if (quit_ready()) {
        quit_armed=0;
        /* A save still writing finishes first (runtime_quit_on_save_done). */
        if (runtime_savedata_mounted()) quit_waiting=1; else quit_game();
    }
    host_unlock(&quit_lock);
}
void runtime_quit_on_save_done(void) {
    host_lock(&quit_lock);
    if (quit_waiting && !runtime_savedata_mounted()) { quit_waiting=0; quit_game(); }
    host_unlock(&quit_lock);
}
static SYS_ABI int32_t system_launch_web_browser(const char *url) {
    if (url && !strcmp(url,"bb:q")) { quit_game(); return 0; }
    if (url && !strcmp(url,"bb:p")) { host_lock(&quit_lock);quit_pending=1;host_unlock(&quit_lock);return 0; }
    if (url && !strcmp(url,"bb:d")) {
        host_lock(&quit_lock);
        const int armed=quit_pending;
        quit_pending=0;
        quit_armed=armed ? time(NULL) : 0;
        host_unlock(&quit_lock);
        return armed;
    }
    if (url && !strcmp(url,"bb:t")) {
        host_lock(&quit_lock);
        if (quit_ready() || quit_waiting) {
            if(runtime_savedata_mounted())quit_waiting=1;else {quit_waiting=0;quit_game();}
        }
        quit_armed=0;
        host_unlock(&quit_lock);
        return 0;
    }
    puts("Runtime: SystemService: web browser request ignored (offline)");
    return 0;
}

static const SysExport exports[] = {
    {"rPo6tV8D9bM", "sceSystemServiceGetStatus", system_get_status},
    {"Vo5V8KAwCmk", "sceSystemServiceHideSplashScreen", system_hide_splash_screen},
    {"-+3hY+y8bNo", "sceSystemServiceLaunchWebBrowser", system_launch_web_browser},
    {"fZo48un7LK4", "sceSystemServiceParamGetInt", system_param_get_int},
    {"656LMQSrg6U", "sceSystemServiceReceiveEvent", system_receive_event},
};

SYS_LIBRARY(sys_system_service, "libSceSystemService", exports);
