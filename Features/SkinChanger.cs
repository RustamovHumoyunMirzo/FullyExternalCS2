using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public sealed class SkinChanger : ThreadedServiceBase
{
    private readonly GameProcess _gameProcess;
    private readonly GameData _gameData;
    private SkinState? _activeState;
    private bool _reportedUnavailable;

    private sealed record SkinState(
        IntPtr Weapon,
        IntPtr ItemView,
        int PaintKit,
        int Seed,
        float Wear,
        int StatTrak,
        int Quality,
        int ItemIdHigh);

    public SkinChanger(GameProcess gameProcess, GameData gameData)
    {
        _gameProcess = gameProcess;
        _gameData = gameData;
    }

    protected override string ThreadName => nameof(SkinChanger);
    protected override TimeSpan ThreadFrameSleep { get; set; } = TimeSpan.FromMilliseconds(120);

    protected override void FrameAction()
    {
        if (!_gameProcess.IsValid || _gameProcess.Process == null)
        {
            RestoreActive();
            return;
        }

        if (!OffsetsAvailable())
        {
            if (!_reportedUnavailable && ConfigManager.Load().SkinConfigs.Any(entry => entry.Enabled))
            {
                Console.WriteLine("[SkinChanger] Fallback skin offsets unavailable. Refresh offsets online before using Skins.");
                _reportedUnavailable = true;
            }
            RestoreActive();
            return;
        }

        var player = _gameData.Player;
        var weapon = player?.CurrentWeaponAddress ?? IntPtr.Zero;
        var definitionIndex = player?.WeaponIndex ?? 0;
        var profile = ConfigManager.Load().SkinConfigs.FirstOrDefault(entry =>
            entry.Enabled && entry.WeaponDefinitionIndex == definitionIndex);

        if (weapon == IntPtr.Zero || profile == null)
        {
            RestoreActive();
            return;
        }

        try
        {
            var process = _gameProcess.Process;
            var controller = player?.ControllerAddress ?? IntPtr.Zero;
            if (controller == IntPtr.Zero || process.Read<ulong>(controller + Offsets.m_steamID) == 0)
            {
                RestoreActive();
                return;
            }

            var ownerHigh = process.Read<uint>(weapon + Offsets.m_OriginalOwnerXuidHigh);
            var ownerLow = process.Read<uint>(weapon + Offsets.m_OriginalOwnerXuidLow);
            var ownerXuid = ((ulong)ownerHigh << 32) | ownerLow;
            if (ownerXuid != process.Read<ulong>(controller + Offsets.m_steamID))
            {
                RestoreActive();
                return;
            }

            var itemView = weapon + Offsets.m_AttributeManager + Offsets.m_Item;
            if (_activeState == null || _activeState.Weapon != weapon || _activeState.ItemView != itemView)
            {
                RestoreActive();
                _activeState = new SkinState(
                    weapon,
                    itemView,
                    process.Read<int>(weapon + Offsets.m_nFallbackPaintKit),
                    process.Read<int>(weapon + Offsets.m_nFallbackSeed),
                    process.Read<float>(weapon + Offsets.m_flFallbackWear),
                    process.Read<int>(weapon + Offsets.m_nFallbackStatTrak),
                    process.Read<int>(itemView + Offsets.m_iEntityQuality),
                    process.Read<int>(itemView + Offsets.m_iItemIDHigh));
            }

            WriteIfChanged(weapon + Offsets.m_nFallbackPaintKit, profile.PaintKit);
            WriteIfChanged(weapon + Offsets.m_nFallbackSeed, Math.Clamp(profile.Seed, 0, 1000));
            WriteIfChanged(weapon + Offsets.m_flFallbackWear, Math.Clamp(profile.Wear, 0.0001f, 1f));
            WriteIfChanged(weapon + Offsets.m_nFallbackStatTrak,
                profile.StatTrakEnabled ? Math.Max(0, profile.StatTrakCount) : -1);
            WriteIfChanged(itemView + Offsets.m_iEntityQuality,
                profile.StatTrakEnabled ? 9 : _activeState.Quality);
            WriteIfChanged(itemView + Offsets.m_iItemIDHigh, -1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkinChanger] Could not apply local fallback cosmetics: {ex.Message}");
            RestoreActive();
        }
    }

    private static bool OffsetsAvailable() =>
        Offsets.m_AttributeManager > 0 && Offsets.m_Item > 0 && Offsets.m_iItemDefinitionIndex > 0 &&
        Offsets.m_nFallbackPaintKit > 0 && Offsets.m_nFallbackSeed > 0 && Offsets.m_flFallbackWear > 0 &&
        Offsets.m_nFallbackStatTrak > 0 && Offsets.m_iEntityQuality > 0 && Offsets.m_iItemIDHigh > 0 &&
        Offsets.m_OriginalOwnerXuidHigh > 0 && Offsets.m_OriginalOwnerXuidLow > 0 && Offsets.m_steamID > 0;

    private void WriteIfChanged<T>(IntPtr address, T value) where T : unmanaged
    {
        var process = _gameProcess.Process;
        if (process == null || EqualityComparer<T>.Default.Equals(process.Read<T>(address), value)) return;
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(new[] { value }.AsSpan()).ToArray();
        if (!Kernel32.WriteMemory(process.Handle, address, bytes))
            throw new InvalidOperationException($"memory write failed ({System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
    }

    private void RestoreActive()
    {
        var state = _activeState;
        if (state == null) return;
        _activeState = null;
        try
        {
            if (!_gameProcess.IsValid || _gameProcess.Process == null) return;
            WriteIfChanged(state.Weapon + Offsets.m_nFallbackPaintKit, state.PaintKit);
            WriteIfChanged(state.Weapon + Offsets.m_nFallbackSeed, state.Seed);
            WriteIfChanged(state.Weapon + Offsets.m_flFallbackWear, state.Wear);
            WriteIfChanged(state.Weapon + Offsets.m_nFallbackStatTrak, state.StatTrak);
            WriteIfChanged(state.ItemView + Offsets.m_iEntityQuality, state.Quality);
            WriteIfChanged(state.ItemView + Offsets.m_iItemIDHigh, state.ItemIdHigh);
        }
        catch
        {
            // The weapon may have left the entity list during a switch or round transition.
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        RestoreActive();
    }
}
