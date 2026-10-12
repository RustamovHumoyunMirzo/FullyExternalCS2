using System.Numerics;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;

namespace CS2Cheat.Data.Entity;

public class Player : EntityBase
{
    private Matrix4x4 MatrixViewProjection { get; set; }
    public Matrix4x4 MatrixViewport { get; private set; }
    public Matrix4x4 MatrixViewProjectionViewport { get; private set; }
    private Vector3 ViewOffset { get; set; }
    public Vector3 EyePosition { get; private set; }
    public Vector3 ViewAngles { get; private set; }
    public Vector3 AimPunchAngle { get; private set; }
    public int AimPunchCacheCount { get; private set; }
    public Vector3 AimDirection { get; private set; }
    public Vector3 EyeDirection { get; private set; }
    public static int Fov => 90;
    public int FFlags { get; private set; }

    private int _previousTotalHits;
    private bool _totalHitsInitialized;
    private Team _lastCrosshairTargetTeam = Team.Unknown;
    private DateTime _lastCrosshairTargetTime;
    public int HitSequence { get; private set; }
    public Team LastHitTargetTeam { get; private set; } = Team.Unknown;

    protected override IntPtr ReadControllerBase(GameProcess gameProcess)
    {
        if (gameProcess.ModuleClient == null)
            throw new ArgumentNullException(nameof(gameProcess.ModuleClient), "ModuleClient cannot be null.");
        return gameProcess.ModuleClient.Read<IntPtr>(Offsets.dwLocalPlayerController);
    }

    protected override IntPtr ReadAddressBase(GameProcess gameProcess)
    {
        if (gameProcess.ModuleClient == null)
            throw new ArgumentNullException(nameof(gameProcess.ModuleClient), "ModuleClient cannot be null.");
        return gameProcess.ModuleClient.Read<IntPtr>(Offsets.dwLocalPlayerPawn);
    }

    public override bool Update(GameProcess gameProcess)
    {
        if (!base.Update(gameProcess)) return false;


        if (gameProcess.ModuleClient == null)
            throw new ArgumentNullException(nameof(gameProcess.ModuleClient), "ModuleClient cannot be null.");
        MatrixViewProjection = Matrix4x4.Transpose(gameProcess.ModuleClient.Read<Matrix4x4>(Offsets.dwViewMatrix));
        MatrixViewport = Utility.GetMatrixViewport(gameProcess.WindowRectangleClient.Size);
        MatrixViewProjectionViewport = MatrixViewProjection * MatrixViewport;

        if (gameProcess.Process == null)
            throw new ArgumentNullException(nameof(gameProcess.Process), "Process cannot be null.");

        ViewOffset = gameProcess.Process.Read<Vector3>(AddressBase + Offsets.m_vecViewOffset);
        EyePosition = Origin + ViewOffset;
        ViewAngles = gameProcess.ModuleClient.Read<Vector3>(Offsets.dwViewAngles);
        AimPunchAngle = ReadAimPunchAngle(gameProcess);
        FFlags = gameProcess.Process.Read<int>(AddressBase + Offsets.m_fFlags);

        EyeDirection =
            GraphicsMath.GetVectorFromEulerAngles(ViewAngles.X.DegreeToRadian(), ViewAngles.Y.DegreeToRadian());
        AimDirection = GraphicsMath.GetVectorFromEulerAngles
        (
            (ViewAngles.X + AimPunchAngle.X * Offsets.WeaponRecoilScale).DegreeToRadian(),
            (ViewAngles.Y + AimPunchAngle.Y * Offsets.WeaponRecoilScale).DegreeToRadian()
        );


        ReadCrosshairTargetTeam(gameProcess);
        UpdateHitCounter(gameProcess);


        return true;
    }

    private void UpdateHitCounter(GameProcess gameProcess)
    {
        if (Offsets.m_pBulletServices <= 0 || Offsets.m_totalHitsOnServer <= 0 || gameProcess.Process == null)
            return;

        var services = gameProcess.Process.Read<IntPtr>(AddressBase + Offsets.m_pBulletServices);
        if (services == IntPtr.Zero) return;

        var totalHits = gameProcess.Process.Read<int>(services + Offsets.m_totalHitsOnServer);
        if (!_totalHitsInitialized || totalHits < _previousTotalHits)
        {
            _previousTotalHits = totalHits;
            _totalHitsInitialized = true;
            return;
        }

        if (totalHits == _previousTotalHits) return;
        HitSequence += totalHits - _previousTotalHits;
        _previousTotalHits = totalHits;
        LastHitTargetTeam = ReadCrosshairTargetTeam(gameProcess);
    }

    private Team ReadCrosshairTargetTeam(GameProcess gameProcess)
    {
        if (gameProcess.Process == null || Offsets.m_iIDEntIndex <= 0) return GetRecentCrosshairTeam();
        var index = gameProcess.Process.Read<int>(AddressBase + Offsets.m_iIDEntIndex);
        if (index >= 0 && index < 32768)
        {
            var listEntry = gameProcess.Process.Read<IntPtr>(EntityList + 16 + 8 * (index >> 9));
            if (listEntry != IntPtr.Zero)
            {
                var target = gameProcess.Process.Read<IntPtr>(listEntry + 112 * (index & 0x1FF));
                if (target != IntPtr.Zero)
                {
                    var team = gameProcess.Process.Read<int>(target + Offsets.m_iTeamNum).ToTeam();
                    if (team is Team.Terrorists or Team.CounterTerrorists)
                    {
                        _lastCrosshairTargetTeam = team;
                        _lastCrosshairTargetTime = DateTime.UtcNow;
                        return team;
                    }
                }
            }
        }

        return GetRecentCrosshairTeam();
    }

    private Team GetRecentCrosshairTeam() =>
        DateTime.UtcNow - _lastCrosshairTargetTime <= TimeSpan.FromMilliseconds(250)
            ? _lastCrosshairTargetTeam
            : Team.Unknown;

    private Vector3 ReadAimPunchAngle(GameProcess gameProcess)
    {
        if (gameProcess.Process == null)
            throw new ArgumentNullException(nameof(gameProcess.Process), "Process cannot be null.");

        var aimPunchService = gameProcess.Process.Read<IntPtr>(AddressBase + Offsets.m_pAimPunchServices);
        if (aimPunchService != IntPtr.Zero)
        {
            try
            {
                return gameProcess.Process.Read<Vector3>(aimPunchService + Offsets.m_vecCsViewPunchAngle);
            }
            catch (Exception)
            {
                // Fall back to legacy path if the service access fails
            }
        }

        var cacheAddress = AddressBase + Offsets.m_AimPunchCache;
        AimPunchCacheCount = gameProcess.Process.Read<int>(cacheAddress);
        var cacheData = gameProcess.Process.Read<IntPtr>(cacheAddress + 0x8);

        if (AimPunchCacheCount > 0 && AimPunchCacheCount < 128 && cacheData != IntPtr.Zero)
            return gameProcess.Process.Read<Vector3>(cacheData + (AimPunchCacheCount - 1) * 12);

        AimPunchCacheCount = gameProcess.Process.Read<int>(cacheAddress + 0x8);
        cacheData = gameProcess.Process.Read<IntPtr>(cacheAddress + 0x10);

        if (AimPunchCacheCount > 0 && AimPunchCacheCount < 128 && cacheData != IntPtr.Zero)
            return gameProcess.Process.Read<Vector3>(cacheData + (AimPunchCacheCount - 1) * 12);

        return gameProcess.Process.Read<Vector3>(AddressBase + Offsets.m_AimPunchAngle);
    }

    private static readonly HashSet<string> GrenadeNames = new()
    {
        nameof(WeaponIndexes.Smokegrenade),
        nameof(WeaponIndexes.Flashbang),
        nameof(WeaponIndexes.Hegrenade),
        nameof(WeaponIndexes.Molotov)
    };

    public bool IsGrenade()
    {
        return GrenadeNames.Contains(CurrentWeaponName);
    }
}
