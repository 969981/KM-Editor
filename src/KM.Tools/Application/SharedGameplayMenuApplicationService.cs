// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KM.Api.Output;
using KM.Api.RuntimeSettings;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;
using KM.Formats.Executable;
using KM.SwSh.RuntimeSettings;
using static KM.Tools.Application.InGameSettingsPackageApplicationService;

namespace KM.Tools.Application;

/// <summary>Edits shared project files and installs emulator settings as separate reviewed operations.</summary>
internal sealed partial class SharedGameplayMenuApplicationService : IDisposable
{
    private static readonly OwnershipOwnerId Owner = new("native-gameplay-output");
    private static readonly PreservationRuleDescriptor Rule = new("native-gameplay-shared-output", 1, true, true);
    private const string ReceiptRulePrefix = "native-gameplay-receipt.";
    private const string OutputMode = "sword-shield-layered-output";
    private const int MaximumBytes = 192 * 1024 * 1024;
    private const long MaximumReviewBytes = 512L * 1024 * 1024;
    private readonly NativeGameplayMenuBundleProvider provider = new();
    private readonly object sync = new();
    private readonly Dictionary<string, Review> reviews = new(StringComparer.Ordinal);
    private readonly Timer sweep;
    private bool disposed;

    public SharedGameplayMenuApplicationService() => sweep = new Timer(_ => { lock (sync) Prune(); },
        null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    public async Task<InspectInGameSettingsPackageResponse> InspectAsync(InspectInGameSettingsPackageRequest request,
        CancellationToken token) => new((await LoadAsync(request.Scope, request.InstallationTarget,
            request.InstallationRootPath, token, request.RequiredCameraHook is null, request.CameraControl).ConfigureAwait(false)).Snapshot);

    public async Task<PreviewInGameSettingsPackageResponse> PreviewAsync(PreviewInGameSettingsPackageRequest request,
        CancellationToken token)
    {
        var loaded = await LoadAsync(request.Scope, request.InstallationTarget, request.InstallationRootPath, token,
            request.RequiredCameraHook is null, request.CameraControl).ConfigureAwait(false);
        if (loaded.Snapshot.Revision != request.ExpectedRevision) throw new InGameSettingsPackageStateConflictException();
        var remove = request.Operation == InGameSettingsPackageOperationDto.Remove;
        var removeAll = remove;
        if (remove && !loaded.Context.IsExternal && loaded.Receipt?.Features is { } installedFeatures) {
            var remaining = installedFeatures & ~(request.CameraControl ? SwShNativeMenuFeatures.Camera : SwShNativeMenuFeatures.Gameplay);
            if (remaining != 0) {
                loaded = await LoadAsync(request.Scope, request.InstallationTarget, request.InstallationRootPath, token,
                    request.RequiredCameraHook is null, request.CameraControl, remaining).ConfigureAwait(false);
                removeAll = false;
            }
        }
        var requiredHook = !remove && !loaded.Context.IsExternal ? request.RequiredCameraHook : null;
        if (requiredHook is not null)
            loaded.ExternalDependencies = loaded.ExternalDependencies.Add(await ReviewRequiredHook(request.Scope, requiredHook, token).ConfigureAwait(false));
        if (remove ? loaded.Snapshot.InstalledPackage is null || loaded.Snapshot.State is not
                (InGameSettingsPackageStateDto.Installed or InGameSettingsPackageStateDto.UpgradeAvailable or InGameSettingsPackageStateDto.CoexistenceConflict)
            : !loaded.Snapshot.PackageAvailable || loaded.Snapshot.State != (request.Operation == InGameSettingsPackageOperationDto.Install
                ? InGameSettingsPackageStateDto.NotInstalled : InGameSettingsPackageStateDto.UpgradeAvailable))
            throw new InGameSettingsPackageUnavailableException(loaded.Snapshot.Detail ?? "The requested shared output operation is unavailable.");
        var mutations = loaded.Context.IsExternal ? PlanSettings(loaded, remove) : PlanOutput(loaded, removeAll);
        var mutationKeys = mutations.Select(mutation => mutation.Path.CanonicalKey).ToHashSet(StringComparer.Ordinal);
        var dependencies = loaded.Targets.Values.Where(target => !mutationKeys.Contains(target.Path.CanonicalKey))
            .Select(target => new OutputReadDependency(target.Path, target.State)).ToArray();
        var plan = new OutputApplyPlan(loaded.Context.ProjectId, loaded.Context.GameFamily, OutputMode,
            OutputReviewFingerprint.FromMutations(mutations),
            [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, Owner.Value)], mutations, dependencies,
            ownershipInventoryRevision: loaded.Ownership.Revision);
        var id = Guid.NewGuid().ToString("N");
        var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
        var retained = mutations.Sum(mutation => mutation.PlannedPostimage.LengthBytes)
            + (!removeAll && !loaded.Context.IsExternal ? loaded.NextReceiptBytes!.LongLength : 0);
        if (retained > MaximumReviewBytes) throw new InGameSettingsPackageUnavailableException("The shared output review exceeds its memory limit.");
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Prune();
            while (reviews.Count >= 16 || reviews.Values.Sum(review => review.RetainedBytes) + retained > MaximumReviewBytes)
                reviews.Remove(reviews.Values.MinBy(review => review.Expires)!.Id);
            reviews.Add(id, new(id, loaded.Context.ScopeKey, request.InstallationTarget, request.Operation, expiry, plan,
                loaded.ExternalDependencies, remove ? null : loaded.SourceReceiptState, requiredHook,
                !removeAll && !loaded.Context.IsExternal ? loaded.NextReceiptBytes : null, retained, request.CameraControl));
        }
        return new(id, expiry, request.Operation, loaded.Snapshot,
            mutations.Select(mutation => new InGameSettingsPackageTargetDto(mutation.Path.Value,
                mutation.Kind == OutputMutationKind.Delete ? InGameSettingsPackageTargetOperationDto.Delete : InGameSettingsPackageTargetOperationDto.Write)).ToArray(),
            false, [], false, remove ? null : new(
                loaded.Context.IsExternal ? InGameSettingsExecutableCompositionStrategyDto.SettingsOnly : InGameSettingsExecutableCompositionStrategyDto.SharedOutput,
                loaded.Context.IsExternal ? loaded.JournalPath.Value : "exefs/main", false, true, loaded.Context.IsExternal ? 0 : 9));
    }

    public async Task<ApplyInGameSettingsPackageResponse> ApplyAsync(ApplyInGameSettingsPackageRequest request, CancellationToken token)
    {
        var context = ResolveContext(request.Scope, request.InstallationTarget, request.InstallationRootPath);
        Review review;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Prune();
            if (string.IsNullOrEmpty(request.ReviewId) || !reviews.Remove(request.ReviewId, out review!)
                || review.ScopeKey != context.ScopeKey || review.Target != request.InstallationTarget)
                throw new InGameSettingsPackageReviewExpiredException();
        }
        if (review.RequiredHook is not null) await ReviewRequiredHook(request.Scope, review.RequiredHook, token).ConfigureAwait(false);
        using var leases = await AcquireExternalSourceLeasesAsync(review.ExternalDependencies, token).ConfigureAwait(false);
        await using var receiptLease = review.ReceiptBytes is null ? null
            : await context.Coordinator.StorePreservationArtifactAsync(review.ReceiptBytes, token).ConfigureAwait(false);
        OutputApplyResult result;
        if (context.IsExternal && review.SourceReceiptState is not null)
        {
            result = await context.Source.Coordinator.ExecuteExclusiveOutputOperationAsync(async cancellation =>
            {
                var path = ReceiptPath(ProjectGameMetadata.Get(context.Paths.SelectedGame!.Value).TitleId);
                var current = await Read(context.Source, path, cancellation).ConfigureAwait(false);
                if (current.State != review.SourceReceiptState) throw new InGameSettingsPackageStateConflictException();
                return await context.Coordinator.ApplyAsync(review.Plan, cancellation).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        else result = await context.Coordinator.ApplyAsync(review.Plan, token).ConfigureAwait(false);
        if (result.Outcome == OutputApplyOutcome.Committed && review.Operation == InGameSettingsPackageOperationDto.Remove)
        {
            try
            {
                await context.Coordinator.PruneRemovedDirectoriesAsync(context.ProjectId, context.GameFamily,
                    review.Plan.Mutations.Select(mutation => mutation.Path).ToArray(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        InGameSettingsPackageSnapshotDto? snapshot = null;
        if (result.Outcome != OutputApplyOutcome.RecoveryRequired)
        {
            try { snapshot = (await LoadAsync(request.Scope, request.InstallationTarget, request.InstallationRootPath, CancellationToken.None,
                review.RequiredHook is null, review.CameraControl).ConfigureAwait(false)).Snapshot; }
            catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException) { }
        }
        return new(result.TransactionId.Value, result.Outcome switch
        {
            OutputApplyOutcome.Committed => InGameSettingsPackageApplyOutcomeDto.Committed,
            OutputApplyOutcome.RolledBack => InGameSettingsPackageApplyOutcomeDto.RolledBack,
            _ => InGameSettingsPackageApplyOutcomeDto.RecoveryRequired,
        }, snapshot);
    }

    private async Task<Loaded> LoadAsync(OutputScopeDto scope, InGameSettingsInstallationTargetDto target, string? root, CancellationToken token,
        bool includeConsoleSettings = true, bool cameraControl = false, SwShNativeMenuFeatures? remainingFeatures = null)
    {
        var context = ResolveContext(scope, target, root);
        var title = ProjectGameMetadata.Get(context.Paths.SelectedGame!.Value).TitleId;
        var ownership = await context.Coordinator.GetOwnershipInventorySnapshotAsync(token).ConfigureAwait(false);
        var loaded = new Loaded(context, ownership, title) { IncludeConsoleSettings = includeConsoleSettings };
        try
        {
            var legacy = await Read(context.Destination, new($"{context.ControlPrefix}config/km-editor/gameplay-settings/{title:X16}/bundle.manifest"), token).ConfigureAwait(false);
            loaded.Targets.Add(legacy.Path.CanonicalKey, legacy);
            if (legacy.Exists) return Finish(loaded, InGameSettingsPackageStateDto.Conflict,
                "Remove the older package through Existing installations before preparing shared output or installing its settings.", "legacy-package-present");
            if (context.IsExternal) return await LoadSettings(loaded, token).ConfigureAwait(false);
            var receiptFile = await Read(context.Source, ReceiptPath(title), token).ConfigureAwait(false);
            loaded.SourceReceiptState = receiptFile.State;
            SharedGameplayMenuReceipt? sourceReceipt = null;
            if (receiptFile.Exists)
            {
                RequireOwned(context.Source, ownership, receiptFile, mutable: false);
                sourceReceipt = SharedGameplayMenuReceipt.Read(receiptFile.Bytes.ToArray(), title.ToString("X16"));
            }
            var runtimeOwner = ownership.Inventory.Files.SingleOrDefault(record => record.Path.CanonicalKey == "EXEFS/SUBSDK9");
            var privateReceiptHash = runtimeOwner?.Claims.FirstOrDefault(claim => claim.OwnerId == Owner
                && claim.PreservationRule.Key.StartsWith(ReceiptRulePrefix, StringComparison.Ordinal))?.PreservationRule.Key[ReceiptRulePrefix.Length..];
            if (privateReceiptHash is not null)
            {
                if (sourceReceipt is not null || runtimeOwner!.GameFamily != context.GameFamily
                    || !context.Source.Coordinator.ProjectScopeMatches(runtimeOwner.ProjectId, context.Source.ProjectId)
                    || runtimeOwner.Claims.Length != 1 || !runtimeOwner.FileDeleteEligible
                    || runtimeOwner.RuntimeMutableDescriptor is not null || !IsReceiptRule(runtimeOwner.Claims[0].PreservationRule))
                    throw new InvalidDataException("The private restoration reference does not match this output.");
                var privateReceipt = await context.Coordinator.ReadPreservationArtifactAsync(privateReceiptHash, token).ConfigureAwait(false);
                sourceReceipt = SharedGameplayMenuReceipt.Read(privateReceipt, title.ToString("X16"));
                loaded.ExternalDependencies = loaded.ExternalDependencies.Add(new(
                    Path.Combine(new OutputWorkspaceStorage(context.Source.Paths).MetadataRoot, "preservation", privateReceiptHash + ".bin"),
                    privateReceipt.LongLength, privateReceiptHash, MaximumBytes));
            }
            loaded.Targets.Add(receiptFile.Path.CanonicalKey, receiptFile);
            loaded.Receipt = sourceReceipt;
            loaded.Installed = sourceReceipt?.Package;
            var journal = await Track(loaded, loaded.JournalPath, token).ConfigureAwait(false);
            if (journal.Exists) RequireJournal(loaded, journal);
            var normalized = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            if (sourceReceipt is not null)
            {
                foreach (var file in sourceReceipt.Files) await Track(loaded, new(file.Path), token).ConfigureAwait(false);
                normalized = sourceReceipt.RemoveFrom(loaded.Targets.Values.ToDictionary(file => file.Path.Value,
                    file => file.Exists ? file.Bytes.ToArray() : null, StringComparer.Ordinal));
                loaded.Restored = normalized;
                var runtime = loaded.Targets["EXEFS/SUBSDK9"];
                if (runtime.Exists) RequireOwned(context.Source, ownership, runtime, mutable: false);
            }
            var requestedFeature = cameraControl ? SwShNativeMenuFeatures.Camera : SwShNativeMenuFeatures.Gameplay;
            var features = remainingFeatures ?? ((sourceReceipt?.Features ?? 0) | requestedFeature);
            var resolution = await provider.ResolveSharedAsync(context.Source.Paths, context.Paths.SelectedGame.Value, normalized, token, features).ConfigureAwait(false);
            loaded.ExternalDependencies = loaded.ExternalDependencies.AddRange(resolution.ExternalSourceDependencies ?? []);
            foreach (var dependency in resolution.SourceDependencies ?? [])
            {
                var current = await Track(loaded, dependency.Path, token).ConfigureAwait(false);
                if (current.State != dependency.ExpectedState) throw new InGameSettingsPackageStateConflictException();
            }
            var entry = resolution.Catalog.GetEntries(context.GameFamily, title)
                .SingleOrDefault(candidate => candidate.IsCurrent && resolution.Authority.IsAuthorized(candidate.AuthorityKey));
            if (entry is null) return Finish(loaded, sourceReceipt is null ? InGameSettingsPackageStateDto.Unavailable : InGameSettingsPackageStateDto.CoexistenceConflict,
                resolution.UnavailableDetail, resolution.SourceLayoutRejected ? "source-layout-incompatible" : "shared-output-source-unavailable");
            ValidateSource(loaded, "exefs/main", resolution.RequiresOwnedMainSource && !resolution.SemanticallyVerifiedMainSource);
            ValidateSource(loaded, "exefs/main.npdm", resolution.RequiresOwnedMainNpdmSource);
            var bundle = GameplayBundleArchive.Read(entry.ArchiveBytes, GameplaySettingsFamily.SwordShield);
            loaded.Available = ToDto(entry);
            loaded.Bootstrap = journal.Exists ? journal.Bytes.ToArray() : sourceReceipt?.Journal ?? bundle.SettingsJournal.ToArray();
            var generated = new List<SharedGameplayMenuFile>();
            foreach (var component in bundle.ImmutableComponents)
            {
                var prefix = $"atmosphere/contents/{title:X16}/";
                if (!component.Key.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Invalid shared component root.");
                var path = component.Key[prefix.Length..];
                if (!SharedGameplayMenuReceipt.IsComponentPath(path)) throw new InvalidDataException("Invalid shared component path.");
                var current = await Track(loaded, new(path), token).ConfigureAwait(false);
                if (path != "exefs/subsdk9")
                {
                    var baseRoot = path.StartsWith("exefs/", StringComparison.Ordinal) ? context.Paths.BaseExeFsPath! : context.Paths.BaseRomFsPath!;
                    loaded.VerifiedBases[path] = (await ReviewTargetAsync(baseRoot, new(path[6..]), MaximumBytes, token).ConfigureAwait(false)).Bytes.ToArray();
                }
                byte[]? before;
                if (!normalized.TryGetValue(path, out before)) before = current.Exists ? current.Bytes.ToArray() : null;
                if (before is null && path != "exefs/subsdk9")
                {
                    before = loaded.VerifiedBases[path];
                    if (before.Length == 0) throw new InvalidDataException("A required shared output base file is missing.");
                }
                var previous = sourceReceipt?.Files.Single(file => file.Path == path);
                generated.Add(new(path, previous?.Existed ?? current.Exists, before, component.Value.ToArray()));
            }
            loaded.NextReceipt = new(1, loaded.Available, loaded.Bootstrap, generated.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray(),
                includeConsoleSettings, features);
            loaded.NextReceiptBytes = loaded.NextReceipt.Write();
            loaded.NextReceiptRule = new(ReceiptRulePrefix + Hash(loaded.NextReceiptBytes), 1, true, true);
            var needsUpdate = sourceReceipt is not null && (generated.Any(file =>
                !ComponentMatches(file.Path, file.After, loaded.Targets[new RelativeOutputPath(file.Path).CanonicalKey].Bytes.AsSpan()))
                || privateReceiptHash is null || includeConsoleSettings && !journal.Exists
                || sourceReceipt.ConsoleSettings != includeConsoleSettings
                || sourceReceipt.Package.PackageVersion != loaded.Available.PackageVersion);
            var selectedInstalled = sourceReceipt is not null && (sourceReceipt.Features is null
                || sourceReceipt.Features.Value.HasFlag(requestedFeature));
            if (!selectedInstalled) loaded.Installed = null;
            return Finish(loaded, !selectedInstalled ? InGameSettingsPackageStateDto.NotInstalled
                : needsUpdate ? InGameSettingsPackageStateDto.UpgradeAvailable : InGameSettingsPackageStateDto.Installed);
        }
        catch (SourceOwnershipException exception)
        {
            return Finish(loaded, InGameSettingsPackageStateDto.Conflict,
                exception.Message, exception.Reason, exception.Target);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return Finish(loaded, InGameSettingsPackageStateDto.Conflict,
                "The shared output or settings receipt does not match its current owned files. Existing edits were preserved.", "shared-output-conflict");
        }
    }

    private async Task<InGameSettingsExternalSourceDependency> ReviewRequiredHook(KM.Api.Output.OutputScopeDto scope,
        CameraHookRequirementDto hook, CancellationToken token)
    {
        if (hook.InstallationTarget is not (InGameSettingsInstallationTargetDto.Ryujinx or InGameSettingsInstallationTargetDto.Eden))
            throw new InGameSettingsPackageUnavailableException("Choose an emulator installation in Camera Hook.");
        var loaded = await LoadAsync(scope, hook.InstallationTarget, hook.InstallationRootPath, token).ConfigureAwait(false);
        if (loaded.Snapshot.State != InGameSettingsPackageStateDto.Installed)
            throw new InGameSettingsPackageUnavailableException("Install or repair Camera Hook before preparing emulator output in Camera Control.");
        var receipt = loaded.Targets[loaded.ReceiptPath.CanonicalKey];
        return new(Path.GetFullPath(Path.Combine(loaded.Context.Destination.Paths.OutputRootPath!, receipt.Path.Value)),
            receipt.State.LengthBytes, receipt.State.Sha256, MaximumBytes);
    }

    private static async Task<Loaded> LoadSettings(Loaded loaded, CancellationToken token)
    {
        var receipt = await Track(loaded, loaded.ReceiptPath, token).ConfigureAwait(false);
        var journal = await Track(loaded, loaded.JournalPath, token).ConfigureAwait(false);
        if (receipt.Exists)
        {
            RequireOwned(loaded.Context.Destination, loaded.Ownership, receipt, mutable: false);
            loaded.Installed = JsonSerializer.Deserialize<InGameSettingsPackageDescriptorDto>(receipt.Bytes.AsSpan());
            if (loaded.Installed?.TitleId != loaded.TitleId.ToString("X16")) throw new InvalidDataException("Invalid settings receipt.");
        }
        if (journal.Exists) RequireJournal(loaded, journal);
        var initialValues = receipt.Exists || journal.Exists ? null
            : await ReadInitialCameraHookValues(loaded, token).ConfigureAwait(false);
        var settings = NativeGameplayMenuBundleFactory.CreateCameraHookSettings(loaded.Context.Paths.SelectedGame!.Value, initialValues);
        loaded.Available = settings.Package;
        loaded.Bootstrap = settings.Journal;
        if (receipt.Exists) return Finish(loaded, journal.Exists && loaded.Installed!.PackageVersion == settings.Package.PackageVersion
            ? InGameSettingsPackageStateDto.Installed : InGameSettingsPackageStateDto.UpgradeAvailable);
        if (journal.Exists) throw new InvalidDataException("An unrecognized settings journal occupies the destination.");
        return Finish(loaded, InGameSettingsPackageStateDto.NotInstalled);
    }

    private static List<OutputMutation> PlanOutput(Loaded loaded, bool remove)
    {
        var mutations = new List<OutputMutation>();
        var receipt = remove ? loaded.Receipt! : loaded.NextReceipt!;
        foreach (var file in receipt.Files)
        {
            var path = new RelativeOutputPath(file.Path);
            var current = loaded.Targets[path.CanonicalKey];
            var owned = FindOwner(loaded.Ownership, path, current.State);
            var beforeClaims = owned?.Claims ?? [];
            var otherClaims = beforeClaims.Where(claim => claim.OwnerId != Owner).ToArray();
            if (!remove)
            {
                var claim = path.CanonicalKey == "EXEFS/SUBSDK9"
                    ? new OwnedTarget(loaded.Context.GameFamily, new(path), Owner, loaded.NextReceiptRule!) : Claim(loaded, path);
                var claims = (path.CanonicalKey == "EXEFS/SUBSDK9" ? otherClaims.AsEnumerable() : beforeClaims)
                    .Append(claim).Distinct().ToArray();
                if (current.Exists && current.Bytes.AsSpan().SequenceEqual(file.After)
                    && (path.CanonicalKey != "EXEFS/SUBSDK9" || beforeClaims.SequenceEqual(claims))) continue;
                mutations.Add(current.Exists && current.Bytes.AsSpan().SequenceEqual(file.After)
                    ? OutputMutation.RebindOwnership(path, file.After, current.State, claims, owned?.OutputMode ?? OutputMode, Owner)
                    : OutputMutation.Write(path, file.After, current.State, claims,
                        owned?.OutputMode ?? OutputMode, ownershipActor: Owner));
                continue;
            }
            if (!current.Exists) continue;
            var restored = loaded.Restored![file.Path];
            if (restored is null)
            {
                RequireOwned(loaded.Context.Source, loaded.Ownership, current, mutable: false);
                mutations.Add(OutputMutation.Delete(path, current.State, owned!.Claims));
                continue;
            }
            var baseEquivalent = loaded.VerifiedBases.TryGetValue(file.Path, out var verifiedBase)
                && ComponentMatches(file.Path, verifiedBase, restored);
            if (baseEquivalent && owned is { FileDeleteEligible: true } && otherClaims.All(OutputCreatorProvenance.IsClaim)
                && beforeClaims.Any(claim => claim.OwnerId == Owner))
            {
                mutations.Add(OutputMutation.DeleteVerifiedBase(path, current.State, beforeClaims,
                    new(loaded.Context.ProjectId, loaded.Context.GameFamily, Owner, owned.OutputMode, path,
                        current.State, State(loaded.VerifiedBases[file.Path]), beforeClaims)));
                continue;
            }
            var claimsAfter = otherClaims.AsEnumerable();
            if (owned is { FileDeleteEligible: true } && !otherClaims.Any(claim => claim.Address.ScopeKind == OwnedTargetScopeKind.File))
                claimsAfter = claimsAfter.Append(OutputCreatorProvenance.Create(loaded.Context.GameFamily, path));
            if (!claimsAfter.Any()) claimsAfter = [Claim(loaded, path)];
            if (current.Bytes.AsSpan().SequenceEqual(restored)) continue;
            mutations.Add(OutputMutation.Write(path, restored, current.State, claimsAfter,
                owned?.OutputMode ?? OutputMode, ownershipActor: Owner));
        }
        // Restoration data stays in private storage. Console delivery alone needs a journal copy.
        var legacyReceipt = loaded.Targets[loaded.ReceiptPath.CanonicalKey];
        if (legacyReceipt.Exists)
        {
            var owned = RequireOwned(loaded.Context.Destination, loaded.Ownership, legacyReceipt, false);
            mutations.Add(OutputMutation.Delete(legacyReceipt.Path, legacyReceipt.State, owned.Claims));
        }
        var journal = loaded.Targets[loaded.JournalPath.CanonicalKey];
        if (remove || !loaded.IncludeConsoleSettings)
        {
            if (journal.Exists)
            {
                var owned = RequireJournal(loaded, journal);
                mutations.Add(OutputMutation.DeleteRuntimeMutable(journal.Path, journal.Bytes.AsMemory(), journal.State,
                    owned.Claims, loaded.Context.GameFamily, loaded.TitleId));
            }
        }
        else if (!journal.Exists)
            mutations.Add(OutputMutation.WriteRuntimeMutableBootstrap(journal.Path, loaded.Bootstrap!, journal.State,
                [Claim(loaded, journal.Path)], loaded.Context.GameFamily, loaded.TitleId));
        return mutations;
    }

    private static List<OutputMutation> PlanSettings(Loaded loaded, bool remove)
    {
        var mutations = new List<OutputMutation>();
        AddControlMutations(loaded, mutations, remove, remove ? null : JsonSerializer.SerializeToUtf8Bytes(loaded.Available));
        return mutations;
    }

    private static void AddControlMutations(Loaded loaded, List<OutputMutation> mutations, bool remove, byte[]? receipt)
    {
        var receiptTarget = loaded.Targets[loaded.ReceiptPath.CanonicalKey];
        var journal = loaded.Targets[loaded.JournalPath.CanonicalKey];
        if (remove)
        {
            if (receiptTarget.Exists)
            {
                var owned = RequireOwned(loaded.Context.Destination, loaded.Ownership, receiptTarget, false);
                mutations.Add(OutputMutation.Delete(receiptTarget.Path, receiptTarget.State, owned.Claims));
            }
            if (journal.Exists)
            {
                var owned = RequireJournal(loaded, journal);
                mutations.Add(OutputMutation.DeleteRuntimeMutable(journal.Path, journal.Bytes.AsMemory(), journal.State,
                    owned.Claims, loaded.Context.GameFamily, loaded.TitleId));
            }
            return;
        }
        if (!receiptTarget.Exists || !receiptTarget.Bytes.AsSpan().SequenceEqual(receipt))
            mutations.Add(OutputMutation.Write(receiptTarget.Path, receipt!, receiptTarget.State, [Claim(loaded, receiptTarget.Path)]));
        if (!journal.Exists) mutations.Add(OutputMutation.WriteRuntimeMutableBootstrap(journal.Path, loaded.Bootstrap!, journal.State,
            [Claim(loaded, journal.Path)], loaded.Context.GameFamily, loaded.TitleId));
    }

    private static void ValidateSource(Loaded loaded, string path, bool needsOwnership)
    {
        var file = loaded.Targets[new RelativeOutputPath(path).CanonicalKey];
        if (!file.Exists) return;
        var owned = loaded.Ownership.Inventory.Files.SingleOrDefault(record => record.Path == file.Path);
        if (owned is not null && (!loaded.Context.Source.Coordinator.ProjectScopeMatches(owned.ProjectId, loaded.Context.Source.ProjectId)
            || owned.GameFamily != loaded.Context.GameFamily || owned.RuntimeMutableDescriptor is not null))
            throw new SourceOwnershipException(file, "shared-source-scope-conflict",
                $"The ownership record for {path} belongs to another game workspace or describes a runtime settings file. Check the selected game and Output Root, then refresh. Existing files were preserved.");
        // A replaced file can be admitted by current executable proof, but old claims cannot authorize its new bytes.
        if (owned?.CurrentState != file.State) owned = null;
        if (needsOwnership && (owned is null || !owned.Claims.Any(claim => !OutputCreatorProvenance.IsClaim(claim))
            || owned.Claims.Any(claim => !claim.PreservationRule.PreservesUnownedData || !claim.PreservationRule.RequiresPreimage)))
            throw new SourceOwnershipException(file, "shared-source-unverified",
                $"KM cannot verify the changes in {path}, and no matching preservation record covers its current contents. Restore a verified version of that file or reapply its changes with the relevant KM editor, then refresh. Existing files were preserved.");
    }

    private static OutputOwnershipRecord RequireJournal(Loaded loaded, ReviewedTarget journal)
    {
        var owned = RequireOwned(loaded.Context.Destination, loaded.Ownership, journal, mutable: true);
        var inspection = GameplaySettingsJournal.Inspect(journal.Bytes.ToArray(), GameplaySettingsFamily.SwordShield, loaded.TitleId);
        if (!inspection.WritesAllowed || inspection.ActiveSnapshot is null) throw new InvalidDataException("Invalid gameplay settings journal.");
        return owned;
    }

    private static OutputOwnershipRecord RequireOwned(OutputScopeContext scope, OutputOwnershipInventorySnapshot inventory,
        ReviewedTarget target, bool mutable)
    {
        var owned = inventory.Inventory.Files.SingleOrDefault(record => record.Path == target.Path);
        if (owned is null || !scope.Coordinator.ProjectScopeMatches(owned.ProjectId, scope.ProjectId)
            || owned.GameFamily != scope.GameFamily || !owned.FileDeleteEligible
            || owned.Claims.Length != 1 || owned.Claims[0].OwnerId != Owner
            || owned.Claims[0].PreservationRule != Rule && !(target.Path.CanonicalKey == "EXEFS/SUBSDK9" && IsReceiptRule(owned.Claims[0].PreservationRule))
            || !mutable && (owned.CurrentState != target.State || owned.RuntimeMutableDescriptor is not null)
            || mutable && owned.RuntimeMutableDescriptor is null)
            throw new InvalidDataException("The native settings file does not have exact KM ownership.");
        return owned;
    }

    private static OutputOwnershipRecord? FindOwner(OutputOwnershipInventorySnapshot inventory, RelativeOutputPath path, OutputFileState state) =>
        inventory.Inventory.Files.SingleOrDefault(record => record.Path == path && record.CurrentState == state && state.Exists);

    private static OwnedTarget Claim(Loaded loaded, RelativeOutputPath path) =>
        new(loaded.Context.GameFamily, new OwnedTargetAddress(path), Owner, Rule);

    private static bool IsReceiptRule(PreservationRuleDescriptor rule) => rule.SchemaVersion == 1
        && rule.PreservesUnownedData && rule.RequiresPreimage
        && rule.Key.StartsWith(ReceiptRulePrefix, StringComparison.Ordinal)
        && rule.Key.Length == ReceiptRulePrefix.Length + 64
        && rule.Key[ReceiptRulePrefix.Length..].All(char.IsAsciiHexDigitLower);

    private static PackageScopeContext ResolveContext(OutputScopeDto scope, InGameSettingsInstallationTargetDto target, string? root)
    {
        var context = ResolvePackageScope(scope, target, root);
        if (context.Paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
            || target != InGameSettingsInstallationTargetDto.Atmosphere && !context.IsExternal)
            throw new InGameSettingsPackageUnavailableException("Choose a supported game and an emulator data folder for settings installation.");
        return context;
    }

    private static Loaded Finish(Loaded loaded, InGameSettingsPackageStateDto state, string? detail = null, string? reason = null,
        ReviewedTarget? rejectedSource = null)
    {
        var fingerprint = new StringBuilder(loaded.Context.ScopeKey).Append(loaded.Ownership.Revision).Append(state)
            .Append(loaded.SourceReceiptState).Append(loaded.Available?.ArchiveSha256).Append(loaded.IncludeConsoleSettings);
        foreach (var target in loaded.Targets.Values.OrderBy(target => target.Path.CanonicalKey, StringComparer.Ordinal))
            fingerprint.Append(target.Path.CanonicalKey).Append(target.State);
        foreach (var dependency in loaded.ExternalDependencies) fingerprint.Append(dependency.AbsolutePath).Append(dependency.ExpectedSha256);
        var source = rejectedSource ?? loaded.Targets.GetValueOrDefault("EXEFS/MAIN");
        loaded.Snapshot = new(state, loaded.Installed is not null, Hash(Encoding.UTF8.GetBytes(fingerprint.ToString())),
            loaded.Available is not null, loaded.Installed, loaded.Available,
            new(source?.Exists == true ? InGameSettingsExecutableInputSourceDto.StandaloneOutput : InGameSettingsExecutableInputSourceDto.None,
                rejectedSource is not null ? InGameSettingsExecutableCompatibilityDto.OwnershipUnverified
                    : source?.Exists == true ? InGameSettingsExecutableCompatibilityDto.CompatiblePreservable : InGameSettingsExecutableCompatibilityDto.Absent,
                reason ?? "shared-output-ready", source?.Exists == true ? source.Path.Value : null,
                source?.Exists == true ? source.State.Sha256 : null, source?.Exists == true ? source.State.LengthBytes : null), detail);
        return loaded;
    }

    private static RelativeOutputPath ReceiptPath(ulong title) => new($"config/km-editor/gameplay-settings/{title:X16}/shared-output.bin");
    private static bool ComponentMatches(string path, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> current) =>
        expected.SequenceEqual(current) || path == "exefs/main"
            && (NsoRegisteredRegionCompositionVerifier.SemanticallyMatches(expected, current)
                || NsoRegisteredRegionCompositionVerifier.SemanticallyMatches(current, expected));
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static OutputFileState State(byte[] bytes) => OutputFileState.Existing(Hash(bytes), bytes.LongLength);
    private static Task<ReviewedTarget> Read(OutputScopeContext context, RelativeOutputPath path, CancellationToken token) =>
        ReviewTargetAsync(context.Paths.OutputRootPath!, path, MaximumBytes, token);
    private static async Task<ReviewedTarget> Track(Loaded loaded, RelativeOutputPath path, CancellationToken token)
    {
        if (loaded.Targets.TryGetValue(path.CanonicalKey, out var previous)) return previous;
        var target = await Read(loaded.Context.Destination, path, token).ConfigureAwait(false);
        loaded.Targets.Add(path.CanonicalKey, target);
        return target;
    }

    private void Prune()
    {
        foreach (var id in reviews.Values.Where(review => review.Expires <= DateTimeOffset.UtcNow).Select(review => review.Id).ToArray()) reviews.Remove(id);
    }

    public void Dispose()
    {
        lock (sync) { disposed = true; reviews.Clear(); }
        sweep.Dispose();
    }

    private sealed class SourceOwnershipException(ReviewedTarget source, string reason, string message) : IOException(message)
    {
        public ReviewedTarget Target { get; } = source;
        public string Reason { get; } = reason;
    }

    private sealed record Review(string Id, string ScopeKey, InGameSettingsInstallationTargetDto Target,
        InGameSettingsPackageOperationDto Operation, DateTimeOffset Expires,
        OutputApplyPlan Plan, ImmutableArray<InGameSettingsExternalSourceDependency> ExternalDependencies,
        OutputFileState? SourceReceiptState, CameraHookRequirementDto? RequiredHook, byte[]? ReceiptBytes, long RetainedBytes,
        bool CameraControl);

    private sealed class Loaded(PackageScopeContext context, OutputOwnershipInventorySnapshot ownership, ulong title)
    {
        public PackageScopeContext Context { get; } = context;
        public OutputOwnershipInventorySnapshot Ownership { get; } = ownership;
        public ulong TitleId { get; } = title;
        public RelativeOutputPath ReceiptPath { get; } = context.IsExternal
            ? new($"{context.ControlPrefix}config/km-editor/gameplay-settings/{title:X16}/settings.installation.json")
            : SharedGameplayMenuApplicationService.ReceiptPath(title);
        public RelativeOutputPath JournalPath { get; } = new($"{context.ControlPrefix}config/km-editor/gameplay-settings/{title:X16}/settings.bin");
        public Dictionary<string, ReviewedTarget> Targets { get; } = new(StringComparer.Ordinal);
        public InGameSettingsPackageSnapshotDto Snapshot { get; set; } = null!;
        public InGameSettingsPackageDescriptorDto? Installed { get; set; }
        public InGameSettingsPackageDescriptorDto? Available { get; set; }
        public SharedGameplayMenuReceipt? Receipt { get; set; }
        public SharedGameplayMenuReceipt? NextReceipt { get; set; }
        public byte[]? NextReceiptBytes { get; set; }
        public PreservationRuleDescriptor? NextReceiptRule { get; set; }
        public bool IncludeConsoleSettings { get; set; }
        public byte[]? Bootstrap { get; set; }
        public Dictionary<string, byte[]?>? Restored { get; set; }
        public Dictionary<string, byte[]> VerifiedBases { get; } = new(StringComparer.Ordinal);
        public OutputFileState? SourceReceiptState { get; set; }
        public ImmutableArray<InGameSettingsExternalSourceDependency> ExternalDependencies { get; set; } = [];
    }
}
