// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;
using KM.Formats.Audio;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.Items;

namespace KM.SwSh.Restoration;

public sealed record SwShRestorationIntent(string DefinitionId, string PreparedHash, bool Enabled, string? ExpectedStateHash);
public sealed record SwShRestorationStageResult(EditSession Session, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShRestorationPreview(IReadOnlyDictionary<string, byte[]?> Files, string StateHash, string? PreviousStateHash,
    SwShRestorationState State, IReadOnlyList<ProjectFileReference> Sources, IReadOnlyList<int> RemovedSpecies);

public sealed class SwShRestorationService
{
    public const string Domain = SwShRestorationDefinitions.Domain;
    public const string SourceCode = "KM-SWSH-RESTORATION-SOURCE-UNAVAILABLE";
    public const string ConflictCode = "KM-SWSH-RESTORATION-CONFLICT";
    public const string SessionCode = "KM-SWSH-RESTORATION-SESSION-INVALID";
    public const string ReferenceCode = "KM-SWSH-RESTORATION-REFERENCE";
    public const string StaleCode = "KM-SWSH-RESTORATION-PREPARED-STALE";

    public SwShRestorationStageResult Stage(ProjectPaths paths, string definitionId, string preparedHash, bool enabled, EditSession? session)
    {
        var current = session ?? EditSession.Start();
        try
        {
            current = UpgradeSession(paths, current);
            if (definitionId == SwShRestorationDefinitions.Piplup.Id)
            {
                foreach (var definition in SwShRestorationDefinitions.PiplupSpecies)
                {
                    var result = Stage(paths, definition.Id, SwShRestorationCompatibility.Split(paths, preparedHash, definition.Id), enabled, current);
                    if (result.Diagnostics.Count > 0) return new(session ?? EditSession.Start(), result.Diagnostics);
                    current = result.Session;
                }
                return new(current, []);
            }
            var snapshot = SwShRestorationStateStore.Read(paths);
            var intent = new SwShRestorationIntent(definitionId, preparedHash, enabled, snapshot.Hash);
            var edit = CreateEdit(paths, intent);
            var pending = current.PendingEdits.Where(e => e.Domain != Domain || e.RecordId != definitionId).Append(edit).ToArray();
            var next = current with { PendingEdits = pending };
            _ = Preview(paths, next);
            return new(next, []);
        }
        catch (Exception exception) when (SourceError(exception)) { return new(current, [Error(SessionCode, exception.Message)]); }
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        var plan = CreateChangePlan(paths, session);
        return new(session, plan.CanApply, plan.Diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        try
        {
            var preview = Preview(paths, session);
            var references = CheckReferences(paths, preview);
            if (references.References.Count > 0) return new(session.Id, [], ReferenceDiagnostics(references));
            var sources = preview.Sources.Concat(references.Sources).Distinct().ToArray();
            var writes = preview.Files.Keys.Order(StringComparer.Ordinal).Select(path => new PlannedFileWrite(path,
                path == SwShRestorationComposer.PersonalPath ? sources : [],
                System.IO.File.Exists(Output(paths, path)), "Update restored Pokemon while retaining unrelated resources and project customizations.")).ToArray();
            return SwShChangePlanSourceGuard.Capture(paths, new(session.Id, writes, []) { EffectivePendingEdits = session.PendingEdits }, preserveExplicitSourceLayers: true);
        }
        catch (Exception exception) when (SourceError(exception)) { return new(session.Id, [], [Error(SourceCode, exception.Message)]); }
    }

    public static IReadOnlyList<ValidationDiagnostic> ReferenceDiagnostics(SwShPokemonReferences references) => references.References.Select(r =>
        new ValidationDiagnostic(DiagnosticSeverity.Error,
            $"Pokemon {r.Species}, form {r.Form}, is still used by {r.Editor}, record {r.Record}. Change that assignment before removal.",
            File: r.File, Domain: "workflow." + r.Editor, Field: r.Record + ":" + r.Field,
            Expected: $"Pokemon {r.Species}, form {r.Form}") { Code = ReferenceCode }).ToArray();

    private static SwShPokemonReferences CheckReferences(ProjectPaths paths, SwShRestorationPreview preview)
    {
        if (preview.RemovedSpecies.Count == 0) return new([], []);
        using var view = new SwShRestorationView(paths);
        WritePreview(view.Paths, preview);
        var references = SwShPokemonReferenceIndex.Find(view.Paths, preview.RemovedSpecies.ToHashSet());
        return references with { Sources = references.Sources.Select(s => new ProjectFileReference(
            System.IO.File.Exists(Output(paths, s.RelativePath)) ? ProjectFileLayer.Layered : ProjectFileLayer.Base, s.RelativePath)).ToArray() };
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed)
    {
        try
        {
            var plan = CreateChangePlan(paths, session);
            if (!plan.CanApply) return Result(plan, [], plan.Diagnostics);
            if (!ChangePlanReview.Matches(reviewed, plan)) return Result(plan, [], [Error(StaleCode, "The restoration plan changed. Review it again.")]);
            var failures = SwShChangePlanSourceGuard.Validate(paths, reviewed);
            if (failures.Any(d => d.Severity == DiagnosticSeverity.Error)) return Result(plan, [], failures);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, plan, out var scope, out failures, preserveExplicitSourceLayers: true)) return Result(plan, [], failures);
            using var verified = scope!;
            if (!verified.TryPrepareSnapshotPlan(CreateChangePlan(verified.ApplyPaths, session), out var prepared))
                return Result(plan, [], [Error(StaleCode, "Restoration sources changed while preparing the verified output.")]);
            var preview = Preview(verified.ApplyPaths, session);
            SwShChangePlanSourceGuard.BindRestorationState(verified.ApplyPaths, preview.PreviousStateHash, preview.StateHash);
            var written = WritePreview(verified.ApplyPaths, preview);
            return verified.Commit(Result(prepared, written, []));
        }
        catch (Exception exception) when (SourceError(exception)) { return Result(reviewed, [], [Error(ConflictCode, exception.Message)]); }
    }

    public SwShRestorationPreview Preview(ProjectPaths paths, EditSession session)
    {
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield) || string.IsNullOrWhiteSpace(paths.OutputRootPath)
            || string.IsNullOrWhiteSpace(paths.BaseRomFsPath)) throw new InvalidDataException("Select a Sword or Shield project with a base dump and output folder.");
        var edits = UpgradeSession(paths, session).PendingEdits.Where(e => e.Domain == Domain).ToArray();
        if (edits.Length is < 1 or > 234 || edits.Select(e => e.RecordId).Distinct(StringComparer.Ordinal).Count() != edits.Length)
            throw new InvalidDataException("Restoration selections are missing, duplicated, or excessive.");
        var snapshot = SwShRestorationStateStore.Read(paths); var selections = snapshot.State.Selections.ToDictionary(s => s.DefinitionId, StringComparer.Ordinal);
        var owned = SwShRestorationOwnership.ExactFiles(paths);
        var outputs = new Dictionary<string, byte[]?>(StringComparer.Ordinal); var sources = new HashSet<ProjectFileReference>(); var removed = new HashSet<int>();
        byte[]? Current(string path)
        {
            if (outputs.TryGetValue(path, out var value)) return value;
            return ReadEffective(paths, path, sources);
        }
        foreach (var edit in edits.OrderBy(e => e.RecordId, StringComparer.Ordinal))
        {
            var intent = Parse(edit);
            if (intent.ExpectedStateHash != snapshot.Hash) throw new InvalidDataException("Restoration selection changed after staging. Stage it again.");
            var expected = CreateEdit(paths, intent);
            if (edit.Summary != expected.Summary || edit.Owner != expected.Owner || edit.Field != expected.Field || !edit.Sources.SequenceEqual(expected.Sources))
                throw new InvalidDataException("Staged restoration metadata does not match its prepared definition.");
            foreach (var source in expected.Sources) sources.Add(source);
            var bundle = SwShRestorationStateStore.ReadPrepared(paths, intent.PreparedHash);
            if (bundle.DefinitionId != intent.DefinitionId) throw new InvalidDataException("Prepared restoration definition does not match the selection.");
            selections.TryGetValue(intent.DefinitionId, out var installed);
            if (installed is not null && installed.PreparedHash != intent.PreparedHash) throw new InvalidDataException("Installed definition versions remain pinned until a supported update is reviewed.");
            if (!intent.Enabled && (installed is null || !installed.Enabled)) throw new InvalidDataException("Only an installed, owned restoration can be removed.");
            VerifyTargetSources(paths, bundle);
            var effective = installed?.CustomizationHash is { } customHash ? SwShRestorationStateStore.ReadPrepared(paths, customHash) : bundle;
            if (effective.DefinitionId != bundle.DefinitionId) throw new InvalidDataException("Retained customizations belong to another definition.");
            if (!intent.Enabled) effective = SwShRestorationCustomizations.IncludeLooseModels(paths, effective, owned);
            if (!intent.Enabled) VerifyVoiceRemoval(paths, effective, Current(SwShRestorationComposer.VoicesPath)!);
            foreach (var item in effective.Contributions.Where(c => c.Writer == SwShRestorationWriter.File))
            {
                if (!System.IO.File.Exists(Output(paths, item.Path))) continue;
                var nativeFallback = intent.Enabled && item.Before is not null && BoundedRead(Output(paths, item.Path)).AsSpan().SequenceEqual(item.Before);
                if (!nativeFallback && (installed is null || !owned.Contains(item.Path)))
                    throw new InvalidDataException($"Restoration cannot replace or remove an externally supplied file: {item.Path}.");
            }
            var composition = SwShRestorationComposer.Compose(effective.Contributions, Current, intent.Enabled,
                allowOwnedCustomizations: installed?.Enabled == true, ownedPaths: owned);
            foreach (var file in composition.Files) outputs[file.Key] = file.Value;
            var customization = installed?.CustomizationHash;
            if (!intent.Enabled)
            {
                var retained = composition.RetainedCustomizations.ToDictionary(c => (c.Path, c.Key));
                var customized = effective with { Contributions = effective.Contributions.Select(c =>
                    SwShRestorationDependencies.Retain(c, retained.GetValueOrDefault((c.Path, c.Key), c), snapshot.State)).ToArray() };
                var stored = SwShRestorationStateStore.Store(paths, customized); using (stored.Lease) customization = stored.Hash;
                foreach (var identity in SwShRestorationDefinitions.Get(intent.DefinitionId).Identities) removed.Add(identity.Species);
            }
            selections[intent.DefinitionId] = new(intent.DefinitionId, intent.PreparedHash, intent.Enabled, customization);
        }
        var state = new SwShRestorationState(1, selections.Values.OrderBy(s => s.DefinitionId, StringComparer.Ordinal).ToArray());
        foreach (var file in SwShRestorationDependencies.Reconcile(paths, snapshot.State, state, Current)) outputs[file.Key] = file.Value;
        var storedState = SwShRestorationStateStore.Store(paths, state); using (storedState.Lease) { }
        if (!outputs.ContainsKey(SwShRestorationComposer.PersonalPath)) outputs.Add(SwShRestorationComposer.PersonalPath, Current(SwShRestorationComposer.PersonalPath)
            ?? throw new InvalidDataException("Restoration state requires a personal data anchor."));
        return new(outputs, storedState.Hash, snapshot.Hash, state, sources.OrderBy(s => s.RelativePath, StringComparer.Ordinal).ThenBy(s => s.Layer).ToArray(), removed.Order().ToArray());
    }

    public static IReadOnlyList<ProjectFileReference> WritePreview(ProjectPaths paths, SwShRestorationPreview preview)
    {
        var written = new List<ProjectFileReference>();
        foreach (var (path, bytes) in preview.Files)
        {
            var target = Output(paths, path, includeExecutable: true);
            if (bytes is null) { if (System.IO.File.Exists(target)) System.IO.File.Delete(target); }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); System.IO.File.WriteAllBytes(target, bytes);
                if (!System.IO.File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) throw new IOException("Restoration output failed its readback check.");
            }
            written.Add(new(ProjectFileLayer.Generated, path));
        }
        return written;
    }

    private static void VerifyVoiceRemoval(ProjectPaths paths, SwShPreparedRestoration prepared, byte[] current)
    {
        var items = prepared.Contributions.Where(c => c.Writer == SwShRestorationWriter.Voices).ToArray();
        var objectIds = items.Where(c => !c.Key.StartsWith("media:", StringComparison.Ordinal)).Select(c => uint.Parse(c.Key.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        var mediaIds = items.Where(c => c.Key.StartsWith("media:", StringComparison.Ordinal)).Select(c => uint.Parse(c.Key[6..], System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        var native = new SoundBankDocument(BoundedRead(Path.Combine(paths.BaseRomFsPath!, SwShRestorationComposer.VoicesPath[6..])));
        var bank = new SoundBankDocument(current);
        if (bank.Version != 128 || native.Version != 128) throw new InvalidDataException("Voice removal requires a supported native bank layout.");
        var nativeObjects = native.Objects.ToLookup(o => (o.Type, o.Id));
        foreach (var node in bank.Objects.Where(o => !objectIds.Contains(o.Id)))
        {
            if (nativeObjects[(node.Type, node.Id)].Any(o => o.Payload.AsSpan().SequenceEqual(node.Payload))) continue;
            var payload = node.Payload;
            uint Read(int at)
            {
                if (at < 0 || at > payload.Length - 4) throw new InvalidDataException("An external voice reference is truncated.");
                return BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(at));
            }
            var referenced = node.Type switch
            {
                2 when payload.Length >= 25 => objectIds.Contains(Read(21)) || mediaIds.Contains(Read(5)),
                3 when payload.Length >= 6 => objectIds.Contains(Read(2)),
                4 when payload.Length >= 1 && payload[0] <= 64 && payload.Length >= 1 + payload[0] * 4
                    => Enumerable.Range(0, payload[0]).Any(i => objectIds.Contains(Read(1 + i * 4))),
                7 when SoundBankActorMixer.TryReadReferences(bank.Version, payload, out var references)
                    => references.Any(objectIds.Contains),
                _ => throw new InvalidDataException($"External voice object {node.Type}:{node.Id} has an unsupported reference layout. Removal cannot safely inspect its dependencies."),
            };
            if (referenced) throw new InvalidDataException($"External voice object {node.Type}:{node.Id} still references this restoration. Remove that reference first.");
        }
    }
    private static PendingEdit CreateEdit(ProjectPaths paths, SwShRestorationIntent intent)
    {
        paths = SwShChangePlanSourceGuard.OriginalPaths(paths);
        var bundle = SwShRestorationStateStore.ReadPrepared(paths, intent.PreparedHash);
        if (bundle.DefinitionId != intent.DefinitionId) throw new InvalidDataException("Prepared restoration does not match its selection.");
        var sources = new HashSet<ProjectFileReference>();
        foreach (var path in bundle.Contributions.Select(c => c.Path).Distinct(StringComparer.Ordinal)) _ = ReadEffective(paths, path, sources);
        foreach (var source in bundle.Sources.Where(s => s.Role == "target")) sources.Add(new(ProjectFileLayer.Base, "romfs/" + source.RelativePath));
        return new(Domain, (intent.Enabled ? "Restore " : "Remove restored ") + intent.DefinitionId, sources.OrderBy(s => s.RelativePath, StringComparer.Ordinal).ThenBy(s => s.Layer).ToArray(),
            intent.DefinitionId, "enabled", JsonSerializer.Serialize(intent), Domain);
    }
    public static EditSession UpgradeSession(ProjectPaths paths, EditSession session)
    {
        if (!session.PendingEdits.Any(e => e.Domain == Domain && e.RecordId == SwShRestorationDefinitions.Piplup.Id)) return session;
        var edits = new List<PendingEdit>();
        foreach (var edit in session.PendingEdits)
        {
            if (edit.Domain != Domain || edit.RecordId != SwShRestorationDefinitions.Piplup.Id) { edits.Add(edit); continue; }
            var intent = Parse(edit); var expected = CreateEdit(paths, intent);
            if (edit.Summary != expected.Summary || edit.Owner != expected.Owner || edit.Field != expected.Field || !edit.Sources.SequenceEqual(expected.Sources))
                throw new InvalidDataException("Staged family restoration metadata is invalid.");
            foreach (var definition in SwShRestorationDefinitions.PiplupSpecies)
                edits.Add(CreateEdit(paths, intent with { DefinitionId = definition.Id,
                    PreparedHash = SwShRestorationCompatibility.Split(paths, intent.PreparedHash, definition.Id) }));
        }
        return session with { PendingEdits = edits };
    }
    private static SwShRestorationIntent Parse(PendingEdit edit)
    {
        if (edit.NewValue is null || edit.NewValue.Length > 4096) throw new InvalidDataException("Staged restoration intent is missing or oversized.");
        var intent = JsonSerializer.Deserialize<SwShRestorationIntent>(edit.NewValue) ?? throw new InvalidDataException("Staged restoration intent is empty.");
        if (intent.DefinitionId != edit.RecordId) throw new InvalidDataException("Staged restoration identity is inconsistent.");
        _ = SwShRestorationDefinitions.Get(intent.DefinitionId); return intent;
    }
    private static void VerifyTargetSources(ProjectPaths paths, SwShPreparedRestoration prepared)
    {
        foreach (var source in prepared.Sources.Where(s => s.Role == "target"))
        {
            _ = new KM.Core.Semantics.RelativeOutputPath(source.RelativePath);
            var path = Path.Combine(paths.BaseRomFsPath!, source.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != source.Length || Convert.ToHexStringLower(SHA256.HashData(stream)) != source.Sha256)
                throw new InvalidDataException("The target dump changed since restoration preparation. Prepare the Pokemon again.");
        }
    }
    private static byte[]? ReadEffective(ProjectPaths paths, string relative, ISet<ProjectFileReference> sources)
    {
        var output = Output(paths, relative);
        var native = Path.Combine(paths.BaseRomFsPath!, relative[6..].Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(output)) { sources.Add(new(ProjectFileLayer.Layered, relative)); return BoundedRead(output); }
        if (System.IO.File.Exists(native)) { sources.Add(new(ProjectFileLayer.Base, relative)); return BoundedRead(native); }
        return null;
    }
    private static byte[] BoundedRead(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Restoration source exceeds its file budget.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
    private static string Output(ProjectPaths paths, string relative, bool includeExecutable = false)
    {
        _ = new KM.Core.Semantics.RelativeOutputPath(relative);
        if (!(relative.StartsWith("romfs/", StringComparison.Ordinal) || includeExecutable && relative.StartsWith("exefs/", StringComparison.Ordinal))
            || string.IsNullOrWhiteSpace(paths.OutputRootPath)) throw new InvalidDataException("Restoration output path is invalid.");
        return Path.Combine(paths.OutputRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
    }
    internal static bool SourceError(Exception exception) => exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException
        or JsonException or OverflowException or KM.Core.Output.OutputCoordinatorException;
    internal static ValidationDiagnostic Error(string code, string message) => new(DiagnosticSeverity.Error, message, Domain: Domain) { Code = code };
    private static ApplyResult Result(ChangePlan plan, IReadOnlyList<ProjectFileReference> written, IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        return new(id, now, written, new(id, now, plan.Writes), diagnostics);
    }
}
