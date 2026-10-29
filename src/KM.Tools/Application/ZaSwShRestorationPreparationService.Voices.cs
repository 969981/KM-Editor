// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using KM.Formats.Audio;
using KM.Formats.SwSh;
using KM.Formats.ZA;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal IReadOnlyList<SwShRestorationContribution> Voices(IReadOnlyList<KM.SwSh.Pokemon.SwShPokemonIdentity> identities)
        {
            if (identities.All(i => Profile(i).PlaAssets)) return PlaVoices(identities);
            var audioRoot = Path.Combine(donorRoot, "audio", "wwise", "bin");
            var directories = Directory.EnumerateDirectories(audioRoot).Take(33).ToArray();
            if (directories.Length > 32) throw new InvalidDataException("Donor audio directory count exceeds its budget.");
            var banks = directories.Prepend(audioRoot).Select(path => Path.Combine(path, "PM_VOICE.bnk"))
                .Where(System.IO.File.Exists).ToArray();
            if (banks.Length != 1) throw new InvalidDataException("Donor voice bank is missing or ambiguous.");
            var relative = Path.GetRelativePath(donorRoot, banks[0]).Replace('\\', '/');
            var bytes = archive.Read(relative, 64 * 1024 * 1024);
            Fingerprint("donor", relative, bytes); var source = new SoundBankDocument(bytes);
            var targetBank = new SoundBankDocument(Native(SwShRestorationComposer.VoicesPath[6..]));
            if (source.Version != 145 || targetBank.Version != 128) throw new InvalidDataException("Voice conversion is not defined for these sound bank versions.");
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
            var result = new List<SwShRestorationContribution>();
            foreach (var identity in identities)
            {
                var id = identity.Species;
                IReadOnlySet<uint>? inactiveGroups = null;
                if (id == 491)
                {
                    const uint donorContext = 0xe0515992;
                    var key = BitConverter.GetBytes(donorContext);
                    if (targetBank.Objects.Any(o => o.Payload.AsSpan().IndexOf(key) >= 0))
                        throw new InvalidDataException("Darkrai's donor voice context overlaps a native context.");
                    inactiveGroups = new HashSet<uint> { donorContext };
                }
                var states = ZaRestorationVoiceGraph.ResolvePlayback(source, id, identity.Form, inactiveGroups);
                var media = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var state in SwShVoiceWriter.States)
                {
                    token.ThrowIfCancellationRequested();
                    var mediaId = states[state].MediaId;
                    if (!packages.TryGetValue(mediaId, out var entry) || entry.Media.Length > 32 * 1024 * 1024) throw new InvalidDataException("Donor cry media is unavailable or oversized.");
                    using var input = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read); input.Position = entry.Media.Offset;
                    var encoded = new byte[entry.Media.Length]; input.ReadExactly(encoded);
                    Fingerprint("donor-media", Path.GetRelativePath(donorRoot, entry.Path).Replace('\\', '/') + "/" + mediaId.ToString(CultureInfo.InvariantCulture), encoded);
                    var decoded = PtAdpcmDecoder.Decode(encoded, token); media.Add(state, SoundBankDocument.EncodePcm16(decoded.Samples, decoded.Channels, decoded.SampleRate));
                }
                var contribution = SwShVoiceWriter.Create(targetBank, id, 7, media, identity.Form);
                var gains = states.Values.Select(v => v.GainDecibels).Distinct().ToArray();
                if (gains.Length != 1) throw new InvalidDataException("Z-A cry gains need a per state target adapter.");
                result.AddRange(contribution.Objects.Select(o => o.Type == 2 ? Gain(o, gains[0]) : o).Select(o => new SwShRestorationContribution(SwShRestorationComposer.VoicesPath, SwShRestorationWriter.Voices,
                    string.Create(CultureInfo.InvariantCulture, $"{o.Type}:{o.Id}"), null, o.Payload)));
                result.AddRange(contribution.Media.Select(m => new SwShRestorationContribution(SwShRestorationComposer.VoicesPath, SwShRestorationWriter.Voices,
                    "media:" + m.Id.ToString(CultureInfo.InvariantCulture), null, m.Bytes)));
            }
            return result;
        }

        private static SoundBankObject Gain(SoundBankObject sound, float decibels)
        {
            if (decibels == 0) return sound;
            var source = sound.Payload;
            // The selected version 128 PCM template has no local properties.
            // Preserve sample data and carry source makeup gain as a bank property.
            if (!float.IsFinite(decibels) || decibels is < -96 or > 24 || source.Length < 28
                || source[14] != 0 || source[15] != 0 || source[16] != 0 || source[25] != 0 || source[26] != 0)
                throw new InvalidDataException("Native voice gain requires another target adapter.");
            var bytes = new byte[source.Length + 5]; source.AsSpan(0, 27).CopyTo(bytes);
            bytes[26] = 1; bytes[27] = 6; BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(28), decibels);
            source.AsSpan(27).CopyTo(bytes.AsSpan(32));
            return sound with { Payload = bytes };
        }
    }
}
