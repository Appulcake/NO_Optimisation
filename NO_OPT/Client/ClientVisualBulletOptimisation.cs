using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using NO_OPT.Modules;
using NuclearOption.Networking;
using UnityEngine;

namespace NO_OPT.Client;

[OptimisationModule(ModuleScope.Client, "Client - Visual Bullets", "0. Enable Visual Bullet Culling",
    true, "Reduce visual remote bullets based on distance.")]
internal sealed class ClientVisualBulletOptimisation : OptimisationModule
{
    private static ConfigEntry<float> _reducedTracerKeepPercent = null!;
    private static ConfigEntry<float> _reducedNonTracerKeepPercent = null!;
    private static ConfigEntry<float> _farTracerKeepPercent = null!;
    private static ConfigEntry<float> _farNonTracerKeepPercent = null!;
    private static ConfigEntry<float> _strategicTracerKeepPercent = null!;
    private static ConfigEntry<float> _strategicNonTracerKeepPercent = null!;
    
    private static float _reducedTracerKeep;
    private static float _reducedNonTracerKeep;
    private static float _farTracerKeep;
    private static float _farNonTracerKeep;
    private static float _strategicTracerKeep;
    private static float _strategicNonTracerKeep;
    private static int _cameraFrame = -1;
    private static Vector3 _cameraPosition;
    private static uint _rng = 0xA341316Cu;
    
    protected override void Configure()
    {
        _reducedTracerKeepPercent =
            Bind("Client - Visual Bullets", "1. Reduced Tracer Keep Percent", 100f);
        _reducedNonTracerKeepPercent =
            Bind("Client - Visual Bullets", "1. Reduced Non-Tracer Keep Percent", 50f);
        _farTracerKeepPercent =
            Bind("Client - Visual Bullets", "2. Far Tracer Keep Percent", 50f);
        _farNonTracerKeepPercent =
            Bind("Client - Visual Bullets", "2. Far Non-Tracer Keep Percent", 10f);
        _strategicTracerKeepPercent =
            Bind("Client - Visual Bullets", "3. Strategic Tracer Keep Percent", 0f);
        _strategicNonTracerKeepPercent =
            Bind("Client - Visual Bullets", "3. Strategic Non-Tracer Keep Percent", 0f);
        
        Watch(_reducedTracerKeepPercent, RefreshSettings);
        Watch(_reducedNonTracerKeepPercent, RefreshSettings);
        Watch(_farTracerKeepPercent, RefreshSettings);
        Watch(_farNonTracerKeepPercent, RefreshSettings);
        Watch(_strategicTracerKeepPercent, RefreshSettings);
        Watch(_strategicNonTracerKeepPercent, RefreshSettings);
        RefreshSettings();
    }
    
    private static void RefreshSettings()
    {
        _reducedTracerKeep = ToProbability(_reducedTracerKeepPercent.Value);
        _reducedNonTracerKeep = ToProbability(_reducedNonTracerKeepPercent.Value);
        _farTracerKeep = ToProbability(_farTracerKeepPercent.Value);
        _farNonTracerKeep = ToProbability(_farNonTracerKeepPercent.Value);
        _strategicTracerKeep = ToProbability(_strategicTracerKeepPercent.Value);
        _strategicNonTracerKeep = ToProbability(_strategicNonTracerKeepPercent.Value);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ToProbability(float percent) => Mathf.Clamp01(percent * 0.01f);
    
    private static bool ShouldSimulate(Vector3 muzzlePosition, bool tracer)
    {
        var frame = Time.frameCount;
        if (_cameraFrame != frame)
        {
            var cameraState = SceneSingleton<CameraStateManager>.i;
            if (cameraState == null)
                return true;
            
            _cameraFrame = frame;
            _cameraPosition = cameraState.transform.position;
        }
        
        var tier = ClientActivity.GetTier(muzzlePosition, _cameraPosition);
        var keep = GetKeepProbability(tier, tracer);
        if (keep >= 1f)
            return true;
        
        if (keep <= 0f)
            return false;
        
        return Next01() < keep;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetKeepProbability(ClientActivityTier tier, bool tracer)
    {
        return tier switch
        {
            ClientActivityTier.Reduced => tracer
                ? _reducedTracerKeep
                : _reducedNonTracerKeep,
            ClientActivityTier.Far => tracer
                ? _farTracerKeep
                : _farNonTracerKeep,
            ClientActivityTier.Strategic => tracer
                ? _strategicTracerKeep
                : _strategicNonTracerKeep,
            _ => 1f
        };
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Next01()
    {
        var x = _rng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _rng = x;
        return (x & 0x00FFFFFFu) * (1f / 16777216f);
    }
    
    [HarmonyPatch]
    private static class BulletPatches
    {
        [HarmonyPatch(typeof(BulletSim), nameof(BulletSim.AddBullet))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool BulletSimAddBulletPrefix(bool ___visualOnly, Transform muzzle, bool tracer)
        {
            if (!___visualOnly || muzzle == null)
                return true;
            
            if (NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active)
                return true;
            
            return ShouldSimulate(muzzle.position, tracer);
        }
    }
}