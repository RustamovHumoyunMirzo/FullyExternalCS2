using System.IO;
using System.Net.Http;
using CS2Cheat.DTO.ClientDllDTO;
using CS2Cheat.Utils.DTO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CS2Cheat.Utils;

public abstract class Offsets
{
    private const string OffsetsUrl = "https://raw.githubusercontent.com/a2x/cs2-dumper/main/output/offsets.json";
    private const string ClientDllUrl = "https://raw.githubusercontent.com/a2x/cs2-dumper/main/output/client_dll.json";
    private const string CacheDirectory = "cache";
    private static readonly string CacheFile = Path.Combine(CacheDirectory, "offsets.json");

    #region offsets

    public const float WeaponRecoilScale = 2f;
    public static int dwLocalPlayerPawn;
    public static int m_vOldOrigin;
    public static int m_vecViewOffset;
    public static int m_AimPunchAngle;
    public static int m_AimPunchCache;
    public static int m_pAimPunchServices;
    public static int m_vecCsViewPunchAngle;
    public static int m_modelState;
    public static int m_pGameSceneNode;
    public static int m_fFlags;
    public static int m_iIDEntIndex;
    public static int m_lifeState;
    public static int m_iHealth;
    public static int m_iTeamNum;
    public static int dwEntityList;
    public static int m_bDormant;
    public static int m_iShotsFired;
    public static int m_hPawn;
    public static int m_bIsLocalPlayerController;
    public static int dwLocalPlayerController;
    public static int dwViewMatrix;
    public static int dwViewAngles;
    public static int m_entitySpottedState;
    public static int m_bSpottedByMask;
    public static int m_Glow;
    public static int m_bGlowing;
    public static int m_glowColorOverride;
    public static int m_iGlowType;
    public static int m_iGlowTeam;
    public static int m_pWeaponServices;
    public static int m_hActiveWeapon;
    public static int m_pBulletServices;
    public static int m_totalHitsOnServer;
    public static int m_Item;
    public static int m_pClippingWeapon;
    public static int m_AttributeManager;
    public static int m_iItemDefinitionIndex;
    public static int m_flFallbackWear;
    public static int m_nFallbackPaintKit;
    public static int m_nFallbackSeed;
    public static int m_nFallbackStatTrak;
    public static int m_iEntityQuality;
    public static int m_iItemIDHigh;
    public static int m_OriginalOwnerXuidHigh;
    public static int m_OriginalOwnerXuidLow;
    public static int m_steamID;
    public static int m_bIsScoped;
    public static int m_flFlashDuration;
    public static int m_iszPlayerName;
    public static int dwPlantedC4;
    public static int dwGlobalVars;
    public static int m_nBombSite;
    public static int m_bBombDefused;
    public static int m_vecAbsVelocity;
    public static int m_flDefuseCountDown;
    public static int m_flC4Blow;
    public static int m_bBeingDefused;
    public static int m_bBombTicking;
    public const nint m_nCurrentTickThisFrame = 0x34;

    public static readonly Dictionary<string, int> Bones = new()
    {
        { "head", 7 },
        { "neck_0", 6 },
        { "spine_1", 8 },
        { "spine_2", 3 },
        { "pelvis", 1 },
        { "arm_upper_L", 9 },
        { "arm_lower_L", 10 },
        { "hand_L", 11 },
        { "arm_upper_R", 13 },
        { "arm_lower_R", 14 },
        { "hand_R", 15 },
        { "leg_upper_L", 17 },
        { "leg_lower_L", 18 },
        { "ankle_L", 19 },
        { "leg_upper_R", 20 },
        { "leg_lower_R", 21 },
        { "ankle_R", 22 }
    };

    public static async Task<bool> UpdateOffsets()
    {
        try
        {
            if (await HasInternetConnection())
            {
                try
                {
                    var data = await DownloadOffsets();
                    SaveCache(data);
                    UpdateStaticFields(data);
                    Console.WriteLine($"[INFO] Downloaded and validated {data.Count} offset entries. Cache: {Path.GetFullPath(CacheFile)}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Could not download offsets: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("[WARN] No internet connection. Checking offset cache...");
            }

            if (TryLoadCache(out var cachedData))
            {
                UpdateStaticFields(cachedData);
                Console.WriteLine("[INFO] Loaded offsets from cache.");
                return true;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] Offsets not found.");
            Console.ResetColor();
            return false;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    private static readonly HttpClient HttpClientInstance = new();

    static Offsets()
    {
        HttpClientInstance.Timeout = TimeSpan.FromSeconds(5);
    }

    private static async Task<bool> HasInternetConnection()
    {
        try
        {
            using var response = await HttpClientInstance.GetAsync("https://raw.githubusercontent.com/",
                HttpCompletionOption.ResponseHeadersRead);
            return (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<Dictionary<string, int>> DownloadOffsets(string? outputDirectory = null)
    {
        var offsetsJson = outputDirectory == null
            ? await FetchJson(OffsetsUrl)
            : await File.ReadAllTextAsync(Path.Combine(outputDirectory, "offsets.json"));
        var clientJson = outputDirectory == null
            ? await FetchJson(ClientDllUrl)
            : await File.ReadAllTextAsync(Path.Combine(outputDirectory, "client_dll.json"));
        var sourceDataDw = JsonConvert.DeserializeObject<OffsetsDTO>(offsetsJson) ??
                           throw new InvalidOperationException("offsets.json is empty.");
        var sourceDataClient = JsonConvert.DeserializeObject<ClientDllDTO>(clientJson) ??
                               throw new InvalidOperationException("client_dll.json is empty.");

        var data = BuildOffsetMap(sourceDataDw, sourceDataClient);
        var classes = JObject.Parse(clientJson)["client.dll"]?["classes"] ??
                      throw new InvalidOperationException("client_dll.json has no client.dll classes.");
        data["m_bIsScoped"] = classes["C_CSPlayerPawn"]?["fields"]?["m_bIsScoped"]?.Value<int>() ??
                              throw new InvalidOperationException("Missing C_CSPlayerPawn.m_bIsScoped.");
        data["m_pBulletServices"] = classes["C_CSPlayerPawn"]?["fields"]?["m_pBulletServices"]?.Value<int>() ??
                                    throw new InvalidOperationException("Missing C_CSPlayerPawn.m_pBulletServices.");
        data["m_totalHitsOnServer"] = classes["CCSPlayer_BulletServices"]?["fields"]?["m_totalHitsOnServer"]?.Value<int>() ??
                                      throw new InvalidOperationException("Missing CCSPlayer_BulletServices.m_totalHitsOnServer.");
        AddOptionalField(data, classes, "C_EconEntity", "m_flFallbackWear");
        AddOptionalField(data, classes, "C_EconEntity", "m_nFallbackPaintKit");
        AddOptionalField(data, classes, "C_EconEntity", "m_nFallbackSeed");
        AddOptionalField(data, classes, "C_EconEntity", "m_nFallbackStatTrak");
        AddOptionalField(data, classes, "C_EconItemView", "m_iEntityQuality");
        AddOptionalField(data, classes, "C_EconItemView", "m_iItemIDHigh");
        AddOptionalField(data, classes, "C_EconEntity", "m_OriginalOwnerXuidHigh");
        AddOptionalField(data, classes, "C_EconEntity", "m_OriginalOwnerXuidLow");
        AddOptionalField(data, classes, "CBasePlayerController", "m_steamID");
        ValidateOffsetMap(data);
        var optionalMissing = new[] { "m_aimPunchAngle", "m_aimPunchCache", "m_pClippingWeapon" }
            .Where(key => data.GetValueOrDefault(key) == 0).ToArray();
        if (optionalMissing.Length > 0)
            Console.WriteLine($"[INFO] Legacy fields absent; service fallbacks will be used: {string.Join(", ", optionalMissing)}");
        return data;
    }

    public static bool LoadOffsetsFromDirectory(string outputDirectory)
    {
        try
        {
            var data = DownloadOffsets(outputDirectory).GetAwaiter().GetResult();
            SaveCache(data);
            UpdateStaticFields(data);
            Console.WriteLine($"[INFO] Fresh offsets loaded and saved to the shared cache: {Path.GetFullPath(CacheFile)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Fresh offset import failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<string> FetchJson(string url)
    {
        return await HttpClientInstance.GetStringAsync(url);
    }

    private static void AddOptionalField(Dictionary<string, int> data, JToken classes, string className, string fieldName)
    {
        var value = classes[className]?["fields"]?[fieldName]?.Value<int>();
        if (value.HasValue) data[fieldName] = value.Value;
    }

    private static Dictionary<string, int> BuildOffsetMap(OffsetsDTO sourceDataDw, ClientDllDTO sourceDataClient)
    {
        return new Dictionary<string, int>
        {
            ["dwBuildNumber"] = sourceDataDw.engine2dll.dwBuildNumber,
            ["dwLocalPlayerController"] = sourceDataDw.clientdll.dwLocalPlayerController,
            ["dwEntityList"] = sourceDataDw.clientdll.dwEntityList,
            ["dwViewMatrix"] = sourceDataDw.clientdll.dwViewMatrix,
            ["dwPlantedC4"] = sourceDataDw.clientdll.dwPlantedC4,
            ["dwLocalPlayerPawn"] = sourceDataDw.clientdll.dwLocalPlayerPawn,
            ["dwViewAngles"] = sourceDataDw.clientdll.dwViewAngles,
            ["dwGlobalVars"] = sourceDataDw.clientdll.dwGlobalVars,
            ["m_fFlags"] = sourceDataClient.clientdll.classes.C_BaseEntity.fields.m_fFlags,
            ["m_vOldOrigin"] = sourceDataClient.clientdll.classes.C_BasePlayerPawn.fields.m_vOldOrigin,
            ["m_vecViewOffset"] = sourceDataClient.clientdll.classes.C_BaseModelEntity.fields.m_vecViewOffset,
            ["m_aimPunchAngle"] = sourceDataClient.clientdll.classes.C_CSPlayerPawn.fields.m_aimPunchAngle,
            ["m_aimPunchCache"] = sourceDataClient.clientdll.classes.C_CSPlayerPawn.fields.m_aimPunchCache,
            ["m_pAimPunchServices"] = sourceDataClient.clientdll.classes.C_CSPlayerPawn.fields.m_pAimPunchServices,
            ["m_vecCsViewPunchAngle"] = sourceDataClient.clientdll.classes.CPlayer_CameraServices.fields.m_vecCsViewPunchAngle,
            ["m_modelState"] = sourceDataClient.clientdll.classes.CSkeletonInstance.fields.m_modelState,
            ["m_pGameSceneNode"] = sourceDataClient.clientdll.classes.C_BaseEntity.fields.m_pGameSceneNode,
            ["m_iIDEntIndex"] = sourceDataClient.clientdll.classes.C_CSPlayerPawn.fields.m_iIDEntIndex,
            ["m_lifeState"] = sourceDataClient.clientdll.classes.C_BaseEntity.fields.m_lifeState,
            ["m_iHealth"] = sourceDataClient.clientdll.classes.C_BaseEntity.fields.m_iHealth,
            ["m_iTeamNum"] = sourceDataClient.clientdll.classes.C_BaseEntity.fields.m_iTeamNum,
            ["m_bDormant"] = sourceDataClient.clientdll.classes.CGameSceneNode.fields.m_bDormant,
            ["m_iShotsFired"] = sourceDataClient.clientdll.classes.C_CSPlayerPawn.fields.m_iShotsFired,
            ["m_hPawn"] = sourceDataClient.clientdll.classes.CBasePlayerController.fields.m_hPawn,
            ["m_bIsLocalPlayerController"] = sourceDataClient.clientdll.classes.CBasePlayerController.fields.m_bIsLocalPlayerController,
            ["m_entitySpottedState"] = sourceDataClient.clientdll.classes.C_CSPlayerPawn.fields.m_entitySpottedState,
            ["m_bSpottedByMask"] = sourceDataClient.clientdll.classes.EntitySpottedState_t.fields.m_bSpottedByMask,
            ["m_Glow"] = sourceDataClient.clientdll.classes.C_BaseModelEntity.fields.m_Glow,
            ["m_bGlowing"] = sourceDataClient.clientdll.classes.CGlowProperty.fields.m_bGlowing,
            ["m_glowColorOverride"] = sourceDataClient.clientdll.classes.CGlowProperty.fields.m_glowColorOverride,
            ["m_iGlowType"] = sourceDataClient.clientdll.classes.CGlowProperty.fields.m_iGlowType,
            ["m_iGlowTeam"] = sourceDataClient.clientdll.classes.CGlowProperty.fields.m_iGlowTeam,
            ["m_pWeaponServices"] = sourceDataClient.clientdll.classes.C_BasePlayerPawn.fields.m_pWeaponServices,
            ["m_hActiveWeapon"] = sourceDataClient.clientdll.classes.CPlayer_WeaponServices.fields.m_hActiveWeapon,
            ["m_Item"] = sourceDataClient.clientdll.classes.C_AttributeContainer.fields.m_Item,
            ["m_pClippingWeapon"] = sourceDataClient.clientdll.classes.C_CSPlayerPawnBase.fields.m_pClippingWeapon,
            ["m_AttributeManager"] = sourceDataClient.clientdll.classes.C_EconEntity.fields.m_AttributeManager,
            ["m_iItemDefinitionIndex"] = sourceDataClient.clientdll.classes.C_EconItemView.fields.m_iItemDefinitionIndex,
            ["m_bIsScoped"] = sourceDataClient.clientdll.classes.C_CSPlayerPawnBase.fields.m_bIsScoped,
            ["m_flFlashDuration"] = sourceDataClient.clientdll.classes.C_CSPlayerPawnBase.fields.m_flFlashDuration,
            ["m_iszPlayerName"] = sourceDataClient.clientdll.classes.CBasePlayerController.fields.m_iszPlayerName,
            ["m_nBombSite"] = sourceDataClient.clientdll.classes.C_PlantedC4.fields.m_nBombSite,
            ["m_bBombDefused"] = sourceDataClient.clientdll.classes.C_PlantedC4.fields.m_bBombDefused,
            ["m_vecAbsVelocity"] = sourceDataClient.clientdll.classes.C_BaseEntity.fields.m_vecAbsVelocity,
            ["m_flDefuseCountDown"] = sourceDataClient.clientdll.classes.C_PlantedC4.fields.m_flDefuseCountDown,
            ["m_flC4Blow"] = sourceDataClient.clientdll.classes.C_PlantedC4.fields.m_flC4Blow,
            ["m_bBeingDefused"] = sourceDataClient.clientdll.classes.C_PlantedC4.fields.m_bBeingDefused,
            ["m_bBombTicking"] = sourceDataClient.clientdll.classes.C_PlantedC4.fields.m_bBombTicking
        };
    }

    private static void SaveCache(Dictionary<string, int> data)
    {
        Directory.CreateDirectory(CacheDirectory);
        File.WriteAllText(CacheFile, JsonConvert.SerializeObject(data, Formatting.Indented));
    }

    private static bool TryLoadCache(out Dictionary<string, int> data)
    {
        data = new Dictionary<string, int>();

        try
        {
            if (!File.Exists(CacheFile))
            {
                return false;
            }

            var json = File.ReadAllText(CacheFile);
            data = JsonConvert.DeserializeObject<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
            ValidateOffsetMap(data);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not load offset cache: {ex.Message}");
            return false;
        }
    }

    private static void ValidateOffsetMap(IReadOnlyDictionary<string, int> data)
    {
        var required = new[] { "dwLocalPlayerPawn", "dwLocalPlayerController", "dwEntityList", "dwViewMatrix",
            "dwViewAngles", "m_iHealth", "m_lifeState", "m_iTeamNum", "m_hPawn", "m_pGameSceneNode",
            "m_modelState", "m_bDormant", "m_vOldOrigin", "m_vecViewOffset", "m_AttributeManager",
            "m_Item", "m_iItemDefinitionIndex", "m_bIsScoped", "m_pWeaponServices", "m_hActiveWeapon",
            "m_pBulletServices", "m_totalHitsOnServer",
            "m_Glow", "m_bGlowing", "m_glowColorOverride", "m_iGlowType", "m_iGlowTeam" };
        var invalid = required.Where(key => !data.TryGetValue(key, out var value) || value <= 0).ToArray();
        if (invalid.Length > 0)
            throw new InvalidOperationException($"Missing or invalid required offsets: {string.Join(", ", invalid)}");
    }

    private static void UpdateStaticFields(IReadOnlyDictionary<string, int> data)
    {
        dwLocalPlayerPawn = data["dwLocalPlayerPawn"];
        m_vOldOrigin = data["m_vOldOrigin"];
        m_vecViewOffset = data["m_vecViewOffset"];
        m_AimPunchAngle = data["m_aimPunchAngle"];
        m_AimPunchCache = data["m_aimPunchCache"];
        m_pAimPunchServices = data["m_pAimPunchServices"];
        m_vecCsViewPunchAngle = data["m_vecCsViewPunchAngle"];
        m_modelState = data["m_modelState"];
        m_pGameSceneNode = data["m_pGameSceneNode"];
        m_iIDEntIndex = data["m_iIDEntIndex"];
        m_lifeState = data["m_lifeState"];
        m_iHealth = data["m_iHealth"];
        m_iTeamNum = data["m_iTeamNum"];
        m_bDormant = data["m_bDormant"];
        m_iShotsFired = data["m_iShotsFired"];
        m_hPawn = data["m_hPawn"];
        m_bIsLocalPlayerController = data.TryGetValue("m_bIsLocalPlayerController", out var isLocalPlayerController)
            ? isLocalPlayerController
            : 0;
        m_fFlags = data["m_fFlags"];
        dwLocalPlayerController = data["dwLocalPlayerController"];
        dwViewMatrix = data["dwViewMatrix"];
        dwViewAngles = data["dwViewAngles"];
        dwEntityList = data["dwEntityList"];
        m_entitySpottedState = data["m_entitySpottedState"];
        m_bSpottedByMask = data.TryGetValue("m_bSpottedByMask", out var spottedByMask) ? spottedByMask : 0xC;
        m_Glow = data.GetValueOrDefault("m_Glow");
        m_bGlowing = data.GetValueOrDefault("m_bGlowing");
        m_glowColorOverride = data.GetValueOrDefault("m_glowColorOverride");
        m_iGlowType = data.GetValueOrDefault("m_iGlowType");
        m_iGlowTeam = data.GetValueOrDefault("m_iGlowTeam");
        m_pWeaponServices = data.GetValueOrDefault("m_pWeaponServices");
        m_hActiveWeapon = data.GetValueOrDefault("m_hActiveWeapon");
        m_pBulletServices = data.GetValueOrDefault("m_pBulletServices");
        m_totalHitsOnServer = data.GetValueOrDefault("m_totalHitsOnServer");
        m_Item = data["m_Item"];
        m_pClippingWeapon = data["m_pClippingWeapon"];
        m_AttributeManager = data["m_AttributeManager"];
        m_iItemDefinitionIndex = data["m_iItemDefinitionIndex"];
        m_flFallbackWear = data.GetValueOrDefault("m_flFallbackWear");
        m_nFallbackPaintKit = data.GetValueOrDefault("m_nFallbackPaintKit");
        m_nFallbackSeed = data.GetValueOrDefault("m_nFallbackSeed");
        m_nFallbackStatTrak = data.GetValueOrDefault("m_nFallbackStatTrak");
        m_iEntityQuality = data.GetValueOrDefault("m_iEntityQuality");
        m_iItemIDHigh = data.GetValueOrDefault("m_iItemIDHigh");
        m_OriginalOwnerXuidHigh = data.GetValueOrDefault("m_OriginalOwnerXuidHigh");
        m_OriginalOwnerXuidLow = data.GetValueOrDefault("m_OriginalOwnerXuidLow");
        m_steamID = data.GetValueOrDefault("m_steamID");
        m_bIsScoped = data["m_bIsScoped"];
        m_flFlashDuration = data["m_flFlashDuration"];
        m_iszPlayerName = data["m_iszPlayerName"];
        dwPlantedC4 = data["dwPlantedC4"];
        dwGlobalVars = data["dwGlobalVars"];
        m_nBombSite = data["m_nBombSite"];
        m_bBombDefused = data["m_bBombDefused"];
        m_vecAbsVelocity = data["m_vecAbsVelocity"];
        m_flDefuseCountDown = data["m_flDefuseCountDown"];
        m_flC4Blow = data["m_flC4Blow"];
        m_bBeingDefused = data["m_bBeingDefused"];
        m_bBombTicking = data["m_bBombTicking"];
    }

    #endregion

}
