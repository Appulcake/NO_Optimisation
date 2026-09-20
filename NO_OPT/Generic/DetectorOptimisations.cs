using System.Runtime.CompilerServices;
using HarmonyLib;
using NO_OPT.Modules;
using NuclearOption.Jobs;

namespace NO_OPT.Generic;

[OptimisationModule(ModuleScope.Any, "--- Generic ---", "0. Enable Detector Optimisations",
    true, "Disables redundant LoS/Radar/Datalink detection checks for scenery props, this can " +
          "massively boost client FPS and server UPS on scenery prop-heavy maps.")]
internal sealed class DetectorOptimisations : OptimisationModule
{
    // TO-DO: Check how much of this all is even needed on a remote client/non host?
    // seems to intentionally be running on client without IsServer for GameManager.IsLocalAircraft(attachedUnit)
    // but what part(s) are even necessary there?
    
    [HarmonyPatch]
    internal static class DetectorPatches
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldIgnoreTarget(Unit target) => target is Scenery;
        
        [HarmonyPatch(typeof(DetectorManager), nameof(DetectorManager.RequestLoSCheck))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool RequestLoSCheckPrefix(Unit target) => !ShouldIgnoreTarget(target);
        
        [HarmonyPatch(typeof(DetectorManager), nameof(DetectorManager.RequestRadarCheck))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool RequestRadarCheckPrefix(Unit target) => !ShouldIgnoreTarget(target);
        
        [HarmonyPatch(typeof(TargetDetector), nameof(TargetDetector.VisualCheck))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool TargetDetectorVisualCheckPrefix(TargetDetector __instance)
        {
            BattlefieldGrid.GetUnitsInRangeNonAlloc(__instance.scanner.GlobalPosition(), __instance.visualRange,
                __instance.unitsInRange);
            var units = __instance.unitsInRange;
            var detected = __instance.detectedTargets;
            var ownHq = __instance.attachedUnit.NetworkHQ;
            var maxSpeed = __instance.maxSpeed;
            foreach (var unit in units)
            {
                if (unit == null || unit is Scenery || unit.NetworkHQ == ownHq || unit.disabled ||
                    unit.speed > maxSpeed || detected.Contains(unit))
                    continue;
                
                DetectorManager.RequestLoSCheck(__instance, unit);
            }
            
            return false;
        }
    }
}