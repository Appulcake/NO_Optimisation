using HarmonyLib;
using UnityEngine;

namespace NO_OPT.Server;

internal sealed partial class HeadlessServerOptimisations
{
    [HarmonyPatch]
    private static class MiscPatches
    {
        [HarmonyPatch(typeof(JetNozzle.Afterburner), nameof(JetNozzle.Afterburner.Run))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool JetNozzleAfterburnerRunPrefix(JetNozzle.Afterburner __instance, float throttleAmount)
        {
            var b = Mathf.Clamp01((throttleAmount - __instance.throttleStart) /
                                  (__instance.throttleEnd - __instance.throttleStart));
            __instance.afterburnerAmount =
                Mathf.Lerp(__instance.afterburnerAmount, b, __instance.smoothing * Time.deltaTime);
            return false;
        }
        
        [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.StartMission))]
        [HarmonyPostfix]
        private static void MissionManagerStartMissionPostfix()
        {
            DisableExistingAudioSources();
        }
        
        private static void DisableExistingAudioSources()
        {
            AudioListener.pause = true;
            foreach (var source in Object.FindObjectsOfType<AudioSource>(true))
            {
                source.playOnAwake = false;
                source.enabled = false;
            }
        }
        
        [HarmonyPatch(typeof(Lightning), nameof(Lightning.OnEnable))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool LightningOnEnablePrefix(Lightning __instance)
        {
            __instance.enabled = false;
            __instance.lightningSystem?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (__instance.flashLight != null)
                __instance.flashLight.enabled = false;
            return false;
        }
        
        [HarmonyPatch(typeof(BulletSim), nameof(BulletSim.AddBullet))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool SkipUnneededServerVisualBullet(Unit ___owner,
            // ReSharper disable once InconsistentNaming
            bool ___visualOnly, Transform muzzle, Vector3 inheritedVelocity, Unit target)
        {
            if (!___visualOnly || ___owner == null || muzzle == null)
                return true;
            
            var proximityFuse = target != null && target.definition.armorTier < 2f;
            if (proximityFuse)
                return true;
            
            HitValidator.LogFiring(___owner.persistentID, muzzle.position - Datum.origin.position, inheritedVelocity);
            return false;
        }
        
        [HarmonyPatch(typeof(DebugUI), nameof(DebugUI.Start))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool DebugUIStartPrefix(DebugUI __instance)
        {
            __instance.enabled = false;
            __instance.graphy?.SetActive(false);
            __instance.performanceText?.SetActive(false);
            __instance.bandwidthText?.SetActive(false);
            __instance.mission?.SetActive(false);
            if (__instance.canvas != null)
                __instance.canvas.gameObject.SetActive(false);
            return false;
        }
        
        [HarmonyPatch(typeof(ShipPropulsion), nameof(ShipPropulsion.Awake))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void ShipPropulsionAwakePostfix(ShipPropulsion __instance)
        {
            foreach (var particles in __instance.particles)
                particles?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            __instance.particles = [];
            __instance.rotators = [];
            __instance.thrustSound = null;
            __instance.engineSound = null;
        }
        
        [HarmonyPatch(typeof(VLSBooster), nameof(VLSBooster.Awake))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void VLSBoosterAwakePostfix(
            // ReSharper disable once InconsistentNaming
            VLSBooster __instance)
        {
            foreach (var particles in __instance.particleSystems)
                particles?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var light in __instance.lights)
                if (light != null)
                    light.enabled = false;
            __instance.particleSystems = [];
            __instance.trailEmitters = [];
            __instance.audioSources = [];
            __instance.lights = [];
        }
    }
}