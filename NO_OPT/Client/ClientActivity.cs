using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using NO_OPT.Modules;
using UnityEngine;

namespace NO_OPT.Client;

internal enum ClientActivityTier : byte
{
    Full,
    Reduced,
    Far,
    Strategic
}

[OptimisationModule(ModuleScope.Client, RespectClientMaster = false)]
internal sealed class ClientActivity : OptimisationModule
{
    private const float PresentationWakeHysteresis = 1000f;
    
    private static ConfigEntry<float> _reducedDistance = null!;
    private static ConfigEntry<float> _farDistance = null!;
    private static ConfigEntry<float> _strategicDistance = null!;
    
    private static float _reducedDistanceSq;
    private static float _farDistanceSq;
    private static float _strategicDistanceSq;
    private static float _presentationWakeDistanceSq;
    
    protected override void Configure()
    {
        _reducedDistance = Bind("--- Client - Fidelity ---", "1. Reduced Fidelity Distance", 2500f);
        _farDistance = Bind("--- Client - Fidelity ---", "2. Far Fidelity Distance", 7500f);
        _strategicDistance = Bind("--- Client - Fidelity ---", "3. Strategic Fidelity Distance", 15000f);
        Watch(_reducedDistance, RefreshSettings);
        Watch(_farDistance, RefreshSettings);
        Watch(_strategicDistance, RefreshSettings);
        RefreshSettings();
    }
    
    private static void RefreshSettings()
    {
        var reduced = Mathf.Max(_reducedDistance.Value, 0f);
        var far = Mathf.Max(_farDistance.Value, reduced);
        var strategic = Mathf.Max(_strategicDistance.Value, far);
        _reducedDistanceSq = reduced * reduced;
        _farDistanceSq = far * far;
        _strategicDistanceSq = strategic * strategic;
        var wake = Mathf.Max(strategic - PresentationWakeHysteresis, 0f);
        _presentationWakeDistanceSq = wake * wake;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ClientActivityTier GetTier(Vector3 worldPosition, Vector3 cameraPosition)
    {
        var delta = worldPosition - cameraPosition;
        return GetTierFromDistanceSq(delta.sqrMagnitude);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ClientActivityTier GetTierFromDistanceSq(float distanceSq)
    {
        if (distanceSq >= _strategicDistanceSq)
            return ClientActivityTier.Strategic;
        if (distanceSq >= _farDistanceSq)
            return ClientActivityTier.Far;
        if (distanceSq >= _reducedDistanceSq)
            return ClientActivityTier.Reduced;
        return ClientActivityTier.Full;
    }
    
    internal static bool ShouldSleepPresentation(Unit unit, bool currentlySleeping)
    {
        if (unit == null || !IsPresentationCandidate(unit) || IsPromoted(unit))
            return false;
        
        var cameraState = SceneSingleton<CameraStateManager>.i;
        if (cameraState == null)
            return false;
        
        var delta = unit.transform.position - cameraState.transform.position;
        var thresholdSq = currentlySleeping ? _presentationWakeDistanceSq : _strategicDistanceSq;
        return delta.sqrMagnitude >= thresholdSq;
    }
    
    private static bool IsPresentationCandidate(Unit unit) => unit is Aircraft || unit is GroundVehicle || unit is Ship;
    
    private static bool IsPromoted(Unit unit)
    {
        if (GameManager.IsLocalAircraft(unit))
            return true;
        
        var cameraState = SceneSingleton<CameraStateManager>.i;
        if (cameraState != null && cameraState.followingUnit == unit)
            return true;
        
        var hud = SceneSingleton<CombatHUD>.i;
        var targets = hud?.GetTargetList();
        if (targets != null)
            foreach (var t in targets)
                if (t == unit)
                    return true;
        
        return false;
    }
}