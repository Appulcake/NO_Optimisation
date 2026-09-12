using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NO_OPT.Client;
using NO_OPT.Generic;
using NO_OPT.Server;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace NO_OPT;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private bool _clientPatched;
    private Harmony _clientPatches = null!;
    private bool _genericMissileOptimisationPatched;
    private Harmony _genericMissileOptimisationPatches = null!;
    
    private bool _headlessPatched;
    private Harmony _headlessPatches = null!;
    
    private new static ManualLogSource Logger { get; set; } = null!;
    private bool IsHeadlessServer { get; set; }
    
    private static ConfigEntry<bool> HeadlessPatchesEnabled { get; set; } = null!;
    private static ConfigEntry<bool> GenericMissileOptimisationPatchesEnabled { get; set; } = null!;
    internal static ConfigEntry<bool> GenericMissileOptimisation_GroupByOwner { get; private set; } = null!;
    internal static ConfigEntry<float> GenericMissileOptimisation_LaunchProximity { get; private set; } = null!;
    internal static ConfigEntry<float> GenericMissileOptimisation_ETAWindow { get; private set; } = null!;
    internal static ConfigEntry<bool> GenericMissileOptimisation_CruiseRetargeting { get; private set; } = null!;
    internal static ConfigEntry<float> GenericMissileOptimisation_CruiseRetargetRange { get; private set; } = null!;
    
    private static ConfigEntry<bool> ClientPatchesEnabled { get; set; } = null!;
    internal static ConfigEntry<bool> ClientHudMarkerOptimisationEnabled { get; private set; } = null!;
    internal static ConfigEntry<bool> ClientDatalinkTargetSearchOptimisationEnabled { get; private set; } = null!;
    internal static ConfigEntry<bool> ClientPresentationOptimisationEnabled { get; private set; } = null!;
    internal static ConfigEntry<bool> ClientDynamicMapOptimisationEnabled { get; private set; } = null!;
    internal static ConfigEntry<float> ClientMinimisedMapUPS { get; private set; } = null!;
    
    internal static ConfigEntry<float> ClientFidelity_ReducedDistance { get; private set; } = null!;
    internal static ConfigEntry<float> ClientFidelity_FarDistance { get; private set; } = null!;
    internal static ConfigEntry<float> ClientFidelity_StrategicDistance { get; private set; } = null!;
    
    internal static ConfigEntry<float> ClientHUD_ReducedUPS { get; private set; } = null!;
    internal static ConfigEntry<float> ClientHUD_FarUPS { get; private set; } = null!;
    internal static ConfigEntry<float> ClientHUD_StrategicUPS { get; private set; } = null!;
    
    internal static ConfigEntry<bool> ClientDynamicMapViewportCullingEnabled { get; private set; } = null!;
    internal static ConfigEntry<float> ClientDynamicMapViewportOverscan { get; private set; } = null!;
    
    internal static ConfigEntry<bool> ClientVisualBulletOptimisationEnabled { get; private set; } = null!;
    internal static ConfigEntry<float> ClientBulletReducedTracerKeepPercent { get; private set; } = null!;
    internal static ConfigEntry<float> ClientBulletReducedNonTracerKeepPercent { get; private set; } = null!;
    internal static ConfigEntry<float> ClientBulletFarTracerKeepPercent { get; private set; } = null!;
    internal static ConfigEntry<float> ClientBulletFarNonTracerKeepPercent { get; private set; } = null!;
    internal static ConfigEntry<float> ClientBulletStrategicTracerKeepPercent { get; private set; } = null!;
    internal static ConfigEntry<float> ClientBulletStrategicNonTracerKeepPercent { get; private set; } = null!;
    internal static ConfigEntry<float> ClientHUD_CullDistance { get; private set; } = null!;
    internal static ConfigEntry<bool> ClientObjectiveHudMarkersEnabled { get; private set; } = null!;
    
    internal static ConfigEntry<float> ClientObjectiveMapUPS { get; private set; } = null!;
    internal static ConfigEntry<float> ClientObjectiveHudDataUPS { get; private set; } = null!;
    
    internal static ConfigEntry<bool> ClientDebugHotkeys { get; private set; } = null!;
    private static ConfigEntry<bool> DebugLogs { get; set; } = null!;
    
    private void Awake()
    {
        Logger = base.Logger;
        
        HeadlessPatchesEnabled = Config.Bind("Headless Server", "Enable Headless Server Patches", true);
        GenericMissileOptimisationPatchesEnabled = Config.Bind("Generic", "Enable Missile Optimisation Patches", true);
        GenericMissileOptimisation_GroupByOwner =
            Config.Bind("Generic - Cruise Missile", "Group Formation By Owner", true);
        GenericMissileOptimisation_LaunchProximity =
            Config.Bind("Generic - Cruise Missile", "Group Formation Launch Distance", 10000f);
        GenericMissileOptimisation_ETAWindow =
            Config.Bind("Generic - Cruise Missile", "Group Formation ETA Window", 15f);
        GenericMissileOptimisation_CruiseRetargeting =
            Config.Bind("Generic - Cruise Missile", "Retarget Destroyed Targets", false);
        GenericMissileOptimisation_CruiseRetargetRange =
            Config.Bind("Generic - Cruise Missile", "Retarget Group Range", 7500f);
        
        ClientPatchesEnabled = Config.Bind("Client", "Enable Client Patches", true);
        ClientDatalinkTargetSearchOptimisationEnabled =
            Config.Bind("Client", "Datalink Target Search Optimisation", true);
        ClientPresentationOptimisationEnabled = Config.Bind("Client", "Distant Unit Rendering Optimisation", true);
        ClientHudMarkerOptimisationEnabled = Config.Bind("Client", "HUD Marker Optimisation", true);
        ClientDynamicMapOptimisationEnabled = Config.Bind("Client", "Dynamic Map Optimisation", true);
        ClientMinimisedMapUPS = Config.Bind("Client", "Dynamic Map Update Rate", 30f);
        
        ClientFidelity_ReducedDistance = Config.Bind("Client - Fidelity", "1. Reduced Fidelity Distance", 2500f);
        ClientFidelity_FarDistance = Config.Bind("Client - Fidelity", "2. Far Fidelity Distance", 7500f);
        ClientFidelity_StrategicDistance = Config.Bind("Client - Fidelity", "3. Strategic Fidelity Distance", 15000f);
        
        ClientHUD_ReducedUPS = Config.Bind("Client - HUD", "1. Reduced Marker Update Rate", 30f,
            "HUD marker updates per second in Reduced Fidelity. 0 = full rate.");
        ClientHUD_FarUPS = Config.Bind("Client - HUD", "2. Far Marker Update Rate", 15f,
            "HUD marker updates per second in Far fidelity. 0 = full rate.");
        ClientHUD_StrategicUPS = Config.Bind("Client - HUD", "3. Strategic Marker Update Rate", 6f,
            "HUD marker updates per second in Strategic fidelity. 0 = full rate.");
        ClientHUD_CullDistance = Config.Bind("Client - HUD", "HUD Marker Hide Distance", 0f,
            "Hide unit HUD markers entirely beyond this distance. Can help performance a little extra, but is mainly "
            + "subjectively nicer looking if you want less clutter. 0 disables distance hiding.");
        ClientObjectiveHudMarkersEnabled = Config.Bind("Client - HUD", "Show Objective HUD Markers", true);
        
        ClientDynamicMapViewportCullingEnabled = Config.Bind("Client - Map", "Minimised Map Culling", true,
            "Hide unit map icons outside the visible minimap area " +
            "(vanilla normally keeps rendering icons that are off screen on the minimap, this disables that).");
        ClientDynamicMapViewportOverscan = Config.Bind("Client - Map", "Minimised Map Culling Extra Margin", 15f,
            "Extra percentage outside the visible minimap where units are still rendered to prevent a late icon pop in.");
        ClientObjectiveMapUPS = Config.Bind("Client - Map", "Objective Map Update Rate", 2f,
            "Map objective marker update refresh speed (per second). Since map objectives don't tend to move a lot, "
            + "reducing how often their marker updates is essentially free performance savings. 0 = full rate / vanilla.");
        ClientObjectiveHudDataUPS = Config.Bind("Client - HUD", "Objective HUD Update Rate", 2f,
            "Same as the one for Map Update Rate, but for markers on the HUD. 0 = full rate / vanilla.");
        
        ClientVisualBulletOptimisationEnabled = Config.Bind("Client - Visual Bullets", "Enable Visual Bullet Culling",
            true,
            "Reduce visual remote bullets based on distance.");
        ClientBulletReducedTracerKeepPercent =
            Config.Bind("Client - Visual Bullets", "1. Reduced Tracer Keep Percent", 100f);
        ClientBulletReducedNonTracerKeepPercent =
            Config.Bind("Client - Visual Bullets", "1. Reduced Non-Tracer Keep Percent", 50f);
        ClientBulletFarTracerKeepPercent = Config.Bind("Client - Visual Bullets", "2. Far Tracer Keep Percent", 50f);
        ClientBulletFarNonTracerKeepPercent =
            Config.Bind("Client - Visual Bullets", "2. Far Non-Tracer Keep Percent", 10f);
        ClientBulletStrategicTracerKeepPercent =
            Config.Bind("Client - Visual Bullets", "3. Strategic Tracer Keep Percent", 0f);
        ClientBulletStrategicNonTracerKeepPercent =
            Config.Bind("Client - Visual Bullets", "3. Strategic Non-Tracer Keep Percent", 0f);
        
        ClientDebugHotkeys = Config.Bind("Debug", "Client Debug Hotkeys", false);
        DebugLogs = Config.Bind("Debug", "Debug Logs", false);
        
        _headlessPatches = new Harmony($"{MyPluginInfo.PLUGIN_GUID}.HeadlessPatches");
        _genericMissileOptimisationPatches =
            new Harmony($"{MyPluginInfo.PLUGIN_GUID}.GenericMissileOptimisationPatches");
        _clientPatches = new Harmony($"{MyPluginInfo.PLUGIN_GUID}.ClientPatches");
        
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }
    
    private void Start()
    {
        IsHeadlessServer = GameManager.IsHeadless;
        
        GenericMissileOptimisationPatchesEnabled.SettingChanged += GenericMissileOptimisationPatchesEnabledChanged;
        SetGenericMissileOptimisationPatchesEnabled(GenericMissileOptimisationPatchesEnabled.Value);
        
        if (!IsHeadlessServer)
        {
            ClientPatchesEnabled.SettingChanged += ClientPatchesEnabledChanged;
            ClientFidelity_ReducedDistance.SettingChanged += ClientCachedSettingChanged;
            ClientFidelity_FarDistance.SettingChanged += ClientCachedSettingChanged;
            ClientFidelity_StrategicDistance.SettingChanged += ClientCachedSettingChanged;
            
            ClientHUD_ReducedUPS.SettingChanged += ClientCachedSettingChanged;
            ClientHUD_FarUPS.SettingChanged += ClientCachedSettingChanged;
            ClientHUD_StrategicUPS.SettingChanged += ClientCachedSettingChanged;
            ClientDynamicMapViewportOverscan.SettingChanged += ClientCachedSettingChanged;
            ClientBulletReducedTracerKeepPercent.SettingChanged += ClientCachedSettingChanged;
            ClientBulletReducedNonTracerKeepPercent.SettingChanged += ClientCachedSettingChanged;
            ClientBulletFarTracerKeepPercent.SettingChanged += ClientCachedSettingChanged;
            ClientBulletFarNonTracerKeepPercent.SettingChanged += ClientCachedSettingChanged;
            ClientBulletStrategicTracerKeepPercent.SettingChanged += ClientCachedSettingChanged;
            ClientBulletStrategicNonTracerKeepPercent.SettingChanged += ClientCachedSettingChanged;
            
            ClientHUD_CullDistance.SettingChanged += ClientCachedSettingChanged;
            ClientObjectiveMapUPS.SettingChanged += ClientCachedSettingChanged;
            ClientObjectiveHudDataUPS.SettingChanged += ClientCachedSettingChanged;
            ClientObjectiveHudMarkersEnabled.SettingChanged += ClientCachedSettingChanged;
            
            SetClientPatchesEnabled(ClientPatchesEnabled.Value);
            
            gameObject.AddComponent<ClientUiDiagnostics>();
            return;
        }
        
        HeadlessPatchesEnabled.SettingChanged += HeadlessPatchesEnabledChanged;
        SetHeadlessPatchesEnabled(HeadlessPatchesEnabled.Value);
    }
    
    private void OnDestroy()
    {
        HeadlessPatchesEnabled.SettingChanged -= HeadlessPatchesEnabledChanged;
        GenericMissileOptimisationPatchesEnabled.SettingChanged -= GenericMissileOptimisationPatchesEnabledChanged;
        ClientPatchesEnabled.SettingChanged -= ClientPatchesEnabledChanged;
        _headlessPatches.UnpatchSelf();
        _genericMissileOptimisationPatches.UnpatchSelf();
        ClientPatches.RestoreRuntimeState();
        _clientPatches.UnpatchSelf();
    }
    
    internal static void Debug(string message, DebugType type = DebugType.LogInfo)
    {
        if (!DebugLogs.Value) return;
        switch (type)
        {
            case DebugType.LogDebug:
                Logger.LogDebug(message);
                break;
            case DebugType.LogError:
                Logger.LogError(message);
                break;
            case DebugType.LogWarning:
                Logger.LogWarning(message);
                break;
            case DebugType.LogInfo:
            default:
                Logger.LogInfo(message);
                break;
        }
    }
    
    private void HeadlessPatchesEnabledChanged(object sender, EventArgs e)
    {
        SetHeadlessPatchesEnabled(HeadlessPatchesEnabled.Value);
    }
    
    private void GenericMissileOptimisationPatchesEnabledChanged(object sender, EventArgs e)
    {
        SetGenericMissileOptimisationPatchesEnabled(GenericMissileOptimisationPatchesEnabled.Value);
    }
    
    private void ClientPatchesEnabledChanged(object sender, EventArgs e)
    {
        SetClientPatchesEnabled(ClientPatchesEnabled.Value);
    }
    
    private void ClientCachedSettingChanged(object sender, EventArgs e)
    {
        ClientPatches.RefreshSettings();
    }
    
    private void SetHeadlessPatchesEnabled(bool enable)
    {
        if (enable == _headlessPatched)
            return;
        
        if (enable)
        {
            if (!IsHeadlessServer)
            {
                Logger.LogInfo("Instance is not a headless server, skipping enabling Headless Server patches.");
                return;
            }
            
            Debug("Applying Headless Server patches...");
            AudioListener.pause = true;
            HeadlessServerPatches.Apply(_headlessPatches);
            _headlessPatched = true;
            Debug("Patched Headless Server patches.");
        }
        else
        {
            Debug("Disabling Headless Server patches...");
            _headlessPatches.UnpatchSelf();
            _headlessPatched = false;
            Debug("Disabled Headless Server patches.");
        }
    }
    
    private void SetGenericMissileOptimisationPatchesEnabled(bool enable)
    {
        if (enable == _genericMissileOptimisationPatched)
            return;
        
        if (enable)
        {
            Debug("Applying Generic Missile Optimisation patches...");
            MissileOptimisations.Apply(_genericMissileOptimisationPatches);
            _genericMissileOptimisationPatched = true;
            Debug("Patched Generic Missile Optimisation patches.");
        }
        else
        {
            Debug("Disabling Generic Missile Optimisation patches...");
            _genericMissileOptimisationPatches.UnpatchSelf();
            _genericMissileOptimisationPatched = false;
            Debug("Disabled Generic Missile Optimisation patches.");
        }
    }
    
    private void SetClientPatchesEnabled(bool enable)
    {
        if (enable == _clientPatched)
            return;
        
        if (enable)
        {
            Debug("Applying Client Optimisation patches...");
            ClientPatches.Apply(_clientPatches);
            _clientPatched = true;
            Debug("Patched Client Optimisation patches.");
        }
        else
        {
            Debug("Disabling Client Optimisation patches...");
            ClientPatches.RestoreRuntimeState();
            _clientPatches.UnpatchSelf();
            _clientPatched = false;
            Debug("Disabled Client Optimisation patches.");
        }
    }
    
    internal enum DebugType
    {
        LogDebug,
        LogError,
        LogInfo,
        LogWarning
    }
}