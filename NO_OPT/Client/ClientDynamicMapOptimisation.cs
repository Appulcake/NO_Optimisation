using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace NO_OPT.Client;

internal static class ClientDynamicMapOptimisation
{
    [HarmonyPatch]
    internal static class DynamicMapPatches
    {
        private static readonly HashSet<UnitMapIcon> CulledIcons = [];
        private static DynamicMap? _trackedMap;
        private static float _nextMinimisedIconUpdate;
        private static float _overscanRatio = 0.15f;
        
        internal static void RefreshSettings()
        {
            _overscanRatio = Mathf.Max(Plugin.ClientDynamicMapViewportOverscan.Value, 0f) * 0.01f;
        }
        
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.UpdateIcons))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool DynamicMapUpdateIconsPrefix(DynamicMap __instance)
        {
            if (_trackedMap != __instance)
            {
                RestoreAll();
                _trackedMap = __instance;
                _nextMinimisedIconUpdate = 0f;
            }
            
            if (!Plugin.ClientDynamicMapOptimisationEnabled.Value || DynamicMap.mapMaximized)
            {
                RestoreAll();
                return true;
            }
            
            var updatesPerSecond = Mathf.Max(Plugin.ClientMinimisedMapUPS.Value, 1f);
            var now = Time.unscaledTime;
            if (now < _nextMinimisedIconUpdate)
                return false;
            
            _nextMinimisedIconUpdate = now + 1f / updatesPerSecond;
            if (!Plugin.ClientDynamicMapViewportCullingEnabled.Value)
            {
                RestoreAll();
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
                RestoreAll();
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
            RestoreAll();
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
        
        internal static void RestoreAll()
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
    }
}