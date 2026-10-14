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
            foreach (var id in species)
            {
                var states = ZaRestorationVoiceGraph.Resolve(source, id);
                var media = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var state in SwShVoiceWriter.States)
                {
                    token.ThrowIfCancellationRequested();
                    var mediaId = states[state];
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
    }
}
