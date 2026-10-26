// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.ZA;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

/// <summary>Prepares a registered stone from local donor assets using native item layouts.</summary>
public static class SwShRestorationMegaItemPreparation
{
    public static IReadOnlyList<SwShRestorationContribution> Create(SwShRestorationMegaItem stone,
        Func<string, byte[]> native, Func<string, byte[]> donor, Func<string, bool> nativeExists,
        CancellationToken cancellationToken = default, ModelTextureMipCache? encodingCache = null)
    {
        if (!SwShRestorationMegaItems.All.Contains(stone)) throw new InvalidDataException("Mega Stone is not registered.");
        cancellationToken.ThrowIfCancellationRequested();
        var table = SwShItemTable.Parse(native(SwShItemTable.ItemDataRelativePath[6..]), 4096);
        var original = table.Records.Single(r => r.ItemId == stone.ItemId);
        if (original.RawRowIndex != 0) throw new InvalidDataException("The reserved Mega Stone ID is already used in the base item table.");
        var key = stone.ItemId.ToString(CultureInfo.InvariantCulture);
        // Ordinary, non-consumable held item. No unrelated native held effect, field
        // action, or Fling effect is inherited from the texture template's item.
        // Native category 7 identifies a Mega Stone; the restoration runtime protects it.
        var row = new byte[48]; row[16] = 0x9f; row[17] = 4; row[22] = 2; row[24] = 200; row[28] = 7;
        BinaryPrimitives.WriteInt16LittleEndian(row.AsSpan(26), checked((short)stone.ItemId));
        var result = new List<SwShRestorationContribution> {
            new(SwShItemTable.ItemDataRelativePath, SwShRestorationWriter.Items, key, table.ReadRow(stone.ItemId), row)
        };
        var iconPath = SwShRestorationMegaItems.IconPath(stone.ItemId);
        var previousIcon = nativeExists(iconPath[6..]) ? native(iconPath[6..]) : null;
        if (stone.ItemId == stone.DonorItemId && previousIcon is not null)
            _ = new ModelTextureDocument(previousIcon);
        else
        {
            if (table.Records.Any(r => r.ItemId != stone.ItemId && r.RawRowIndex != 0 && r.ItemSprite == stone.ItemId))
                throw new InvalidDataException("The reserved Mega Stone icon is referenced by another native item.");
            var icon = ZaSwShMaterialBaker.Icon(donor($"ui/tex/icon_item/item_{stone.DonorItemId:0000}/item_{stone.DonorItemId:0000}.bntx"),
                native("bin/appli/icon_item/item_0249.bntx"), cancellationToken, encodingCache);
            _ = new ModelTextureDocument(icon);
            result.Add(new(iconPath, SwShRestorationWriter.File, "", previousIcon, icon));
        }
        foreach (var language in SwShRestorationMegaItems.TextLanguages)
        foreach (var file in SwShRestorationMegaItems.TextFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = SwShRestorationMegaItems.TextPath(language, file);
            var text = SwShGameTextFile.Parse(native(path[6..]), 4096);
            var before = text.Lines[stone.ItemId];
            if (stone.ItemId != stone.DonorItemId && language == "English" && file == "itemname.dat" && before.Text != "???")
                throw new InvalidDataException("The reserved Mega Stone ID already has a native item name.");
            // Earlier stones already have correct native localized text and grammar.
            var after = stone.ItemId == stone.DonorItemId ? before :
                SwShGameTextFile.Parse(donor($"ik_message/dat/{language}/common/{file}"), 8192).Lines[stone.DonorItemId];
            if (file is "itemname.dat" or "iteminfo.dat" && (string.IsNullOrWhiteSpace(after.Text) || after.Text == "???"))
                throw new InvalidDataException("Mega Stone name or description is missing.");
            SwShGameTextFile.ValidateText(after.Text);
            if (after == before) continue;
            result.Add(new(path, SwShRestorationWriter.ItemText, key,
                JsonSerializer.SerializeToUtf8Bytes(before), JsonSerializer.SerializeToUtf8Bytes(after)));
        }
        return result;
    }
}
