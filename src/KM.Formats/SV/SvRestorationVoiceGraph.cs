// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Audio;
using KM.Formats.SwSh;

namespace KM.Formats.SV;

public sealed record SvRestorationVoice(uint MediaId, float GainDecibels);

/// <summary>Resolves the form and cry switches in supported Scarlet and Violet voice banks.</summary>
public static class SvRestorationVoiceGraph
{
    public static IReadOnlyDictionary<string, uint> Resolve(SoundBankDocument bank, int species, int form = 0)
        => ResolvePlayback(bank, species, form).ToDictionary(p => p.Key, p => p.Value.MediaId, StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, SvRestorationVoice> ResolvePlayback(SoundBankDocument bank, int species, int form = 0)
    {
        if (bank.Version != 140 || species is < 1 or > 1025 || form is < 0 or > 99)
            throw new InvalidDataException("S/V voice bank version or identity is unsupported.");
        SoundBankObject Object(byte type, uint id)
        {
            var matches = bank.Objects.Where(o => o.Type == type && o.Id == id).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidDataException("S/V voice graph is incomplete or ambiguous.");
        }
        var evt = Object(4, SwShVoiceWriter.Hash($"Play_PV_{species:0000}"));
        if (evt.Payload.Length != 5 || evt.Payload[0] != 1) throw new InvalidDataException("S/V voice event layout is unsupported.");
        var action = Object(3, U32(evt.Payload, 1));
        if (action.Payload.Length < 6 || action.Payload[0] != 3 || action.Payload[1] != 4)
            throw new InvalidDataException("S/V voice event does not contain a play action.");
        var choice = Switch(Object(6, U32(action.Payload, 2)).Payload);
        var gain = choice.Gain;
        if (choice.Group == SwShVoiceWriter.Hash("FORM"))
        {
            if (!choice.States.TryGetValue(SwShVoiceWriter.Hash($"FORM{form:00}"), out var children) || children.Length != 1)
                throw new InvalidDataException("S/V voice form requires another conversion adapter.");
            choice = Switch(Object(6, children[0]).Payload);
            gain += choice.Gain;
        }
        else if (form != 0) throw new InvalidDataException("S/V voice event has no requested form.");
        if (choice.Group != SwShVoiceWriter.Hash("PV")) throw new InvalidDataException("S/V cry state switch is missing.");
        var result = new Dictionary<string, SvRestorationVoice>(StringComparer.Ordinal);
        foreach (var state in SwShVoiceWriter.States)
        {
            if (!choice.States.TryGetValue(SwShVoiceWriter.Hash(state), out var children) || children.Length != 1)
                throw new InvalidDataException($"S/V cry state requires another conversion adapter: {state}");
            var sound = Object(2, children[0]).Payload;
            if (sound.Length < 9 || U32(sound, 0) != 0x00020001 || sound[4] != 2)
                throw new InvalidDataException("S/V cry codec or streaming layout is unsupported.");
            result.Add(state, new(U32(sound, 5), gain + SoundGain(sound)));
        }
        return result;
    }

    private static float SoundGain(byte[] bytes)
    {
        // Streaming version 140 sounds have a bounded local property bundle.
        // Keep gain separate from PCM so the native mixer still controls volume.
        if (bytes.Length < 46 || bytes[13] != 0 || bytes.AsSpan(14, 9).ContainsAnyExcept((byte)0) || bytes[27] != 0)
            throw new InvalidDataException("S/V cry has unsupported source effects or routing.");
        var count = bytes[28];
        if (count > 1 || bytes.Length != 46 + count * 5 || count == 1 && bytes[29] != 6)
            throw new InvalidDataException("S/V cry has unconverted playback properties.");
        var gain = count == 0 ? 0 : BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(30));
        ReadOnlySpan<byte> inherited = [0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0];
        if (!float.IsFinite(gain) || gain is < -96 or > 24 || !bytes.AsSpan(29 + count * 5).SequenceEqual(inherited))
            throw new InvalidDataException("S/V cry has unsupported playback modulation.");
        return gain;
    }

    private static (uint Group, Dictionary<uint, uint[]> States, float Gain) Switch(byte[] bytes)
    {
        // Version 140 stores effect, metadata and attachment flags before the parent data.
        // Parse the property bundle instead of assuming a fixed child offset.
        var at = 0;
        byte Byte() { if (at >= bytes.Length) throw new InvalidDataException("S/V voice switch is truncated."); return bytes[at++]; }
        uint Word() { var value = U32(bytes, at); at += 4; return value; }
        int Count() { var value = Word(); return value <= 128 ? (int)value : throw new InvalidDataException("S/V voice switch exceeds its budget."); }
        void Zero() { if (Byte() != 0) throw new InvalidDataException("S/V voice switch has unsupported effects or routing."); }
        for (var i = 0; i < 5; i++) Zero();
        if (Word() != 0) throw new InvalidDataException("S/V voice switch overrides its output bus.");
        _ = Word(); // Direct parent identity is resolved by the donor game's bank.
        Zero();
        var properties = Byte();
        if (properties > 32) throw new InvalidDataException("S/V voice property count exceeds its budget.");
        var ids = Enumerable.Range(0, properties).Select(_ => Byte()).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw new InvalidDataException("S/V voice properties are duplicated.");
        var gain = 0f;
        foreach (var id in ids)
        {
            var value = BitConverter.UInt32BitsToSingle(Word());
            if (id != 6 || !float.IsFinite(value) || value is < -96 or > 24)
                throw new InvalidDataException("S/V voice switch has unconverted playback properties.");
            gain += value;
        }
        Zero(); // Random property ranges.
        Zero(); // Inherit positioning.
        Zero(); // Inherit auxiliary sends.
        if (Word() != 0) throw new InvalidDataException("S/V voice switch has unsupported auxiliary routing.");
        Zero(); _ = Byte(); _ = Byte(); _ = Byte(); _ = Byte(); _ = Byte();
        Zero(); Zero(); Zero(); Zero(); // State and RTPC counts.
        if (Byte() != 0) throw new InvalidDataException("S/V voice selector is not a switch.");
        var group = Word(); var defaultState = Word(); Zero();
        var childCount = Count(); var children = new HashSet<uint>();
        for (var i = 0; i < childCount; i++) if (!children.Add(Word())) throw new InvalidDataException("S/V voice child identity is duplicated.");
        var stateCount = Count(); var states = new Dictionary<uint, uint[]>();
        for (var i = 0; i < stateCount; i++)
        {
            var state = Word(); var count = Count(); var nodes = new uint[count];
            for (var j = 0; j < count; j++) { nodes[j] = Word(); if (!children.Contains(nodes[j])) throw new InvalidDataException("S/V voice state references an undeclared child."); }
            if (!states.TryAdd(state, nodes)) throw new InvalidDataException("S/V voice state is duplicated.");
        }
        if (!states.ContainsKey(defaultState)) throw new InvalidDataException("S/V voice default state is missing.");
        var parameterCount = Count(); var configured = new HashSet<uint>();
        for (var i = 0; i < parameterCount; i++)
        {
            var id = Word();
            if (!children.Contains(id) || !configured.Add(id) || Byte() != 0 || Byte() != 1 || Word() != 0 || Word() != 0)
                throw new InvalidDataException("S/V voice switch has unsupported child transitions.");
        }
        if (at != bytes.Length || configured.Count != children.Count) throw new InvalidDataException("S/V voice switch contains unconverted data.");
        return (group, states, gain);
    }

    private static uint U32(byte[] bytes, int at) => at >= 0 && at <= bytes.Length - 4
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)) : throw new InvalidDataException("S/V voice record is truncated.");
}
