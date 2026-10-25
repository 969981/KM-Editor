// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.Trainers;

namespace KM.SwSh.Randomizer;

public sealed partial class SwShRandomizerService
{
    private SwShTrainersEditSessionService CreateTrainerRestoreService(ProjectPaths paths,
        IReadOnlyList<RandomizerRestoreEntry> entries, IReadOnlyList<RandomizerValueOwnership> ownership,
        ICollection<ValidationDiagnostic> diagnostics, out Dictionary<string, RestoreFilePreimage> sourcePreimages)
    {
        sourcePreimages = new(StringComparer.OrdinalIgnoreCase);
        if (!ownership.Any(row => row.Domain == "Trainers")) return trainersEditSessionService;
        var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var retainedBytes = 0;
        byte[] ReadOriginal(string file)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 16 * 1024 * 1024 || stream.Length > 64 * 1024 * 1024 - retainedBytes)
                throw new InvalidDataException("Trainer restoration sources exceed their supported size.");
            var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new IOException("Trainer restoration source changed while reading.");
            retainedBytes += bytes.Length;
            return bytes;
        }
        foreach (var entry in entries.Where(row => row.RelativePath.StartsWith("romfs/bin/trainer/", StringComparison.OrdinalIgnoreCase)))
        {
            byte[] bytes;
            if (entry.OriginalExisted == true)
            {
                var backup = PrepareRequiredRandomizerBackup(paths, entry, entry.RelativePath, diagnostics);
                if (backup is null) return trainersEditSessionService;
                bytes = ReadOriginal(backup.Path);
                if (RestoreFilePreimage.ForFile(bytes) != backup.Preimage) throw new IOException("Trainer restoration backup changed while reading.");
            }
            else if (entry.OriginalExisted == false && paths.BaseRomFsPath is not null)
            {
                var file = Path.GetFullPath(Path.Combine(paths.BaseRomFsPath, entry.RelativePath[6..]));
                bytes = ReadOriginal(file);
                sourcePreimages[file] = RestoreFilePreimage.ForFile(bytes);
            }
            else throw new InvalidDataException("Trainer restoration source ownership is unavailable.");
            originals[entry.RelativePath[5..]] = bytes;
        }
        var reader = SwShTrainersWorkflowService.CreateBounded(file =>
        {
            var normalized = file.Replace('\\', '/');
            var offset = normalized.LastIndexOf("/bin/trainer/", StringComparison.OrdinalIgnoreCase);
            return offset >= 0 && originals.TryGetValue(normalized[offset..], out var bytes) ? bytes.ToArray() : File.ReadAllBytes(file);
        });
        var baseline = reader.Load(projectWorkspaceService.Open(paths));
        AddWorkflowErrors(baseline.Diagnostics, diagnostics);
        // Some original teams encode a forced gender that the species ignores.
        // Only a matching verified preimage may restore that exact stored choice.
        return new(projectWorkspaceService, trainersWorkflowService, baseline.Trainers.SelectMany(trainer => trainer.Team
            .Select(pokemon => (Identity: (trainer.TrainerId, pokemon.Slot), Pokemon: pokemon)))
            .ToDictionary(row => row.Identity, row => row.Pokemon));
    }
}
