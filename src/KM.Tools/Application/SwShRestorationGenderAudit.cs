// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Tools.Application;

internal static class SwShRestorationGenderAudit
{
    // Shared geometry is a verified property of a registered species, never a
    // fallback for an unconverted female resource in the donor catalog.
    internal static void RequireSharedModel(byte[] catalogBytes, int species, int form, string expectedModel)
    {
        var data = new ModelBuffer(catalogBytes); var genders = new HashSet<int>(); var found = false;
        foreach (var row in data.Tables(data.Root, 1, 20000))
        {
            var identity = data.Table(row, 0);
            int Number(int field, bool wide) => data.Field(identity, field) is var at && at != 0 ? wide ? data.U16(at) : data.U8(at) : 0;
            if (Number(0, true) != species || Number(1, true) != form) continue;
            var gender = Number(2, false);
            if (!genders.Add(gender)) throw new InvalidDataException("Donor model gender identity is duplicated.");
            if (gender != 0) throw new InvalidDataException("This restoration requires separate female model, texture, animation, icon, and context conversion.");
            if (data.Text(row, 1) != expectedModel) throw new InvalidDataException("Donor shared model identity does not match its restoration profile.");
            found = true;
        }
        if (!found) throw new InvalidDataException("Donor shared model identity is missing.");
    }
}
