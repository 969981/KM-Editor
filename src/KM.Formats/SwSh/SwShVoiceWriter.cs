// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using KM.Formats.Audio;

namespace KM.Formats.SwSh;

public sealed record SwShVoiceContribution(SoundBankObject[] Objects, SoundBankMedia[] Media);

/// <summary>Creates native voice actor, action, and event graphs from locally converted cries.</summary>
public static class SwShVoiceWriter
{
    public static readonly IReadOnlyList<string> States = Array.AsReadOnly(new[]
        { "INDEX", "GLAD1", "GLAD2", "GLAD3", "ANGER", "SAD", "ATK", "SPATK", "EX1", "EX2", "EX3", "EX4", "ENV" });
    private static readonly string[] NativeStates = ["INDEX", "GLAD1", "ANGER", "SAD", "ATK"];

    public static IReadOnlySet<uint> ObjectIds(int species)
    {
        if (species is < 1 or > 898) throw new ArgumentOutOfRangeException(nameof(species));
        var result = new HashSet<uint>();
        uint Owned(string suffix) => Hash(string.Create(CultureInfo.InvariantCulture, $"KM_Restoration_v1_{species}_{suffix}"));
        foreach (var family in new[] { "", "EV_", "Btl_" })
        {
            var label = family.TrimEnd('_');
            result.Add(Owned(family.Length == 0 ? "parent" : label + "_parent"));
            foreach (var state in family.Length == 0 ? States : NativeStates)
            {
                var prefix = family.Length == 0 ? state : label + "_" + state;
                result.Add(Owned(prefix + "_sound")); result.Add(Owned(prefix + "_action"));
                var variant = Array.IndexOf(NativeStates, state);
                result.Add(Hash(variant >= 0 ? string.Create(CultureInfo.InvariantCulture, $"Play_PV_{family}{species:000}_00_{variant:00}")
                    : string.Create(CultureInfo.InvariantCulture, $"Play_KM_PV_{species:000}_{state}")));
            }
        }
        return result;
    }

    public static uint MediaId(int species, string state)
    {
        if (species is < 1 or > 898 || !States.Contains(state))
            throw new ArgumentOutOfRangeException(nameof(species), "Choose a supported species and cry state.");
        return Hash(string.Create(CultureInfo.InvariantCulture, $"KM_Restoration_v1_{species}_{state}_media")) & 0x3fffffff;
    }

    public static uint Hash(string name)
    {
        uint hash = 2166136261;
        foreach (var character in System.Text.Encoding.UTF8.GetBytes(name.ToLowerInvariant())) hash = unchecked(hash * 16777619) ^ character;
        return hash;
    }
    public static SwShVoiceContribution Create(SoundBankDocument native, int species, int donor, IReadOnlyDictionary<string, byte[]> pcmMedia)
    {
        if (species is < 1 or > 898 || donor is < 1 or > 898 || pcmMedia.Count != States.Count || States.Any(s => !pcmMedia.ContainsKey(s)))
            throw new InvalidDataException("Voice restoration requires all declared cry states.");
        var objects = new List<SoundBankObject>(); var media = new List<SoundBankMedia>();
        var used = native.Objects.Select(o => o.Id).Concat(native.Media.Select(m => m.Id)).ToHashSet();
        uint Identity(string name, bool mediaId = false)
        {
            var id = Hash(name); if (mediaId) id &= 0x3fffffff;
            if (!used.Add(id)) throw new InvalidDataException("Restored voice identity collides with existing audio.");
            return id;
        }
        uint New(string suffix) => Identity(string.Create(CultureInfo.InvariantCulture, $"KM_Restoration_v1_{species}_{suffix}"));
        SoundBankObject Find(byte type, uint id)
        {
            var found = native.Objects.Where(o => o.Type == type && o.Id == id).ToArray();
            return found.Length == 1 ? found[0] : throw new InvalidDataException("Native voice graph is ambiguous or incomplete.");
        }
        (SoundBankObject Sound, SoundBankObject Action) Donor(string family, int variant)
        {
            var eventName = string.Create(CultureInfo.InvariantCulture, $"Play_PV_{family}{donor:000}_00_{variant:00}");
            var evt = Find(4, Hash(eventName));
            if (evt.Payload.Length != 5 || evt.Payload[0] != 1) throw new InvalidDataException("Native voice event layout is unsupported.");
            var action = Find(3, Read(evt.Payload, 1)); var sound = Find(2, Read(action.Payload, 2));
            if (sound.Payload.Length < 25) throw new InvalidDataException("Native voice sound is truncated.");
            return (sound, action);
        }
        foreach (var state in States)
        {
            var bytes = pcmMedia[state];
            if (bytes.Length < 68 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20)) != 0xfffe)
                throw new InvalidDataException("Restored cry must use native extensible PCM.");
            media.Add(new(Identity(string.Create(CultureInfo.InvariantCulture, $"KM_Restoration_v1_{species}_{state}_media"), true), bytes));
        }
        foreach (var family in new[] { "", "EV_", "Btl_" })
        {
            var familyLabel = family.TrimEnd('_');
            var parent = New(family.Length == 0 ? "parent" : familyLabel + "_parent"); var children = new List<uint>();
            SoundBankObject? originalParent = null;
            var states = family.Length == 0 ? States : NativeStates;
            foreach (var state in states)
            {
                var variant = Array.IndexOf(NativeStates, state); var donorGraph = Donor(family, family.Length == 0 ? 0 : variant);
                var parentId = Read(donorGraph.Sound.Payload, 21);
                var candidates = native.Objects.Where(o => o.Id == parentId && o.Type != 2 && o.Type != 3 && o.Type != 4).ToArray();
                if (candidates.Length != 1 || (originalParent is not null && originalParent.Id != parentId))
                    throw new InvalidDataException("Native voice mixer could not be resolved uniquely.");
                originalParent = candidates[0];
                var prefix = family.Length == 0 ? state : familyLabel + "_" + state;
                var sound = New(prefix + "_sound"); children.Add(sound);
                var payload = donorGraph.Sound.Payload.ToArray(); var clip = media[States.ToList().IndexOf(state)];
                Write(payload, 0, 0x00010001); payload[4] = 0; Write(payload, 5, clip.Id); Write(payload, 9, checked((uint)clip.Bytes.Length)); Write(payload, 21, parent);
                objects.Add(new(2, sound, payload));
                var action = New(prefix + "_action"); var actionBytes = donorGraph.Action.Payload.ToArray(); Write(actionBytes, 2, sound);
                objects.Add(new(3, action, actionBytes));
                var eventName = variant >= 0 ? string.Create(CultureInfo.InvariantCulture, $"Play_PV_{family}{species:000}_00_{variant:00}")
                    : string.Create(CultureInfo.InvariantCulture, $"Play_KM_PV_{species:000}_{state}");
                var eventBytes = new byte[5]; eventBytes[0] = 1; Write(eventBytes, 1, action);
                objects.Add(new(4, Identity(eventName), eventBytes));
            }
            var mixer = originalParent!.Payload;
            if (mixer.Length < 24 || Read(mixer, mixer.Length - 24) != 5)
                throw new InvalidDataException("Native voice mixer child layout is unsupported.");
            var result = new byte[mixer.Length - 24 + 4 + children.Count * 4]; mixer.AsSpan(0, mixer.Length - 24).CopyTo(result);
            Write(result, mixer.Length - 24, checked((uint)children.Count));
            for (var i = 0; i < children.Count; i++) Write(result, mixer.Length - 20 + i * 4, children[i]);
            objects.Add(new(originalParent.Type, parent, result));
        }
        return new(objects.ToArray(), media.ToArray());
    }
    private static uint Read(byte[] data, int at)
    {
        if (at < 0 || at > data.Length - 4) throw new InvalidDataException("Voice graph reference is truncated.");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
    }
    private static void Write(byte[] data, int at, uint value)
    {
        if (at < 0 || at > data.Length - 4) throw new InvalidDataException("Voice graph patch is out of bounds.");
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
    }
}
