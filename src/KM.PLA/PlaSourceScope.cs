// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.PLA;

internal sealed class PlaSourceScope : IDisposable
{
    internal const string PersonalPath = "bin/pml/personal/personal_data_total.perbin";
    internal const string LearnsetsPath = "bin/pml/waza_oboe/waza_oboe_total.wazaoboe";
    internal const string EvolutionsPath = "bin/pml/evolution/evolution_data_total.evobin";
    internal const string TutorsPath = "bin/appli/wazaremember/bin/wazashop_table.bin";
    internal static readonly string[] Languages = ["English", "French", "German", "Italian", "JPN", "JPN_KANJI", "Korean", "Simp_Chinese", "Spanish", "Trad_Chinese"];
    private readonly List<FileStream> leases = [];
    private readonly Dictionary<string, byte[]> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> vanilla = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OutputFileState> outputStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> catalogs = new(StringComparer.Ordinal);
    private readonly string executableFingerprint;
    private KM.Formats.PLA.PlaCompressionRuntime? compression;
    private string? compressionFingerprint;
    internal KM.Formats.PLA.PlaCompressionRuntime Compression
    {
        get
        {
            if (compression is not null) return compression;
            var folder = Paths.PokemonLegendsArceusSupportFolderPath;
            if (string.IsNullOrWhiteSpace(folder))
                throw new InvalidDataException("Set the PLA compression support folder in Project Setup before opening archive based PLA editors.");
            var path = Resolve(folder, KM.Formats.PLA.PlaCompressionRuntime.RequiredFileName);
            if (!File.Exists(path)) throw new InvalidDataException("The PLA support folder must contain oo2core_8_win64.dll.");
            using var hash = SHA256.Create();
            compressionFingerprint = Convert.ToHexString(hash.ComputeHash(OpenBase(path)));
            return compression = new(folder);
        }
    }
    internal ProjectPaths Paths { get; }
    internal string Language { get; }

    public PlaSourceScope(ProjectPaths paths)
    {
        if (paths.SelectedGame != ProjectGame.PLA) throw new InvalidDataException("Select Pokemon Legends Arceus for this editor.");
        if (!Directory.Exists(paths.BaseRomFsPath) || !Directory.Exists(paths.BaseExeFsPath))
            throw new InvalidDataException("PLA requires a Base RomFS folder and a Base ExeFS folder.");
        Paths = paths;
        Language = Languages.Contains(paths.GameTextLanguage, StringComparer.Ordinal) ? paths.GameTextLanguage! : paths.GameTextLanguage switch
        {
            "es" => "Spanish", "fr" => "French", "de" => "German", "zh" => "Simp_Chinese", _ => "English",
        };
        try
        {
            if (!string.IsNullOrWhiteSpace(paths.OutputRootPath))
            {
                var output = Path.GetFullPath(paths.OutputRootPath);
                foreach (var source in new[] { paths.BaseRomFsPath!, paths.BaseExeFsPath! })
                    if (Contains(output, source) || Contains(source, output))
                        throw new InvalidDataException("PLA Output Root must be separate from both Base folders.");
            }
            var main = OpenBase(Resolve(paths.BaseExeFsPath!, "main"));
            Span<byte> header = stackalloc byte[0x100];
            main.ReadExactly(header);
            if (!PlaProjectProfile.Matches(header))
                throw new InvalidDataException("This PLA build is not supported. The supported loose table profile is version 1.1.1.");
            main.Position = 0;
            var npdm = ReadBounded(OpenBase(Resolve(paths.BaseExeFsPath!, "main.npdm")));
            if (!PlaProjectProfile.MatchesMetadata(npdm))
                throw new InvalidDataException("The executable metadata does not identify Pokemon Legends Arceus.");
            executableFingerprint = Convert.ToHexString(SHA256.HashData(main)) + Hash(npdm);
        }
        catch { Dispose(); throw; }
    }

    private static bool Contains(string root, string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var other = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return other.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || other.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    internal static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException("The PLA resource path is invalid.");
        var result = Path.GetFullPath(Path.Combine(root, relative));
        if (!Contains(root, result)) throw new InvalidDataException("The PLA resource is outside its configured folder.");
        for (var current = new FileInfo(result) as FileSystemInfo; current is not null; current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("PLA source and output paths must not traverse filesystem links.");
        return result;
    }

    private FileStream OpenBase(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        leases.Add(stream);
        return stream;
    }

    private static byte[] ReadBounded(Stream stream)
    {
        if (stream.Length > KM.Formats.PLA.PlaFlatBuffer.MaximumLength)
            throw new InvalidDataException("The PLA resource exceeds the supported size limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    internal byte[] Base(string relative)
    {
        if (!vanilla.TryGetValue(relative, out var bytes))
            vanilla[relative] = bytes = ReadBounded(OpenBase(Resolve(Paths.BaseRomFsPath!, relative)));
        return bytes;
    }

    internal byte[] Read(string relative)
    {
        if (sources.TryGetValue(relative, out var bytes)) return bytes;
        var original = Base(relative);
        var output = string.IsNullOrWhiteSpace(Paths.OutputRootPath) ? null : Resolve(Paths.OutputRootPath, "romfs/" + relative);
        if (output is not null && File.Exists(output))
        {
            // The transaction coordinator rechecks this exact output preimage.
            using var stream = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read);
            bytes = ReadBounded(stream);
            outputStates[relative] = State(bytes);
        }
        else
        {
            bytes = original;
            outputStates[relative] = OutputFileState.Missing;
        }
        sources[relative] = bytes;
        return bytes;
    }

    internal string[] TextFiles(string? language = null) => Catalog("text/" + (language ?? Language), new[] { "common", "script" }
        .SelectMany(group => Directory.EnumerateFiles(Resolve(Paths.BaseRomFsPath!, $"bin/message/{language ?? Language}/{group}"), "*.dat", SearchOption.TopDirectoryOnly)
            .Select(path => group + "/" + Path.GetFileName(path)))
        .Order(StringComparer.Ordinal).Take(1025).ToArray() is var files && files.Length <= 1024
            ? files : throw new InvalidDataException("The PLA message catalog exceeds the supported limit."));

    internal string[] MoveFiles() => Catalog("moves", Directory.EnumerateFiles(Resolve(Paths.BaseRomFsPath!, "bin/pml/waza"), "*.wazabin", SearchOption.TopDirectoryOnly)
        .Select(path => "bin/pml/waza/" + Path.GetFileName(path)).Order(StringComparer.Ordinal).Take(2049).ToArray() is var files && files.Length is > 0 and <= 2048
            ? files : throw new InvalidDataException("The PLA move catalog is invalid."));

    internal string[] TrainerFiles() => Catalog("trainers", Directory.EnumerateFiles(Resolve(Paths.BaseRomFsPath!, "bin/trainer"), "trdata_*.bin", SearchOption.TopDirectoryOnly)
        .Select(path => "bin/trainer/" + Path.GetFileName(path)).Order(StringComparer.Ordinal).Take(1025).ToArray() is var files && files.Length is > 0 and <= 1024
            ? files : throw new InvalidDataException("The PLA trainer catalog is invalid."));

    private string[] Catalog(string key, string[] files)
    {
        if (catalogs.TryGetValue(key, out var previous) && !previous.SequenceEqual(files))
            throw new InvalidDataException("The PLA file catalog changed while reading. Reload the editor.");
        catalogs[key] = files;
        return files;
    }

    internal OutputFileState OutputState(string relative) { _ = Read(relative); return outputStates[relative]; }
    internal void RequireAbsent(string relative)
    {
        if (File.Exists(Resolve(Paths.BaseRomFsPath!, relative)))
            throw new InvalidDataException("Loose PLA model overrides are unsupported. Use the catalog's original model archives.");
        if (!string.IsNullOrWhiteSpace(Paths.OutputRootPath))
        {
            if (File.Exists(Resolve(Paths.OutputRootPath, "romfs/" + relative)))
                throw new InvalidDataException("A loose output overrides this PLA model asset. Remove the override before editing its archive.");
            outputStates[relative] = OutputFileState.Missing;
        }
    }

    internal void VerifyOutput()
    {
        if (string.IsNullOrWhiteSpace(Paths.OutputRootPath)) return;
        foreach (var (relative, expected) in outputStates)
        {
            var path = Resolve(Paths.OutputRootPath, "romfs/" + relative);
            var actual = OutputFileState.Missing;
            if (File.Exists(path))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                actual = State(ReadBounded(stream));
            }
            if (actual != expected) throw new InvalidDataException("The PLA output changed while loading. Reload the editor.");
        }
    }
    internal IEnumerable<OutputReadDependency> Dependencies => outputStates.OrderBy(p => p.Key, StringComparer.Ordinal)
        .Select(pair => new OutputReadDependency(new RelativeOutputPath("romfs/" + pair.Key), pair.Value));
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static OutputFileState State(byte[] bytes) => OutputFileState.Existing(Hash(bytes), bytes.Length);

    internal string Fingerprint()
    {
        var text = new StringBuilder(executableFingerprint);
        if (compressionFingerprint is not null) text.Append("\ncompression:").Append(compressionFingerprint);
        foreach (var pair in catalogs.OrderBy(p => p.Key, StringComparer.Ordinal))
            text.Append('\n').Append(pair.Key).Append(':').AppendJoin('|', pair.Value);
        foreach (var pair in sources.OrderBy(p => p.Key, StringComparer.Ordinal))
            text.Append('\n').Append(pair.Key).Append(':').Append(Hash(pair.Value)).Append(':').Append(Hash(Base(pair.Key)))
                .Append(':').Append(outputStates[pair.Key].Exists);
        return Hash(Encoding.UTF8.GetBytes(text.ToString()));
    }

    public void Dispose() { compression?.Dispose(); foreach (var lease in leases) lease.Dispose(); leases.Clear(); }
}
