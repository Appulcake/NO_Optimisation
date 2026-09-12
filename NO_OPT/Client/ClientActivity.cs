using System.Runtime.CompilerServices;
using UnityEngine;

namespace NO_OPT.Client;

internal enum ClientActivityTier : byte
{
    Full,
    Reduced,
    Far,
    Strategic
}

internal static class ClientActivity
{
    private const float PresentationWakeHysteresis = 1000f;
    private static float _reducedDistanceSq;
    private static float _farDistanceSq;
    private static float _strategicDistanceSq;
    private static float _presentationWakeDistanceSq;
    
    internal static void RefreshSettings()
    {
        var reduced = Mathf.Max(Plugin.ClientFidelity_ReducedDistance.Value, 0f);
        var far = Mathf.Max(Plugin.ClientFidelity_FarDistance.Value, reduced);
        var strategic = Mathf.Max(Plugin.ClientFidelity_StrategicDistance.Value, far);
        _reducedDistanceSq = reduced * reduced;
        _farDistanceSq = far * far;
        _strategicDistanceSq = strategic * strategic;
        var presentationWake = Mathf.Max(strategic - PresentationWakeHysteresis, 0f);
        _presentationWakeDistanceSq = presentationWake * presentationWake;
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