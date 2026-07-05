using System.Runtime.InteropServices;
using CS2Cheat.Core;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public sealed class AntiFlash : ThreadedServiceBase
{
    private readonly GameProcess _gameProcess;
    private readonly GameData _gameData;

    public AntiFlash(GameProcess gameProcess, GameData gameData)
    {
        _gameProcess = gameProcess ?? throw new ArgumentNullException(nameof(gameProcess));
        _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
    }

    protected override string ThreadName => nameof(AntiFlash);

    protected override void FrameAction()
    {
        if (!_gameProcess.IsValid || !_gameProcess.IsGameForeground)
        {
            return;
        }

        if (!ConfigManager.Load().AntiFlash)
        {
            return;
        }

        if (_gameData.Player == null || _gameData.Player.AddressBase == IntPtr.Zero)
        {
            return;
        }

        if (_gameProcess.Process == null)
        {
            return;
        }

        var hProcess = _gameProcess.Process.Handle;
        var flashAddress = _gameData.Player.AddressBase + Offsets.m_flFlashDuration;

        // Read current flash duration
        var flashDuration = _gameProcess.Process.Read<float>(flashAddress);

        // If flashed (duration > 0), reset it to 0 immediately
        if (flashDuration > 0f)
        {
            var zeroBytes = BitConverter.GetBytes(0f);
            Kernel32.WriteMemory(hProcess, flashAddress, zeroBytes);

            Console.WriteLine($"[AntiFlash] Flash blocked! ({flashDuration:F2}s duration prevented)");
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
