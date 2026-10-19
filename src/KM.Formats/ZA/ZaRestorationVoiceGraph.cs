// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Audio;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

public sealed record ZaRestorationVoice(uint MediaId, float GainDecibels);

/// <summary>Resolves the form and cry switches in supported Legends Z-A voice banks.</summary>
public static class ZaRestorationVoiceGraph
{
    public static IReadOnlyDictionary<string, uint> Resolve(SoundBankDocument bank, int species, int form = 0)
        => ResolvePlayback(bank, species, form).ToDictionary(p => p.Key, p => p.Value.MediaId, StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, ZaRestorationVoice> ResolvePlayback(SoundBankDocument bank, int species, int form = 0)
    {
        if (bank.Version != 145 || species is < 1 or > 1025 || form is < 0 or > 99)
            throw new InvalidDataException("Z-A voice bank version or identity is unsupported.");
        SoundBankObject Object(byte type, uint id)
        {
            var matches = bank.Objects.Where(o => o.Type == type && o.Id == id).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Z-A voice graph is incomplete or ambiguous.");
        }
        var evt = Object(4, SwShVoiceWriter.Hash($"Play_PV_{species:0000}"));
        if (evt.Payload.Length != 5 || evt.Payload[0] != 1) throw new InvalidDataException("Z-A voice event layout is unsupported.");
        var action = Object(3, U32(evt.Payload, 1));
        if (action.Payload.Length < 6 || action.Payload[0] != 3 || action.Payload[1] != 4)
            throw new InvalidDataException("Z-A voice event does not contain a play action.");
        var choice = Switch(Object(6, U32(action.Payload, 2)).Payload);
        var gain = choice.Gain;
        if (choice.Group == SwShVoiceWriter.Hash("FORM"))
        {
            if (!choice.States.TryGetValue(SwShVoiceWriter.Hash($"FORM{form:00}"), out var children) || children.Length != 1)
                throw new InvalidDataException("Z-A voice form requires another conversion adapter.");
            choice = Switch(Object(6, children[0]).Payload);
            gain += choice.Gain;
        }
        else if (form != 0) throw new InvalidDataException("Z-A voice event has no requested form.");
        if (choice.Group != SwShVoiceWriter.Hash("PV")) throw new InvalidDataException("Z-A cry state switch is missing.");
        var result = new Dictionary<string, ZaRestorationVoice>(StringComparer.Ordinal);
        foreach (var state in SwShVoiceWriter.States)
        {
            if (!choice.States.TryGetValue(SwShVoiceWriter.Hash(state), out var children) || children.Length != 1)
                throw new InvalidDataException($"Z-A cry state requires another conversion adapter: {state}");
            var sound = Object(2, children[0]).Payload;
            if (sound.Length < 9 || U32(sound, 0) != 0x00020001 || sound[4] != 2)
                throw new InvalidDataException("Z-A cry codec or streaming layout is unsupported.");
            result.Add(state, new(U32(sound, 5), gain));
        }
        return result;
    }

    private static (uint Group, Dictionary<uint, uint[]> States, float Gain) Switch(byte[] bytes)
    {
        // Version 145 adds metadata and attachment flags before the parent data.
        // Parse the property bundle instead of assuming a fixed child offset.
        var at = 0;
        byte Byte() { if (at >= bytes.Length) throw new InvalidDataException("Z-A voice switch is truncated."); return bytes[at++]; }
        uint Word() { var value = U32(bytes, at); at += 4; return value; }
        int Count() { var value = Word(); return value <= 128 ? (int)value : throw new InvalidDataException("Z-A voice switch exceeds its budget."); }
        void Zero() { if (Byte() != 0) throw new InvalidDataException("Z-A voice switch has unsupported effects or routing."); }
        for (var i = 0; i < 5; i++) Zero();
        if (Word() != 0) throw new InvalidDataException("Z-A voice switch overrides its output bus.");
        _ = Word(); // Direct parent identity is resolved by the donor game's bank.
        Zero();
        var properties = Byte();
        if (properties > 32) throw new InvalidDataException("Z-A voice property count exceeds its budget.");
        var ids = Enumerable.Range(0, properties).Select(_ => Byte()).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw new InvalidDataException("Z-A voice properties are duplicated.");
        var gain = 0f;
        var inheritedAuxProperties = false;
        foreach (var id in ids)
        {
            var value = BitConverter.UInt32BitsToSingle(Word());
            if ((id != 6 && id is not (>= 19 and <= 22)) || !float.IsFinite(value) || value is < -96 or > 24)
                throw new InvalidDataException("Z-A voice switch has unconverted playback properties.");
            if (id == 6) gain += value;
            else inheritedAuxProperties = true;
        }
        Zero(); // Random property ranges.
        Zero(); // Inherit positioning.
        var auxiliaryFlags = Byte();
        // Stored sends are inactive when user sends inherit from the parent.
        // Keep the target game's environment routing instead of donor bus IDs.
        if ((auxiliaryFlags & ~10) != 0 || (inheritedAuxProperties && (auxiliaryFlags & 8) == 0))
            throw new InvalidDataException("Z-A voice switch has unsupported auxiliary routing.");
        if ((auxiliaryFlags & 8) != 0) for (var i = 0; i < 4; i++) _ = Word();
        if (Word() != 0) throw new InvalidDataException("Z-A voice switch has unsupported auxiliary routing.");
        Zero(); _ = Byte(); _ = Byte(); _ = Byte(); _ = Byte(); _ = Byte();
        Zero(); Zero(); // State counts.
        var curves = Byte(); Zero();
        if (curves > 1) throw new InvalidDataException("Z-A cry modulation needs another adapter.");
        if (curves == 1)
        {
            // This nearly neutral source volume correction is sampled at its
            // default endpoint. Native distance and context mixing remain active.
            if (Word() != 617048655 || Byte() != 0 || Byte() != 2 || Byte() != 0)
                throw new InvalidDataException("Z-A cry modulation needs another adapter.");
            _ = Word();
            if (Byte() != 2 || Byte() != 3 || Byte() != 0) throw new InvalidDataException("Z-A cry volume curve is unsupported.");
            for (var i = 0; i < 3; i++)
            {
                var x = BitConverter.UInt32BitsToSingle(Word()); var y = BitConverter.UInt32BitsToSingle(Word());
                if (x != i || !float.IsFinite(y) || (i == 1 ? y is < -.05f or > 0 : y != 0) || Word() != 4)
                    throw new InvalidDataException("Z-A cry volume curve is unsupported.");
            }
        }
        if (Byte() != 0) throw new InvalidDataException("Z-A voice selector is not a switch.");
        var group = Word(); var defaultState = Word(); Zero();
        var childCount = Count(); var children = new HashSet<uint>();
        for (var i = 0; i < childCount; i++) if (!children.Add(Word())) throw new InvalidDataException("Z-A voice child identity is duplicated.");
        var stateCount = Count(); var states = new Dictionary<uint, uint[]>();
        for (var i = 0; i < stateCount; i++)
        {
            var state = Word(); var count = Count(); var nodes = new uint[count];
            for (var j = 0; j < count; j++) { nodes[j] = Word(); if (!children.Contains(nodes[j])) throw new InvalidDataException("Z-A voice state references an undeclared child."); }
            if (!states.TryAdd(state, nodes)) throw new InvalidDataException("Z-A voice state is duplicated.");
        }
        if (!states.ContainsKey(defaultState)) throw new InvalidDataException("Z-A voice default state is missing.");
        var parameterCount = Count(); var configured = new HashSet<uint>();
        for (var i = 0; i < parameterCount; i++)
        {
            var id = Word();
            if (!children.Contains(id) || !configured.Add(id) || Byte() != 0 || Byte() != 1 || Word() != 0 || Word() != 0)
                throw new InvalidDataException("Z-A voice switch has unsupported child transitions.");
        }
        if (at != bytes.Length || configured.Count != children.Count) throw new InvalidDataException("Z-A voice switch contains unconverted data.");
        return (group, states, gain);
    }

    private static uint U32(byte[] bytes, int at) => at >= 0 && at <= bytes.Length - 4
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)) : throw new InvalidDataException("Z-A voice record is truncated.");
}
