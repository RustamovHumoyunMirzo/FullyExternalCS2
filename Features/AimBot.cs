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
    private const double AnglePerPixel = 0.0005;
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

            if ((DateTime.Now - _lastAiUpdate).TotalMilliseconds > AimUpdateIntervalMs)
            {
                _lastAiUpdate = DateTime.Now;
            }

            var cfgFov = (double)Config.AimFov;
            var aimPixels = Point.Empty;
            var aimActive = Config.AimBot;
            var aimResult = aimActive && GetAimTargetPixels(out aimPixels, cfgFov);
            var recoilPixels = GetRecoilControlPixels(aimActive, aimResult);

            if (!aimActive)
            {
                _lastTargetId = -1;
                _lastTargetPos = Vector3.Zero;
                MoveMouse(recoilPixels);
                return;
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

    private bool GetAimTargetPixels(out Point aimPixels, double customFovDegrees)
    {
        var minDistanceSquared = float.MaxValue;
        aimPixels = Point.Empty;
        var targetFound = false;
        var lockedTargetFound = false;
        var aimPositionScreen = Vector2.Zero;

        if (GameData?.Player == null || GameProcess == null)
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
                TryGetAimTargetScreen(lockedTarget, boneName, float.MaxValue, out _, out aimPositionScreen))
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

            var fovRadius = GetFovRadiusPixels(customFovDegrees);
            if (!TryGetAimTargetScreen(entity, boneName, fovRadius, out var distanceSquared, out var boneScreen))
            {
                continue;
            }
            if (distanceSquared < minDistanceSquared)
            {
                minDistanceSquared = distanceSquared;
                aimPositionScreen = boneScreen;
                targetFound = true;
                _lastTargetId = entity.Id;
            }
        }

        if (!targetFound)
        {
            _lastTargetId = -1;
            _lastTargetPos = Vector3.Zero;
            return false;
        }

        var screenCenter = GetScreenCenter();
        var smoothFactor = Math.Max(Config.AimSmoothing, 1.0f);
        aimPixels = new Point(
            (int)Math.Round((aimPositionScreen.X - screenCenter.X) / smoothFactor),
            (int)Math.Round((aimPositionScreen.Y - screenCenter.Y) / smoothFactor)
        );

        _lastTargetPos = new Vector3(aimPositionScreen, 0f);
        _lastTargetVel = Vector3.Zero;
        _lastTargetUpdate = DateTime.Now;

        return targetFound;
    }

    private bool TryGetAimTargetScreen(Entity entity, string boneName, float maxDistancePixels,
        out float distanceSquared, out Vector2 aimPositionScreen)
    {
        distanceSquared = float.MaxValue;
        aimPositionScreen = Vector2.Zero;

        if (GameData?.Player == null || !entity.IsAlive() || entity.AddressBase == GameData.Player.AddressBase)
        {
            return false;
        }

        if (Config.TeamCheck && entity.Team == GameData?.Player?.Team)
        {
            return false;
        }

        if (Config.AimOnlyVisible && !IsVisibleTarget(entity))
        {
            return false;
        }

        if (!entity.BonePos.TryGetValue(boneName, out var bonePos) || bonePos == Vector3.Zero)
        {
            return false;
        }

        var rectangle = GameProcess?.WindowRectangleClient ?? System.Drawing.Rectangle.Empty;
        var transformed = GameData.Player.MatrixViewProjectionViewport.Transform(bonePos);
        if (transformed.Z >= 1 ||
            transformed.X < 0 || transformed.Y < 0 ||
            transformed.X > rectangle.Width || transformed.Y > rectangle.Height)
        {
            return false;
        }

        var screenCenter = GetScreenCenter();
        aimPositionScreen = new Vector2(transformed.X, transformed.Y);
        distanceSquared = Vector2.DistanceSquared(aimPositionScreen, screenCenter);
        if (distanceSquared > maxDistancePixels * maxDistancePixels)
        {
            return false;
        }

        return true;
    }

    private Vector2 GetScreenCenter()
    {
        var rectangle = GameProcess?.WindowRectangleClient ?? System.Drawing.Rectangle.Empty;
        return new Vector2(rectangle.Width * 0.5f, rectangle.Height * 0.5f);
    }

    private float GetFovRadiusPixels(double fovDegrees)
    {
        var rectangle = GameProcess?.WindowRectangleClient ?? System.Drawing.Rectangle.Empty;
        var halfWidth = rectangle.Width > 0 ? rectangle.Width * 0.5 : Player.Fov * 10.0;
        return (float)(Math.Tan(fovDegrees.DegreeToRadian() / 2.0) /
                       Math.Tan(90.0.DegreeToRadian() / 2.0) * halfWidth);
    }

    private bool IsVisibleTarget(Entity entity)
    {
        if (GameData == null)
        {
            return false;
        }

        return GameData.LocalPlayerId >= 0 && entity.IsSpottedBy(GameData.LocalPlayerId);
    }

    private void GetAimAngles(Vector3 pointWorld, out float angleSize, out Vector2 aimAngles)
    {
        aimAngles = Vector2.Zero;
        angleSize = 0f;

        if (GameData == null || GameData.Player == null)
        {
            return;
        }

        var desiredDirection = pointWorld - GameData.Player.EyePosition;
        if (desiredDirection.LengthSquared() < 0.000001f)
        {
            return;
        }

        var horizontalLength = MathF.Sqrt(desiredDirection.X * desiredDirection.X + desiredDirection.Y * desiredDirection.Y);
        if (horizontalLength < 0.000001f)
        {
            return;
        }

        var desiredPitch = -MathF.Atan2(desiredDirection.Z, horizontalLength) * 180f / MathF.PI;
        var desiredYaw = MathF.Atan2(desiredDirection.Y, desiredDirection.X) * 180f / MathF.PI;
        var currentAngles = GameData.Player.ViewAngles;

        var horizontalAngle = NormalizeAngleDegrees(desiredYaw - currentAngles.Y).DegreeToRadian();
        var verticalAngle = NormalizeAngleDegrees(desiredPitch - currentAngles.X).DegreeToRadian();

        aimAngles = new Vector2(horizontalAngle, verticalAngle);
        angleSize = MathF.Sqrt(horizontalAngle * horizontalAngle + verticalAngle * verticalAngle);
    }

    private static float NormalizeAngleDegrees(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    private static void GetAimPixels(Vector2 aimAngles, out Point aimPixels)
    {
        var fovRatio = 90.0 / Player.Fov;
        aimPixels = new Point(
            (int)Math.Round(aimAngles.X / AnglePerPixel * fovRatio),
            (int)Math.Round(aimAngles.Y / AnglePerPixel * fovRatio)
        );
    }
}
