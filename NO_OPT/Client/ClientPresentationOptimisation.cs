using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using NO_OPT.Modules;
using UnityEngine;

namespace NO_OPT.Client;

[OptimisationModule(ModuleScope.Client, "Client", "Distant Unit Rendering Optimisation")]
internal sealed class ClientPresentationOptimisation : OptimisationModule
{
    protected override void OnDisable()
    {
        ClientPresentationManager.RestoreAll();
    }
    
    [HarmonyPatch]
    private static class TurretPatches
    {
        // Stop animating turrets of sleeping units
        [HarmonyPatch(typeof(Turret), nameof(Turret.FixedUpdate))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool TurretFixedUpdatePrefix(Turret __instance)
        {
            var unit = __instance.GetAttachedUnit();
            if (unit == null || unit.IsServer || unit.LocalSim)
                return true;
            
            return !ClientPresentationManager.IsSleeping(unit);
        }
    }
    
    [HarmonyPatch]
    private static class PresentationPatches
    {
        [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.LateUpdate))]
        [HarmonyPostfix]
        private static void CameraStateManagerLateUpdatePostfix()
        {
            ClientPresentationManager.Tick();
        }
        
        [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SetFollowingUnit))]
        [HarmonyPrefix]
        private static void CameraStateManagerSetFollowingUnitPrefix(Unit unit)
        {
            if (unit != null)
                ClientPresentationManager.Wake(unit);
        }
    }
    
    [HarmonyPatch]
    private static class AudioSourcePatches
    {
        [HarmonyTargetMethods]
        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(AudioSource), nameof(AudioSource.Play), Type.EmptyTypes);
            yield return AccessTools.Method(typeof(AudioSource), nameof(AudioSource.Play), [typeof(ulong)]);
            yield return AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayDelayed), [typeof(float)]);
            yield return AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayScheduled), [typeof(double)]);
            yield return AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayOneShot), [typeof(AudioClip)]);
            yield return AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayOneShot),
                [typeof(AudioClip), typeof(float)]);
        }
        
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool AudioPlaybackPrefix(AudioSource __instance) =>
            !ClientPresentationManager.ShouldBlockAudioPlayback(__instance);
    }
}