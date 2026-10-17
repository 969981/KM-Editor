// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.RuntimeSettings;
using KM.Core.Output;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;
using KM.SwSh.RuntimeSettings;
using static KM.Tools.Application.InGameSettingsPackageApplicationService;

namespace KM.Tools.Application;

internal sealed partial class SharedGameplayMenuApplicationService
{
    private const string RegistrationReason = "shared-installation-registration-required";

    private async Task<SharedGameplayMenuReceipt?> RecognizeOutput(Loaded loaded, CancellationToken token)
    {
        var runtime = await Track(loaded, new("exefs/subsdk9"), token).ConfigureAwait(false);
        if (!runtime.Exists) return null;
        RequireUnregistered(loaded, runtime);
        SwShNativeMenuFeatures features;
        try { features = SwShNativeMenuFeatureReader.Read(runtime.Bytes.ToArray()); }
        catch (InvalidDataException) { throw RecognitionFailure(runtime, "shared-runtime-unrecognized"); }

        var paths = new[] { "exefs/main", "exefs/main.npdm", "exefs/subsdk9" }
            .Concat(SharedGameplayMenuReceipt.Languages.SelectMany(language => new[] {
                $"romfs/bin/message/{language}/common/option.dat", $"romfs/bin/message/{language}/common/option.tbl" })).ToArray();
        var clean = paths.ToDictionary(path => path, _ => (byte[]?)null, StringComparer.Ordinal);
        var templateResolution = await provider.ResolveSharedAsync(loaded.Context.Paths,
            loaded.Context.Paths.SelectedGame!.Value, clean, token, features).ConfigureAwait(false);
        var entry = templateResolution.Catalog.GetEntries(loaded.Context.GameFamily, loaded.TitleId)
            .SingleOrDefault(candidate => candidate.IsCurrent && templateResolution.Authority.IsAuthorized(candidate.AuthorityKey));
        if (entry is null) throw RecognitionFailure(runtime, "shared-base-unavailable");
        loaded.ExternalDependencies = loaded.ExternalDependencies.AddRange(templateResolution.ExternalSourceDependencies ?? []);
        var template = GameplayBundleArchive.Read(entry.ArchiveBytes, GameplaySettingsFamily.SwordShield);
        var prefix = $"atmosphere/contents/{loaded.TitleId:X16}/";
        if (!runtime.Bytes.AsSpan().SequenceEqual(template.ImmutableComponents[prefix + runtime.Path.Value].AsSpan()))
            throw RecognitionFailure(runtime, "shared-runtime-unrecognized");
        var files = new List<SharedGameplayMenuFile>();
        foreach (var path in paths)
        {
            var current = await Track(loaded, new(path), token).ConfigureAwait(false);
            if (!current.Exists) throw RecognitionFailure(current, "shared-installation-incomplete");
            byte[]? before = null;
            if (path != runtime.Path.Value)
            {
                var root = path.StartsWith("exefs/", StringComparison.Ordinal)
                    ? loaded.Context.Paths.BaseExeFsPath! : loaded.Context.Paths.BaseRomFsPath!;
                before = (await InGameSettingsPackageApplicationService.ReviewTargetAsync(root, new(path[6..]), MaximumBytes, token)
                    .ConfigureAwait(false)).Bytes.ToArray();
            }
            files.Add(new(path, true, before, template.ImmutableComponents[prefix + path].ToArray()));
        }
        var receipt = new SharedGameplayMenuReceipt(1, InGameSettingsPackageApplicationService.ToDto(entry),
            template.SettingsJournal.ToArray(), files.ToArray(), loaded.Targets[loaded.JournalPath.CanonicalKey].Exists, features);
        Dictionary<string, byte[]?> normalized;
        try { normalized = receipt.RemoveFrom(paths.ToDictionary(path => path, path => (byte[]?)loaded.Targets[new RelativeOutputPath(path).CanonicalKey].Bytes.ToArray())); }
        catch (InvalidDataException) { throw RecognitionFailure(runtime, "shared-installation-content-conflict"); }
        var resolution = await provider.ResolveSharedAsync(loaded.Context.Paths,
            loaded.Context.Paths.SelectedGame.Value, normalized, token, features).ConfigureAwait(false);
        var rebuiltEntry = resolution.Catalog.GetEntries(loaded.Context.GameFamily, loaded.TitleId)
            .SingleOrDefault(candidate => candidate.IsCurrent && resolution.Authority.IsAuthorized(candidate.AuthorityKey));
        if (rebuiltEntry is null) throw RecognitionFailure(runtime, "shared-installation-content-conflict");
        var rebuilt = GameplayBundleArchive.Read(rebuiltEntry.ArchiveBytes, GameplaySettingsFamily.SwordShield);
        foreach (var path in paths)
        {
            var current = loaded.Targets[new RelativeOutputPath(path).CanonicalKey];
            if (!ComponentMatches(path, rebuilt.ImmutableComponents[prefix + path].AsSpan(), current.Bytes.AsSpan()))
                throw RecognitionFailure(current, "shared-installation-content-conflict");
        }
        loaded.Registrations.Add(runtime.Path.CanonicalKey);
        return receipt with { Package = InGameSettingsPackageApplicationService.ToDto(rebuiltEntry),
            Files = files.Select(file => file with { Before = normalized[file.Path],
                After = loaded.Targets[new RelativeOutputPath(file.Path).CanonicalKey].Bytes.ToArray() }).ToArray() };
    }

    private static void RequireUnregistered(Loaded loaded, ReviewedTarget target)
    {
        if (loaded.Ownership.Inventory.Files.Any(record => record.Path == target.Path))
            throw RecognitionFailure(target, "shared-installation-record-conflict");
    }

    private static void ValidateOrRecognizeJournal(Loaded loaded, ReviewedTarget journal)
    {
        if (loaded.Ownership.Inventory.Files.Any(record => record.Path == journal.Path))
        {
            RequireJournal(loaded, journal);
            return;
        }
        var inspection = GameplaySettingsJournal.Inspect(journal.Bytes.ToArray(), GameplaySettingsFamily.SwordShield, loaded.TitleId);
        if (!inspection.WritesAllowed || inspection.ActiveSnapshot is null)
            throw RecognitionFailure(journal, "shared-settings-invalid");
        loaded.Registrations.Add(journal.Path.CanonicalKey);
    }

    private static SourceOwnershipException RecognitionFailure(ReviewedTarget source, string reason) => new(source, reason,
        reason switch {
            "shared-base-unavailable" => "The selected Base files could not be verified. Check the game version and Base folders, then refresh.",
            "shared-runtime-unrecognized" => "The installed runtime is not a supported KM module. Its contents were preserved.",
            "shared-installation-incomplete" => "The copied installation is missing a required file. Restore the complete installation, then refresh.",
            "shared-settings-invalid" => "The settings file is damaged, unsupported, or belongs to another game. Its contents were preserved.",
            "shared-installation-record-conflict" => "The existing installation records do not match this file. Review the affected installation before making changes.",
            _ => "The installed runtime, executable changes, or menu text could not be verified together. Existing files were preserved."
        });

    private static OutputMutation Register(Loaded loaded, ReviewedTarget target, OwnedTarget claim, bool settings = false) =>
        OutputMutation.RegisterVerifiedFile(target.Path, target.Bytes.AsMemory(), target.State, claim,
            OutputMode, settings ? loaded.TitleId : null);
}
