using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace NO_OPT.Client;

internal static class ClientPatches
{
    internal static void Apply(Harmony harmony)
    {
        RefreshSettings();
        Patch(harmony, typeof(TurretPatches));
        Patch(harmony, typeof(AudioSourcePatches));
        Patch(harmony, typeof(PresentationPatches));
        Patch(harmony, typeof(ClientDynamicMapOptimisation.DynamicMapPatches));
        Patch(harmony, typeof(ClientHudOptimisation.CombatHudPatches));
        Patch(harmony, typeof(BulletPatches));
        Patch(harmony, typeof(ClientObjectiveOptimisation.ObjectivePatches));
    }
    
    internal static void RefreshSettings()
    {
        ClientActivity.RefreshSettings();
        ClientDynamicMapOptimisation.DynamicMapPatches.RefreshSettings();
        ClientHudOptimisation.CombatHudPatches.RefreshSettings();
        ClientObjectiveOptimisation.ObjectivePatches.RefreshSettings();
        ClientVisualBulletFilter.RefreshSettings();
    }
    
    internal static void RestoreRuntimeState()
    {
        ClientDynamicMapOptimisation.DynamicMapPatches.RestoreAll();
        ClientHudOptimisation.CombatHudPatches.RestoreAll();
        ClientObjectiveOptimisation.ObjectivePatches.RestoreAll();
        ClientPresentationManager.RestoreAll();
    }
    
    private static void Patch(Harmony harmony, Type patchType)
    {
        try
        {
            var patched = harmony.CreateClassProcessor(patchType).Patch();
            Plugin.Debug($"Applied {patchType.Name}: {patched?.Count ?? 0} patched method(s).");
        }
        catch (Exception ex)
        {
            Plugin.Debug($"Failed applying {patchType.Name}, continuing with remaining patch groups.\n{ex}",
                Plugin.DebugType.LogError);
        }
    }
    
    [HarmonyPatch]
    internal static class TurretPatches
    {
        // Turret.Turret_OnInitialize() properly restricts TargetAcquisitionMode.DatalinkTargetSearch to server only
        // But Turret.DatalinkTargetSearch() can still call it on client and lower FPS running turret checks like
        // LoS, despite not doing anything further with that information
        [HarmonyPatch(typeof(Turret), nameof(Turret.DatalinkTargetSearch))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool TurretDatalinkTargetSearchPrefix(Turret __instance)
        {
            if (!Plugin.ClientDatalinkTargetSearchOptimisationEnabled.Value)
                return true;
            var unit = __instance.GetAttachedUnit();
            return unit?.IsServer == true;
        }
        
        // Stop animating turrets of sleeping units
        [HarmonyPatch(typeof(Turret), nameof(Turret.FixedUpdate))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool TurretFixedUpdatePrefix(Turret __instance)
        {
            if (!Plugin.ClientPresentationOptimisationEnabled.Value)
                return true;
            
            var unit = __instance.GetAttachedUnit();
            if (unit == null || unit.IsServer || unit.LocalSim)
                return true;
            return !ClientPresentationManager.IsSleeping(unit);
        }
    }
    
    [HarmonyPatch]
    internal static class PresentationPatches
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
    internal static class AudioSourcePatches
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