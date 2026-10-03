using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using UnityEngine;

namespace NO_OPT.Compatibility;

internal static class GwWeaponDamageCompatibility
{
    private const string GrayWarTakeDamageType = "GW_server_plugin.Patches.KillsLogging.TakeDamageExtensions";
    private const string GrayWarMissileExtensionsType = "GW_server_plugin.Patches.KillsLogging.MissileExtensions";
    private const string GrayWarPluginType = "GW_server_plugin.GwServerPlugin";
    private static readonly Assembly? GrayWarAssembly = ResolveGrayWarAssembly();
    private static readonly WeaponAwareTakeDamageDelegate? GrayWarTakeDamage = ResolveGrayWarTakeDamage();
    
    private static readonly WeaponAwareHasShockwaveReachedDelegate? GrayWarHasShockwaveReached =
        ResolveGrayWarHasShockwaveReached();
    
    private static readonly Func<Shockwave, string>?
        GrayWarGetShockwaveWeaponName = ResolveGrayWarShockwaveWeaponName();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void TakeDamage(IDamageable component, float pierceDamage, float blastDamage, float amountAffected,
        float fireDamage, float impactDamage, PersistentID dealerID, string weaponName)
    {
        var grayWarTakeDamage = GrayWarTakeDamage;
        if (grayWarTakeDamage != null)
        {
            grayWarTakeDamage(component, pierceDamage, blastDamage, amountAffected, fireDamage, impactDamage, dealerID,
                weaponName);
            return;
        }
        
        component.TakeDamage(pierceDamage, blastDamage, amountAffected, fireDamage, impactDamage, dealerID);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string? GetShockwaveWeaponName(Shockwave shockwave) =>
        GrayWarGetShockwaveWeaponName?.Invoke(shockwave);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool HasShockwaveReached(Shockwave.InfluencedObject influencedObject, Vector3 blastOrigin,
        float blastPropagation, float overpressure, float blastYield, float blastPower, PersistentID ownerID,
        string? weaponName)
    {
        var grayWarHasShockwaveReached = GrayWarHasShockwaveReached;
        if (grayWarHasShockwaveReached != null && weaponName != null)
            return grayWarHasShockwaveReached(influencedObject, blastOrigin, blastPropagation, overpressure, blastYield,
                blastPower, ownerID, weaponName);
        return influencedObject.HasShockwaveReached(blastOrigin, blastPropagation, overpressure, blastYield, blastPower,
            ownerID);
    }
    
    private static Assembly? ResolveGrayWarAssembly()
    {
        foreach (var pluginInfo in Chainloader.PluginInfos.Values)
        {
            var plugin = pluginInfo.Instance;
            if (plugin == null)
                continue;
            
            var assembly = plugin.GetType().Assembly;
            if (assembly.GetType(GrayWarPluginType, false) != null)
                return assembly;
        }
        
        return null;
    }
    
    private static WeaponAwareTakeDamageDelegate? ResolveGrayWarTakeDamage()
    {
        if (GrayWarAssembly == null)
            return null;
        
        var apiType = GrayWarAssembly.GetType(GrayWarTakeDamageType, false);
        if (apiType == null)
            return null;
        
        var method = apiType.GetMethod(nameof(TakeDamage), BindingFlags.Public | BindingFlags.Static, null,
        [
            typeof(IDamageable),
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(PersistentID),
            typeof(string)
        ], null);
        if (method == null || method.ReturnType != typeof(void))
        {
            Plugin.LogWarning("GrayWar detected, but its TakeDamage API does not have the expected signature.");
            return null;
        }
        
        try
        {
            var result =
                (WeaponAwareTakeDamageDelegate)Delegate.CreateDelegate(typeof(WeaponAwareTakeDamageDelegate), method);
            Plugin.Log("GrayWar TakeDamage API detected.");
            return result;
        }
        catch (Exception ex)
        {
            Plugin.LogWarning($"Failed binding GrayWar TakeDamage API:\n{ex}");
            return null;
        }
    }
    
    private static WeaponAwareHasShockwaveReachedDelegate? ResolveGrayWarHasShockwaveReached()
    {
        if (GrayWarAssembly == null)
            return null;
        
        var apiType = GrayWarAssembly.GetType(GrayWarMissileExtensionsType, false);
        if (apiType == null)
            return null;
        
        var method = apiType.GetMethod(nameof(HasShockwaveReached), BindingFlags.Public | BindingFlags.Static, null,
        [
            typeof(Shockwave.InfluencedObject),
            typeof(Vector3),
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(PersistentID),
            typeof(string)
        ], null);
        
        if (method == null || method.ReturnType != typeof(bool))
        {
            Plugin.LogWarning(
                "GrayWar detected, but its HasShockwaveReached API does not have the expected signature.");
            return null;
        }
        
        try
        {
            var result =
                (WeaponAwareHasShockwaveReachedDelegate)Delegate.CreateDelegate(
                    typeof(WeaponAwareHasShockwaveReachedDelegate), method);
            Plugin.Log("GrayWar shockwave damage API detected.");
            return result;
        }
        catch (Exception ex)
        {
            Plugin.LogWarning($"Failed binding GrayWar shockwave damage API:\n{ex}");
            return null;
        }
    }
    
    private static Func<Shockwave, string>? ResolveGrayWarShockwaveWeaponName()
    {
        if (GrayWarAssembly == null)
            return null;
        
        try
        {
            var pluginType = GrayWarAssembly.GetType(GrayWarPluginType, false);
            var storageField =
                pluginType?.GetField("ShockwaveWeaponStorage", BindingFlags.Public | BindingFlags.Static);
            var storage = storageField?.GetValue(null);
            if (storage == null)
                return null;
            
            var storageType = storage.GetType();
            var getMethod = storageType.GetMethod("Get", BindingFlags.Public | BindingFlags.Instance, null,
                [typeof(Shockwave)], null);
            if (getMethod == null)
                return null;
            
            var weaponNameField =
                getMethod.ReturnType.GetField("WeaponName", BindingFlags.Public | BindingFlags.Instance);
            if (weaponNameField == null || weaponNameField.FieldType != typeof(string))
                return null;
            
            var shockwaveParameter = Expression.Parameter(typeof(Shockwave), "shockwave");
            var storageExpression = Expression.Constant(storage, storageType);
            var getCall = Expression.Call(storageExpression, getMethod, shockwaveParameter);
            var weaponName = Expression.Field(getCall, weaponNameField);
            var getter = Expression.Lambda<Func<Shockwave, string>>(weaponName, shockwaveParameter).Compile();
            Plugin.Log("GrayWar shockwave weapon name storage detected.");
            return getter;
        }
        catch (Exception ex)
        {
            Plugin.LogWarning($"Failed binding GrayWar shockwave weapon name storage:\n{ex}");
            return null;
        }
    }
    
    
    private delegate void WeaponAwareTakeDamageDelegate(IDamageable component, float pierceDamage, float blastDamage,
        float amountAffected, float fireDamage, float impactDamage, PersistentID dealerID, string weaponName);
    
    private delegate bool WeaponAwareHasShockwaveReachedDelegate(Shockwave.InfluencedObject influencedObject,
        Vector3 blastOrigin, float blastPropagation, float overpressure, float blastYield, float blastPower,
        PersistentID ownerID, string weaponName);
}