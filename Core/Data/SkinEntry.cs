namespace CS2Cheat.Core.Data;

public sealed record SkinEntry
{
    public int DefinitionIndex { get; init; }
    public bool Enabled { get; init; } = true;
    public int PaintKit { get; init; }
    public int PatternSeed { get; init; }
    public float Wear { get; init; } = 0.01f;
    public int StatTrak { get; init; } = -1;
    public string CustomName { get; init; } = "";

    public SkinEntry Sanitize() => this with
    {
        PaintKit = Math.Max(0, PaintKit),
        PatternSeed = Math.Clamp(PatternSeed, 0, 1000),
        Wear = float.IsFinite(Wear) ? Math.Clamp(Wear, 0f, 1f) : 0.01f,
        StatTrak = Math.Max(-1, StatTrak),
        CustomName = (CustomName ?? "").Replace("\0", "")
    };
}

public static class SkinCatalog
{
    private static readonly HashSet<int> Firearms = [1, 2, 3, 4, 7, 8, 9, 10, 11, 13, 14, 16,
        17, 19, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 38, 39, 40, 60, 61, 63, 64];

    public static readonly WeaponIndexes[] Weapons = Enum.GetValues<WeaponIndexes>()
        .Where(weapon => Firearms.Contains((int)weapon) || (int)weapon is 41 or 42 or 59 ||
                         (int)weapon is >= 500 and < 600)
        .OrderBy(weapon => weapon.ToString(), StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool Supports(int definition) => Weapons.Any(weapon => (int)weapon == definition);
    public static string Name(int definition) => Enum.GetName(typeof(WeaponIndexes), definition) ?? definition.ToString();
    public static int WearCategory(float wear) => wear < 0.07f ? 0 : wear < 0.15f ? 1 : wear < 0.38f ? 2 : wear < 0.45f ? 3 : 4;
    public static readonly string[] WearNames = ["Factory New", "Minimal Wear", "Field-Tested", "Well-Worn", "Battle-Scarred"];
    public static readonly float[] WearDefaults = [0.01f, 0.10f, 0.25f, 0.40f, 0.60f];
}
