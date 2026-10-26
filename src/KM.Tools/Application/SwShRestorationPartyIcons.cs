// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;
using KM.Formats.Models;

namespace KM.Tools.Application;

internal static class SwShRestorationPartyIcons
{
    private static readonly IReadOnlyDictionary<int, (string Sprite, string Frame)> Profiles = new Dictionary<int, (string, string)>
    {
        // Frames use native resource IDs, which are not always National Dex IDs.
        [6] = ("charizard", "0006_00s"),
        [46] = ("paras", "0007_00s"), [47] = ("parasect", "0007_00s"),
        [48] = ("venonat", "0007_00s"), [49] = ("venomoth", "0007_00s"),
        [56] = ("mankey", "0007_00s"), [57] = ("primeape", "0007_00s"),
        [979] = ("annihilape", "0007_00s"),
        [152] = ("chikorita", "0001_00s"), [153] = ("bayleef", "0002_00s"), [154] = ("meganium", "0003_00m"),
        [155] = ("cyndaquil", "0004_00s"), [156] = ("quilava", "0005_00s"), [157] = ("typhlosion", "0006_00s"),
        [158] = ("totodile", "0007_00s"), [159] = ("croconaw", "0008_00s"), [160] = ("feraligatr", "0009_00s"),
        [495] = ("snivy", "0252_00s"), [496] = ("servine", "0253_00s"), [497] = ("serperior", "0350_00m"),
        [498] = ("tepig", "0004_00s"), [499] = ("pignite", "0005_00s"), [500] = ("emboar", "0006_00s"),
        [501] = ("oshawott", "0007_00s"), [502] = ("dewott", "0008_00s"), [503] = ("samurott", "0009_00s"),
        [650] = ("chespin", "0007_00s"), [651] = ("quilladin", "0008_00s"), [652] = ("chesnaught", "0009_00s"),
        [653] = ("fennekin", "0004_00s"), [654] = ("braixen", "0005_00s"), [655] = ("delphox", "0006_00s"),
        [656] = ("froakie", "0007_00s"), [657] = ("frogadier", "0008_00s"), [658] = ("greninja", "0009_00s"),
        [13] = ("weedle", "0010_00s"), [14] = ("kakuna", "0011_00s"), [15] = ("beedrill", "0012_00m"),
        [16] = ("pidgey", "0918_00s"), [17] = ("pidgeotto", "0919_00s"), [18] = ("pidgeot", "0920_00s"),
        [23] = ("ekans", "0147_00s"), [24] = ("arbok", "0350_00m"),
        [74] = ("geodude", "0109_00s"), [75] = ("graveler", "0525_00s"), [76] = ("golem", "0112_00m"),
        [387] = ("turtwig", "0001_00s"), [388] = ("grotle", "0002_00s"), [389] = ("torterra", "0003_00m"),
        [390] = ("chimchar", "0948_00s"), [391] = ("monferno", "0949_00s"), [392] = ("infernape", "0815_00s"),
        [352] = ("kecleon", "0560_00s"), [393] = ("piplup", "0007_00s"), [394] = ("prinplup", "0008_00s"), [395] = ("empoleon", "0009_00s"),
    };
    internal static byte[] Create(int species, Func<string, byte[]> native, CancellationToken token, int form = 0,
        ModelTextureMipCache? encodingCache = null)
    {
        if (!Profiles.TryGetValue(species, out var profile)) throw new InvalidDataException("Restoration party icon profile is missing.");
        if (form != 0)
        {
            var suffix = (species, form) switch
            {
                (6, 1) => "-mega-x", (6, 2) => "-mega-y",
                (74 or 75 or 76, 1) => "-alola",
                _ => throw new InvalidDataException("Restoration party icon form is unsupported."),
            };
            profile = (profile.Sprite + suffix, profile.Frame);
        }
        using var stream = typeof(SwShRestorationPartyIcons).Assembly.GetManifestResourceStream("KM.Tools.PartySprites." + profile.Sprite + ".png")
            ?? throw new InvalidDataException("Restoration party sprite is missing.");
        if (stream.Length is < 1 or > 1024 * 1024) throw new InvalidDataException("Restoration party sprite exceeds its budget.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        return SwShPartyIconWriter.CreateMiniature(bytes, native("bin/appli/icon_pokemon/poke_icon_" + profile.Frame + "_n.bntx"), token, encodingCache);
    }
}
