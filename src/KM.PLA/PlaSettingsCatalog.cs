// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json;
using KM.Formats.PLA;

namespace KM.PLA;

internal sealed class PlaSettingsCatalog
{
    private const string Directory = "bin/misc/app_config/";
    private static readonly PlaFlatBuffer.ObjectLayout Layout = new([4, 8, 4, 4, 4, 4, 4], [0, 3, 4, 6], new Dictionary<int, PlaFlatBuffer.ObjectLayout>(), new Dictionary<int, PlaFlatBuffer.ObjectLayout>(), [5]);
    private sealed record Control(string Key, string Source, long Minimum, long Maximum, bool Factor = false);
    private sealed record Resource(string Path, PlaFlatBuffer Base, PlaFlatBuffer Current);
    private readonly PlaCatalog catalog;
    private readonly Dictionary<string, Resource> resources = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Files = new(StringComparer.Ordinal)
    {
        ["capture"] = "common_capture_config.bin", ["friendship"] = "pokemon_friendship_config.bin", ["shiny"] = "pokemon_rare.bin",
    };
    private static readonly Dictionary<string, Control[]> Controls = new(StringComparer.Ordinal)
    {
        ["capture"] = [
            new("ballPoke", "capture_index_ball_factor_default", 0, 10, true),
            new("ballGreat", "capture_index_ball_factor_super", 0, 10, true),
            new("ballUltra", "capture_index_ball_factor_hyper", 0, 10, true),
            new("ballFeather", "capture_index_ball_factor_wing", 0, 10, true),
            new("ballWing", "capture_index_ball_factor_wing_super", 0, 10, true),
            new("ballJet", "capture_index_ball_factor_wing_hyper", 0, 10, true),
            new("ballHeavy", "capture_index_ball_factor_stealth", 0, 10, true),
            new("ballLeaden", "capture_index_ball_factor_stealth_super", 0, 10, true),
            new("ballGigaton", "capture_index_ball_factor_stealth_hyper", 0, 10, true),
            new("captureFrostbite", "capture_index_sick_factor_freeze", 0, 10, true),
            new("captureDrowsy", "capture_index_sick_factor_sleep", 0, 10, true),
            new("capturePoison", "capture_index_sick_factor_poison", 0, 10, true),
            new("captureParalysis", "capture_index_sick_factor_paralysis", 0, 10, true),
            new("captureBurn", "capture_index_sick_factor_burn", 0, 10, true),
            new("captureBackstrike", "capture_index_backstrike_success_factor", 0, 10, true),
        ],
        ["friendship"] = [
            new("friendshipLevelLow", "offset_lvup_1", -255, 255), new("friendshipLevelMid", "offset_lvup_2", -255, 255), new("friendshipLevelHigh", "offset_lvup_3", -255, 255),
            new("friendshipFaintLow", "offset_dead_1", -255, 255), new("friendshipFaintMid", "offset_dead_2", -255, 255), new("friendshipFaintHigh", "offset_dead_3", -255, 255),
            new("friendshipExpLow", "offset_exp_1", -255, 255), new("friendshipExpMid", "offset_exp_2", -255, 255), new("friendshipExpHigh", "offset_exp_3", -255, 255),
            new("friendshipExpChance", "update_per_by_exp", 0, 100),
        ],
        ["shiny"] = [new("shinyCharmRolls", "TryCount_Item", 0, 32), new("researchCompleteRolls", "TryCount_Research100", 0, 32), new("researchPerfectRolls", "TryCount_ResearchPerfect", 0, 32)],
    };

    internal PlaSettingsCatalog(PlaCatalog catalog) => this.catalog = catalog;
    internal static bool Supports(string kind) => Controls.ContainsKey(kind);
    internal PlaField[] Fields(string kind) => Controls[kind].Select(control => new PlaField(control.Key, control.Key,
        control.Factor ? "factor" : "number", control.Minimum, control.Maximum)).ToArray();
    internal string PathFor(string kind, string id)
    {
        if (id != kind || !Supports(kind)) throw new InvalidDataException("The PLA setting identity is invalid.");
        return Directory + Files[kind];
    }
    private Resource Load(string kind)
    {
        if (resources.TryGetValue(kind, out var resource)) return resource;
        var path = PathFor(kind, kind);
        resource = new(path, new(catalog.Scope.Base(path)), new(catalog.Scope.Read(path)));
        Validate(kind, resource.Base, resource.Current);
        resources.Add(kind, resource);
        return resource;
    }
    private static Dictionary<string, int> Rows(PlaFlatBuffer b)
    {
        b.RequireKnownFields(b.Root, [4]);
        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var reference in b.VectorReferences(b.Root, 0, 2048))
        {
            var row = b.TableAtReference(reference); b.RequireKnownFields(row, Layout.Widths);
            var name = b.String(row, 0);
            if (name.Length == 0 || !rows.TryAdd(name, reference)) throw new InvalidDataException("PLA setting names are missing or ambiguous.");
            _ = b.String(row, 3); _ = b.String(row, 4); _ = b.String(row, 6);
            if (b.StringVector(row, 5, 16).Length != 3) throw new InvalidDataException("The PLA setting parameter layout is unsupported.");
        }
        return rows;
    }
    private static string Value(PlaFlatBuffer b, int reference, Control control)
    {
        var row = b.TableAtReference(reference);
        if (b.Scalar(row, 2, 4) != (control.Factor ? 2 : 0)
            || b.Hash(row, 1) != (control.Factor ? 0x27E11BAA8284C9A5UL : 0xFBFEBF26A85EB07EUL))
            throw new InvalidDataException("The PLA setting type is unsupported.");
        var value = b.StringVector(row, 5, 16)[0];
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)
            || number < control.Minimum || number > control.Maximum || !control.Factor && number != Math.Truncate(number))
            throw new InvalidDataException("A PLA gameplay setting is outside its supported range.");
        return number.ToString("R", CultureInfo.InvariantCulture);
    }
    private static void Validate(string kind, PlaFlatBuffer original, PlaFlatBuffer current)
    {
        var controls = Controls[kind].ToDictionary(control => control.Source, StringComparer.Ordinal);
        string Signature(PlaFlatBuffer b)
        {
            var rows = Rows(b);
            if (controls.Keys.Any(key => !rows.ContainsKey(key))) throw new InvalidDataException("A required PLA setting is missing.");
            return JsonSerializer.Serialize(rows.Select(pair =>
            {
                var row = b.TableAtReference(pair.Value); var parameters = b.StringVector(row, 5, 16);
                if (controls.TryGetValue(pair.Key, out var control)) { _ = Value(b, pair.Value, control); parameters[0] = ""; }
                return new { Name = pair.Key, Type = b.Scalar(row, 2, 4), Hash = b.Hash(row, 1), Min = b.String(row, 3), Max = b.String(row, 4), Description = b.String(row, 6), Parameters = parameters };
            }));
        }
        if (Signature(original) != Signature(current)) throw new InvalidDataException("PLA setting identities, types, and protected parameters must be preserved.");
    }
    internal PlaRecord Record(string kind, string id, byte[]? edited = null)
    {
        _ = PathFor(kind, id); var resource = Load(kind); var b = edited is null ? resource.Current : new PlaFlatBuffer(edited);
        Validate(kind, resource.Base, b); var rows = Rows(b);
        return new(id, kind, Controls[kind].ToDictionary(control => control.Key, control => Value(b, rows[control.Source], control), StringComparer.Ordinal));
    }
    internal IReadOnlyList<PlaRecord> Records(string kind, IReadOnlyDictionary<string, byte[]> files) => [Record(kind, kind, files.GetValueOrDefault(PathFor(kind, kind)))];
    internal byte[] Edit(string kind, string id, IReadOnlyDictionary<string, string> values)
    {
        _ = PathFor(kind, id); var resource = Load(kind); var b = new PlaFlatBuffer(resource.Current.ToArray()); var rows = Rows(b);
        var controls = Controls[kind].ToDictionary(control => control.Key, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (!controls.TryGetValue(key, out var control)) throw new InvalidDataException("The PLA setting is unsupported.");
            var reference = rows[control.Source]; var parameters = b.StringVector(b.TableAtReference(reference), 5, 16);
            if (Value(b, reference, control) == value) continue;
            parameters[0] = value;
            b.EditObjectTree(reference, Layout, new Dictionary<int, IReadOnlyDictionary<int, long>>(),
                new Dictionary<(int, int), string[]> { [(reference, 5)] = parameters });
        }
        Validate(kind, resource.Base, b);
        return b.ToArray();
    }
}
