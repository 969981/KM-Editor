// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private float AirborneOffset(int species, int form, byte[] configuration)
        {
            // Z-A applies this actor offset outside the skeletal animation. Normalize
            // its authored model size to the unit geometry used by the converter.
            var data = new ModelBuffer(Donor("param_chr/data/pokemon/poke_model_param/poke_model_param_array.bin"));
            var rows = data.Tables(data.Root, 0, 4096).SelectMany(db => data.Tables(db, 0, 4096))
                .Where(row => data.Field(row, 0) is var at && at != 0 && data.U16(at) == species
                    && (data.Field(row, 1) is var formAt && formAt != 0 ? data.U16(formAt) : 0) == form).ToArray();
            if (rows.Length != 1) throw new InvalidDataException("Source airborne placement identity is ambiguous.");
            var config = new ModelBuffer(configuration);
            var lowAt = config.Field(config.Root, 1); var highAt = config.Field(config.Root, 2);
            if (lowAt == 0 || highAt == 0) throw new InvalidDataException("Source model size range is missing.");
            var low = config.Float(lowAt); var high = config.Float(highAt); var mean = low + (high - low) * .5f;
            float Floating(int field)
            {
                var at = data.Field(data.Table(rows[0], field), 2);
                if (at == 0) throw new InvalidDataException("Source floating offset is missing.");
                return data.Float(at);
            }
            var wild = Floating(14); var partner = Floating(15);
            if (!float.IsFinite(low + high + wild + partner) || low <= 0 || high < low || wild <= 0 || wild != partner)
                throw new InvalidDataException("Source airborne placement requires another adapter.");
            return wild / mean * 100;
        }
    }
}
