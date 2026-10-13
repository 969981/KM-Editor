// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using KM.Formats.Audio;
using KM.Formats.SwSh;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal IReadOnlyList<SwShRestorationContribution> Voices(int[] species)
        {
            var audioRoot = Path.Combine(donorRoot, "audio", "wwise", "bin");
            var directories = Directory.EnumerateDirectories(audioRoot).Take(33).ToArray();
            if (directories.Length > 32) throw new InvalidDataException("Donor audio directory count exceeds its budget.");
            var banks = directories.Prepend(audioRoot).Select(path => Path.Combine(path, "PM_VOICE.bnk"))
                .Where(System.IO.File.Exists).ToArray();
            if (banks.Length != 1) throw new InvalidDataException("Donor voice bank is missing or ambiguous.");
            var relative = Path.GetRelativePath(donorRoot, banks[0]).Replace('\\', '/');
            var bytes = ReadPhysical(banks[0], 64 * 1024 * 1024);
            Fingerprint("donor", relative, bytes); var source = new SoundBankDocument(bytes);
            var targetBank = new SoundBankDocument(Native(SwShRestorationComposer.VoicesPath[6..]));
            if (source.Version != 140 || targetBank.Version != 128) throw new InvalidDataException("Voice conversion is not defined for these sound bank versions.");
            var directory = Path.GetDirectoryName(banks[0])!;
            var packages = new Dictionary<ulong, (string Path, SoundPackageMedia Media)>();
            var paths = Directory.EnumerateFiles(directory, "*.pck", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).Take(129).ToArray();
            if (paths.Length > 128) throw new InvalidDataException("Donor audio package count exceeds its budget.");
            foreach (var path in paths)
            {
                using var package = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                foreach (var item in SoundPackageIndex.Read(package, token).Where(m => m.Group == 1 && m.Language == 0))
                    if (!packages.TryAdd(item.Id, (path, item))) throw new InvalidDataException("Donor voice media identity exists in multiple packages.");
            }
            SoundBankObject Object(byte type, uint id)
            {
                var matches = source.Objects.Where(o => o.Type == type && o.Id == id).ToArray();
                return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Donor voice graph is incomplete or ambiguous.");
            }
            var result = new List<SwShRestorationContribution>();
            foreach (var id in species)
            {
                var evt = Object(4, SwShVoiceWriter.Hash($"Play_PV_{id:0000}"));
                if (evt.Payload.Length != 5 || evt.Payload[0] != 1) throw new InvalidDataException("Donor voice event layout is unsupported.");
                var action = Object(3, U32(evt.Payload, 1)); var container = Object(6, U32(action.Payload, 2));
                var childrenCount = checked((int)U32(container.Payload, 42));
                if (childrenCount is < 1 or > 128) throw new InvalidDataException("Donor voice switch has an invalid child count.");
                var children = Enumerable.Range(0, childrenCount).Select(i => U32(container.Payload, 46 + i * 4)).ToHashSet();
                var at = 46 + childrenCount * 4; var stateCount = checked((int)U32(container.Payload, at)); at += 4;
                if (stateCount is < 1 or > 128) throw new InvalidDataException("Donor voice state count is invalid.");
                var states = new Dictionary<uint, uint>();
                for (var i = 0; i < stateCount; i++, at += 12)
                {
                    var state = U32(container.Payload, at); var count = U32(container.Payload, at + 4); var child = U32(container.Payload, at + 8);
                    if (count != 1 || !children.Contains(child) || !states.TryAdd(state, child)) throw new InvalidDataException("Donor voice switch needs another state adapter.");
                }
                var media = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var state in SwShVoiceWriter.States)
                {
                    token.ThrowIfCancellationRequested();
                    if (!states.TryGetValue(SwShVoiceWriter.Hash(state), out var soundId)) throw new InvalidDataException($"Donor cry state is missing: {state}");
                    var sound = Object(2, soundId); var mediaId = U32(sound.Payload, 5);
                    if (!packages.TryGetValue(mediaId, out var entry) || entry.Media.Length > 32 * 1024 * 1024) throw new InvalidDataException("Donor cry media is unavailable or oversized.");
                    using var input = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read); input.Position = entry.Media.Offset;
                    var encoded = new byte[entry.Media.Length]; input.ReadExactly(encoded);
                    Fingerprint("donor-media", Path.GetRelativePath(donorRoot, entry.Path).Replace('\\', '/') + "/" + mediaId.ToString(CultureInfo.InvariantCulture), encoded);
                    var decoded = PtAdpcmDecoder.Decode(encoded, token); media.Add(state, SoundBankDocument.EncodePcm16(decoded.Samples, decoded.Channels, decoded.SampleRate));
                }
                var contribution = SwShVoiceWriter.Create(targetBank, id, 7, media);
                result.AddRange(contribution.Objects.Select(o => new SwShRestorationContribution(SwShRestorationComposer.VoicesPath, SwShRestorationWriter.Voices,
                    string.Create(CultureInfo.InvariantCulture, $"{o.Type}:{o.Id}"), null, o.Payload)));
                result.AddRange(contribution.Media.Select(m => new SwShRestorationContribution(SwShRestorationComposer.VoicesPath, SwShRestorationWriter.Voices,
                    "media:" + m.Id.ToString(CultureInfo.InvariantCulture), null, m.Bytes)));
            }
            return result;
        }
        private static uint U32(byte[] bytes, int at)
        {
            if (at < 0 || at > bytes.Length - 4) throw new InvalidDataException("Donor voice record is truncated.");
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        }
    }
}
