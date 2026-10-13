// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Models;
using KM.Formats.SV;
using KM.Formats.SwSh;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed record SwShRestorationPreparationProgress(string Phase, int CompletedSpecies, int TotalSpecies);

/// <summary>Converts a registered family using the user's donor and target dumps.</summary>
public sealed partial class SwShRestorationPreparationService
{
    public SwShPreparedRestoration Prepare(ProjectPaths target, string donorRomFs, string? compressionSupport,
        string definitionId, IProgress<SwShRestorationPreparationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var definition = SwShRestorationDefinitions.Get(definitionId);
        if (target.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield) || string.IsNullOrWhiteSpace(target.BaseRomFsPath)
            || definition.Id != SwShRestorationDefinitions.Piplup.Id) throw new InvalidDataException("This restoration is not supported for the selected target.");
        var context = new Preparation(target, donorRomFs, compressionSupport, definition, progress, cancellationToken);
        using (context)
        {
            var contributions = new List<SwShRestorationContribution>(); var unavailableMoves = new Dictionary<int, int[]>();
            foreach (var identity in definition.Identities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new("models", unavailableMoves.Count, definition.Identities.Count));
                contributions.AddRange(context.Assets(identity.Species));
                progress?.Report(new("gameplay", unavailableMoves.Count, definition.Identities.Count));
                var gameplay = context.Gameplay(identity.Species); contributions.AddRange(gameplay.Contributions); unavailableMoves.Add(identity.Species, gameplay.ExcludedMoves);
            }
            progress?.Report(new("voices", definition.Identities.Count, definition.Identities.Count));
            contributions.AddRange(context.Voices(definition.Identities.Select(i => i.Species).ToArray()));
            progress?.Report(new("validation", definition.Identities.Count, definition.Identities.Count));
            var prepared = new SwShPreparedRestoration(definition.Id, definition.Version, definition.ConverterVersion, target.SelectedGame.Value,
                context.Fingerprints.Values.ToArray(), contributions, unavailableMoves);
            // Validate the complete resource closure and every shared writer before
            // preparation can be offered as a stageable artifact.
            context.Verify(prepared);
            return prepared with { Sources = context.Fingerprints.Values.ToArray() };
        }
    }

    private sealed partial class Preparation : IDisposable
    {
        private readonly ProjectPaths target;
        private readonly string donorRoot;
        private readonly SvTrinityArchive archive;
        private readonly CancellationToken token;
        private readonly Dictionary<string, byte[]> native = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> donor = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, SwShRestorationSourceFingerprint> Fingerprints = new(StringComparer.Ordinal);
        private long retainedBytes;

        internal Preparation(ProjectPaths target, string donorRoot, string? support, SwShRestorationDefinition definition,
            IProgress<SwShRestorationPreparationProgress>? progress, CancellationToken token)
        {
            this.target = target; this.donorRoot = Path.GetFullPath(donorRoot); this.token = token;
            progress?.Report(new("sources", 0, definition.Identities.Count));
            token.ThrowIfCancellationRequested();
            archive = SvTrinityArchive.Open(donorRoot, support, maximumIndexBytes: 32 * 1024 * 1024, maximumPackBytes: 256L * 1024 * 1024);
        }
        internal byte[] Native(string path)
        {
            path = Canonical(path);
            if (native.TryGetValue(path, out var bytes)) return bytes;
            bytes = ReadPhysical(Path.Combine(target.BaseRomFsPath!, path.Replace('/', Path.DirectorySeparatorChar)), 64 * 1024 * 1024);
            Retain(bytes); native.Add(path, bytes); Fingerprint("target", path, bytes); return bytes;
        }
        private byte[] Donor(string path)
        {
            path = Canonical(path);
            if (donor.TryGetValue(path, out var bytes)) return bytes;
            token.ThrowIfCancellationRequested(); bytes = archive.ReadFile(path, 32 * 1024 * 1024);
            Retain(bytes); donor.Add(path, bytes); Fingerprint("donor", path, bytes); return bytes;
        }
        private void Retain(byte[] bytes)
        {
            retainedBytes += bytes.Length;
            if (retainedBytes > 384L * 1024 * 1024) throw new InvalidDataException("Restoration inputs exceed the preparation memory budget.");
        }
        private void Fingerprint(string role, string path, byte[] bytes) => Fingerprints[role + ":" + path] = new(role, path, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength);
        private byte[] ReadPhysical(string path, int maximum)
        {
            token.ThrowIfCancellationRequested();
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (source.Length is < 1 || source.Length > maximum) throw new InvalidDataException("Restoration input exceeds its file budget.");
            var bytes = new byte[checked((int)source.Length)]; source.ReadExactly(bytes);
            if (source.ReadByte() != -1) throw new InvalidDataException("Restoration input changed while reading.");
            return bytes;
        }
        private static string Canonical(string path)
        {
            if (TrinityPreviewReader.Resolve("catalog", path) != path) throw new InvalidDataException("Restoration input path is invalid.");
            return path;
        }
        private static byte[] Member(SwShGfPackFile pack, string name) => pack.TryGetFileByName(name, 32 * 1024 * 1024, out var bytes)
            ? bytes : throw new InvalidDataException($"A native donor resource is missing: {name}");
        private SwShRestorationContribution File(string path, byte[] bytes)
        {
            var physical = Path.Combine(target.BaseRomFsPath!, Canonical(path).Replace('/', Path.DirectorySeparatorChar));
            return new("romfs/" + path, SwShRestorationWriter.File, "", System.IO.File.Exists(physical) ? Native(path) : null, bytes);
        }
        public void Dispose() => archive.Dispose();
    }
}
