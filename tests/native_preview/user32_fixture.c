/* Linux-only test adapter: upstream monitor enumeration returns an empty list. */
#include <stdint.h>
void *SetThreadDpiAwarenessContext(void *context) { (void)context; return (void *)(intptr_t)-4; }
int EnumDisplayMonitors(void *dc, void *rect, void *callback, intptr_t data) {
    (void)dc; (void)rect; (void)callback; (void)data; return 1;
}
