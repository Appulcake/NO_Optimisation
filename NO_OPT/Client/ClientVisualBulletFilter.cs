using System.Runtime.CompilerServices;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace NO_OPT.Client;

internal static class ClientVisualBulletFilter
{
    private static float _reducedTracerKeep;
    private static float _reducedNonTracerKeep;
    private static float _farTracerKeep;
    private static float _farNonTracerKeep;
    private static float _strategicTracerKeep;
    private static float _strategicNonTracerKeep;
    private static int _cameraFrame = -1;
    private static Vector3 _cameraPosition;
    private static uint _rng = 0xA341316Cu;
    
    internal static void RefreshSettings()
    {
        _reducedTracerKeep = ToProbability(Plugin.ClientBulletReducedTracerKeepPercent.Value);
        _reducedNonTracerKeep = ToProbability(Plugin.ClientBulletReducedNonTracerKeepPercent.Value);
        _farTracerKeep = ToProbability(Plugin.ClientBulletFarTracerKeepPercent.Value);
        _farNonTracerKeep = ToProbability(Plugin.ClientBulletFarNonTracerKeepPercent.Value);
        _strategicTracerKeep = ToProbability(Plugin.ClientBulletStrategicTracerKeepPercent.Value);
        _strategicNonTracerKeep = ToProbability(Plugin.ClientBulletStrategicNonTracerKeepPercent.Value);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ToProbability(float percent) => Mathf.Clamp01(percent * 0.01f);
    
    internal static bool ShouldSimulate(Vector3 muzzlePosition, bool tracer)
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
        
        return
            (x & 0x00FFFFFFu) * (1f / 16777216f);
    }
}

[HarmonyPatch]
internal static class BulletPatches
{
    [HarmonyPatch(typeof(BulletSim), nameof(BulletSim.AddBullet))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    // ReSharper disable once InconsistentNaming
    private static bool BulletSimAddBulletPrefix(bool ___visualOnly, Transform muzzle, bool tracer)
    {
        if (!Plugin.ClientVisualBulletOptimisationEnabled.Value || !___visualOnly || muzzle == null)
            return true;
        
        if (NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active)
            return true;
        
        return ClientVisualBulletFilter.ShouldSimulate(muzzle.position, tracer);
    }
}