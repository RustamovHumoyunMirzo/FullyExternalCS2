using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public sealed class RadarHack : ThreadedServiceBase
{
    private readonly GameProcess _gameProcess;
    private readonly GameData _gameData;

    public RadarHack(GameProcess gameProcess, GameData gameData)
    {
        _gameProcess = gameProcess ?? throw new ArgumentNullException(nameof(gameProcess));
        _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
    }

    protected override string ThreadName => nameof(RadarHack);

    protected override void FrameAction()
    {
        if (!ConfigManager.Load().RadarHack) return;
        if (!_gameProcess.IsValid || _gameProcess.Process == null) return;
        if (_gameData.Entities == null || _gameData.Player == null) return;

        var localTeam = _gameData.Player.Team;

        foreach (var entity in _gameData.Entities)
        {
            if (!entity.IsAlive()) continue;
            if (entity.Team == localTeam) continue;

            // Write m_bSpotted = true to force enemy on radar
            var spottedAddress = entity.AddressBase + Offsets.m_entitySpottedState + 0x8;
            Kernel32.WriteMemory(_gameProcess.Process.Handle, spottedAddress, [1]);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
