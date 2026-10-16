// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Lua;
using KM.SV.RuntimeSettings;
using KM.SV.SummaryStats;
using KM.SV.TitanSwapper;

namespace KM.SV.Workflows;

internal sealed record SvSharedScriptState(byte[] Core, IReadOnlyList<string> TitanLabels,
    bool SummaryStats, bool HasGameplayOptions);

// Each helper is recognized completely before removing its exact root call and child.
internal static class SvSharedScript
{
    private const int Insertion = 429030;
    private const int ChildCount = 30829;
    private const int CodeCount = 429034;
    private const string TitanMarker = "KM_TITAN_LABELS";
    private static readonly Lua54Prototype Titan = Lua54BinaryChunk.Parse(Convert.FromBase64String(SvTitanSwapperBytecode.Value)).Root;
    private static readonly Lua54Prototype Summary = Lua54BinaryChunk.Parse(Convert.FromBase64String(SvSummaryStatsBytecode.Value)).Root;
    private static readonly Lua54Upvalue[] Environment = [new(0, 0, 0)];
    private static uint[] Call(int child) => [79u | (69u << 7) | ((uint)child << 15), 68u | (69u << 7) | (1u << 16) | (1u << 24)];

    public static SvSharedScriptState Read(byte[] source)
    {
        var chunk = Lua54BinaryChunk.Parse(source);
        if (!chunk.Serialize().AsSpan().SequenceEqual(source)) throw new InvalidDataException("The shared script did not round trip.");
        var root = chunk.Root;
        var count = root.Children.Count - ChildCount;
        if (count is < 0 or > 2 || root.Code.Count != CodeCount + count * 2)
            throw new InvalidDataException("Unsupported shared S/V script inventory.");
        IReadOnlyList<string> labels = [];
        var hasSummary = false;
        for (var index = count - 1; index >= 0; index--)
        {
            if (!root.Code.Skip(Insertion).Take(2).SequenceEqual(Call(ChildCount + index)))
                throw new InvalidDataException("A shared script helper call changed.");
            var helper = root.Children[^1];
            if (!helper.Upvalues.SequenceEqual(Environment))
                throw new InvalidDataException("A shared script helper environment changed.");
            if (Matches(chunk, helper, WithEnvironment(Summary)))
            {
                if (hasSummary) throw new InvalidDataException("Duplicate Summary helper.");
                hasSummary = true;
            }
            else
            {
                if (labels.Count != 0) throw new InvalidDataException("Duplicate Titan helper.");
                var marker = Titan.Constants.Select((constant, i) => (constant, i))
                    .Single(pair => pair.constant.TryGetUtf8String(out var text) && text == TitanMarker).i;
                if (helper.Constants.Count != Titan.Constants.Count || !helper.Constants[marker].TryGetUtf8String(out var text))
                    throw new InvalidDataException("Unrecognized shared script helper.");
                labels = text.Split(';');
                ValidateLabels(labels);
                var constants = helper.Constants.ToArray();
                constants[marker] = Lua54Constant.FromUtf8String(TitanMarker);
                if (!Matches(chunk, helper.WithCodeAndConstants(helper.Code, constants), WithEnvironment(Titan)))
                    throw new InvalidDataException("The Titan helper changed outside its configuration.");
            }
            root = Clone(root, root.Code.Take(Insertion).Concat(root.Code.Skip(Insertion + 2)).ToArray(),
                root.Constants, root.Children.Take(root.Children.Count - 1).ToArray(), root.Upvalues);
        }
        chunk = chunk.WithRoot(root);
        var children = root.Children.ToArray();
        var tm = children[24349];
        if (tm.Code.Count != 412 || tm.Code[168] is not (0x000085C2u or 0x000B0580u))
            throw new InvalidDataException("The shared TM script instruction is unsupported.");
        var instructions = tm.Code.ToArray();
        instructions[168] = 0x000085C2;
        children[24349] = tm.WithCodeAndConstants(instructions, tm.Constants);
        var hash = Convert.ToHexString(SHA256.HashData(chunk.WithRoot(root.WithChildren(children)).Serialize()));
        if (hash != SvGameplayOptionsLuaTransformer.VanillaSourceSha256 && hash != SvGameplayOptionsLuaTransformer.DerivedSourceSha256)
            throw new InvalidDataException("The shared script is not a supported S/V 4.0.0 source or recognized composed output.");
        return new(chunk.Serialize(), labels, hasSummary, hash == SvGameplayOptionsLuaTransformer.DerivedSourceSha256);
    }

    public static byte[] Compose(SvSharedScriptState state)
    {
        var chunk = Lua54BinaryChunk.Parse(state.Core);
        if (chunk.Root.Children.Count != ChildCount || chunk.Root.Code.Count != CodeCount)
            throw new InvalidDataException("Shared script composition requires a validated core.");
        if (state.TitanLabels.Count != 0)
        {
            ValidateLabels(state.TitanLabels);
            var constants = Titan.Constants.Select(constant => constant.TryGetUtf8String(out var text) && text == TitanMarker
                ? Lua54Constant.FromUtf8String(string.Join(';', state.TitanLabels)) : constant).ToArray();
            chunk = Add(chunk, Titan.WithCodeAndConstants(Titan.Code, constants));
        }
        if (state.SummaryStats) chunk = Add(chunk, Summary);
        return chunk.Serialize();
    }

    private static Lua54BinaryChunk Add(Lua54BinaryChunk chunk, Lua54Prototype template)
    {
        var root = chunk.Root;
        return chunk.WithRoot(Clone(root, root.Code.Take(Insertion).Concat(Call(root.Children.Count)).Concat(root.Code.Skip(Insertion)).ToArray(),
            root.Constants, [.. root.Children, WithEnvironment(template)], root.Upvalues));
    }

    private static bool Matches(Lua54BinaryChunk chunk, Lua54Prototype left, Lua54Prototype right) =>
        chunk.WithRoot(left).Serialize().AsSpan().SequenceEqual(chunk.WithRoot(right).Serialize());

    private static Lua54Prototype WithEnvironment(Lua54Prototype p) => Clone(p, p.Code, p.Constants, p.Children, Environment);

    internal static void ValidateLabels(IReadOnlyList<string> labels)
    {
        if (labels.Count > 13 || labels.Distinct(StringComparer.Ordinal).Count() != labels.Count
            || labels.Any(label => !SvTitanSwapperDocument.Encounters.Any(encounter => encounter.Label == label)))
            throw new InvalidDataException("Unsupported Titan encounter selection.");
    }

    private static Lua54Prototype Clone(Lua54Prototype p, IReadOnlyList<uint> code,
        IReadOnlyList<Lua54Constant> constants, IReadOnlyList<Lua54Prototype> children, IReadOnlyList<Lua54Upvalue> upvalues) =>
        new(p.DeclaredSourceBytes?.ToArray(), p.LineDefined, p.LastLineDefined, p.NumParams, p.IsVarArg, p.MaxStackSize,
            code, constants, upvalues, children, p.LineInfoBytes, p.AbsoluteLines, p.LocalVariables, p.UpvalueNameBytes);
}
