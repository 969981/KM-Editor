// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SV.Moves;

public sealed record SvTeraBlastSettings(
    int TypeBehavior = 0,
    int CategoryBehavior = 0,
    int StellarPower = 100,
    int StellarEffectiveness = 8,
    int NormalEffectiveness = 7,
    int AttackDrop = 1,
    int SpecialAttackDrop = 1)
{
    public static SvTeraBlastSettings Default { get; } = new();
    public const string Prefix = "teraBlast.";

    internal static readonly IReadOnlyList<SvMoveEditableField> Fields =
    [
        Field("typeBehavior", "Tera Blast type behavior", 0, 1,
            new(0, "Match the Tera type"), new(1, "Keep the move type")),
        Field("categoryBehavior", "Tera Blast category behavior", 0, 3,
            new(0, "Physical when Attack is higher"), new(1, "Keep the move category"),
            new(2, "Always Physical while Terastallized"), new(3, "Always Special while Terastallized")),
        Field("stellarPower", "Stellar power", 1, 255),
        Field("stellarEffectiveness", "Stellar effectiveness against Terastallized targets", 0, 9, Multipliers),
        Field("normalEffectiveness", "Stellar effectiveness against other targets", 0, 9, Multipliers),
        Field("attackDrop", "Stellar Attack drop (stages)", 0, 6),
        Field("specialAttackDrop", "Stellar Sp. Atk drop (stages)", 0, 6),
    ];

    private static SvMoveEditableFieldOption[] Multipliers =>
        [new(0, "0×"), new(5, "0.25×"), new(6, "0.5×"), new(7, "1×"), new(8, "2×"), new(9, "4×")];

    private static SvMoveEditableField Field(string name, string label, int min, int max,
        params SvMoveEditableFieldOption[] options) => new(Prefix + name, label, "integer", min, max, options);

    internal static bool IsField(string? field) => field?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    internal int Get(string field) => field switch
    {
        Prefix + "typeBehavior" => TypeBehavior,
        Prefix + "categoryBehavior" => CategoryBehavior,
        Prefix + "stellarPower" => StellarPower,
        Prefix + "stellarEffectiveness" => StellarEffectiveness,
        Prefix + "normalEffectiveness" => NormalEffectiveness,
        Prefix + "attackDrop" => AttackDrop,
        Prefix + "specialAttackDrop" => SpecialAttackDrop,
        _ => throw new InvalidDataException("Unknown Tera Blast setting."),
    };

    internal SvTeraBlastSettings With(string field, int value) => field switch
    {
        Prefix + "typeBehavior" => this with { TypeBehavior = value },
        Prefix + "categoryBehavior" => this with { CategoryBehavior = value },
        Prefix + "stellarPower" => this with { StellarPower = value },
        Prefix + "stellarEffectiveness" => this with { StellarEffectiveness = value },
        Prefix + "normalEffectiveness" => this with { NormalEffectiveness = value },
        Prefix + "attackDrop" => this with { AttackDrop = value },
        Prefix + "specialAttackDrop" => this with { SpecialAttackDrop = value },
        _ => throw new InvalidDataException("Unknown Tera Blast setting."),
    };

    internal void Validate()
    {
        foreach (var field in Fields)
        {
            var value = Get(field.Field);
            if (value < field.MinimumValue || value > field.MaximumValue
                || field.Options.Count > 0 && field.Options.All(option => option.Value != value))
                throw new InvalidDataException($"Invalid {field.Label.ToLowerInvariant()}.");
        }
    }
}
