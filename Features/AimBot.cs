using System.Numerics;
using System.Runtime.InteropServices;
using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using Point = System.Drawing.Point;

namespace CS2Cheat.Features;

public class AimBot : ThreadedServiceBase
{
    private const int AimUpdateIntervalMs = 500;
    private const int AimEventWindowMs = 1000;
    private static double _anglePerPixel;
    private DateTime _lastAimEvent = DateTime.MinValue;
    private DateTime _lastAiUpdate = DateTime.MinValue;

    private int _lastTargetId = -1;
    private Vector3 _lastTargetPos = Vector3.Zero;
    private DateTime _lastTargetUpdate = DateTime.MinValue;
    private Vector3 _lastTargetVel = Vector3.Zero;
    private Vector2 _previousPunch = Vector2.Zero;
    private int _previousShotsFired;

    private bool _wasAimKeyDown;
    private bool _wasRcsKeyDown;

    public AimBot(GameProcess gameProcess, GameData gameData)
    {
        GameProcess = gameProcess;
        GameData = gameData;
    }

    private static ConfigManager Config => ConfigManager.Load();

    private bool IsCalibrated { get; set; }

    protected override string ThreadName => nameof(AimBot);

    private GameProcess? GameProcess { get; set; }
    private GameData? GameData { get; set; }
    private float CurrentSmoothing { get; set; } = 3f;

    public override void Dispose()
    {
        base.Dispose();
        GameData = null;
        GameProcess = null;
    }

    protected override void FrameAction()
    {
        try
        {
            if (GameProcess == null || !GameProcess.IsValid)
            {
                return;
            }

            var config = Config;
            var aimKeyDown = config.AimBotKey.IsKeyDown();
            var rcsKeyDown = config.AimRcsKey.IsKeyDown();
            if (aimKeyDown && !_wasAimKeyDown)
            {
                config.AimBot = !config.AimBot;
                ConfigManager.UpdateCache(config);

                if (!config.AimBot)
                {
                    _lastTargetId = -1;
                    _lastTargetPos = Vector3.Zero;
                }
            }
            if (rcsKeyDown && !_wasRcsKeyDown)
            {
                config.AimRcs = !config.AimRcs;
                ConfigManager.UpdateCache(config);

                if (!config.AimRcs)
                {
                    _previousPunch = Vector2.Zero;
                    _previousShotsFired = 0;
                }
            }

            _wasAimKeyDown = aimKeyDown;
            _wasRcsKeyDown = rcsKeyDown;

            if (GameData?.Player == null || !GameData.Player.IsAlive())
            {
                _previousPunch = Vector2.Zero;
                _lastTargetId = -1;
                _lastTargetPos = Vector3.Zero;
                return;
            }

            if (!config.AimBot && !config.AimRcs)
            {
                _previousPunch = Vector2.Zero;
                return;
            }

            if (GameData.Player.IsGrenade())
            {
                _previousPunch = Vector2.Zero;
                return;
            }

            if (!IsCalibrated)
            {
                Calibrate();
                IsCalibrated = true;
            }

            if ((DateTime.Now - _lastAiUpdate).TotalMilliseconds > AimUpdateIntervalMs)
            {
                _lastAiUpdate = DateTime.Now;
            }

            var aimAngles = Vector2.Zero;
            var cfgFov = (double)Config.AimFov;
            var aimPixels = Point.Empty;
            var aimActive = Config.AimBot;
            var aimResult = aimActive &&
                            GetAimTargetWithPrediction(out aimAngles, cfgFov.DegreeToRadian());
            var recoilPixels = GetRecoilControlPixels(aimActive, aimResult);

            if (!aimActive)
            {
                _lastTargetId = -1;
                _lastTargetPos = Vector3.Zero;
                MoveMouse(recoilPixels);
                return;
            }

            if (aimResult)
            {
                if (!float.IsNaN(aimAngles.X) && !float.IsNaN(aimAngles.Y))
                {
                    GetAimPixels(aimAngles, out aimPixels);
                }
            }

            aimPixels.X = Math.Clamp(aimPixels.X + recoilPixels.X, -100, 100);
            aimPixels.Y = Math.Clamp(aimPixels.Y + recoilPixels.Y, -100, 100);

            MoveMouse(aimPixels);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AimBot ERROR] {ex.Message}");
        }
    }

    private Point GetRecoilControlPixels(bool aimActive, bool hasAimTarget)
    {
        if (!Config.AimRcs || GameData?.Player == null)
        {
            _previousPunch = Vector2.Zero;
            _previousShotsFired = 0;
            return Point.Empty;
        }

        var player = GameData.Player;
        var currentPunch = ToPunchVector(player.AimPunchAngle);
        var rcsScale = Config.AimRcsStrength / 100f;

        if (currentPunch.LengthSquared() >= 0.0001f)
        {
            var deltaPunch = currentPunch - _previousPunch;
            _previousPunch = currentPunch;
            _previousShotsFired = player.ShotsFired;

            if (deltaPunch.LengthSquared() < 0.000001f)
            {
                return Point.Empty;
            }

            var recoilAngles = new Vector2(-deltaPunch.Y, deltaPunch.X) * Offsets.WeaponRecoilScale * rcsScale;
            GetAimPixels(recoilAngles, out var recoilPixels);

            recoilPixels.X = Math.Clamp(recoilPixels.X, -75, 75);
            recoilPixels.Y = Math.Clamp(recoilPixels.Y, -75, 75);

            return recoilPixels;
        }

        if (aimActive && !hasAimTarget)
        {
            _previousShotsFired = player.ShotsFired;
            return Point.Empty;
        }

        return GetFallbackRecoilPixels(player.ShotsFired, rcsScale);
    }

    private static Vector2 ToPunchVector(Vector3 punch)
    {
        return new Vector2(punch.X, punch.Y);
    }

    private Point GetFallbackRecoilPixels(int shotsFired, float rcsScale)
    {
        if (shotsFired <= 1)
        {
            _previousShotsFired = shotsFired;
            return Point.Empty;
        }

        var shotDelta = shotsFired > _previousShotsFired ? shotsFired - Math.Max(_previousShotsFired, 1) : 1;
        _previousShotsFired = shotsFired;

        var x = 0;
        var y = Math.Clamp((int)Math.Round((6 + Math.Min(shotsFired, 12) * 0.9) * shotDelta * rcsScale), 1, 45);
        return new Point(x, y);
    }

    private static void MoveMouse(Point pixels)
    {
        if (pixels.X == 0 && pixels.Y == 0)
        {
            return;
        }

        Utility.MouseMove(pixels.X, pixels.Y);
        Thread.Sleep(15);
    }

    private bool GetAimTargetWithPrediction(out Vector2 aimAngles, double customFov)
    {
        var minAngleSize = float.MaxValue;
        aimAngles = new Vector2((float)Math.PI, (float)Math.PI);
        var targetFound = false;
        var lockedTargetFound = false;
        var aimPosition = Vector3.Zero;
        var targetVel = Vector3.Zero;

        if (GameData == null)
        {
            _lastTargetId = -1;
            return false;
        }

        var boneIdx = Math.Clamp(Config.AimBoneIndex, 0, ConfigManager.BoneNames.Length - 1);
        var boneName = ConfigManager.BoneNames[boneIdx];

        if (Config.AimLockTarget && _lastTargetId >= 0)
        {
            var lockedTarget = GameData.Entities?.FirstOrDefault(entity => entity.Id == _lastTargetId);
            if (lockedTarget != null &&
                TryGetAimTarget(lockedTarget, boneName, double.MaxValue, out _, out aimAngles, out aimPosition,
                    out targetVel))
            {
                targetFound = true;
                lockedTargetFound = true;
            }
            else
            {
                _lastTargetId = -1;
                _lastTargetPos = Vector3.Zero;
            }
        }

        foreach (var entity in GameData.Entities)
        {
            if (lockedTargetFound)
            {
                break;
            }

            if (!TryGetAimTarget(entity, boneName, customFov, out var angleSize, out var localAngles,
                    out var bonePos, out var entityVel))
            {
                continue;
            }
            if (angleSize < minAngleSize)
            {
                minAngleSize = angleSize;
                aimAngles = localAngles;
                aimPosition = bonePos;
                targetFound = true;
                targetVel = entityVel;
                _lastTargetId = entity.Id;
            }
        }

        if (!targetFound)
        {
            _lastTargetId = -1;
            _lastTargetPos = Vector3.Zero;
            return false;
        }

        if (targetFound && targetVel != Vector3.Zero)
        {
            var now = DateTime.Now;
            var timeSinceLastTarget = (now - _lastTargetUpdate).TotalSeconds;

            if (timeSinceLastTarget < 0.5 && _lastTargetPos != Vector3.Zero)
            {
                var predictedPos = aimPosition + targetVel * (float)(timeSinceLastTarget * 0.5);
                float angleSize;
                Vector2 predictedAngles;
                GetAimAngles(predictedPos, out angleSize, out predictedAngles);
                if (angleSize < customFov)
                {
                    aimAngles = predictedAngles;
                }
            }

            _lastTargetPos = aimPosition;
            _lastTargetVel = targetVel;
            _lastTargetUpdate = now;
        }

        if (targetFound)
        {
            var smoothFactor = Math.Max(Config.AimSmoothing, 1.0f);
            aimAngles /= smoothFactor;
        }

        return targetFound;
    }

    private bool TryGetAimTarget(Entity entity, string boneName, double maxFov, out float angleSize,
        out Vector2 aimAngles, out Vector3 aimPosition, out Vector3 targetVel)
    {
        angleSize = float.MaxValue;
        aimAngles = Vector2.Zero;
        aimPosition = Vector3.Zero;
        targetVel = Vector3.Zero;

        if (!entity.IsAlive())
        {
            return false;
        }

        if (Config.TeamCheck && entity.Team == GameData?.Player?.Team)
        {
            return false;
        }

        if (Config.AimOnlyVisible && !entity.IsSpotted)
        {
            return false;
        }

        if (!entity.BonePos.TryGetValue(boneName, out var bonePos) || bonePos == Vector3.Zero)
        {
            return false;
        }

        GetAimAngles(bonePos, out angleSize, out aimAngles);

        if (angleSize >= maxFov)
        {
            return false;
        }

        aimPosition = bonePos;
        targetVel = entity.Velocity;
        return true;
    }


    private void GetAimAngles(Vector3 pointWorld, out float angleSize, out Vector2 aimAngles)
    {
        aimAngles = Vector2.Zero;
        angleSize = 0f;

        if (GameData == null || GameData.Player == null)
        {
            return;
        }

        var aimDirection = GameData.Player.EyeDirection;
        if (Config.AimRcs && GameData.Player.AimPunchAngle.LengthSquared() >= 0.0001f)
        {
            var rcsScale = Config.AimRcsStrength / 100f;
            var viewAngles = GameData.Player.ViewAngles;
            var punch = GameData.Player.AimPunchAngle * Offsets.WeaponRecoilScale * rcsScale;
            aimDirection = GraphicsMath.GetVectorFromEulerAngles(
                (viewAngles.X + punch.X).DegreeToRadian(),
                (viewAngles.Y + punch.Y).DegreeToRadian()
            );
        }

        var aimDirectionDesired = Vector3.Normalize(pointWorld - GameData.Player.EyePosition);

        var horizontalAngle = aimDirectionDesired.GetSignedAngleTo(aimDirection, new Vector3(0, 0, 1));
        var verticalAngle = aimDirectionDesired.GetSignedAngleTo(aimDirection,
            Vector3.Normalize(Vector3.Cross(aimDirectionDesired, new Vector3(0, 0, 1))));

        aimAngles = new Vector2(horizontalAngle, verticalAngle);

        angleSize = aimDirection.GetAngleTo(aimDirectionDesired);
    }


    private static void GetAimPixels(Vector2 aimAngles, out Point aimPixels)
    {
        var fovRatio = 90.0 / Player.Fov;
        var anglePerPx = _anglePerPixel > 0 ? _anglePerPixel : 0.0005;
        aimPixels = new Point(
            (int)Math.Round(aimAngles.X / anglePerPx * fovRatio),
            (int)Math.Round(aimAngles.Y / anglePerPx * fovRatio)
        );
    }

    private void Calibrate()
    {
        var measures = new[]
        {
            CalibrationMeasureAnglePerPixel(100),
            CalibrationMeasureAnglePerPixel(-200),
            CalibrationMeasureAnglePerPixel(300),
            CalibrationMeasureAnglePerPixel(-400),
            CalibrationMeasureAnglePerPixel(200)
        }.Where(x => x > 0).ToArray();

        _anglePerPixel = measures.Length > 0 ? measures.Average() : 0.0005;
    }

    private double CalibrationMeasureAnglePerPixel(int deltaPixels)
    {
        Thread.Sleep(100);

        if (GameData == null || GameData.Player == null)
        {
            return 0.0;
        }

        var eyeDirectionStart = GameData.Player.EyeDirection;
        eyeDirectionStart.Z = 0;

        Utility.MouseMove(deltaPixels, 0);

        Thread.Sleep(100);

        if (GameData == null || GameData.Player == null)
        {
            return 0.0;
        }

        var eyeDirectionEnd = GameData.Player.EyeDirection;
        eyeDirectionEnd.Z = 0;

        return eyeDirectionEnd.GetAngleTo(eyeDirectionStart) / Math.Abs(deltaPixels);
    }
}
