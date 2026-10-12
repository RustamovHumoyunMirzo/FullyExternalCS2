using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public sealed class PlayerGlow : ThreadedServiceBase
{
    private readonly GameProcess _gameProcess;
    private readonly GameData _gameData;
    private readonly Dictionary<IntPtr, GlowState> _original = new();
    private bool _warnedMissingOffsets;

    private sealed record GlowState(int Type, int Team, uint Color, bool Glowing);

    public PlayerGlow(GameProcess gameProcess, GameData gameData)
    {
        _gameProcess = gameProcess;
        _gameData = gameData;
    }

    protected override string ThreadName => nameof(PlayerGlow);
    protected override TimeSpan ThreadFrameSleep { get; set; } = TimeSpan.FromMilliseconds(75);

    protected override void FrameAction()
    {
        if (!_gameProcess.IsValid || _gameProcess.Process == null) return;
        var config = ConfigManager.Load();
        if (Offsets.m_Glow <= 0 || Offsets.m_bGlowing <= 0 ||
            Offsets.m_glowColorOverride <= 0 || Offsets.m_iGlowType <= 0 || Offsets.m_iGlowTeam <= 0)
        {
            if (config.PlayerGlow && !_warnedMissingOffsets)
            {
                Console.WriteLine("[PlayerGlow] Glow offsets missing from cache. Restart with internet to refresh offsets.");
                _warnedMissingOffsets = true;
            }
            return;
        }

        var player = _gameData.Player;
        var entities = _gameData.Entities;
        if (entities == null) return;
        var seen = new HashSet<IntPtr>();
        foreach (var entity in entities)
        {
            var pawn = entity.AddressBase;
            if (pawn == IntPtr.Zero || !seen.Add(pawn)) continue;
            var enabled = config.PlayerGlow && player != null && pawn != player.AddressBase &&
                          entity.IsAliveIgnoringDormancy() && (!config.GlowTeamCheck || entity.Team != player.Team);
            var glow = pawn + Offsets.m_Glow;
            if (!enabled)
            {
                Restore(pawn);
                continue;
            }

            if (!_original.ContainsKey(pawn))
            {
                var process = _gameProcess.Process;
                _original[pawn] = new GlowState(
                    process.Read<int>(glow + Offsets.m_iGlowType),
                    process.Read<int>(glow + Offsets.m_iGlowTeam),
                    process.Read<uint>(glow + Offsets.m_glowColorOverride),
                    process.Read<bool>(glow + Offsets.m_bGlowing));
            }

            var color = entity.Team == Team.Terrorists ? config.GlowTColor : config.GlowCtColor;
            Write(glow + Offsets.m_iGlowType, BitConverter.GetBytes(3));
            Write(glow + Offsets.m_iGlowTeam, BitConverter.GetBytes(0));
            Write(glow + Offsets.m_glowColorOverride, BitConverter.GetBytes(PackColor(color)));
            Write(glow + Offsets.m_bGlowing, [1]);
        }

        // Discard despawned pawns without writing into potentially reused memory.
        foreach (var pawn in _original.Keys.Where(pawn => !seen.Contains(pawn)).ToArray())
            _original.Remove(pawn);
    }

    private static uint PackColor(float[] color)
    {
        uint Channel(int index) => (uint)Math.Round(Math.Clamp(color[index], 0f, 1f) * 255f);
        return Channel(0) | (Channel(1) << 8) | (Channel(2) << 16) | (Channel(3) << 24);
    }

    private void Write(IntPtr address, byte[] bytes)
    {
        if (_gameProcess.Process != null)
        {
            var changed = false;
            for (var index = 0; index < bytes.Length; index++)
            {
                if (_gameProcess.Process.Read<byte>(address + index) == bytes[index]) continue;
                changed = true;
                break;
            }
            if (!changed) return;

            if (!Kernel32.WriteMemory(_gameProcess.Process.Handle, address, bytes))
                throw new InvalidOperationException($"Glow write failed (Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}).");
        }
    }

    private void Restore(IntPtr pawn)
    {
        if (!_original.TryGetValue(pawn, out var state)) return;
        var glow = pawn + Offsets.m_Glow;
        Write(glow + Offsets.m_bGlowing, [0]);
        Write(glow + Offsets.m_iGlowType, BitConverter.GetBytes(state.Type));
        Write(glow + Offsets.m_iGlowTeam, BitConverter.GetBytes(state.Team));
        Write(glow + Offsets.m_glowColorOverride, BitConverter.GetBytes(state.Color));
        Write(glow + Offsets.m_bGlowing, [state.Glowing ? (byte)1 : (byte)0]);
        _original.Remove(pawn);
    }

    public override void Dispose()
    {
        base.Dispose();
        if (_gameProcess.IsValid && _gameProcess.Process != null && _gameData.Entities != null)
            foreach (var pawn in _gameData.Entities.Select(entity => entity.AddressBase).Distinct())
                Restore(pawn);
        _original.Clear();
    }
}
