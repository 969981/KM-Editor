// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using KM.Formats.Lua;

namespace KM.PLA;

internal sealed class PlaScriptCatalog(PlaCatalog catalog)
{
    private sealed record Control(string Key, int Constant);
    private sealed record Definition(string File, string Prototype, string Signature, Control[] Controls);
    private static readonly Dictionary<string, Definition> Definitions = new(StringComparer.Ordinal)
    {
        ["bagPrices"] = new("bagexpansionevent", "0/67", "2439B3AFD2C428FA187A1B8B30BBBF7B69291288BABB578387EC85A8E11505D7",
            Enumerable.Range(1, 39).Select(step => new Control("bagPrice." + step, step + 1)).ToArray()),
        ["charmPrices"] = new("amuletshopevent", "0/68", "113A89EE4F4274B30671F70AF2A6DA0039442AFE5885928942C14C3A471C3022",
            [.. Enumerable.Range(1, 5).Select(step => new Control("wardingPrice." + step, step + 1)),
             new("sharedCharmPrice", 9),
             .. Enumerable.Range(2, 4).Select(step => new Control("survivalPrice." + step, step + 8)),
             .. Enumerable.Range(2, 4).Select(step => new Control("temptingPrice." + step, step + 13))]),
    };
    private readonly Dictionary<string, (Lua53BinaryChunk Base, Lua53BinaryChunk Current)> resources = new(StringComparer.Ordinal);

    internal static bool Supports(string kind) => Definitions.ContainsKey(kind);
    internal PlaField[] Fields(string kind) => Definitions[kind].Controls.Select(c => new PlaField(c.Key, c.Key, "number", 0, 1_000_000)).ToArray();
    internal string PathFor(string kind, string id)
    {
        if (!Supports(kind) || id != kind) throw new InvalidDataException("The PLA script control identity is invalid.");
        return "bin/haxe/release/event/" + Definitions[kind].File + ".blua";
    }

    private (Lua53BinaryChunk Base, Lua53BinaryChunk Current) Load(string kind)
    {
        if (resources.TryGetValue(kind, out var resource)) return resource;
        var path = PathFor(kind, kind);
        resource = (Lua53BinaryChunk.Parse(catalog.Scope.Base(path)), Lua53BinaryChunk.Parse(catalog.Scope.Read(path)));
        Validate(kind, resource.Base); Validate(kind, resource.Current);
        resources.Add(kind, resource);
        return resource;
    }

    private static void Validate(string kind, Lua53BinaryChunk chunk)
    {
        var definition = Definitions[kind];
        var prototype = chunk.Prototypes.SingleOrDefault(p => p.Path == definition.Prototype);
        if (prototype is null || prototype.ParameterCount != 1 || prototype.UpvalueCount != 2)
            throw new InvalidDataException("The PLA event function signature is unsupported.");
        var masked = chunk.ToArray();
        foreach (var control in definition.Controls)
        {
            if (control.Constant >= prototype.Constants.Count) throw new InvalidDataException("A PLA event price is missing.");
            var constant = prototype.Constants[control.Constant];
            if (constant.Tag != 19 || constant.Integer is null or < 0 or > 1_000_000)
                throw new InvalidDataException("The PLA event price is outside its supported range.");
            masked.AsSpan(constant.Offset, 8).Clear();
        }
        // Qualify the entire function graph and its host calls. Only the listed
        // price payloads may differ; code, event state, item IDs and aliases stay fixed.
        if (Convert.ToHexString(SHA256.HashData(masked)) != definition.Signature)
            throw new InvalidDataException("The PLA event script layout is unsupported. Instructions, event links, and protected constants must match the supported script.");
    }

    internal PlaRecord Record(string kind, string id, byte[]? edited = null)
    {
        _ = PathFor(kind, id);
        var resource = Load(kind); var chunk = edited is null ? resource.Current : Lua53BinaryChunk.Parse(edited);
        Validate(kind, chunk);
        var definition = Definitions[kind]; var prototype = chunk.Prototypes.Single(p => p.Path == definition.Prototype);
        return new(id, kind, definition.Controls.ToDictionary(c => c.Key, c => prototype.Constants[c.Constant].Integer!.Value.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal));
    }

    internal IReadOnlyList<PlaRecord> Records(string kind, IReadOnlyDictionary<string, byte[]> files) => [Record(kind, kind, files.GetValueOrDefault(PathFor(kind, kind)))];

    internal byte[] Edit(string kind, string id, IReadOnlyDictionary<string, string> values)
    {
        _ = PathFor(kind, id); var resource = Load(kind); var definition = Definitions[kind];
        var changes = new Dictionary<(string Prototype, int Constant), long>();
        foreach (var (key, value) in values)
        {
            var control = definition.Controls.SingleOrDefault(c => c.Key == key);
            if (control is null || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 0 or > 1_000_000)
                throw new InvalidDataException("The PLA event price is invalid.");
            changes.Add((definition.Prototype, control.Constant), number);
        }
        var edited = resource.Current.WithIntegers(changes);
        Validate(kind, edited);
        return edited.ToArray();
    }
}
