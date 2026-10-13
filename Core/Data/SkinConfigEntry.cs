namespace CS2Cheat.Core.Data;

public sealed class SkinConfigEntry
{
    public int WeaponDefinitionIndex { get; set; }
    public bool Enabled { get; set; }
    public int PaintKit { get; set; }
    public int Seed { get; set; }
    public float Wear { get; set; } = 0.0001f;
    public bool StatTrakEnabled { get; set; }
    public int StatTrakCount { get; set; }
}
