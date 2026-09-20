using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using NO_OPT.Modules;
using UnityEngine;
using Random = UnityEngine.Random;

namespace NO_OPT.Client;

[OptimisationModule(ModuleScope.Client, "--- Client ---", "2. HUD Marker Optimisation")]
internal sealed class ClientHudOptimisation : OptimisationModule
{
    private static ConfigEntry<float> _reducedUps = null!;
    private static ConfigEntry<float> _farUps = null!;
    private static ConfigEntry<float> _strategicUps = null!;
    private static ConfigEntry<float> _cullDistance = null!;
    private static ConfigEntry<float> _neutralCullDistance = null!;
    
    internal static bool IsActive { get; private set; }
    
    protected override void Configure()
    {
        _reducedUps = Bind("--- Client - HUD ---", "1. Reduced Marker Update Rate", 30f,
            "HUD marker updates per second in Reduced Fidelity. 0 = full rate.");
        _farUps = Bind("--- Client - HUD ---", "2. Far Marker Update Rate", 15f,
            "HUD marker updates per second in Far fidelity. 0 = full rate.");
        _strategicUps = Bind("--- Client - HUD ---", "3. Strategic Marker Update Rate", 6f,
            "HUD marker updates per second in Strategic fidelity. 0 = full rate.");
        _cullDistance = Bind("--- Client - HUD ---", "4. HUD Marker Hide Distance", 0f,
            "Hide all unit HUD markers beyond this distance. Can help with performance, but is mainly " +
            "subjectively nicer looking if you want less clutter. 0 disables distance hiding.");
        _neutralCullDistance = Bind("--- Client - HUD ---", "4. HUD Neutral Marker Hide Distance", 5000f,
            "Hide neutral unit HUD markers beyond this distance. The normal HUD Marker Hide Distance still " +
            "applies to all markers. 0 disables this separate neutral hiding.");
        
        Watch(_reducedUps, CombatHudPatches.RefreshSettings);
        Watch(_farUps, CombatHudPatches.RefreshSettings);
        Watch(_strategicUps, CombatHudPatches.RefreshSettings);
        Watch(_cullDistance, CombatHudPatches.RefreshSettings);
        Watch(_neutralCullDistance, CombatHudPatches.RefreshSettings);
        CombatHudPatches.RefreshSettings();
    }
    
    protected override void OnEnable()
    {
        IsActive = true;
    }
    
    protected override void OnDisable()
    {
        IsActive = false;
        CombatHudPatches.RestoreAll();
    }
    
    [HarmonyPatch]
    private static class CombatHudPatches
    {
        private const float TierRefreshInterval = 0.25f;
        private const float MaxCullWakeHysteresis = 500f;
        private static MarkerScheduleState[] _states = [];
        private static float _reducedInterval;
        private static float _farInterval;
        private static float _strategicInterval;
        private static float _cullDistanceSq;
        private static float _cullWakeDistanceSq;
        private static float _neutralCullDistanceSq;
        private static float _neutralCullWakeDistanceSq;
        
        internal static void RefreshSettings()
        {
            _reducedInterval = ToInterval(_reducedUps.Value);
            _farInterval = ToInterval(_farUps.Value);
            _strategicInterval = ToInterval(_strategicUps.Value);
            CacheCullDistance(_cullDistance.Value, out _cullDistanceSq, out _cullWakeDistanceSq);
            CacheCullDistance(_neutralCullDistance.Value, out _neutralCullDistanceSq, out _neutralCullWakeDistanceSq);
            if (_cullDistanceSq <= 0f && _neutralCullDistanceSq <= 0f)
                RestoreCulledMarkers();
        }
        
        private static void CacheCullDistance(float configuredDistance, out float cullDistanceSq,
            out float wakeDistanceSq)
        {
            var distance = Mathf.Max(configuredDistance, 0f);
            if (distance <= 0f)
            {
                cullDistanceSq = 0f;
                wakeDistanceSq = 0f;
                return;
            }
            
            cullDistanceSq = distance * distance;
            var hysteresis = Mathf.Min(MaxCullWakeHysteresis, distance * 0.1f);
            var wakeDistance = Mathf.Max(distance - hysteresis, 0f);
            wakeDistanceSq = wakeDistance * wakeDistance;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ToInterval(float updatesPerSecond) => updatesPerSecond > 0f ? 1f / updatesPerSecond : 0f;
        
        [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.UpdateMarkers))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool CombatHUDUpdateMarkersPrefix(CombatHUD __instance)
        {
            var aircraft = __instance.aircraft;
            var cameraState = SceneSingleton<CameraStateManager>.i;
            if (aircraft == null || cameraState == null || cameraState.mainCamera == null)
                return true;
            
            var networkHq = aircraft.NetworkHQ;
            if (networkHq == null)
                return true;
            
            var cameraTransform = cameraState.transform;
            var mainCamera = cameraState.mainCamera;
            var viewPosition = cameraTransform.GlobalPosition();
            var cameraPosition = cameraTransform.position;
            var cameraForward = cameraTransform.forward;
            var followedUnit = cameraState.followingUnit;
            var now = Time.unscaledTime;
            UpdateJammingAudio(__instance);
            var jamAcc = __instance.jamAccumulation;
            var markers = __instance.markers;
            var count = markers?.Count ?? 0;
            if (markers != null && count > 0)
            {
                EnsureStateCapacity(count);
                for (var i = 0; i < count; i++)
                {
                    var marker = markers[i];
                    if (marker?.unit == null || marker.image == null)
                        continue;
                    
                    ref var state = ref _states[i];
                    var isNew = state.Marker != marker;
                    if (isNew)
                        InitialiseState(ref state, marker, i, now, cameraPosition, followedUnit);
                    else
                        RefreshTierAndCull(ref state, marker, now, cameraPosition, followedUnit, false);
                    
                    if (state.Culled || marker.hidden)
                        continue;
                    
                    if (!ShouldRunUpdate(marker, ref state, isNew, now))
                    {
                        FastReproject(marker, viewPosition, cameraForward, mainCamera, ref state);
                        if (jamAcc > 0f) marker.JammingDistortion(jamAcc);
                        VanillaIconsPlusCompat.SyncVisual(marker, ref state.VanillaIconsPlus);
                        continue;
                    }
                    
                    marker.UpdatePosition(networkHq, viewPosition, cameraForward);
                    RefreshKnownPosition(marker, networkHq, ref state);
                    VanillaIconsPlusCompat.CaptureState(marker, ref state.VanillaIconsPlus);
                    if (jamAcc > 0f) marker.JammingDistortion(jamAcc);
                    VanillaIconsPlusCompat.SyncVisual(marker, ref state.VanillaIconsPlus);
                    ScheduleNextUpdate(ref state, isNew, now);
                }
                
                UpdateOneMarkerVisibility(__instance, markers, count, networkHq, viewPosition);
            }
            
            __instance.jamAccumulation = Mathf.Clamp01(jamAcc - Mathf.Max(jamAcc, 0.25f) * Time.deltaTime);
            return false;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FastReproject(HUDUnitMarker marker, GlobalPosition viewPosition, Vector3 cameraForward,
            Camera camera, ref MarkerScheduleState state)
        {
            GlobalPosition position;
            if (!marker.outdated)
            {
                position = marker.unit.GlobalPosition();
            }
            else
            {
                if (!state.HasKnownPosition)
                    return;
                
                position = state.KnownPosition;
            }
            
            if (Vector3.Dot(position - viewPosition, cameraForward) < 0f)
            {
                if (marker.image.enabled)
                    marker.image.enabled = false;
                return;
            }
            
            if (!marker.image.enabled)
                marker.image.enabled = true;
            var markerTransform = state.MarkerTransform;
            if (markerTransform == null)
                return;
            
            markerTransform.position = Vector3.Scale(camera.WorldToScreenPoint(position.ToLocalPosition()),
                new Vector3(1f, 1f, 0f));
        }
        
        private static void RefreshKnownPosition(HUDUnitMarker marker, FactionHQ hq, ref MarkerScheduleState state)
        {
            if (!marker.outdated)
            {
                state.KnownPosition = marker.unit.GlobalPosition();
                state.HasKnownPosition = true;
                return;
            }
            
            if (!hq.TryGetKnownPosition(marker.unit, out var knownPosition))
                return;
            
            state.KnownPosition = knownPosition;
            state.HasKnownPosition = true;
        }
        
        private static void InitialiseState(ref MarkerScheduleState state, HUDUnitMarker marker, int index, float now,
            Vector3 cameraPosition, Unit? followedUnit)
        {
            if (state.Marker != null && state.Marker != marker)
                RestoreMarker(ref state);
            
            state = default;
            state.Marker = marker;
            state.MarkerTransform = marker.image.transform;
            state.Phase = GetPhase(index);
            RefreshTierAndCull(ref state, marker, now, cameraPosition, followedUnit, true);
            var interval = GetInterval(state.Tier);
            state.NextUpdate = interval > 0f ? now + interval * (0.5f + state.Phase * 0.5f) : 0f;
        }
        
        private static void RefreshTierAndCull(ref MarkerScheduleState state, HUDUnitMarker marker, float now,
            Vector3 cameraPosition, Unit? followedUnit, bool force)
        {
            if (marker.selected || marker.unit == followedUnit)
            {
                if (state.Culled)
                    RestoreMarker(ref state);
                if (state.Tier != ClientActivityTier.Full)
                {
                    state.Tier = ClientActivityTier.Full;
                    state.NextUpdate = 0f;
                }
                
                state.NextTierRefresh = now + TierRefreshInterval;
                return;
            }
            
            if (!force && now < state.NextTierRefresh)
                return;
            
            state.IsNeutral = marker.unit.NetworkHQ == null;
            var delta = marker.unit.transform.position - cameraPosition;
            var distanceSq = delta.sqrMagnitude;
            UpdateCullState(marker, ref state, distanceSq);
            var newTier = ClientActivity.GetTierFromDistanceSq(distanceSq);
            if (newTier != state.Tier)
            {
                state.Tier = newTier;
                state.NextUpdate = 0f;
            }
            
            state.NextTierRefresh = now + TierRefreshInterval;
        }
        
        private static void UpdateCullState(HUDUnitMarker marker, ref MarkerScheduleState state, float distanceSq)
        {
            var cullDistanceSq = _cullDistanceSq;
            var wakeDistanceSq = _cullWakeDistanceSq;
            if (state.IsNeutral && _neutralCullDistanceSq > 0f &&
                (cullDistanceSq <= 0f || _neutralCullDistanceSq < cullDistanceSq))
            {
                cullDistanceSq = _neutralCullDistanceSq;
                wakeDistanceSq = _neutralCullWakeDistanceSq;
            }
            
            if (cullDistanceSq <= 0f)
            {
                if (state.Culled)
                    RestoreMarker(ref state);
                return;
            }
            
            var thresholdSq = state.Culled ? wakeDistanceSq : cullDistanceSq;
            var shouldCull = distanceSq >= thresholdSq;
            if (shouldCull == state.Culled)
                return;
            
            if (shouldCull)
            {
                state.Culled = true;
                marker.image.enabled = false;
                if (marker.image.gameObject.activeSelf)
                    marker.image.gameObject.SetActive(false);
                VanillaIconsPlusCompat.Hide(ref state.VanillaIconsPlus);
                return;
            }
            
            RestoreMarker(ref state);
        }
        
        private static void RestoreMarker(ref MarkerScheduleState state)
        {
            var marker = state.Marker;
            if (marker?.image != null && !marker.image.gameObject.activeSelf)
                marker.image.gameObject.SetActive(true);
            VanillaIconsPlusCompat.Restore(ref state.VanillaIconsPlus);
            state.Culled = false;
            state.NextUpdate = 0f;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldRunUpdate(HUDUnitMarker marker, ref MarkerScheduleState state, bool isNew, float now)
        {
            if (isNew || marker.fresh || marker.flashing || state.Tier == ClientActivityTier.Full)
                return true;
            
            var interval = GetInterval(state.Tier);
            if (interval <= 0f)
                return true;
            
            return now >= state.NextUpdate;
        }
        
        private static void ScheduleNextUpdate(ref MarkerScheduleState state, bool isNew, float now)
        {
            var interval = GetInterval(state.Tier);
            if (interval <= 0f)
            {
                state.NextUpdate = 0f;
                return;
            }
            
            if (isNew)
                return;
            
            state.NextUpdate = now + interval;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetInterval(ClientActivityTier tier)
        {
            return tier switch
            {
                ClientActivityTier.Reduced => _reducedInterval,
                ClientActivityTier.Far => _farInterval,
                ClientActivityTier.Strategic => _strategicInterval,
                _ => 0f
            };
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetPhase(int index)
        {
            unchecked
            {
                var x = (uint)(index + 1) * 2654435761u;
                return
                    (x & 0xFFFFu) * (1f / 65536f);
            }
        }
        
        private static void EnsureStateCapacity(int count)
        {
            if (_states.Length >= count)
                return;
            
            var newSize = Mathf.NextPowerOfTwo(Mathf.Max(count, 32));
            Array.Resize(ref _states, newSize);
        }
        
        private static void UpdateOneMarkerVisibility(CombatHUD hud, List<HUDUnitMarker> markers, int count,
            FactionHQ networkHq, GlobalPosition viewPosition)
        {
            if (hud.iconIndex >= count)
                hud.iconIndex = 0;
            var index = hud.iconIndex++;
            var marker = markers[index];
            if (marker == null)
                return;
            
            if (index < _states.Length)
            {
                ref var state = ref _states[index];
                if (state.Marker == marker && state.Culled)
                    return;
            }
            
            marker.UpdateVisibility(networkHq, viewPosition);
        }
        
        private static void UpdateJammingAudio(CombatHUD hud)
        {
            var jamAcc = hud.jamAccumulation;
            if (hud.jammedSource == null)
            {
                if (jamAcc <= 0f)
                    return;
                
                hud.jammedSource = hud.gameObject.AddComponent<AudioSource>();
                hud.jammedSource.spatialBlend = 0f;
                hud.jammedSource.loop = true;
                hud.jammedSource.dopplerLevel = 0f;
                hud.jammedSource.outputAudioMixerGroup = SoundManager.i.JammedNoiseMixer;
                hud.jammedSource.clip = hud.jammedSound;
                hud.jammedSource.volume = 0f;
                hud.jammedSource.Play();
                return;
            }
            
            if (jamAcc == 0f)
                hud.jammedSource.Stop();
            if (jamAcc <= 0f)
                return;
            
            hud.jammedSource.volume = FastMath.SmoothDamp(hud.jammedSource.volume, jamAcc,
                ref hud.smoothVel, 0.5f) * hud.jammedVolumeMultiplier;
            if (hud.jammedSource.isPlaying)
                return;
            
            hud.jammedSource.Play();
            hud.jammedSource.time = Random.Range(0f, hud.jammedSound.length);
        }
        
        private static void RestoreCulledMarkers()
        {
            for (var i = 0; i < _states.Length; i++)
            {
                ref var state = ref _states[i];
                if (state.Culled)
                    RestoreMarker(ref state);
            }
        }
        
        internal static void RestoreAll()
        {
            RestoreCulledMarkers();
            _states = [];
        }
        
        private struct MarkerScheduleState
        {
            internal HUDUnitMarker? Marker;
            internal Transform? MarkerTransform;
            internal ClientActivityTier Tier;
            internal float NextTierRefresh;
            internal float NextUpdate;
            internal float Phase;
            internal GlobalPosition KnownPosition;
            internal bool HasKnownPosition;
            internal bool Culled;
            internal bool IsNeutral;
            internal VanillaIconsPlusHudState VanillaIconsPlus;
        }
    }
}