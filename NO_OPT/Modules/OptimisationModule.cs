using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace NO_OPT.Modules;

internal abstract class OptimisationModule
{
    private readonly List<Harmony> _patchHarmonies = [];
    private readonly List<Action> _unsubscribe = [];
    private bool _active;
    private ConfigEntry<bool>? _enabled;
    private bool _managerEnabled = true;
    private bool _started;
    
    protected Plugin Host { get; private set; } = null!;
    protected ConfigFile Config => Host.Config;
    
    internal OptimisationModuleAttribute Metadata { get; private set; } = null!;
    
    internal void ConfigureCore(Plugin host, OptimisationModuleAttribute metadata)
    {
        Host = host;
        Metadata = metadata;
        if (metadata.Toggleable)
        {
            _enabled = Config.Bind(metadata.Section, metadata.EnableKey, metadata.DefaultEnabled, metadata.Description);
            if (metadata.LiveToggle)
                Watch(_enabled, () =>
                {
                    if (_started)
                        RefreshEnabledState();
                });
        }
        
        Configure();
    }
    
    internal void StartCore()
    {
        _started = true;
        RefreshEnabledState();
    }
    
    internal void SetManagerEnabledCore(bool enabled)
    {
        if (_managerEnabled == enabled)
            return;
        
        _managerEnabled = enabled;
        if (_started)
            RefreshEnabledState();
    }
    
    private void RefreshEnabledState()
    {
        var ownEnabled = _enabled?.Value ?? true;
        SetEnabled(_started && _managerEnabled && ownEnabled);
    }
    
    internal void StopCore()
    {
        if (_started)
            SetEnabled(false);
        _started = false;
        for (var i = _unsubscribe.Count - 1; i >= 0; i--)
            _unsubscribe[i]();
        _unsubscribe.Clear();
    }
    
    protected virtual void Configure()
    {
    }
    
    // This runs before harmony patching
    protected virtual void OnEnable()
    {
    }
    
    // This runs after harmony patches are removed
    protected virtual void OnDisable()
    {
    }
    
    protected ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description = "") =>
        Config.Bind(section, key, defaultValue, description);
    
    protected void Watch<T>(ConfigEntry<T> entry, Action onChanged)
    {
        EventHandler handler = (_, _) => onChanged();
        entry.SettingChanged += handler;
        _unsubscribe.Add(() => entry.SettingChanged -= handler);
    }
    
    private void SetEnabled(bool enable)
    {
        if (enable == _active)
            return;
        
        if (enable)
        {
            try
            {
                OnEnable();
                ApplyPatches();
                _active = true;
                Plugin.Log($"Enabled {GetType().Name}.");
            }
            catch (Exception ex)
            {
                RemovePatches();
                try
                {
                    OnDisable();
                }
                catch (Exception rollbackEx)
                {
                    Plugin.LogError($"Rollback failed for {GetType().Name}:\n{rollbackEx}");
                }
                
                Plugin.LogError($"Failed enabling {GetType().Name}:\n{ex}");
            }
            
            return;
        }
        
        RemovePatches();
        try
        {
            OnDisable();
        }
        catch (Exception ex)
        {
            Plugin.LogError($"Failed disabling {GetType().Name}:\n{ex}");
        }
        
        _active = false;
        Plugin.Log($"Disabled {GetType().Name}.");
    }
    
    private void ApplyPatches()
    {
        foreach (var patchType in FindPatchTypes(GetType()))
        {
            var harmony = new Harmony($"{MyPluginInfo.PLUGIN_GUID}.{GetType().Name}.{patchType.Name}");
            try
            {
                var patched = harmony.CreateClassProcessor(patchType).Patch();
                _patchHarmonies.Add(harmony);
                Plugin.Log($"{GetType().Name}/{patchType.Name}: {patched?.Count ?? 0} patched method(s).");
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                Plugin.LogError($"Failed applying {GetType().Name}/{patchType.Name}; " +
                                $"continuing with remaining groups.\n{ex}");
            }
        }
    }
    
    private void RemovePatches()
    {
        for (var i = _patchHarmonies.Count - 1; i >= 0; i--)
            _patchHarmonies[i].UnpatchSelf();
        _patchHarmonies.Clear();
    }
    
    private static IEnumerable<Type> FindPatchTypes(Type root)
    {
        return FindPatchTypesRecursive(root).OrderBy(type => type.FullName, StringComparer.Ordinal);
    }
    
    private static IEnumerable<Type> FindPatchTypesRecursive(Type type)
    {
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (nested.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0) yield return nested;
            foreach (var deeper in FindPatchTypesRecursive(nested))
                yield return deeper;
        }
    }
}