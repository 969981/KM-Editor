# KM Native Gameplay Menu Runtime

This directory contains the independently authored guest runtime used by KM's native in-game
settings menus. It is a normal game-title `subsdk9` module, not a cheat file, cheat-manager
integration, external overlay, or host DLL.

The runtime supports only these exact game updates:

| Game | Update | Game-owned menu location |
| --- | --- | --- |
| Pokemon Sword and Shield | 1.3.2 | Installed feature rows at the bottom of X > Options |
| Pokemon Scarlet and Violet | 4.0.0 | Three rows in the existing Options screen |
| Pokemon Legends Z-A | 2.0.2 | Three rows in the existing Game Settings page |

Each menu exposes Experience Share On or Off, Experience Rate from 0 through 500 percent in
10 percent steps, and Supported EXP Level Cap Off or 1 through 100. The controls retain the fixed
editor's verified gameplay scopes and do not claim control over unlisted EXP sources.

Sword and Shield also expose **Unlocked Camera: Yes / No**, available through
**Beta Editors > Camera Control** in KM. No is the default. Yes selects the existing
orbit camera outdoors, including towns and routes that normally use a fixed camera.
Buildings and unknown areas retain their native camera without clearing the saved choice.
The runtime retains each area's native camera choice, yields to foreground tasks and
event cameras, and restores the saved preference after those owners and return blends
finish. Naturally adjustable areas retain their native behavior with either choice.
Camera Control installs only Unlocked Camera; Gameplay Settings separately installs the
three EXP options. Either feature can be removed while retaining the other. Camera only
output leaves the game's EXP routines unchanged. The shared journal uses schema three; other profiles keep
schema one. Upgrades preserve existing settings and initialize the camera choice to No.

When the added outdoor camera is active, terrain residency, model visibility, instance
visibility, instance density, and field character visibility use a finite distance floor based on native Wild Area
settings. No restores native distances. Native LOD selection, frustum culling, and area
cleanup remain intact, as do scripted character hiding and fades. Areas with native free
camera controls keep their existing distances.

**Hooks > Camera Hook** installs or removes the settings support in a selected emulator's
SD folder. Camera Control and Gameplay Settings prepare shared RomFS and ExeFS files in
Output Root, preserving compatible edits from other editors. Users copy those game files
to their game installation. Emulator output omits duplicate configuration files; console
preparation includes its settings journal for manual placement. Removing prepared output
preserves other installed features and independent edits.

**Battle Items** is a separate Sword and Shield Beta editor. Installing it alone blocks ordinary
items selected from the player's battle Bag without requiring a settings file. Balls retain their
original eligibility, and field use, opposing trainers, and held items retain their normal behavior.
With a readable shared Camera Hook journal, **Battle Items: Yes / No** appears in Options and uses
the saved choice: Yes allows items and No blocks them. Without that journal the row is hidden and
the restriction remains active. Existing saved choices and earlier journal migrations remain unchanged.
Fresh Battle Items output omits the settings file; adding or removing it from existing shared output
preserves the installation's settings delivery choice and saved values.

Camera Control remains a Beta feature. Area geometry and complete scene coverage still
require verification during gameplay.

The runtime validates its executable dependencies before activation. Its registered `DT_INIT` function runs after the loader
prepares module memory and relocations. During this initialization, it resolves
the required guest filesystem APIs, recognizes one exact supported executable profile, verifies
every patch preimage, and installs that title's complete immutable hook set in one transaction.
Executable code is never patched again during that process. If the KM-owned dual-slot settings
journal is not readable yet, EXP and camera hooks retain retail-equivalent values, while installed
Battle Items retains its standalone restriction. Sword and Shield
defer all journal access until camera construction or Options callbacks run on initialized game
threads. Field updates retry unavailable storage at a bounded cadence without a background thread.
Later menu changes commit and verify the journal,
then publish one atomic data snapshot. A missing, corrupt, foreign, or mismatched executable dependency
prevents activation and never produces a partial live-code transition.

Journal access first mounts the SD card under a dedicated runtime mount name. A failed mount
returns without opening the file and remains eligible for a later retry. Successful mounts are
reused for reads and commits. The journal's physical location on the SD card remains unchanged.

The startup patch transaction validates all owned bytes, maps free ASLR pages outside the reserved
IPC and heap regions, flushes
and invalidates the required caches, verifies the committed bytes, and rolls back on verification
failure. The derived `main.npdm` grants only SD access and the exact process-handle and memory-alias
system calls needed by that transaction in both policy views. The journal uses alternating slots,
generations, title and family binding, CRC32C, flush, and readback verification so an interrupted
write cannot replace the last valid state. An ambiguous write is re-read from both authenticated
slots; the runtime publishes the durable winner or falls back to the same defaults used without
settings for the rest of the process.

`build.ps1` compiles a bounded AArch64 ELF. KM's managed build tooling converts that ELF to a
canonical compressed NSO and verifies every segment on readback. KM combines that independently
authored runtime with executable and menu assets derived from the user's clean Base ExeFS and Base
RomFS. Retail game files are not stored in this directory or embedded in the runtime.

The native path remains Beta until menu, gameplay, persistence, suspend and resume, restart,
upgrade, removal, emulator, and physical-hardware canaries pass for every supported title and
edition pair.
