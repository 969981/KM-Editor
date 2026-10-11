// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_camera.hpp"

extern "C" {
__attribute__((visibility("hidden"))) uintptr_t km_swsh_camera_init_continue;
__attribute__((visibility("hidden"))) uintptr_t km_swsh_camera_update_continue;
__attribute__((visibility("hidden"))) uintptr_t km_swsh_camera_push_continue;
__attribute__((visibility("hidden"))) uintptr_t km_swsh_camera_queue_continue;
bool km_swsh_camera_init_original(uintptr_t);
void km_swsh_camera_update_original(uintptr_t);
void km_swsh_camera_push_original(uintptr_t, uintptr_t);
void km_swsh_camera_queue_original(uintptr_t, uintptr_t);
extern volatile uint64_t km_swsh_effective_snapshot;
__attribute__((visibility("hidden"))) uint8_t km_swsh_camera_scenery_active;
void km_swsh_camera_terrain_distance();
void km_swsh_camera_model_distance();
void km_swsh_camera_instance_distance();
void km_swsh_camera_instance_density_distance();
void km_swsh_camera_actor_distance();
}

namespace {
template<class T> T& Field(uintptr_t object, size_t offset) {
    return *reinterpret_cast<T*>(object + offset);
}
uintptr_t g_main;
uintptr_t g_delta;
uintptr_t g_manager;
uint64_t g_area;
uintptr_t g_handles[3];
bool g_default_extended;
bool g_forced;
bool g_ready;
uint32_t g_branches[4][4];
uint32_t g_scenery_branches[5];

uintptr_t Singleton(size_t relocation) {
    const auto slot = Field<uintptr_t>(g_main, relocation);
    return slot == 0 ? 0 : Field<uintptr_t>(slot, 0);
}

uintptr_t ForegroundScheduler() {
    const auto context = Singleton(0x02610848);
    return context == 0 ? 0 : Field<uintptr_t>(context, 0x68);
}

uint64_t CurrentArea() {
    // The native camera initializer reads this same area identity before
    // loading its controllers (Sword CF9478, Shield CF94A8).
    const auto field = Singleton(0x02617C48);
    return field == 0 ? 0 : Field<uint64_t>(field, 0xF0);
}

constexpr uint64_t AreaHash(const char* name) {
    uint64_t hash = 0xCBF29CE484222645ULL;
    while (*name != 0) hash = (hash ^ static_cast<uint8_t>(*name++)) * 0x100000001B3ULL;
    return hash;
}

bool OutdoorArea(uint64_t area) {
    // Verified placement area identities, shared by Sword and Shield 1.3.2.
    // Buildings have separate areas. Unknown areas retain their native camera;
    // do not infer that an area is outdoors merely because it allows bicycles,
    // weather, or an extended camera controller to be constructed.
    static constexpr uint64_t outdoors[] = {
        AreaHash("a_0101"), AreaHash("a_0201"), AreaHash("a_0301"),
        AreaHash("a_0401"), AreaHash("a_0501"), AreaHash("a_0601"),
        AreaHash("a_0701"), AreaHash("a_0801"), AreaHash("a_0802"),
        AreaHash("a_0901"), AreaHash("a_1001"), AreaHash("a_1101"),
        AreaHash("a_1106"), AreaHash("a_1201"),
        AreaHash("a_d0101"), AreaHash("a_d0102"), // Slumbering Weald
        AreaHash("a_d0401"), // Glimwood Tangle
        AreaHash("a_wr0101"), AreaHash("a_wr0201"), AreaHash("a_wr0301"),
    };
    for (const auto candidate : outdoors) if (area == candidate) return true;
    return false;
}

bool MatchesManager(uintptr_t manager) {
    if (!g_ready || manager == 0 || manager != g_manager
        || manager != Singleton(0x026194E8)) return false;
    for (size_t i = 0; i < 3; ++i) {
        if (Field<uintptr_t>(manager, 0x50 + i * 8) != g_handles[i]) return false;
    }
    return true;
}

// Hold the native weak references throughout a handoff. Their control blocks
// remain owned by the live manager; the atomic acquisition prevents resurrection.
class CameraReference {
public:
    explicit CameraReference(uintptr_t weak) {
        if (weak == 0) return;
        auto* count = &Field<uint32_t>(weak, 0x54);
        auto value = __atomic_load_n(count, __ATOMIC_ACQUIRE);
        while (value != 0 && value != UINT32_MAX) {
            if (__atomic_compare_exchange_n(count, &value, value + 1, false,
                                            __ATOMIC_ACQUIRE, __ATOMIC_RELAXED)) {
                const auto interface = Field<uintptr_t>(weak, 0x60);
                if (interface != 0) object = interface - 0x50;
                else __atomic_fetch_sub(count, 1, __ATOMIC_RELEASE);
                return;
            }
        }
    }
    ~CameraReference() {
        if (object != 0) {
            const auto interface = object + 0x48;
            reinterpret_cast<void (*)(uintptr_t)>(
                Field<uintptr_t>(Field<uintptr_t>(interface, 0), 0x18))(interface);
        }
    }
    CameraReference(const CameraReference&) = delete;
    CameraReference& operator=(const CameraReference&) = delete;
    uintptr_t object = 0;
};

bool HasAction(uintptr_t camera) {
    return reinterpret_cast<bool (*)(uintptr_t)>(
        Field<uintptr_t>(Field<uintptr_t>(camera, 0), 0xF8))(camera);
}

bool Requested() {
    const auto snapshot = __atomic_load_n(&km_swsh_effective_snapshot, __ATOMIC_ACQUIRE);
    km::SettingsValues values{};
    uint64_t presence = 0;
    return snapshot != 0 && km::UnpackSettingsSnapshot(snapshot, &values, &presence)
        && (km_swsh_installed_features() & 1) != 0
        && (presence & km::PresenceUnlockedCamera) != 0 && values.unlocked_camera;
}

void SelectExploration(uintptr_t manager, bool extended, bool entering_task) {
    // A real event owns all active flags until its native end. Changing only
    // its return selection lets that end restore the area's normal camera.
    if (Field<uint32_t>(manager, 0x6C) != 0) {
        if (entering_task && g_forced) {
            Field<uint8_t>(manager, 0x68) = g_default_extended;
            g_forced = false;
        }
        return;
    }
    const bool selected = Field<uint8_t>(manager, 0x68) != 0;
    if (selected == extended) {
        if (!extended) g_forced = false;
        return;
    }
    CameraReference fixed(g_handles[0]), orbit(g_handles[1]), event(g_handles[2]);
    if (fixed.object == 0 || orbit.object == 0 || event.object == 0) return;
    // Inactive native cameras retain action objects as part of their inactive
    // state. Only the current exploration camera can block this handoff.
    // Real event ownership was checked above before acquiring these references.
    if (HasAction(selected ? orbit.object : fixed.object)) return;
    // The fixed camera's target return blend must finish before orbit resumes.
    // An inactive orbit controller may retain an old blend; the native handoff
    // below replaces it using the newly returned field pose.
    const auto fixed_controller = Field<uintptr_t>(fixed.object, 0x4C0);
    if (extended && (Field<uint8_t>(fixed.object, 0x521) != 0
        || fixed_controller == 0 || Field<int32_t>(fixed_controller, 0xD8) != -1)) return;

    // Reuse the game's own pose conversion and return blend. This initializes
    // orbit angles, position relative to the target, and projection together, instead
    // of copying a visible pose into an uninitialized orbit controller.
    if (selected) {
        reinterpret_cast<void (*)(uintptr_t)>(g_main + 0x00CFE9D0 + g_delta)(manager);
    }
    reinterpret_cast<void (*)(uintptr_t)>(g_main + 0x00CFD500 + g_delta)(manager);
    if (Field<uint32_t>(manager, 0x6C) != 1) return;
    Field<uint8_t>(manager, 0x68) = extended;
    reinterpret_cast<void (*)(uintptr_t, uint32_t, uint32_t, float)>(
        g_main + 0x00CFDF50 + g_delta)(manager, extended ? 1U : 0U, 0, 30.0F);
    if (!extended) Field<uint8_t>(orbit.object, 0x4EC) = 0;
    g_forced = extended && !g_default_extended;
}

void Reconcile(uintptr_t manager) {
    if (!MatchesManager(manager) || g_default_extended) return;
    const auto scheduler = ForegroundScheduler();
    // An absent scheduler is a transition, not evidence of player control.
    const bool idle = scheduler != 0
        && Field<uintptr_t>(scheduler, 0x78) == 0
        && Field<uintptr_t>(scheduler, 0x80) == 0;
    const auto area = CurrentArea();
    const bool outdoors = area == g_area && OutdoorArea(area);
    const bool desired = Requested() && idle && outdoors;
    if (desired || g_forced) SelectExploration(manager, desired, !idle || !outdoors);
}

void BeforeTask(uintptr_t scheduler) {
    const auto manager = Singleton(0x026194E8);
    if (scheduler == ForegroundScheduler() && MatchesManager(manager) && g_forced) {
        __atomic_store_n(&km_swsh_camera_scenery_active, 0, __ATOMIC_RELEASE);
        SelectExploration(manager, g_default_extended, true);
    }
}
}

extern "C" bool km_swsh_camera_init(uintptr_t manager) {
    __atomic_store_n(&km_swsh_camera_scenery_active, 0, __ATOMIC_RELEASE);
    // A repeated initialization must observe the native area choice, not an
    // orbit override left by this module during the preceding field lifetime.
    if (MatchesManager(manager) && g_forced) {
        SelectExploration(manager, g_default_extended, true);
    }
    g_ready = false;
    g_forced = false;
    const bool ready = km_swsh_camera_init_original(manager);
    if (ready) {
        km::InitializeSwShSettings();
        g_manager = manager;
        g_area = CurrentArea();
        g_default_extended = Field<uint8_t>(manager, 0x68) != 0;
        for (size_t i = 0; i < 3; ++i) g_handles[i] = Field<uintptr_t>(manager, 0x50 + i * 8);
        g_ready = true;
    }
    return ready;
}

extern "C" void km_swsh_camera_update(uintptr_t manager) {
    if (MatchesManager(manager)) km::PollSwShSettings();
    Reconcile(manager);
    const auto scheduler = ForegroundScheduler();
    const bool scenery = MatchesManager(manager) && !g_default_extended && g_forced
        && Requested() && CurrentArea() == g_area && OutdoorArea(g_area)
        && scheduler != 0 && Field<uintptr_t>(scheduler, 0x78) == 0
        && Field<uintptr_t>(scheduler, 0x80) == 0
        && Field<uint32_t>(manager, 0x6C) == 0
        && Field<uint8_t>(manager, 0x68) != 0;
    __atomic_store_n(&km_swsh_camera_scenery_active, scenery, __ATOMIC_RELEASE);
    km_swsh_camera_update_original(manager);
}

extern "C" void km_swsh_camera_push(uintptr_t scheduler, uintptr_t task) {
    BeforeTask(scheduler);
    km_swsh_camera_push_original(scheduler, task);
}

extern "C" void km_swsh_camera_queue(uintptr_t scheduler, uintptr_t task) {
    BeforeTask(scheduler);
    km_swsh_camera_queue_original(scheduler, task);
}

namespace km {
bool PrepareSwShCamera(const ModuleRange& main, SwShNativeSettingsEdition edition,
                       ExecutablePatch (&patches)[9]) {
    static constexpr uintptr_t offsets[] = {0xCF8F00, 0xCFA230, 0xF19670, 0xF195A0};
    static constexpr uint32_t expected[][4] = {
        {0xD10203FF, 0xA9064FF4, 0xA9077BFD, 0x9101C3FD},
        {0xF81E0FF3, 0xA9017BFD, 0x910043FD, 0xF9402808},
        {0xF81C0FF7, 0xA90157F6, 0xA9024FF4, 0xA9037BFD},
        {0xF81C0FF7, 0xA90157F6, 0xA9024FF4, 0xA9037BFD},
    };
    const uintptr_t targets[] = {
        reinterpret_cast<uintptr_t>(km_swsh_camera_init), reinterpret_cast<uintptr_t>(km_swsh_camera_update),
        reinterpret_cast<uintptr_t>(km_swsh_camera_push), reinterpret_cast<uintptr_t>(km_swsh_camera_queue),
    };
    uintptr_t* continuations[] = {&km_swsh_camera_init_continue, &km_swsh_camera_update_continue,
        &km_swsh_camera_push_continue, &km_swsh_camera_queue_continue};
    g_main = main.base;
    g_delta = edition == SwShNativeSettingsEdition::Shield ? 0x30 : 0;
    for (size_t i = 0; i < 4; ++i) {
        const auto offset = offsets[i] + g_delta;
        if (offset + 16 > main.text_size
            || MemoryCompare(reinterpret_cast<const void*>(main.base + offset), expected[i], 16) != 0) return false;
        g_branches[i][0] = 0x58000051;
        g_branches[i][1] = 0xD61F0220;
        MemoryCopy(g_branches[i] + 2, &targets[i], 8);
        *continuations[i] = main.base + offset + 16;
        patches[i] = {main.base + offset, expected[i], g_branches[i], 16};
    }
    // These loads feed terrain residency, model visibility, instance visibility,
    // instance density, and field actor visibility respectively. Native frustum,
    // LOD selection, scripted visibility, resource lifecycle, and per-area
    // ownership continue unchanged. LR is dead at all five sites and restored
    // by each native function's saved frame.
    static constexpr uintptr_t scenery_offsets[] = {0xCCE090, 0xCCFC5C, 0xCCED38, 0xCCEE00, 0xCB72D4};
    static constexpr uint32_t scenery_expected[] = {0xBD4AC281, 0xBD402101, 0xBD4022C1, 0xBD4022C2, 0xBD437261};
    const uintptr_t scenery_targets[] = {
        reinterpret_cast<uintptr_t>(km_swsh_camera_terrain_distance),
        reinterpret_cast<uintptr_t>(km_swsh_camera_model_distance),
        reinterpret_cast<uintptr_t>(km_swsh_camera_instance_distance),
        reinterpret_cast<uintptr_t>(km_swsh_camera_instance_density_distance),
        reinterpret_cast<uintptr_t>(km_swsh_camera_actor_distance),
    };
    for (size_t i = 0; i < 5; ++i) {
        const auto offset = scenery_offsets[i] + g_delta;
        const auto address = main.base + offset;
        const auto displacement = static_cast<intptr_t>(scenery_targets[i]) - static_cast<intptr_t>(address);
        if (offset + 4 > main.text_size || Field<uint32_t>(address, 0) != scenery_expected[i]
            || (displacement & 3) != 0 || displacement < -0x8000000 || displacement >= 0x8000000) return false;
        g_scenery_branches[i] = 0x94000000U | (static_cast<uint32_t>(displacement >> 2) & 0x03FFFFFFU);
        patches[4 + i] = {address, &scenery_expected[i], &g_scenery_branches[i], 4};
    }
    return true;
}
}
