# KM Editor

[![Latest release](https://img.shields.io/github/v/release/KotMatrosk1n/KM-Editor?label=release)](https://github.com/KotMatrosk1n/KM-Editor/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6)
![Built with](https://img.shields.io/badge/built%20with-.NET%2010%20%2B%20Tauri%202-512BD4)
![Supported games](https://img.shields.io/badge/supports-SwSh%20%7C%20SV%20%7C%20Z--A%20%7C%20PLA-red)
![Localization](https://img.shields.io/badge/localized-EN%20%7C%20ES%20%7C%20FR%20%7C%20DE%20%7C%20RU%20%7C%20UKR%20%7C%20ZH-orange)
[![License](https://img.shields.io/badge/license-GPL--3.0--only-lightgrey)](LICENSE)

Create your own adventures in Pokémon Sword and Shield, Pokémon Scarlet and Violet, Pokémon Legends Z-A, and Pokémon Legends Arceus. KM Editor brings game data, models, audio, and mod output together in a Windows desktop toolkit, with dedicated controls for each game and a clear review before changes reach your project.

[Download the latest release](https://github.com/KotMatrosk1n/KM-Editor/releases/latest) | [Explore the wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki) | [Join KM Hub on Discord](https://discord.gg/ess4reb5eB) | [Report an issue](https://github.com/KotMatrosk1n/KM-Editor/issues/new/choose)

## Editors and Tools

Build a tougher adventure, refresh the Pokémon found along familiar routes, or create a new look for your favorites. KM Editor gives you dedicated controls for each game, with the [3D Model Editor](https://github.com/KotMatrosk1n/KM-Editor/wiki/3D-Model-Editor) to explore animations and customize supported textures and materials.

Start with a small edit and build from there. Review staged changes before writing mod output, and use History and checkpoints to revisit your work. The [wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki) explains what each game supports and walks you through the available tools.

## Mod Merger

Combine existing mods for Sword, Shield, Scarlet, Violet, or Legends Z-A into one reviewed output. Open **Mod Merger** from the tools navigation, add mod folders or ZIP, RAR, and 7z archives, then choose your game and a separate output folder. Mod Merger is not available for Legends Arceus.

- **Basic** compares the supplied mods without an original game dump. Identical data combines automatically; differing values require a choice.
- **Advanced** compares mods against matching original game files, allowing independent edits to the same supported record to combine automatically. Conflicting edits to the same field still require a choice.
- **Package selection** lets you choose components from mods with multiple or optional packages in a separate dialog, including dependencies and alternatives declared by the author.

Supported game data merges by record and field. Textures, models, audio, and unsupported formats use complete file choices when they differ. Review conflicts before exporting; unresolved choices or changed inputs require another review.

See the [Mod Merger guide](docs/mod-merger.md) for supported formats, executable patches, output layouts, recovery, and size limits.

## Getting Started

To combine existing mods, open **Mod Merger** from Tools. Basic mode is available before project validation and does not require an original game dump. For game data editing, follow the project setup below.

1. Download and run the latest Windows x64 setup from [Releases](https://github.com/KotMatrosk1n/KM-Editor/releases/latest).
2. Choose your game in the welcome hub, then select **Open** for that game.
3. Open **Project Setup** and select clean Base RomFS and Base ExeFS folders.
4. Choose a separate Output Root that does not overlap your source folders.
5. Select **Validate Paths**. Scarlet, Violet, and Legends Z-A also require a valid Oodle support folder.
6. Open an editor, select **Start Editing**, and stage your changes. Review the output plan before applying it.

The welcome hub shows release notes filtered by game and restores saved project paths. Loading game data and preparing caches wait until you select **Validate Paths**.

Beta Editors require their setting to be enabled. See [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup) for supported builds, compression support, and complete path requirements.

Legends Arceus supports version 1.1.1 with standalone output. Its loose table editors do not need Oodle, while archive editors and selected model preparation require compatible compression support. See the [Legends Arceus guide](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Arceus-Overview) for supported tools and limits.

KM Editor does not include ROM dumps, RomFS, ExeFS, console keys, or save data. You are responsible for obtaining and using required game data in compliance with applicable law.

Regular users do not need .NET, Node.js, Rust, Git, or a separate backend installation.

## Supported Games

| Game family | Complete guide |
| --- | --- |
| Pokémon Sword and Shield | [Sword and Shield Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Sword-and-Shield-Overview) |
| Pokémon Scarlet and Violet | [Scarlet and Violet Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Overview) |
| Pokémon Legends Z-A | [Legends Z-A Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Overview) |
| Pokémon Legends Arceus | [Legends Arceus Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Arceus-Overview) |

Data models, supported editors, and output rules differ by game. Depending on the workflow, KM Editor can produce standard LayeredFS output or layouts for supported Trinity Mod Manager setups. Keep a separate Output Root for each game.

## Projects and Output

Normal editor changes are staged before they are applied. Open **Changes** to inspect pending targets and remove anything you do not want. Advanced workflows provide their own review and apply plans when they cannot safely share the normal edit session.

Unstaged drafts and staged changes are separate. Moving to Changes preserves staged work; if a local draft still needs attention, the warning identifies that draft before you discard it. Review checks the combined session against current source and output files, and a failed final review blocks output even when the individual edits passed validation.

Clean Base RomFS and Base ExeFS inputs remain untouched. Normal edits write to a separate Output Root, with History, checkpoints, and recovery tools. Supported writers preserve independent edits and check the current files again before applying a reviewed plan. Advanced native features explain their additional installation requirements in their guides.

## Learn More

| What do you need?              | Start here                                                                                                |
| ------------------------------ | --------------------------------------------------------------------------------------------------------- |
| Set up a project               | [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup)                              |
| Learn the editing workflow     | [Editing Workflow](https://github.com/KotMatrosk1n/KM-Editor/wiki/Editing-Workflow)                       |
| Combine existing mods         | [Mod Merger](https://github.com/KotMatrosk1n/KM-Editor/wiki/Mod-Merger) |
| Use Beta Gameplay Settings     | [Gameplay Settings](https://github.com/KotMatrosk1n/KM-Editor/wiki/Gameplay-Settings)                     |
| Explore Workbench tools        | [Workbench](https://github.com/KotMatrosk1n/KM-Editor/wiki/Workbench)                                    |
| Restore supported missing Pokémon in Sword and Shield | [Restore Missing Pokemon](https://github.com/KotMatrosk1n/KM-Editor/wiki/Restore-Missing-Pokemon) |
| Export and edit records as YAML | [Editable YAML Game Dumps](docs/game-dump-yaml.md) |
| Inspect output or recover a write | [Output Safety and Recovery](https://github.com/KotMatrosk1n/KM-Editor/wiki/Output-Safety-And-Recovery) |
| Browse every supported feature | [Wiki Home](https://github.com/KotMatrosk1n/KM-Editor/wiki)                                               |
| Install or update the editor   | [Installing and Updating](https://github.com/KotMatrosk1n/KM-Editor/wiki/Installing-and-Updating)         |
| Diagnose a problem             | [Error Codes and Diagnostics](https://github.com/KotMatrosk1n/KM-Editor/wiki/Error-Codes-And-Diagnostics) |

KM Editor is also available on GameBanana for [Sword and Shield](https://gamebanana.com/tools/23044), [Scarlet and Violet](https://gamebanana.com/tools/23103), and [Legends Z-A](https://gamebanana.com/tools/23168).

## Languages and Network Use

The interface is available in English, Spanish, French, German, Russian, Ukrainian, and Simplified Chinese. Installed releases can check GitHub for stable updates, and the Windows setup supports installation, update, repair, and uninstall.

Update checks contact GitHub. If a bundled Pokémon sprite is unavailable, the interface may request a fallback image from Pokémon Showdown. Project files remain local and are not uploaded by either request.

## Contributing

Development requirements, setup commands, interface contracts, and project checks are maintained in the [Contributing Guide](CONTRIBUTING.md). Installer and release packaging have separate [Windows Setup](installer/windows/README.md) and [Release](docs/releases.md) documentation.

Contributors should also read the [Code of Conduct](CODE_OF_CONDUCT.md) and [Security Policy](SECURITY.md).

## License and Asset Credits

KM Editor includes Pokémon sprites from Pokémon Showdown's public `gen5` and `ani` directories and may use that service when a bundled sprite is missing. See the [Pokémon Showdown credits](https://pokemonshowdown.com/credits) for contributing artists and upstream sources. These files are excluded from KM Editor's GPL license.

Item artwork uses PokeSprite and PokeAPI assets alongside KM category symbols. See the bundled [item artwork notices](apps/desktop/public/item-icons/NOTICE.txt). Starmobile artwork has its own [attribution and license](apps/desktop/public/sprites/starmobiles/NOTICE.txt), and Sound Studio includes [decoder notices](apps/desktop/public/audio-decoder/NOTICE.txt).

Restored Pokémon party icons use static miniatures from PokeSprite. See the [party artwork credits and rights](src/KM.Tools/Resources/PartySprites/NOTICE.txt). These images are excluded from KM Editor's GPL license.

KM Editor is an unofficial fan project and is not affiliated with or endorsed by the games' publishers or developers. Related names, trademarks, and artwork belong to their respective owners.

KM Editor source code is distributed under the [GPL 3.0 only license](LICENSE). Other assets remain subject to their own applicable rights and terms.
