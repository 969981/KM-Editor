// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Formats.ZA.Generated.GameData;

namespace KM.ZA.Pokemon;

internal static class ZaPokemonGender
{
    // Editor values retain their saved selection IDs; native mixed ratios are percentages.
    public static int Read(ZaGenderInfo? gender) => gender?.Group switch
    {
        null => 0,
        1 => 0,
        2 => 254,
        3 => 255,
        _ => FromPercentage(gender?.Ratio ?? 0),
    };

    public static (byte Group, byte Ratio) Encode(int value) => value switch
    {
        0 => (1, 0),
        254 => (2, 0),
        255 => (3, 0),
        >= 1 and <= 253 => (0, (byte)Math.Round(value * 100d / 254, MidpointRounding.AwayFromZero)),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Format(int value) => value switch
    {
        0 => "Always male",
        254 => "Always female",
        255 => "Genderless",
        _ => FormatPercentage(Encode(value).Ratio),
    };

    private static int FromPercentage(int percentage) => percentage switch
    {
        12 => 31,
        25 => 63,
        50 => 127,
        75 => 191,
        89 => 225,
        _ => Math.Clamp((int)Math.Round(percentage * 254d / 100, MidpointRounding.AwayFromZero), 1, 253),
    };

    private static string FormatPercentage(int percentage) =>
        $"{(100 - percentage).ToString(CultureInfo.InvariantCulture)}% male / {percentage.ToString(CultureInfo.InvariantCulture)}% female";
}
