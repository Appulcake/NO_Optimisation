using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NO_OPT.Generic;
using NO_OPT.Server;
using UnityEngine;

namespace NO_OPT;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private bool _genericMissileOptimisationPatched;
    private Harmony _genericMissileOptimisationPatches = null!;
    
    private bool _headlessPatched;
    private Harmony _headlessPatches = null!;
    internal new static ManualLogSource Logger { get; private set; } = null!;
    internal bool IsHeadlessServer { get; private set; }
    
    private static ConfigEntry<bool> HeadlessPatchesEnabled { get; set; } = null!;
    private static ConfigEntry<bool> GenericMissileOptimisationPatchesEnabled { get; set; } = null!;
    internal static ConfigEntry<bool> GenericMissileOptimisation_GroupByOwner { get; private set; } = null!;
    internal static ConfigEntry<float> GenericMissileOptimisation_LaunchProximity { get; private set; } = null!;
    internal static ConfigEntry<float> GenericMissileOptimisation_ETAWindow { get; private set; } = null!;
    
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
        
        _headlessPatches = new Harmony($"{MyPluginInfo.PLUGIN_GUID}.HeadlessPatches");
        _genericMissileOptimisationPatches =
            new Harmony($"{MyPluginInfo.PLUGIN_GUID}.GenericMissileOptimisationPatches");
        
        HeadlessPatchesEnabled.SettingChanged += HeadlessPatchesEnabledChanged;
        GenericMissileOptimisationPatchesEnabled.SettingChanged += GenericMissileOptimisationPatchesEnabledChanged;
        
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }
    
    private void Start()
    {
        IsHeadlessServer = GameManager.IsHeadless;
        SetHeadlessPatchesEnabled(HeadlessPatchesEnabled.Value);
        SetGenericMissileOptimisationPatchesEnabled(GenericMissileOptimisationPatchesEnabled.Value);
    }
    
    private void OnDestroy()
    {
        HeadlessPatchesEnabled.SettingChanged -= HeadlessPatchesEnabledChanged;
        GenericMissileOptimisationPatchesEnabled.SettingChanged -= GenericMissileOptimisationPatchesEnabledChanged;
        _headlessPatches.UnpatchSelf();
        _genericMissileOptimisationPatches.UnpatchSelf();
    }
    
    private void HeadlessPatchesEnabledChanged(object sender, EventArgs e)
    {
        SetHeadlessPatchesEnabled(HeadlessPatchesEnabled.Value);
    }
    
    private void GenericMissileOptimisationPatchesEnabledChanged(object sender, EventArgs e)
    {
        SetGenericMissileOptimisationPatchesEnabled(GenericMissileOptimisationPatchesEnabled.Value);
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
            
            Logger.LogInfo("Applying Headless Server patches...");
            AudioListener.pause = true;
            HeadlessServerPatches.Apply(_headlessPatches);
            _headlessPatched = true;
            Logger.LogInfo("Patched Headless Server patches.");
        }
        else
        {
            Logger.LogInfo("Disabling Headless Server patches...");
            _headlessPatches.UnpatchSelf();
            _headlessPatched = false;
            Logger.LogInfo("Disabled Headless Server patches.");
        }
    }
    
    private void SetGenericMissileOptimisationPatchesEnabled(bool enable)
    {
        if (enable == _genericMissileOptimisationPatched)
            return;
        
        if (enable)
        {
            Logger.LogInfo("Applying Generic Missile Optimisation patches...");
            MissileOptimisations.Apply(_genericMissileOptimisationPatches);
            _genericMissileOptimisationPatched = true;
            Logger.LogInfo("Patched Generic Missile Optimisation patches.");
        }
        else
        {
            Logger.LogInfo("Disabling Generic Missile Optimisation patches...");
            _genericMissileOptimisationPatches.UnpatchSelf();
            _genericMissileOptimisationPatched = false;
            Logger.LogInfo("Disabled Generic Missile Optimisation patches.");
        }
    }
}