using System.Collections.Concurrent;
using System.Numerics;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Data.Entity;

public class Entity : EntityBase
{
    private readonly ConcurrentDictionary<string, Vector3> _bonePositions;
    private bool _dormant = true;
    private ulong _spottedByMask;

    public Entity(int index)
    {
        Id = index;
        _bonePositions = new ConcurrentDictionary<string, Vector3>(Offsets.Bones.ToDictionary(
            bone => bone.Key,
            _ => Vector3.Zero
        ));
    }

    protected internal bool IsSpotted { get; private set; }
    protected internal string Name { get; private set; } = string.Empty;
    protected internal int IsInScope { get; private set; }
    protected internal int FlashAlpha { get; private set; }
    public bool IsLocalPlayerController { get; private set; }
    public IReadOnlyDictionary<string, Vector3> BonePos => _bonePositions;
    public int Id { get; }

    public override bool IsAlive()
    {
        return base.IsAlive() && !_dormant;
    }

    public bool IsAliveIgnoringDormancy() => base.IsAlive();

    protected override IntPtr ReadControllerBase(GameProcess gameProcess)
    {
        var entryIndex = (Id & 0x7FFF) >> 9;

        if (gameProcess?.Process == null) return IntPtr.Zero;

        var listEntry = gameProcess.Process.Read<IntPtr>(EntityList + 8 * entryIndex + 16);

        return listEntry != IntPtr.Zero
            ? gameProcess.Process.Read<IntPtr>(listEntry + 112 * (Id & 0x1FF))
            : IntPtr.Zero;
    }

    protected override IntPtr ReadAddressBase(GameProcess gameProcess)
    {
        if (gameProcess?.Process == null) return IntPtr.Zero;

        var playerPawn = gameProcess.Process.Read<int>(ControllerBase + Offsets.m_hPawn);
        var pawnIndex = (playerPawn & 0x7FFF) >> 9;
        var listEntry = gameProcess.Process.Read<IntPtr>(EntityList + 0x8 * pawnIndex + 16);

        return listEntry != IntPtr.Zero
            ? gameProcess.Process.Read<IntPtr>(listEntry + 112 * (playerPawn & 0x1FF))
            : IntPtr.Zero;
    }

    public override bool Update(GameProcess gameProcess)
    {
        _spottedByMask = 0;
        IsSpotted = false;
        if (!base.Update(gameProcess)) return false;

        var sceneNode = gameProcess.Process?.Read<IntPtr>(AddressBase + Offsets.m_pGameSceneNode) ?? IntPtr.Zero;
        _dormant = sceneNode == IntPtr.Zero || gameProcess.Process == null ||
                   gameProcess.Process.Read<byte>(sceneNode + Offsets.m_bDormant) != 0;
        IsSpotted = gameProcess.Process?.Read<bool>(AddressBase + Offsets.m_entitySpottedState + 0x8) ?? false;
        _spottedByMask = gameProcess.Process?.Read<ulong>(AddressBase + Offsets.m_entitySpottedState + Offsets.m_bSpottedByMask) ?? 0UL;
        IsLocalPlayerController = Offsets.m_bIsLocalPlayerController > 0 &&
                                  (gameProcess.Process?.Read<bool>(ControllerBase + Offsets.m_bIsLocalPlayerController) ?? false);
        IsInScope = gameProcess.Process?.Read<int>(AddressBase + Offsets.m_bIsScoped) ?? 0;
        FlashAlpha = gameProcess.Process?.Read<int>(AddressBase + Offsets.m_flFlashDuration) ?? 0;
        Name = gameProcess.Process != null
            ? gameProcess.Process.ReadString(ControllerBase + Offsets.m_iszPlayerName)
            : string.Empty;

        return !IsAlive() || UpdateBonePositions(gameProcess);
    }

    private bool UpdateBonePositions(GameProcess gameProcess)
    {
        try
        {
            if (gameProcess?.Process == null) return false;

            var gameSceneNode = gameProcess.Process.Read<IntPtr>(AddressBase + Offsets.m_pGameSceneNode);
            var boneArray = gameProcess.Process.Read<IntPtr>(gameSceneNode + Offsets.m_modelState + 128);

            foreach (var (boneName, boneIndex) in Offsets.Bones)
            {
                var bonePos = gameProcess.Process.Read<Vector3>(boneArray + boneIndex * 32);
                _bonePositions.AddOrUpdate(boneName, bonePos, (_, _) => bonePos);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsSpottedBy(int playerId)
    {
        // Spotted-mask bits are indexed by the 1-based player entity ID minus one.
        var bitIndex = playerId - 1;
        if (bitIndex < 0 || bitIndex >= 64)
        {
            return false;
        }

        return (_spottedByMask & (1UL << bitIndex)) != 0;
    }

}
