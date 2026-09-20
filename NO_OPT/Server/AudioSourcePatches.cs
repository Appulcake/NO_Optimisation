using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace NO_OPT.Server;

internal sealed partial class HeadlessServerOptimisations
{
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
            yield return AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayOneShot),
                [typeof(AudioClip), typeof(float)]);
        }
        
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool BlockAudioPlayback(AudioSource __instance)
        {
            __instance.playOnAwake = false;
            __instance.enabled = false;
            return false;
        }
    }
}