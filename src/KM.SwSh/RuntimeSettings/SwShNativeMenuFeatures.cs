// SPDX-License-Identifier: GPL-3.0-only
namespace KM.SwSh.RuntimeSettings;

[Flags]
public enum SwShNativeMenuFeatures
{
    Camera = 1,
    Gameplay = 2,
    BattleItems = 4,
    All = Camera | Gameplay | BattleItems,
}
