// SPDX-License-Identifier: GPL-3.0-or-later
// Adapted from bbhost db5457cbd447f4ef129b58df0a78e0a8819c7033.
#define BBHOST_HAVE_SDL3 1
#include "compat.h"
#include "bindings.h"
#include <SDL3/SDL.h>
static bool native_reserved_key(int sc){return sc==SDL_SCANCODE_INSERT;}
#include "chords.h"
#include "sprint_turn.h"
#include "../bbport_settings.h"


#include <atomic>
#include <chrono>
#include <cmath>
#include <cstring>
#include <string>

#if defined(BBHOST_HAVE_SDL3)

#endif

namespace {

// Bloodborne's pad, for the record, since every row below is one of these:
// R1 attacks, R2 is the strong attack, L1 transforms the weapon, L2 is the
// firearm (and the parry), Circle dodges and sprints, Triangle drinks a Blood
// Vial ("KG_R_U: Restore HP (Uses Blood Vial)" in the game's own help text),
// Square uses the quick item ("KG_R_L: Use item"), Cross interacts, the d-pad
// switches weapons and items, R3 locks on, the touchpad's two sides are
// Gestures and Personal Effects. That is the US disc's layout; the Japanese
// region swaps Circle and Cross, in play and in the menus alike, and the
// region comes from param.sfo (hle/system.cpp).
constexpr std::uint32_t kL3 = 0x2, kR3 = 0x4, kOptions = 0x8, kUp = 0x10, kRight = 0x20, kDown = 0x40, kLeft = 0x80,
                        kL2 = 0x100, kR2 = 0x200, kL1 = 0x400, kR1 = 0x800, kTriangle = 0x1000, kCircle = 0x2000,
                        kCross = 0x4000, kSquare = 0x8000, kTouchPad = 0x100000;

// The defaults are DS3's shape - WASD, Space to roll, E to interact, R for the
// healing item, F for the quick item, Q to lock on, Shift as the strong
// modifier, the mouse's buttons for the hands - put on Bloodborne's buttons.
const BindingInfo kInfo[kBindCount] = {
    {"move_forward", "Move Forward", "Move the character forward.", 0, "W", 0},
    {"move_back", "Move Back", "Move the character backward.", 0, "S", 0},
    {"move_left", "Move Left", "Move the character left.", 0, "A", 0},
    {"move_right", "Move Right", "Move the character right.", 0, "D", 0},
    // Held with the movement keys, as Dark Souls III's Left Alt: a walk.
    {"walk", "Walk", "Hold with movement to walk slowly.", 0, "Left Alt", 0},
    {"roll", "Dodge / Sprint", "Tap to dodge; hold while moving to sprint.", kCircle, "Space", 0},
    {"lock_on", "Lock On", "Lock on to a target or center the camera.", kR3, "Q", 0},

    {"attack", "Attack", "Perform a light attack with the right-hand weapon.", kR1, nullptr, 1},
    {"strong_attack", "Strong Attack", "Perform a strong attack; hold to charge.", kR2, nullptr, 0},
    {"strong_modifier", "Strong Attack Modifier", "Hold with light attack to perform a strong attack.", 0, "Left Shift", 0},
    {"transform", "Transform", "Transform the trick weapon or perform a transformation attack.", kL1, "F", 0},
    {"firearm", "Firearm / Parry", "Fire the left-hand weapon to attack or interrupt an enemy.", kL2, "Left Ctrl", 3},
    {"blood_vial", "Blood Vial", "Restore health using a Blood Vial.", kTriangle, "C", 0},
    {"use_item", "Use Item", "Use the selected quick item.", kSquare, "R", 0},

    {"interact", "Interact", "Talk, examine, pick up items, open doors, and interact.", kCross, "E", 0},
    {"switch_item", "Next Item", "Select the next equipped quick item.", kDown, "Down", 7},
    {"switch_right", "Switch Right Weapon", "Switch between equipped right-hand weapons.", kRight, "Right", 0},
    {"switch_left", "Switch Left Weapon", "Switch between equipped left-hand weapons.", kLeft, "Left", 0},
    {"dpad_up", "Blood Bullets", "Create Blood Bullets at the cost of health.", kUp, "T", 0},
    // The touchpad is a button with a side: the game's key guide reads
    // "<KG_TP_L> : Gestures" and "<KG_TP_R> : Personal Effects". A press alone
    // cannot say which, so these place a finger as well (apply below).
    {"gestures", "Gestures", "Open the gestures menu.", kTouchPad, "G", 0},
    {"personal_effects", "Personal Effects", "Open the personal effects menu.", kTouchPad, "Tab", 0},

    {"look_up", "Look Up", "Rotate the camera upward.", 0, "I", 0},
    {"look_down", "Look Down", "Rotate the camera downward.", 0, "K", 0},
    {"look_left", "Look Left", "Rotate the camera left.", 0, "J", 0},
    {"look_right", "Look Right", "Rotate the camera right.", 0, "L", 0},
    {"menu", "Menu", "Open or close the game menu.", kOptions, "Escape", 0},
    // Menus need an OK and a Back that are not the gameplay keys sharing their
    // pad bits: nobody reaches for Space to accept or E to back out.
    // Their buttons are the region's (engine/menu_pointer.h), set in apply.
    {"confirm", "Menu Confirm", "Confirm the selected menu entry.", 0, "Return", 0},
    {"back", "Menu Back", "Return to the previous menu.", 0, "Backspace", 0},

    {"l3", "Left Stick Press", "Activate the left stick press action.", kL3, "Z", 0},
    // The developers' debug menu: not a pad press at all. The patch opens it
    // on the touchpad's left side, which is also Gestures; this key toggles
    // the menu itself (engine/debug_menu.h), when the Debug Menu plugin is
    // on. The Key Bindings screen lists it only then.
    {"previous_item", "Previous Item", "Select the previous equipped quick item.", kDown, nullptr, 6},
    {"debug_menu", "Debug Menu", "Open or close the developer debug menu when enabled.", 0, "`", 0},
};

// Earlier names a bbhost.toml may still use.
const struct {
    const char* old_name;
    int action;
} kAliases[] = {
    {"dpad_down", kBindSwitchItem}, {"dpad_left", kBindSwitchLeft}, {"dpad_right", kBindSwitchRight},
};

std::atomic<int> g_key[kBindCount];
std::atomic<int> g_mouse[kBindCount];

// bbport: more than a key and a mouse button, and combinations. Each action's key (g_key) may
// need modifiers held with it (g_key_mods), and up to kMore further inputs follow (g_more): a key
// or a mouse button, each with its modifiers - "E, Shift+Q, Mouse2, Ctrl+Mouse4". A chord packs
// the scancode or mouse input (bits 0-9), whether it is the mouse (bit 10) and the modifiers.
constexpr int kMore = 3;
constexpr std::uint32_t kChordCode = 0x3ff, kChordMouse = 1u << 10, kModCtrl = 1u << 11,
                        kModShift = 1u << 12, kModAlt = 1u << 13, kMods = kModCtrl | kModShift | kModAlt;
std::atomic<std::uint32_t> g_key_mods[kBindCount];
std::atomic<std::uint32_t> g_more[kBindCount][kMore];
// The modifiers the keyboard held at its last read, for the mouse's combinations.
std::atomic<std::uint32_t> g_mods_now{0};
// A modifier pressed during a capture: bound alone when released first, or the modifier of the
// next key or mouse button.
std::atomic<int> g_capture_modifier{0};

std::atomic<int> g_capture{-1};
// The Debug Menu key: held per device (keyboard, mouse), since each device's
// actions are read on their own, and a press queued for the pad read.
std::atomic<bool> g_debug_held[2], g_debug_toggle{false};

void note_debug_key(int device, bool down) {
    if (!down) {
        g_debug_held[device].store(false, std::memory_order_relaxed);
    } else if (!g_debug_held[device].exchange(true) && debug_menu_active()) {
        g_debug_toggle.store(true);
    }
}
std::atomic<int> g_capture_done{-1};
std::atomic<bool> g_hold{false};  // after a capture: blocked until nothing is held
std::atomic<std::int64_t> g_hold_ms{0};  // when that hold began
std::atomic<std::int64_t> g_capture_ms{0};  // when the capture began

std::int64_t now_ms() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch())
        .count();
}

int scancode_from_name(const std::string& name) {
#if defined(BBHOST_HAVE_SDL3)
    if(name=="Keypad Plus")return SDL_GetScancodeFromName("Keypad +");
    if(name=="Comma")return SDL_GetScancodeFromName(",");
    if(name=="Keypad Comma")return SDL_GetScancodeFromName("Keypad ,");
    return static_cast<int>(SDL_GetScancodeFromName(name.c_str()));
#else
    (void)name;
    return 0;
#endif
}

const char* scancode_name(int sc) {
#if defined(BBHOST_HAVE_SDL3)
    const char* n = SDL_GetScancodeName(static_cast<SDL_Scancode>(sc));
    return n && *n ? n : nullptr;
#else
    (void)sc;
    return nullptr;
#endif
}

// Mouse 1..5 are the buttons in SDL's numbering, as input.cpp reads them (1 left, 2 middle,
// 3 right); 6 and 7 the wheel turned up and down, which the pad read makes a short press of
// each notch (hle/system.cpp).
constexpr int kMouseInputs = 7;
const char* const kMouseNames[kMouseInputs + 1] = {nullptr,  "Left Button", "Middle Button", "Right Button",
                                                   "Button 4", "Button 5", "Wheel Up", "Wheel Down"};
// The same where the name has to fit in a sentence.
const char* const kMouseShort[kMouseInputs + 1] = {nullptr, "L Mouse", "M Mouse", "R Mouse", "Mouse 4", "Mouse 5", "Wheel Up", "Wheel Down"};
// Their names in [keys]: Mouse1..Mouse5, WheelUp, WheelDown.
std::string mouse_token(int m) { return m == 6 ? "WheelUp" : m == 7 ? "WheelDown" : "Mouse" + std::to_string(m); }

std::string trim(std::string v) {
    while (!v.empty() && (v.back() == ' ' || v.back() == '\t' || v.back() == '"')) v.pop_back();
    std::size_t i = 0;
    while (i < v.size() && (v[i] == ' ' || v[i] == '\t' || v[i] == '"')) ++i;
    return v.substr(i);
}

int action_named(const char* name) {
    for (int a = 0; a < kBindCount; ++a) {
        if (std::strcmp(name, kInfo[a].key) == 0) return a;
    }
    for (const auto& al : kAliases) {
        if (std::strcmp(name, al.old_name) == 0) return al.action;
    }
    return -1;
}

// Bumped by every change, for consumers that cache what they built from a
// binding (host_bindings_serial).
std::atomic<std::uint64_t> g_serial{1};

bool is_modifier(int sc) {
#if defined(BBHOST_HAVE_SDL3)
    return sc == SDL_SCANCODE_LCTRL || sc == SDL_SCANCODE_RCTRL || sc == SDL_SCANCODE_LSHIFT ||
           sc == SDL_SCANCODE_RSHIFT || sc == SDL_SCANCODE_LALT || sc == SDL_SCANCODE_RALT;
#else
    (void)sc;
    return false;
#endif
}

// The modifiers held, from SDL's key state (either side's key).
std::uint32_t mods_held(const bool* keys, int nkeys) {
    std::uint32_t mods = 0;
#if defined(BBHOST_HAVE_SDL3)
    const auto down = [&](int sc) { return sc < nkeys && keys[sc]; };
    if (down(SDL_SCANCODE_LCTRL) || down(SDL_SCANCODE_RCTRL)) mods |= kModCtrl;
    if (down(SDL_SCANCODE_LSHIFT) || down(SDL_SCANCODE_RSHIFT)) mods |= kModShift;
    if (down(SDL_SCANCODE_LALT) || down(SDL_SCANCODE_RALT)) mods |= kModAlt;
#else
    (void)keys;
    (void)nkeys;
#endif
    return mods;
}

std::string mods_text(std::uint32_t chord) {
    std::string text;
    if (chord & kModCtrl) text += "Ctrl+";
    if (chord & kModShift) text += "Shift+";
    if (chord & kModAlt) text += "Alt+";
    return text;
}

// A chord as lantern.ini writes it ("Shift+E", "Ctrl+Mouse2"), or "" when it names nothing.
std::string chord_token(std::uint32_t chord) {
    const int code = int(chord & kChordCode);
    if (!code) return "";
    if (chord & kChordMouse) return mods_text(chord) + mouse_token(code);
    const char* n = scancode_name(code);
    if (!n) return "";
    std::string name=n;
    if(name==",")name="Comma"; else if(name=="Keypad ,")name="Keypad Comma"; else if(name=="Keypad +")name="Keypad Plus";
    return mods_text(chord)+name;
}

// "Shift+E", "Ctrl+Mouse2", "WheelUp": a chord; 0 when the key is not an SDL key name.
std::uint32_t parse_chord(std::string tok) {
    std::uint32_t mods = 0;
    for (bool more = true; more;) {
        more = false;
        for (const auto& [prefix, bit] : {std::pair{"Ctrl+", kModCtrl}, {"Control+", kModCtrl},
                                          {"Shift+", kModShift}, {"Alt+", kModAlt}}) {
            const std::size_t n = std::strlen(prefix);
            if (tok.size() > n && SDL_strncasecmp(tok.c_str(), prefix, n) == 0) {
                mods |= bit;
                tok = trim(tok.substr(n));
                more = true;
            }
        }
    }
    if (tok.size() == 6 && tok.rfind("Mouse", 0) == 0 && tok[5] >= '1' && tok[5] <= '5') {
        return mods | kChordMouse | std::uint32_t(tok[5] - '0');
    }
    if (tok == "WheelUp" || tok == "WheelDown") {
        return mods | kChordMouse | (tok == "WheelUp" ? 6u : 7u);
    }
    const int sc = scancode_from_name(tok);
    return sc > 0 && std::uint32_t(sc) <= kChordCode ? mods | std::uint32_t(sc) : 0;
}

// The action's inputs, in order: its key (with its modifiers), its mouse button, the others.
int chords_of(int a, std::uint32_t out[2 + kMore]) {
    int n = 0;
    if (const int key = g_key[a].load(std::memory_order_relaxed)) {
        out[n++] = std::uint32_t(key) | g_key_mods[a].load(std::memory_order_relaxed);
    }
    if (const int mouse = g_mouse[a].load(std::memory_order_relaxed)) {
        out[n++] = kChordMouse | std::uint32_t(mouse);
    }
    for (int i = 0; i < kMore; ++i) {
        if (const std::uint32_t chord = g_more[a][i].load(std::memory_order_relaxed)) out[n++] = chord;
    }
    return n;
}

// "E", "Mouse2", "X, Mouse2", "E, Shift+Q, Mouse2", "none": the whole binding. The first key is
// the action's key and the first mouse button without modifiers its mouse button (what the in-game
// Key Bindings page shows and rebinds); the rest, up to kMore, follow.
void set_from_text(int a, const std::string& text, const char* where) {
    Chords::Binding chord;
    if(text.find(" + ")!=std::string::npos && text.find(',')!=std::string::npos){
      Chords::Binding combined;combined.text=text;
      std::size_t start=0;
      while(start<=text.size()){
       auto end=text.find(',',start);auto part=trim(text.substr(start,end==std::string::npos?end:end-start));
       Chords::Binding branch;branch.text=part;
       for(const auto& prefix:{std::pair{"Ctrl+","Left Ctrl + "},std::pair{"Shift+","Left Shift + "},std::pair{"Alt+","Left Alt + "}})
        for(std::size_t pos=0;(pos=part.find(prefix.first,pos))!=std::string::npos;) {part.replace(pos,std::strlen(prefix.first),prefix.second);pos+=std::strlen(prefix.second);}
       std::size_t pos=0;
       while(pos<=part.size()){
        auto next=part.find(" + ",pos);auto atom=trim(part.substr(pos,next==std::string::npos?next:next-pos));
        if(atom.rfind("Mouse",0)==0&&atom.size()==6&&atom[5]>='1'&&atom[5]<='5')branch.mouse|=1u<<(atom[5]-'1');
        else if(atom=="WheelUp"||atom=="WheelDown")branch.mouse|=1u<<(atom=="WheelUp"?5:6);
        else if(int code=scancode_from_name(atom))branch.keys.push_back(code);
        else {host_log("keys: invalid alternative token %s",atom.c_str());return;}
        if(next==std::string::npos)break;pos=next+3;
       }
       for(int code:branch.keys)if(std::find(combined.keys.begin(),combined.keys.end(),code)==combined.keys.end())combined.keys.push_back(code);
       combined.mouse|=branch.mouse;combined.alternatives.push_back(std::move(branch));
       if(end==std::string::npos)break;start=end+1;
      }
      Chords::Set(a,std::move(combined));g_key[a]=0;g_mouse[a]=0;g_key_mods[a]=0;
      for(auto& more:g_more[a])more=0;g_serial.fetch_add(1);return;
    }
    if(text.find(" + ")!=std::string::npos){
      std::size_t pos=0;
      while(pos<text.size()){
       auto end=text.find(" + ",pos);auto tok=trim(text.substr(pos,end==std::string::npos?end:end-pos));
       if(tok.size()==6&&tok.rfind("Mouse",0)==0&&tok[5]>='1'&&tok[5]<='5')chord.mouse|=1u<<(tok[5]-'1');
       else if(tok=="WheelUp"||tok=="WheelDown")chord.mouse|=1u<<(tok=="WheelUp"?5:6);
       else{int k=scancode_from_name(tok);if(!k){host_log("keys: invalid chord token %s",tok.c_str());return;}if(std::find(chord.keys.begin(),chord.keys.end(),k)==chord.keys.end())chord.keys.push_back(k);}
       if(end==std::string::npos)break;pos=end+3;
      }
      std::sort(chord.keys.begin(),chord.keys.end());chord.text=text;Chords::Set(a,chord);g_key[a]=0;g_mouse[a]=0;g_key_mods[a]=0;for(auto& more:g_more[a])more=0;g_serial.fetch_add(1);return;
    }
    Chords::Clear(a);
    int key = 0, mouse = 0;
    std::uint32_t key_mods = 0, more[kMore] = {};
    int n_more = 0;
    std::size_t pos = 0;
    while (pos <= text.size()) {
        std::size_t end = text.find(',', pos);
        if (end == std::string::npos) end = text.size();
        const std::string tok = trim(text.substr(pos, end - pos));
        pos = end + 1;
        if (tok.empty() || tok == "none") continue;
        const std::uint32_t chord = parse_chord(tok);
        if (!chord) {
            host_log("keys: %s: %s = \"%s\" - \"%s\" is not an SDL key name", where, kInfo[a].key, text.c_str(),
                     tok.c_str());
            continue;
        }
        if (!(chord & kChordMouse) && !key) {
            key = int(chord & kChordCode);
            key_mods = chord & kMods;
        } else if ((chord & kChordMouse) && !(chord & kMods) && !mouse) {
            mouse = int(chord & kChordCode);
        } else if (n_more < kMore) {
            more[n_more++] = chord;
        } else {
            host_log("keys: %s: %s has more than %d inputs; \"%s\" left out", where, kInfo[a].key, 2 + kMore,
                     tok.c_str());
        }
    }
    g_key[a].store(key, std::memory_order_relaxed);
    g_key_mods[a].store(key_mods, std::memory_order_relaxed);
    g_mouse[a].store(mouse, std::memory_order_relaxed);
    for (int i = 0; i < kMore; ++i) g_more[a][i].store(more[i], std::memory_order_relaxed);
    g_serial.fetch_add(1, std::memory_order_relaxed);
}

}  // namespace

std::uint64_t host_bindings_serial() { return g_serial.load(std::memory_order_relaxed); }

const BindingInfo& host_binding_info(int action) { return kInfo[action < 0 || action >= kBindCount ? 0 : action]; }

void host_bindings_load_defaults() {
    for (int a = 0; a < kBindCount; ++a) {
        Chords::Clear(a);
        g_key[a].store(kInfo[a].def_key ? scancode_from_name(kInfo[a].def_key) : 0, std::memory_order_relaxed);
        g_key_mods[a].store(0, std::memory_order_relaxed);
        g_mouse[a].store(kInfo[a].def_mouse, std::memory_order_relaxed);
        for (auto& more : g_more[a]) more.store(0, std::memory_order_relaxed);
    }
    set_from_text(kBindStrongMod,"none","defaults");
    set_from_text(kBindStrong,"Left Shift + Mouse1","defaults");
    set_from_text(kBindLockOn,"Q, Mouse2","defaults");
    g_serial.fetch_add(1,std::memory_order_relaxed);
}

bool host_bindings_parse(const char* name, const char* value) {
    const int a = action_named(name);
    if (a < 0) return false;
    set_from_text(a, value, "options");
    return true;
}

void host_bindings_save(std::FILE* f) {

    for (int a = 0; a < kBindCount; ++a) {
        std::fprintf(f, "bind.%s=%s\n", kInfo[a].key, host_binding_ini_value(a).c_str());
    }
}

std::string host_binding_ini_value(int a) {
    auto custom=Chords::Get(a); if(custom.active())return custom.text;
    std::uint32_t chords[2 + kMore];
    std::string value;
    for (int i = 0, n = chords_of(a, chords); i < n; ++i) {
        const std::string token = chord_token(chords[i]);
        if (token.empty()) continue;
        if (!value.empty()) value += ", ";
        value += token;
    }
    return value.empty() ? "none" : value;
}
int host_binding_key(int action) { if(action<0||action>=kBindCount)return 0;auto chord=Chords::Get(action);return chord.active()&&!chord.keys.empty()?chord.keys.front():g_key[action].load(); }
int host_binding_mouse(int action) { if(action<0||action>=kBindCount)return 0;auto chord=Chords::Get(action);if(chord.active())for(int m=1;m<=7;++m)if(chord.mouse&(1u<<(m-1)))return m;return g_mouse[action].load(); }

void host_binding_describe(int action, char* out, std::size_t n) {
    if (!n) return;
    auto custom=Chords::Get(action);if(custom.active()){std::snprintf(out,n,"%s",custom.text.c_str());return;}
    if (action < 0 || action >= kBindCount) {
        std::snprintf(out, n, "Unbound");
        return;
    }
    std::uint32_t chords[2 + kMore];
    std::string text;
    for (int i = 0, count = chords_of(action, chords); i < count; ++i) {
        const int code = int(chords[i] & kChordCode);
        const char* name = nullptr;
        if (chords[i] & kChordMouse) {
            name = code >= 1 && code <= kMouseInputs ? kMouseNames[code] : nullptr;
        } else {
            name = scancode_name(code);
            // A lone ` is a speck in the menu's font.
            if (name && std::strcmp(name, "`") == 0) name = "` (backtick)";
        }
        if (!name) continue;
        if (!text.empty()) text += "  /  ";
        text += mods_text(chords[i]) + name;
    }
    std::snprintf(out, n, "%s", text.empty() ? "Unbound" : text.c_str());
}

void host_binding_prompt(int action, char* out, std::size_t n) {
    if (!n) return;
    auto chord=Chords::Get(action);if(chord.active()){std::snprintf(out,n,"%s",chord.text.c_str());return;}
    out[0] = '\0';
    if (action < 0 || action >= kBindCount) return;
    const char* k = scancode_name(host_binding_key(action));
    const int m = host_binding_mouse(action);
    // The key first: a player who bound both reads the key in a sentence more
    // easily than "Left Button", and the prompt has room for one.
    if (k) {
        std::snprintf(out, n, "%s%s", mods_text(g_key_mods[action].load(std::memory_order_relaxed)).c_str(), k);
    } else if (m >= 1 && m <= kMouseInputs) {
        std::snprintf(out, n, "%s", kMouseShort[m]);
    }
}

namespace {

// Keyboard rollover is resolved by press order, not by interpolating a stick
// or inserting neutral input. The physical controller never enters this path.
constexpr int kOpposites[4][2] = {
    {kBindMoveF, kBindMoveB}, {kBindMoveL, kBindMoveR}, {kBindLookU, kBindLookD}, {kBindLookL, kBindLookR}};
std::atomic<std::uint64_t> key_order[512]{};
std::atomic<bool> key_down[512]{};
std::atomic<std::uint64_t> direction_clock{0};
bool movement_was[4]{};
std::uint64_t movement_order[4]{};

// Walk held on a mouse button, for the keyboard's movement keys (the mouse is
// read on the pad read, the keyboard on the window's update).
std::atomic<bool> g_walk_on_mouse{false};

// How far the movement keys push the stick while Walk is held, of a full push.
// Measured in the Hunter's Dream: under 35% the character stands, from 40% to
// 80% it walks (1.65 m/s), at a full push it runs (3.7 m/s) - so the middle of
// the walk, with room either side.
constexpr float kWalkPush = 0.6f;
SprintTurn::State sprint_turn;

}  // namespace

namespace {
// The actions whose keys (or, with `mouse`, mouse inputs) are held with their modifiers. A chord
// held with more modifiers wins over one of the same key with fewer: with E on Interact and
// Shift+E on another action, Shift+E is only the other one. Extra modifiers do not stop a chord
// (E with Shift held for the strong attack still interacts).
template <typename Down>
void held_chords(bool mouse, Down down, std::uint32_t mods, bool held[kBindCount]) {
    struct Held {
        int action;
        std::uint32_t code, mods;
    };
    Held found[kBindCount * (2 + kMore)];
    int count = 0;
    for (int a = 0; a < kBindCount; ++a) {
        held[a] = false;
        std::uint32_t chords[2 + kMore];
        for (int i = 0, n = chords_of(a, chords); i < n; ++i) {
            const std::uint32_t chord = chords[i];
            if (((chord & kChordMouse) != 0) != mouse) continue;
            const std::uint32_t code = chord & kChordCode, need = chord & kMods;
            if (down(code) && (need & ~mods) == 0) found[count++] = {a, code, need};
        }
    }
    for (int i = 0; i < count; ++i) {
        bool beaten = false;
        for (int j = 0; j < count && !beaten; ++j) {
            beaten = found[j].code == found[i].code && found[j].mods != found[i].mods &&
                     (found[j].mods & found[i].mods) == found[i].mods;
        }
        if (!beaten) held[found[i].action] = true;
    }
}
}  // namespace

void host_bindings_keys_held(const bool* keys, int nkeys, bool held[kBindCount]) {
    const std::uint32_t mods = mods_held(keys, nkeys);
    g_mods_now.store(mods, std::memory_order_relaxed);
    held_chords(false, [&](std::uint32_t sc) { return sc > 0 && int(sc) < nkeys && keys[sc]; }, mods, held);
    for (int i=2;i<4;++i) {
        const auto& pair=kOpposites[i];
        if (held[pair[0]] && held[pair[1]]) held[pair[0]] = held[pair[1]] = false;
    }
    held[kBindWalk] = held[kBindWalk] || g_walk_on_mouse.load(std::memory_order_relaxed);
    note_debug_key(0, held[kBindDebugMenu]);
}

void host_bindings_key_event(int sc, bool down) {
    if (sc <= 0 || sc >= 512) return;
    const bool was = key_down[sc].exchange(down);
    if (down && !was) key_order[sc] = direction_clock.fetch_add(1) + 1;
}

void host_bindings_reset_key_events() {
    for (int i=0;i<512;++i) {key_down[i]=false;key_order[i]=0;}
}

void host_bindings_resolve_movement(bool held[kBindCount]) {
    const auto frame = direction_clock.fetch_add(1) + 1;
    for (int action = 0; action < 4; ++action) {
        if (held[action]) {
            std::uint64_t order = 0;
            const int key = host_binding_key(action);
            if (key > 0 && key < 512) order = key_order[key].load();
            const auto chord = Chords::Get(action);
            for (int sc : chord.keys)
                if (sc > 0 && sc < 512) order = std::max(order, key_order[sc].load());
            if (!movement_was[action] || order > movement_order[action])
                movement_order[action] = order ? order : frame;
        }
        movement_was[action] = held[action];
    }
    for (int axis = 0; axis < 2; ++axis) {
        const auto& pair = kOpposites[axis];
        if (!held[pair[0]] || !held[pair[1]]) continue;
        const auto a = movement_order[pair[0]], b = movement_order[pair[1]];
        held[pair[0]] = a > b;
        held[pair[1]] = b > a;
    }
}

void host_bindings_mouse_held(std::uint32_t buttons, bool held[kBindCount]) {
    held_chords(
        true, [&](std::uint32_t m) { return m >= 1 && m <= std::uint32_t(kMouseInputs) && (buttons & (1u << (m - 1))); },
        g_mods_now.load(std::memory_order_relaxed), held);
    g_walk_on_mouse.store(held[kBindWalk], std::memory_order_relaxed);
    note_debug_key(1, held[kBindDebugMenu]);
}

void host_bindings_apply(const bool held[kBindCount], bool strong, PadState& p) {
    bool any = false;
    for (int a = 0; a < kBindCount; ++a) {
        // The Debug Menu key sets no pad bit: its press was queued when it was
        // read, and the pad read toggles the menu on the game's thread
        // (host_bind_take_debug_toggle).
        if (!held[a] || a == kBindDebugMenu) continue;
        any = true;
        std::uint32_t bit = kInfo[a].button;
        // Shift is the strong-attack modifier, as in DS3: one key rather than
        // a second binding for every attack.
        if (a == kBindAttack && strong) bit = kR2;
        if (a == kBindConfirm) bit = menu_confirm_button();
        if (a == kBindBack) bit = menu_back_button();
        p.buttons |= bit;
        if (bit == kR2) p.r2 = 255;  // the triggers are analogue as well as buttons
        if (bit == kL2) p.l2 = 255;
        switch (a) {
            case kBindLookU: p.ry = kStickLow; break;
            case kBindLookD: p.ry = kStickHigh; break;
            case kBindLookL: p.rx = kStickLow; break;
            case kBindLookR: p.rx = kStickHigh; break;
            case kBindGestures:
            case kBindEffects:
                // One quarter in from the edge, half way down, and a stable id
                // for as long as it is held: the game tracks fingers by id, so a
                // new one every frame would read as tapping.
                if (p.touch_count < 2) {
                    PadState::Touch& t = p.touch[p.touch_count++];
                    t.x = static_cast<std::uint16_t>(a == kBindEffects ? kPadTouchW * 3 / 4 : kPadTouchW / 4);
                    t.y = static_cast<std::uint16_t>(kPadTouchH / 2);
                    t.id = 200;
                    t.down = true;
                }
                break;
            default: break;
        }
    }
    // Keep filtered directional input in every keyboard movement mode; a
    // W/S or A/D reversal remains an immediate native skid request.
    const int x = (held[kBindMoveR] ? 1 : 0) - (held[kBindMoveL] ? 1 : 0);
    const int y = (held[kBindMoveB] ? 1 : 0) - (held[kBindMoveF] ? 1 : 0);
    const auto now = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
    const auto& settings=BbSettings::Get();
    auto axes = SprintTurn::Apply(sprint_turn, x, y, held[kBindRoll] && !held[kBindWalk], now,
        {settings.stick_turn_rate,0,settings.stick_circular});
    if (axes.x != 0 || axes.y != 0) {
        if (held[kBindWalk]) {
            // Preserve the filtered direction while limiting slow-walk intensity.
            const double scale = (127*kWalkPush)/std::hypot(double(axes.x),double(axes.y));
            axes = {int(std::lround(axes.x*scale)),int(std::lround(axes.y*scale))};
        }
        p.lx = static_cast<std::uint8_t>(kStickRest + axes.x);
        p.ly = static_cast<std::uint8_t>(kStickRest + axes.y);
    }
    if (any) p.connected = true;
}

namespace {
std::mutex capture_lock;
std::vector<std::string> capture_tokens;
std::atomic<bool> capture_wait_neutral{true};
void capture_add(std::string token){if(token=="Keypad +")token="Keypad Plus";if(token==",")token="Comma";if(token=="Keypad ,")token="Keypad Comma";std::lock_guard guard(capture_lock);if(std::find(capture_tokens.begin(),capture_tokens.end(),token)==capture_tokens.end())capture_tokens.push_back(std::move(token));}
}

void host_bind_capture_begin(int action) {
    if (action < 0 || action >= kBindCount) return;
    {std::lock_guard guard(capture_lock);capture_tokens.clear();}
    capture_wait_neutral=true;
    g_capture_ms.store(now_ms(), std::memory_order_relaxed);
    g_capture.store(action, std::memory_order_relaxed);
    host_log("keys: rebinding %s - accumulate keys/clicks, release all to finish (Escape cancels, Delete unbinds)",
             kInfo[action].key);
}

int host_bind_capturing() { return g_capture.load(std::memory_order_relaxed); }

namespace {
void finish(int a, const char* what) {
    g_serial.fetch_add(1, std::memory_order_relaxed);
    g_capture.store(-1, std::memory_order_relaxed);
    g_hold_ms.store(now_ms(), std::memory_order_relaxed);
    g_hold.store(true, std::memory_order_relaxed);
    g_capture_done.store(a, std::memory_order_relaxed);
    char d[96];
    host_binding_describe(a, d, sizeof(d));
    host_log("keys: %s %s, now %s", kInfo[a].key, what, d);
}
}  // namespace

bool host_bind_capture_key(int scancode, unsigned sdl_mods) {
    (void)sdl_mods;
    const int a=g_capture.load();if(a<0)return false;
    if(scancode==SDL_SCANCODE_ESCAPE){finish(a,"unchanged");return true;}
    if(capture_wait_neutral)return true;
    if(scancode==SDL_SCANCODE_DELETE){host_binding_clear(a);finish(a,"unbound");return true;}
    if(native_reserved_key(scancode)){host_log("keys: reserved port hotkey; choose another key");return true;}
    const char* name=scancode_name(scancode);if(name)capture_add(name);
    return true;
}
bool host_bind_capture_key_up(int scancode) { (void)scancode; return g_capture.load() >= 0; }
bool host_bind_capture_mouse(int button) {
    if(g_capture.load()<0||button<1||button>kMouseInputs)return false;
    if(capture_wait_neutral || now_ms()-g_capture_ms.load()<300)return true;
    capture_add(mouse_token(button));return true;
}

int host_bind_capture_take_done() { return g_capture_done.exchange(-1); }
std::string host_bind_capture_preview(){std::lock_guard guard(capture_lock);std::string text;for(auto& token:capture_tokens){if(!text.empty())text+=" + ";text+=token;}return text.empty()?"Press combination...":text+" ...";}

bool host_bind_take_debug_toggle() { return g_debug_toggle.exchange(false); }

bool host_bind_capture_blocking() {
    return g_capture.load(std::memory_order_relaxed) >= 0 || g_hold.load(std::memory_order_relaxed);
}

namespace {
std::atomic<std::int64_t> g_clear_ms{0};
}  // namespace

void host_bind_note_clear_key() { g_clear_ms.store(now_ms(), std::memory_order_relaxed); }

bool host_bind_take_clear_key() {
    const std::int64_t at = g_clear_ms.exchange(0);
    return at && now_ms() - at < 250;
}

void host_binding_clear(int action) {
    if (action < 0 || action >= kBindCount) return;
    Chords::Clear(action);
    g_key[action].store(0, std::memory_order_relaxed);
    g_key_mods[action].store(0, std::memory_order_relaxed);
    g_mouse[action].store(0, std::memory_order_relaxed);
    for (auto& more : g_more[action]) more.store(0, std::memory_order_relaxed);
    g_serial.fetch_add(1, std::memory_order_relaxed);
    host_log("keys: %s cleared", kInfo[action].key);
}

void host_bind_capture_note_held(bool any_down) {
    if(g_capture.load()>=0 && !any_down){
      if(capture_wait_neutral.exchange(false))return;
      std::string value;
      {std::lock_guard guard(capture_lock);for(const auto& token:capture_tokens){if(!value.empty())value+=" + ";value+=token;}}
      if(!value.empty()){
       const int a=g_capture.load();set_from_text(a,value,"capture");
       const auto chord=Chords::Get(a);
       for(int b=0;b<kBindCount;++b)if(b!=a){
        auto other=Chords::Get(b);
        if(chord.active()&&other.active()&&chord.keys==other.keys&&chord.mouse==other.mouse)host_binding_clear(b);
        else if(!chord.active()&&!other.active()){
         if(g_key[a]&&g_key[a]==g_key[b])g_key[b]=0;
         if(g_mouse[a]&&g_mouse[a]==g_mouse[b])g_mouse[b]=0;
        }
       }
       finish(a,"rebound");
      }
    }
    if(g_hold.load()&&(!any_down||now_ms()-g_hold_ms.load()>1500))g_hold=false;
}
