using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using NO_OPT.Modules;
using UnityEngine;

namespace NO_OPT.Client;

[OptimisationModule(ModuleScope.Client, "--- Client ---", "3. Dynamic Map Optimisation")]
internal sealed class ClientDynamicMapOptimisation : OptimisationModule
{
    private static ConfigEntry<float> _minimisedMapUps = null!;
    private static ConfigEntry<bool> _viewportCullingEnabled = null!;
    private static ConfigEntry<float> _viewportOverscan = null!;
    private static ConfigEntry<float> _objectiveMapUps = null!;
    
    private static float _minimisedMapInterval;
    private static bool _viewportCulling;
    private static float _overscanRatio;
    private static float _mapObjectiveInterval;
    private static float _nextMapObjectiveUpdate;
    private static float _lastMapScale = float.NaN;
    private static ObjectiveMarkerManager? _trackedMapManager;
    private static readonly HashSet<UnitMapIcon> CulledIcons = [];
    private static DynamicMap? _trackedMap;
    private static float _nextMinimisedIconUpdate;
    
    protected override void Configure()
    {
        _minimisedMapUps = Bind("--- Client ---", "3. Dynamic Map Update Rate", 60f);
        _viewportCullingEnabled = Bind("--- Client - Map ---", "0. Minimised Map Culling", true,
            "Hide unit map icons outside the visible minimap area " +
            "(vanilla normally redundantly keeps rendering icons that are off screen on the minimap).");
        _viewportOverscan = Bind("--- Client - Map ---", "1. Minimised Map Culling Extra Margin", 15f,
            "Extra percentage outside the visible minimap where units are still rendered to prevent a late icon pop in.");
        _objectiveMapUps = Bind("--- Client - Map ---", "2. Objective Map Update Rate", 2f,
            "Map objective marker update refresh speed (per second). Since map objectives don't tend to move a lot, " +
            "reducing how often their marker updates is essentially free performance savings. 0 = full rate / vanilla.");
        
        Watch(_minimisedMapUps, RefreshSettings);
        Watch(_viewportCullingEnabled, RefreshSettings);
        Watch(_viewportOverscan, RefreshSettings);
        Watch(_objectiveMapUps, RefreshSettings);
        RefreshSettings();
    }
    
    private static void RefreshSettings()
    {
        var mapUps = Mathf.Max(_minimisedMapUps.Value, 1f);
        _minimisedMapInterval = 1f / mapUps;
        _viewportCulling = _viewportCullingEnabled.Value;
        _overscanRatio = Mathf.Max(_viewportOverscan.Value, 0f) * 0.01f;
        _mapObjectiveInterval = ToInterval(_objectiveMapUps.Value);
        _nextMinimisedIconUpdate = 0f;
        _nextMapObjectiveUpdate = 0f;
        if (!_viewportCulling)
            RestoreCulledIcons();
    }
    
    private static void RestoreCulledIcons()
    {
        if (CulledIcons.Count == 0)
            return;
        
        var mapOptions = SceneSingleton<MapOptions>.i;
        foreach (var icon in CulledIcons)
        {
            if (icon == null)
                continue;
            
            if (icon.unit is PilotDismounted && mapOptions != null && !mapOptions.showPilotIcons)
                continue;
            
            if (!icon.gameObject.activeSelf)
                icon.gameObject.SetActive(true);
        }
        
        CulledIcons.Clear();
    }
    
    protected override void OnDisable()
    {
        RestoreCulledIcons();
        _trackedMap = null;
        _nextMinimisedIconUpdate = 0f;
        _trackedMapManager = null;
        _nextMapObjectiveUpdate = 0f;
        _lastMapScale = float.NaN;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ToInterval(float updatesPerSecond) => updatesPerSecond > 0f ? 1f / updatesPerSecond : 0f;
    
    [HarmonyPatch]
    private static class DynamicMapPatches
    {
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.UpdateIcons))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool DynamicMapUpdateIconsPrefix(DynamicMap __instance)
        {
            if (_trackedMap != __instance)
            {
                RestoreCulledIcons();
                _trackedMap = __instance;
                _nextMinimisedIconUpdate = 0f;
            }
            
            if (DynamicMap.mapMaximized)
            {
                RestoreCulledIcons();
                return true;
            }
            
            var now = Time.unscaledTime;
            if (now < _nextMinimisedIconUpdate)
                return false;
            
            _nextMinimisedIconUpdate = now + _minimisedMapInterval;
            if (!_viewportCulling)
            {
                RestoreCulledIcons();
                return true;
            }
            
            var icons = __instance.mapIcons;
            var count = icons?.Count ?? 0;
            if (icons == null || count == 0)
            {
                __instance.iconIndex = 0;
                return false;
            }
            
            // Vanilla staggers 20% of map icons per update
            var batchSize = Mathf.Max(1, (int)(count * 0.2f));
            if (__instance.iconIndex < 0 || __instance.iconIndex >= count)
                __instance.iconIndex = 0;
            var start = __instance.iconIndex;
            var end = Mathf.Min(start + batchSize, count);
            var mapTransform = __instance.mapImage.transform;
            var mapInverseScale = 1f / mapTransform.localScale.x;
            var viewport = __instance.transform as RectTransform;
            if (viewport == null)
            {
                RestoreCulledIcons();
                return true;
            }
            
            var viewportRect = viewport.rect;
            var worldToViewport = viewport.worldToLocalMatrix;
            var marginX = viewportRect.width * _overscanRatio;
            var marginY = viewportRect.height * _overscanRatio;
            for (var i = start; i < end; i++)
            {
                var icon = icons[i];
                if (icon == null)
                    continue;
                
                icon.UpdateIcon(__instance.mapDisplayFactor, mapInverseScale, mapTransform, false);
                if (icon is not UnitMapIcon unitIcon)
                    continue;
                
                UpdateViewportState(unitIcon, worldToViewport, viewportRect, marginX, marginY);
            }
            
            __instance.iconIndex = end < count ? end : 0;
            // Vanilla map jamming for active icons
            var hud = SceneSingleton<CombatHUD>.i;
            if (hud?.aircraft != null && !hud.aircraft.disabled && hud.jamAccumulation > 0f)
            {
                var jam = hud.jamAccumulation;
                for (var i = 0; i < count; i++)
                {
                    if (icons[i] is not UnitMapIcon unitIcon || !unitIcon.gameObject.activeInHierarchy)
                        continue;
                    
                    unitIcon.JammingDistortion(jam);
                }
            }
            
            return false;
        }
        
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Maximize))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void DynamicMapMaximizePrefix()
        {
            RestoreCulledIcons();
        }
        
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Minimize))]
        [HarmonyPostfix]
        private static void DynamicMapMinimizePostfix()
        {
            _nextMinimisedIconUpdate = 0f;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateViewportState(UnitMapIcon icon, Matrix4x4 worldToViewport, Rect viewportRect,
            float marginX, float marginY)
        {
            if (icon.iconImage == null)
                return;
            
            var mapOptions = SceneSingleton<MapOptions>.i;
            if (icon.unit is PilotDismounted && mapOptions != null && !mapOptions.showPilotIcons)
            {
                CulledIcons.Remove(icon);
                if (icon.gameObject.activeSelf)
                    icon.gameObject.SetActive(false);
                return;
            }
            
#pragma warning disable Harmony003
            var local = worldToViewport.MultiplyPoint3x4(icon.iconImage.rectTransform.position);
            var visible = local.x >= viewportRect.xMin - marginX && local.x <= viewportRect.xMax + marginX &&
                          local.y >= viewportRect.yMin - marginY && local.y <= viewportRect.yMax + marginY;
#pragma warning restore Harmony003
            
            if (visible)
            {
                if (CulledIcons.Remove(icon) && !icon.gameObject.activeSelf)
                    icon.gameObject.SetActive(true);
                return;
            }
            
            CulledIcons.Add(icon);
            if (icon.gameObject.activeSelf)
                icon.gameObject.SetActive(false);
        }
    }
    
    [HarmonyPatch]
    private static class ObjectiveMapPatches
    {
        // This is called from both StartSlowUpdateDelayed() and Initialize() in ObjectiveMarkerManager for some reason
        [HarmonyPatch(typeof(ObjectiveMarkerManager), nameof(ObjectiveMarkerManager.UpdateObjectiveMarkers))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool UpdateObjectiveMarkersPrefix(ObjectiveMarkerManager __instance)
        {
            if (_mapObjectiveInterval <= 0f)
                return true;
            
            if (_trackedMapManager != __instance)
            {
                _trackedMapManager = __instance;
                _nextMapObjectiveUpdate = 0f;
                _lastMapScale = float.NaN;
            }
            
            var now = Time.unscaledTime;
            if (now < _nextMapObjectiveUpdate)
                return false;
            
            _nextMapObjectiveUpdate = now + _mapObjectiveInterval;
            return true;
        }
        
        [HarmonyPatch(typeof(ObjectiveMarkerManager), nameof(ObjectiveMarkerManager.Update))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void ObjectiveMarkerManagerUpdatePostfix(ObjectiveMarkerManager __instance)
        {
            if (_mapObjectiveInterval <= 0f)
                return;
            
            var map = SceneSingleton<DynamicMap>.i;
            if (map?.mapImage == null)
                return;
            
            var mapScale = map.mapImage.transform.localScale.x;
            if (mapScale <= 0f || Mathf.Approximately(mapScale, _lastMapScale))
                return;
            
            _lastMapScale = mapScale;
            var markerScale = Vector3.one * (1f / mapScale);
            foreach (var marker in __instance.objectiveMarkers)
            {
                if (marker == null || !marker.shown)
                    continue;
                
                marker.transform.localScale = markerScale;
            }
        }
    }
}