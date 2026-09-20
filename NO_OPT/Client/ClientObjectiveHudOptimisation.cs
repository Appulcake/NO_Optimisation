using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using NO_OPT.Modules;
using UnityEngine;

namespace NO_OPT.Client;

[OptimisationModule(ModuleScope.Client)]
internal sealed class ClientObjectiveHudOptimisation : OptimisationModule
{
    private static ConfigEntry<bool> _showMarkers = null!;
    private static ConfigEntry<float> _updateRate = null!;
    
    private static bool _showHudObjectives;
    private static float _hudObjectiveDataInterval;
    private static ObjectiveOverlayManager? _trackedHudManager;
    private static float _nextHudDataUpdate;
    private static bool _hudObjectivesForcedHidden;
    
    protected override void Configure()
    {
        _showMarkers = Bind("Client - HUD", "Show Objective HUD Markers", true);
        _updateRate = Bind("Client - HUD", "Objective HUD Update Rate", 2f,
            "Same as the one for Map Update Rate, but for markers on the HUD. 0 = full rate / vanilla.");
        
        Watch(_showMarkers, RefreshSettings);
        Watch(_updateRate, RefreshSettings);
        RefreshSettings();
    }
    
    protected override void OnDisable()
    {
        RestoreAll();
    }
    
    private static void RefreshSettings()
    {
        _showHudObjectives = _showMarkers.Value;
        _hudObjectiveDataInterval = ToInterval(_updateRate.Value);
        _nextHudDataUpdate = 0f;
    }
    
    private static void RestoreAll()
    {
        if (_trackedHudManager != null && _hudObjectivesForcedHidden && _trackedHudManager.aircraft != null &&
            _trackedHudManager.aircraft.NetworkHQ != null && MissionManager.Runner != null)
            _trackedHudManager.UpdateOverlays();
        _trackedHudManager = null;
        _nextHudDataUpdate = 0f;
        _hudObjectivesForcedHidden = false;
    }
    
    private static void HideHudObjectivesOnce(ObjectiveOverlayManager manager)
    {
        if (_hudObjectivesForcedHidden)
            return;
        
        foreach (var overlay in manager.overlays)
            overlay?.HideOverlay();
        _hudObjectivesForcedHidden = true;
    }
    
    private static void TrackHudManager(ObjectiveOverlayManager manager)
    {
        if (_trackedHudManager == manager)
            return;
        
        _trackedHudManager = manager;
        _nextHudDataUpdate = 0f;
        _hudObjectivesForcedHidden = false;
    }
    
    private static void FastUpdateHudObjectives(ObjectiveOverlayManager manager)
    {
        var overlays = manager.overlays;
        var results = manager.resultCache;
        var count = Mathf.Min(overlays.Count, results.Count);
        for (var i = 0; i < count; i++)
        {
            var overlay = overlays[i];
            if (overlay == null)
                continue;
            
            FastUpdateObjectiveOverlay(overlay, results[i]);
        }
    }
    
    // Stripped down ObjectiveOverlay.UpdateOverlay() without the heavy work, just the HUD positioning
    // so that this light method can be called at regular FPS to keep HUD element smooth
    private static void FastUpdateObjectiveOverlay(ObjectiveOverlay overlay, MissionPosition.PositionResult result)
    {
        var cameraState = SceneSingleton<CameraStateManager>.i;
        var mainCamera = cameraState?.mainCamera;
        if (cameraState == null || mainCamera == null)
            return;
        
        var objectivePointer = overlay.objectivePointer;
        var objectiveDot = overlay.objectiveDot;
        var objectiveInfo = overlay.objectiveInfo;
        var sizeIndicator = overlay.sizeIndicator;
        if (!objectiveInfo.enabled)
            objectiveInfo.enabled = true;
#pragma warning disable Harmony003
        var screenPosition = mainCamera.WorldToScreenPoint(result.Position.ToLocalPosition());
        var screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        var halfScreen = screenCenter;
        screenPosition -= screenCenter;
        if (screenPosition.z < 0f)
            screenPosition *= -1f;
        screenPosition.z = 0f;
        var pointerAngle = Mathf.Atan2(screenPosition.y, screenPosition.x);
        var tangent = Mathf.Tan(pointerAngle);
        var directionAngle = Vector3.Angle(cameraState.transform.forward, result.Direction);
        var offScreen = directionAngle > 90f || Mathf.Abs(screenPosition.x) > Screen.width * 0.5f ||
                        Mathf.Abs(screenPosition.y) > Screen.height * 0.5f;
        if (offScreen)
        {
            screenPosition = screenPosition.x > 0f
                ? new Vector3(halfScreen.x, halfScreen.x * tangent, 0f)
                : new Vector3(-halfScreen.x, -halfScreen.x * tangent, 0f);
            if (screenPosition.y > halfScreen.y)
                screenPosition = new Vector3(halfScreen.y / tangent, halfScreen.y, 0f);
            else if (screenPosition.y < -halfScreen.y)
                screenPosition = new Vector3(-halfScreen.y / tangent, -halfScreen.y, 0f);
            if (sizeIndicator.enabled)
                sizeIndicator.enabled = false;
        }
        else if (!sizeIndicator.enabled)
        {
            sizeIndicator.enabled = true;
        }
        
        screenPosition += screenCenter;
        objectivePointer.transform.position = screenPosition;
        objectiveDot.transform.position = screenPosition;
        objectivePointer.transform.localEulerAngles = new Vector3(0f, 0f, pointerAngle * Mathf.Rad2Deg - 90f);
        if (directionAngle > 10f)
        {
            if (!objectivePointer.enabled)
                objectivePointer.enabled = true;
            if (objectiveDot.enabled)
                objectiveDot.enabled = false;
            overlay.TextNoOverlap.SetTarget(overlay.pointerTail.position);
        }
        else
        {
            if (objectivePointer.enabled)
                objectivePointer.enabled = false;
            if (!objectiveDot.enabled)
                objectiveDot.enabled = true;
            overlay.TextNoOverlap.SetTarget(objectiveDot.transform.position - Vector3.up * 25f);
        }
        
        var range = result.Range ?? 0f;
        var inverseDistance = 1f / (result.Distance != 0f ? result.Distance : 0.01f);
#pragma warning restore Harmony003
        var canvasRect = (RectTransform)sizeIndicator.canvas.transform;
        var indicatorHeight = sizeIndicator.rectTransform.rect.height;
        var fovScale =
            canvasRect.rect.height / indicatorHeight / Mathf.Tan(mainCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        sizeIndicator.transform.localScale = Vector3.one * (fovScale * range * inverseDistance);
        sizeIndicator.transform.position = objectivePointer.transform.position;
        sizeIndicator.transform.eulerAngles = new Vector3(0f, 0f, -mainCamera.transform.eulerAngles.z);
        var alpha = Mathf.Clamp01(range * 20f * inverseDistance - 0.5f);
        var currentColor = sizeIndicator.color;
        if (Mathf.Abs(currentColor.a - alpha) > 0.001f)
            sizeIndicator.color = currentColor.WithAlpha(alpha);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ToInterval(float updatesPerSecond) => updatesPerSecond > 0f ? 1f / updatesPerSecond : 0f;
    
    [HarmonyPatch]
    private static class ObjectivePatches
    {
        [HarmonyPatch(typeof(ObjectiveOverlayManager), nameof(ObjectiveOverlayManager.Update))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool ObjectiveOverlayManagerUpdatePrefix(ObjectiveOverlayManager __instance)
        {
            TrackHudManager(__instance);
            if (!_showHudObjectives)
            {
                HideHudObjectivesOnce(__instance);
                return false;
            }
            
            if (_hudObjectivesForcedHidden)
            {
                _hudObjectivesForcedHidden = false;
                _nextHudDataUpdate = 0f;
            }
            
            if (!ClientHudOptimisation.IsActive || _hudObjectiveDataInterval <= 0f)
                return true;
            
            if (__instance.aircraft == null || __instance.aircraft.NetworkHQ == null || MissionManager.Runner == null)
                return false;
            
            var now = Time.unscaledTime;
            if (now >= _nextHudDataUpdate)
            {
                __instance.UpdateOverlays();
                _nextHudDataUpdate = now + _hudObjectiveDataInterval;
            }
            else
            {
                FastUpdateHudObjectives(__instance);
            }
            
            if (__instance.resultCache.Count > 0)
                __instance.StopTextOverlap();
            return false;
        }
    }
}