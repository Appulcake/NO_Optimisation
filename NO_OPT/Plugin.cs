using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using NO_OPT.Modules;

// ReSharper disable InconsistentNaming

namespace NO_OPT;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<bool>? _clientPatchesEnabled;
    private ModuleManager? _moduleManager;
    
    private static ManualLogSource LogSource { get; set; } = null!;
    private static ConfigEntry<bool> DebugLogs { get; set; } = null!;
    
    private void Awake()
    {
        LogSource = Logger;
        DebugLogs = Config.Bind("--- Debug ---", "Debug Logs", false);
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }
    
    private void Start()
    {
        var isHeadlessServer = GameManager.IsHeadless;
        if (!isHeadlessServer)
            _clientPatchesEnabled = Config.Bind("--- Client---", "0. Enable Client Patches", true,
                "Master switch that toggles all client only optimisations, doesn't otherwise do " + 
                "anything on its own.");
        _moduleManager = new ModuleManager(this, isHeadlessServer);
        if (_clientPatchesEnabled != null)
        {
            _moduleManager.SetClientMasterEnabled(_clientPatchesEnabled.Value);
            _clientPatchesEnabled.SettingChanged += ClientPatchesEnabledChanged;
        }
        
        _moduleManager.Initialise();
    }
    
    private void OnDestroy()
    {
        if (_clientPatchesEnabled != null)
            _clientPatchesEnabled.SettingChanged -= ClientPatchesEnabledChanged;
        _moduleManager?.Dispose();
        _moduleManager = null;
    }
    
    private void ClientPatchesEnabledChanged(object sender, EventArgs e)
    {
        if (_clientPatchesEnabled == null)
            return;
        
        _moduleManager?.SetClientMasterEnabled(_clientPatchesEnabled.Value);
    }
    
    internal static void Log(string message)
    {
        if (DebugLogs.Value)
            LogSource.LogInfo(message);
    }
    
    internal static void LogDebug(string message)
    {
        if (DebugLogs.Value)
            LogSource.LogDebug(message);
    }
    
    internal static void LogWarning(string message)
    {
        LogSource.LogWarning(message);
    }
    
    internal static void LogError(string message)
    {
        LogSource.LogError(message);
    }
}