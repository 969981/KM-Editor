// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_battle_items.hpp"

extern "C" {
uintptr_t km_swsh_battle_items_continue;
uint32_t km_swsh_battle_items_original(uintptr_t);
extern volatile uint64_t km_swsh_effective_snapshot;
}

namespace {
uintptr_t g_main;
uint32_t g_branch[4];
template <typename T> T Field(uintptr_t owner, uintptr_t offset) {
    return *reinterpret_cast<const T*>(owner + offset);
}
}

extern "C" uint32_t km_swsh_battle_items_use(uintptr_t selection) {
    const auto allowed = km_swsh_battle_items_original(selection);
    if ((allowed & 1) == 0 || (km_swsh_installed_features() & 4) == 0) return allowed;
    const auto manager = Field<uintptr_t>(Field<uintptr_t>(selection, 0x68), 0x60);
    const auto context = Field<uintptr_t>(manager, 0x58);
    const auto mode = Field<uint32_t>(context, 0);
    if (mode != 1 && mode != 2) return allowed;
    km::SettingsValues values = km::VanillaSettings;
    km::InitializeSwShSettings();
    const auto snapshot = __atomic_load_n(&km_swsh_effective_snapshot, __ATOMIC_ACQUIRE);
    // Standalone Battle Items blocks ordinary items. Authenticated shared
    // settings optionally let the player allow them again through Options.
    if (km::UnpackSettingsSnapshot(snapshot, &values)
        && values.battle_items_enabled) return allowed;
    const auto item = reinterpret_cast<uint32_t (*)(uintptr_t)>(g_main + 0x7C8330)(Field<uintptr_t>(selection, 0xA0));
    if ((reinterpret_cast<uint32_t (*)(uint32_t)>(g_main + 0x7EC410)(item) & 1) != 0) return allowed;
    // Battle Bag has no Give action. Rejecting Use selects the native unavailable
    // item dialog, including its localized professor message and player name.
    return 0;
}

namespace km {
bool PrepareSwShBattleItems(const ModuleRange& main, ExecutablePatch& patch) {
    constexpr uintptr_t offset = 0x7B0140;
    static constexpr uint32_t expected[] = {0xD10103FF, 0xA90157F6, 0xA9024FF4, 0xA9037BFD};
    if (offset + sizeof(expected) > main.text_size
        || MemoryCompare(reinterpret_cast<const void*>(main.base + offset), expected, sizeof(expected)) != 0) return false;
    g_main = main.base;
    km_swsh_battle_items_continue = main.base + offset + sizeof(expected);
    g_branch[0] = 0x58000051;
    g_branch[1] = 0xD61F0220;
    const auto target = reinterpret_cast<uintptr_t>(km_swsh_battle_items_use);
    MemoryCopy(g_branch + 2, &target, sizeof(target));
    patch = {main.base + offset, expected, g_branch, sizeof(expected)};
    return true;
}
}
