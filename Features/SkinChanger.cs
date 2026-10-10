using System.Text;
using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public sealed class SkinChanger : ThreadedServiceBase
{
    private readonly GameProcess _gameProcess;
    private readonly GameData _gameData;
    private WeaponSnapshot? _original;
    private bool _warnedMissingOffsets;
    private sealed record WeaponSnapshot(uint Handle, IntPtr Address, IntPtr Pawn, int Definition,
        Dictionary<int, byte[]> Fields);

    public SkinChanger(GameProcess gameProcess, GameData gameData)
    {
        _gameProcess = gameProcess;
        _gameData = gameData;
    }

    protected override string ThreadName => nameof(SkinChanger);
    protected override TimeSpan ThreadFrameSleep { get; set; } = TimeSpan.FromMilliseconds(100);

    protected override void FrameAction()
    {
        if (!_gameProcess.IsValid || _gameProcess.Process == null || _gameProcess.ModuleClient == null)
        {
            _original = null;
            return;
        }
        var config = ConfigManager.Load();
        var offsets = new[] { Offsets.m_nFallbackPaintKit, Offsets.m_nFallbackSeed, Offsets.m_flFallbackWear,
            Offsets.m_nFallbackStatTrak, Offsets.m_iItemIDHigh, Offsets.m_szCustomName,
            Offsets.m_pWeaponServices, Offsets.m_hActiveWeapon, Offsets.m_hOwnerEntity };
        if (offsets.Any(offset => offset <= 0))
        {
            if (config.SkinChanger && !_warnedMissingOffsets)
            {
                Console.WriteLine("[SkinChanger] Offsets missing from cache. Restart online to refresh offsets.");
                _warnedMissingOffsets = true;
            }
            return;
        }

        var pawn = _gameProcess.ModuleClient.Read<IntPtr>(Offsets.dwLocalPlayerPawn);
        var services = pawn == IntPtr.Zero ? IntPtr.Zero :
            _gameProcess.Process.Read<IntPtr>(pawn + Offsets.m_pWeaponServices);
        var handle = services == IntPtr.Zero ? uint.MaxValue :
            _gameProcess.Process.Read<uint>(services + Offsets.m_hActiveWeapon);
        var weapon = Resolve(handle);
        var itemOffset = Offsets.m_AttributeManager + Offsets.m_Item;
        var definition = weapon == IntPtr.Zero ? 0 :
            _gameProcess.Process.Read<ushort>(weapon + itemOffset + Offsets.m_iItemDefinitionIndex);
        var entry = config.SkinChanger && _gameData.Player?.IsAlive() == true
            ? config.Skins.FirstOrDefault(entry => entry.Enabled && entry.DefinitionIndex == definition)
            : null;

        if (_original != null && (entry == null || _original.Handle != handle ||
                                 _original.Address != weapon || _original.Pawn != pawn)) Restore();
        if (entry == null || weapon == IntPtr.Zero || !OwnedBy(weapon, pawn)) return;
        entry = entry.Sanitize();

        var values = new Dictionary<int, byte[]>
        {
            [Offsets.m_nFallbackPaintKit] = BitConverter.GetBytes(entry.PaintKit),
            [Offsets.m_nFallbackSeed] = BitConverter.GetBytes(entry.PatternSeed),
            [Offsets.m_flFallbackWear] = BitConverter.GetBytes(entry.Wear),
            [Offsets.m_nFallbackStatTrak] = BitConverter.GetBytes(entry.StatTrak),
            [itemOffset + Offsets.m_iItemIDHigh] = BitConverter.GetBytes(-1),
            [itemOffset + Offsets.m_szCustomName] = EncodeName(entry.CustomName)
        };
        if (_original == null)
        {
            var saved = values.ToDictionary(pair => pair.Key, pair => ReadBytes(weapon + pair.Key, pair.Value.Length));
            _original = new WeaponSnapshot(handle, weapon, pawn, definition, saved);
        }
        foreach (var field in values)
            WriteIfChanged(weapon + field.Key, field.Value);
    }

    private IntPtr Resolve(uint handle)
    {
        if (handle is 0 or uint.MaxValue || _gameProcess.Process == null || _gameProcess.ModuleClient == null)
            return IntPtr.Zero;
        var list = _gameProcess.ModuleClient.Read<IntPtr>(Offsets.dwEntityList);
        if (list == IntPtr.Zero) return IntPtr.Zero;
        var index = handle & 0x7FFF;
        var chunk = _gameProcess.Process.Read<IntPtr>(list + 16 + (int)(8 * (index >> 9)));
        return chunk == IntPtr.Zero ? IntPtr.Zero :
            _gameProcess.Process.Read<IntPtr>(chunk + (int)(112 * (index & 0x1FF)));
    }

    private bool OwnedBy(IntPtr weapon, IntPtr pawn) => pawn != IntPtr.Zero && _gameProcess.Process != null &&
        Resolve(_gameProcess.Process.Read<uint>(weapon + Offsets.m_hOwnerEntity)) == pawn;

    private byte[] ReadBytes(IntPtr address, int size) => Enumerable.Range(0, size)
        .Select(index => _gameProcess.Process!.Read<byte>(address + index)).ToArray();

    private void WriteIfChanged(IntPtr address, byte[] value)
    {
        if (ReadBytes(address, value.Length).SequenceEqual(value)) return;
        if (!Kernel32.WriteMemory(_gameProcess.Process!.Handle, address, value))
            throw new InvalidOperationException("Could not write cosmetic fields.");
    }

    private static byte[] EncodeName(string name)
    {
        var bytes = new byte[161];
        Encoding.UTF8.GetEncoder().Convert(name.AsSpan(), bytes.AsSpan(0, 160), true, out _, out _, out _);
        return bytes;
    }

    private void Restore()
    {
        var original = _original;
        _original = null;
        if (original == null || Resolve(original.Handle) != original.Address ||
            !OwnedBy(original.Address, original.Pawn) || _gameProcess.Process == null) return;
        if (_gameProcess.ModuleClient?.Read<IntPtr>(Offsets.dwLocalPlayerPawn) != original.Pawn) return;
        var definition = _gameProcess.Process.Read<ushort>(original.Address + Offsets.m_AttributeManager +
            Offsets.m_Item + Offsets.m_iItemDefinitionIndex);
        if (definition != original.Definition) return;
        foreach (var field in original.Fields) WriteIfChanged(original.Address + field.Key, field.Value);
    }

    public override void Dispose()
    {
        base.Dispose();
        if (_gameProcess.IsValid && _gameProcess.Process != null) Restore();
    }
}
