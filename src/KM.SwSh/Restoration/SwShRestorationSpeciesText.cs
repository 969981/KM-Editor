// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

public sealed record SwShRestoredSpeciesName(int OriginalLineCount, SwShGameTextLine Line);

/// <summary>Owns only the added species name, preserving native names and egg entries.</summary>
public static class SwShRestorationSpeciesText
{
    public static IReadOnlyList<string> Languages => SwShGameTextLanguage.SupportedMessageLanguages;
    public static string Path(string language) => SwShGameTextLanguage.CommonMessagePath(language, "monsname.dat");
    public static bool IsPath(string path) => Languages.Any(language => Path(language) == path);
}
