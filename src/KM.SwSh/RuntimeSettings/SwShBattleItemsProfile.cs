// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.RuntimeSettings;

internal static class SwShBattleItemsProfile
{
    private static readonly (int Offset, int Length)[] Dependencies =
    [
        (0x7AEF00, 0x3C0), (0x7AFBB0, 0x1A0), (0x7B0140, 0x264),
        (0x7C8330, 0x30), (0x7EC410, 0x30),
    ];

    public static void ValidateComposition(byte[] baseBytes, byte[] currentBytes)
    {
        var baseline = NsoFile.Parse(baseBytes).Text.DecompressedData;
        var current = NsoFile.Parse(currentBytes).Text.DecompressedData;
        if (Dependencies.Any(region => region.Offset > baseline.Length - region.Length
            || region.Offset > current.Length - region.Length
            || !current.AsSpan(region.Offset, region.Length).SequenceEqual(baseline.AsSpan(region.Offset, region.Length))))
            throw new InvalidDataException("Battle Items requires compatible Bag selection and item classification code.");
    }

    public static SwShExeFsReservedRegion Reservation => new(
        SwShExeFsReservedRegionLedger.OwnerNativeGameplayMenu, "battle-items-bag-use",
        SwShExeFsReservedRegionLedger.ExeFsMainPath, "main.text", 0x7B0140, 16,
        "Battle Items Bag selection hook", "do-not-overwrite");
}
