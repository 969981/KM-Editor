// SPDX-License-Identifier: GPL-3.0-only
#pragma once

extern "C" unsigned int km_swsh_installed_features();
extern "C" bool km_swsh_settings_ready();

#include "km_settings.hpp"

namespace km {

enum class SwShNativeSettingsEdition : uint8_t {
    Sword = 1,
    Shield = 2,
};

struct SwShNativeSettingsProfileView {
    SwShNativeSettingsEdition edition;
    uint64_t title_id;
    const char* update_version;
    const char* full_build_id;
    uintptr_t text_size;
    const char* settings_journal_path;
};

const SwShNativeSettingsProfileView* GetSwShNativeSettingsProfile(
    SwShNativeSettingsEdition edition);

// Returns true only when the requested exact runtime-managed title was accepted
// and its complete immutable hook set committed during the serialized subsdk
// startup call. Initialized game callbacks later adopt authenticated settings.
// A wrong title or preimage mismatch returns false
// without arming any hook.
bool TryInstallSwShNativeSettings(
    SwShNativeSettingsEdition edition,
    const ModuleRange* modules,
    size_t module_count,
    const GuestFilesystemApi& filesystem);

void InitializeSwShSettings();
void PollSwShSettings();

} // namespace km
