// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Audio;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal void Verify(SwShPreparedRestoration prepared)
        {
            archive.VerifySource();
            var composition = SwShRestorationComposer.Compose(prepared.Contributions, path =>
            {
                var source = Path.Combine(target.BaseRomFsPath!, path[6..].Replace('/', Path.DirectorySeparatorChar));
                return System.IO.File.Exists(source) ? Native(path[6..]) : null;
            }, enable: true);
            var files = composition.Files;
            var speciesCount = SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities.Count;
            var femaleCount = SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities.Count(i => Profile(i).SeparateFemale);
            var itemFiles = prepared.Contributions.Where(SwShRestorationMegaItems.IsItemContribution).Select(c => c.Path).Distinct().Count();
            var additionalIcons = SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities.Sum(i => SwShRestorationComposer.IconGenders(i.Species).Count - 1);
            if (files.Count != 6 + 6 * speciesCount + 3 * femaleCount + itemFiles + additionalIcons || files.Values.Any(v => v is null)) throw new InvalidDataException("Prepared restoration output set is incomplete.");
            var catalog = new SwShPokemonResourceCatalog(files[SwShRestorationComposer.CatalogPath]!);
            var personal = SwShPersonalTable.Parse(files[SwShRestorationComposer.PersonalPath]!, 4096);
            var packs = new Dictionary<string, SwShGfPackFile>(StringComparer.Ordinal);
            foreach (var identity in SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities)
            {
                token.ThrowIfCancellationRequested();
                var species = identity.Species;
                var profile = Profile(identity); var personalId = SwShRestorationForms.PersonalRow(identity);
                foreach (var gender in profile.SeparateFemale ? new[] { 0, 1 } : new[] { 0 })
                {
                var stem = profile.OutputStem ?? $"pm{species:0000}_{gender:00}";
                if (!personal.Records[personalId].IsPresentInGame || personal.Records[personalId].HatchedSpecies != profile.HatchedSpecies || personal.Records[personalId].CanNotDynamax)
                    throw new InvalidDataException("Converted personal data does not support the declared contexts.");
                foreach (var shiny in new[] { false, true })
                {
                    var row = catalog.Read(new(species, identity.Form, gender, shiny));
                    var packPath = "romfs/" + row.Archive;
                    if (!packs.TryGetValue(packPath, out var pack))
                    { pack = SwShGfPackFile.Parse(files[packPath]!); packs.Add(packPath, pack); }
                    _ = pack.ReadFileByHash(SwShGfPackFile.HashMemberPath(row.Configuration), 1024 * 1024);
                    var scene = new SwShPreviewReader(path =>
                    {
                        if (path == row.Model) return pack.ReadFileByHash(SwShGfPackFile.HashMemberPath(path), 32 * 1024 * 1024);
                        return Member(pack, Path.GetFileName(path));
                    }).Load(row.Model);
                    if (!scene.Rig.Bones.Any(b => b.Name == "EffGcloud01") || !scene.Rig.Bones.Any(b => b.Name == "EffMouth01"))
                        throw new InvalidDataException("Converted model effect attachments are incomplete.");
                    foreach (var controllerPath in row.Controllers.Values)
                    {
                        var controller = new ModelBuffer(pack.ReadFileByHash(SwShGfPackFile.HashMemberPath(controllerPath), 16 * 1024 * 1024));
                        var bindings = controller.Table(controller.Root, 6);
                        foreach (var binding in controller.Tables(bindings, 0, 2048))
                        {
                            var animation = controller.Text(binding, 1) ?? throw new InvalidDataException("Converted animation binding is missing.");
                            var path = TrinityPreviewReader.Resolve(controllerPath, animation);
                            var bytes = pack.ReadFileByHash(SwShGfPackFile.HashMemberPath(path), 16 * 1024 * 1024);
                            var data = new ModelBuffer(bytes);
                            _ = PreviewRigReader.Animation(data, path, scene.Rig.Bones);
                        }
                    }
                }
                var giant = SwShGfPackFile.Parse(files[$"romfs/bin/archive/gpokemon/{stem}.gfpak"]!);
                _ = giant.ReadFileByHash(SwShGfPackFile.HashMemberPath($"bin/pokemon/{stem}/{stem}.gfbgpokecfg"), 1024 * 1024);
                foreach (var iconGender in SwShRestorationComposer.IconGenders(species))
                    _ = new ModelTextureDocument(files[SwShRestorationComposer.IconPath(species, identity.Form, iconGender)]!);
                }
            }
            var originalBank = new SoundBankDocument(Native(SwShRestorationComposer.VoicesPath[6..]));
            var newBank = new SoundBankDocument(files[SwShRestorationComposer.VoicesPath]!);
            if (newBank.Objects.Count != originalBank.Objects.Count + 72 * speciesCount || newBank.Media.Count != originalBank.Media.Count + 13 * speciesCount)
                throw new InvalidDataException("Converted voices are incomplete.");
            for (var i = 0; i < originalBank.Objects.Count; i++)
                if (originalBank.Objects[i].Id != newBank.Objects[i].Id || originalBank.Objects[i].Type != newBank.Objects[i].Type
                    || !originalBank.Objects[i].Payload.AsSpan().SequenceEqual(newBank.Objects[i].Payload))
                    throw new InvalidDataException("Voice composition changed an unrelated native object.");
            foreach (var pair in native)
            {
                token.ThrowIfCancellationRequested();
                var current = ReadPhysical(Path.Combine(target.BaseRomFsPath!, pair.Key.Replace('/', Path.DirectorySeparatorChar)), 64 * 1024 * 1024);
                if (!SHA256.HashData(current).AsSpan().SequenceEqual(SHA256.HashData(pair.Value)))
                    throw new InvalidDataException("Target sources changed during restoration preparation.");
            }
        }
    }
}
