using HarmonyLib;
using UnityEngine;

namespace NO_OPT.Client;

internal sealed class ClientUiDiagnostics : MonoBehaviour
{
    private bool _combatHudIconLayerForcedOff;
    private bool _combatHudUpdatesForcedOff;
    private bool _dynamicMapForcedOff;
    private bool _dynamicMapUpdatesForcedOff;
    private bool _flightHudForcedOff;
    private CanvasState[] _hudCanvasStates = [];
    private bool _hudCanvasesForcedOff;
    private CanvasState[] _mapCanvasStates = [];
    private bool _mapCanvasesForcedOff;
    private bool _mapIconLayerForcedOff;
    private ObjectiveMarkerManager? _objectiveMarkerManager;
    private bool _objectiveMarkerUpdatesForcedOff;
    private ObjectiveOverlayManager? _objectiveOverlayManager;
    private bool _objectiveOverlayUpdatesForcedOff;
    private bool _savedCombatHudEnabled;
    private bool _savedCombatHudIconLayerActive;
    private bool _savedDynamicMapActive;
    private bool _savedDynamicMapEnabled;
    private bool _savedFlightHudActive;
    private bool _savedMapIconLayerActive;
    private bool _savedObjectiveMarkerEnabled;
    private bool _savedObjectiveOverlayEnabled;
    
    private void Update()
    {
        if (!Plugin.ClientDebugHotkeys.Value) return;
        
        if (Input.GetKeyDown(KeyCode.F1))
            ToggleMapIconLayer();
        
        if (Input.GetKeyDown(KeyCode.F2))
            ToggleDynamicMapUpdates();
        
        if (Input.GetKeyDown(KeyCode.F3))
            ToggleCombatHudIconLayer();
        
        if (Input.GetKeyDown(KeyCode.F4))
            ToggleCombatHudUpdates();
        
        if (Input.GetKeyDown(KeyCode.F5))
            ToggleObjectiveMarkerUpdates();
        
        if (Input.GetKeyDown(KeyCode.F6))
            ToggleObjectiveOverlayUpdates();
        
        if (Input.GetKeyDown(KeyCode.F7))
            ToggleHudCanvases();
        
        if (Input.GetKeyDown(KeyCode.F9))
            ToggleMapCanvases();
        
        if (Input.GetKeyDown(KeyCode.F10))
            ToggleFlightHud();
        
        if (Input.GetKeyDown(KeyCode.F11))
            ToggleDynamicMap();
    }
    
    private void OnDestroy()
    {
        if (_flightHudForcedOff)
            ToggleFlightHud();
        
        if (_dynamicMapForcedOff)
            ToggleDynamicMap();
        
        if (_hudCanvasesForcedOff)
            ToggleHudCanvases();
        
        if (_mapCanvasesForcedOff)
            ToggleMapCanvases();
        
        if (_mapIconLayerForcedOff)
            ToggleMapIconLayer();
        
        if (_dynamicMapUpdatesForcedOff)
            ToggleDynamicMapUpdates();
        
        if (_combatHudIconLayerForcedOff)
            ToggleCombatHudIconLayer();
        
        if (_combatHudUpdatesForcedOff)
            ToggleCombatHudUpdates();
        
        if (_objectiveMarkerUpdatesForcedOff)
            ToggleObjectiveMarkerUpdates();
        
        if (_objectiveOverlayUpdatesForcedOff)
            ToggleObjectiveOverlayUpdates();
    }
    
    
    private static Canvas? GetFlightHudCanvas()
    {
        var hud = SceneSingleton<FlightHud>.i;
        if (hud == null)
            return null;
        
        return AccessTools.Field(typeof(FlightHud), "canvas")?.GetValue(hud) as Canvas;
    }
    
    // F1
    private void ToggleMapIconLayer()
    {
        var map = SceneSingleton<DynamicMap>.i;
        if (map?.iconLayer == null)
            return;
        
        var layer = map.iconLayer;
        if (!_mapIconLayerForcedOff)
        {
            _savedMapIconLayerActive = layer.activeSelf;
            layer.SetActive(false);
            _mapIconLayerForcedOff = true;
            Plugin.Debug("DynamicMap Icon Layer forced off (DynamicMap logic still running)");
        }
        else
        {
            layer.SetActive(_savedMapIconLayerActive);
            _mapIconLayerForcedOff = false;
            Plugin.Debug("DynamicMap Icon Layer restored");
        }
    }
    
    // F2
    private void ToggleDynamicMapUpdates()
    {
        var map = SceneSingleton<DynamicMap>.i;
        if (map == null)
            return;
        
        if (!_dynamicMapUpdatesForcedOff)
        {
            _savedDynamicMapEnabled = map.enabled;
            map.enabled = false;
            _dynamicMapUpdatesForcedOff = true;
            Plugin.Debug("DynamicMap component disabled (visuals frozen)");
        }
        else
        {
            map.enabled = _savedDynamicMapEnabled;
            _dynamicMapUpdatesForcedOff = false;
            Plugin.Debug("DynamicMap component restored");
        }
    }
    
    // F3
    private void ToggleCombatHudIconLayer()
    {
        var hud = SceneSingleton<CombatHUD>.i;
        if (hud?.iconLayer == null)
            return;
        
        var layer = hud.iconLayer.gameObject;
        if (!_combatHudIconLayerForcedOff)
        {
            _savedCombatHudIconLayerActive = layer.activeSelf;
            layer.SetActive(false);
            _combatHudIconLayerForcedOff = true;
            Plugin.Debug("CombatHUD Icon Layer forced off (CombatHUD marker logic still running)");
        }
        else
        {
            layer.SetActive(_savedCombatHudIconLayerActive);
            _combatHudIconLayerForcedOff = false;
            Plugin.Debug("CombatHUD Icon Layer restored");
        }
    }
    
    // F4
    private void ToggleCombatHudUpdates()
    {
        var hud = SceneSingleton<CombatHUD>.i;
        if (hud == null)
            return;
        
        if (!_combatHudUpdatesForcedOff)
        {
            _savedCombatHudEnabled = hud.enabled;
            hud.enabled = false;
            _combatHudUpdatesForcedOff = true;
            Plugin.Debug("CombatHUD component disabled (HUD visuals frozen)");
        }
        else
        {
            hud.enabled = _savedCombatHudEnabled;
            _combatHudUpdatesForcedOff = false;
            Plugin.Debug("CombatHUD component restored");
        }
    }
    
    // F5
    private void ToggleObjectiveMarkerUpdates()
    {
        if (!_objectiveMarkerUpdatesForcedOff)
        {
            _objectiveMarkerManager = FindSceneComponent<ObjectiveMarkerManager>();
            if (_objectiveMarkerManager == null)
            {
                Plugin.Debug("ObjectiveMarkerManager not found");
                return;
            }
            
            _savedObjectiveMarkerEnabled = _objectiveMarkerManager.enabled;
            _objectiveMarkerManager.enabled = false;
            _objectiveMarkerUpdatesForcedOff = true;
            Plugin.Debug(
                $"ObjectiveMarkerManager disabled at {GetPath(_objectiveMarkerManager.transform)}");
        }
        else
        {
            if (_objectiveMarkerManager != null)
                _objectiveMarkerManager.enabled = _savedObjectiveMarkerEnabled;
            _objectiveMarkerUpdatesForcedOff = false;
            _objectiveMarkerManager = null;
            Plugin.Debug("ObjectiveMarkerManager restored");
        }
    }
    
    // F6
    private void ToggleObjectiveOverlayUpdates()
    {
        if (!_objectiveOverlayUpdatesForcedOff)
        {
            _objectiveOverlayManager = FindSceneComponent<ObjectiveOverlayManager>();
            if (_objectiveOverlayManager == null)
            {
                Plugin.Debug("ObjectiveOverlayManager not found");
                return;
            }
            
            _savedObjectiveOverlayEnabled = _objectiveOverlayManager.enabled;
            _objectiveOverlayManager.enabled = false;
            _objectiveOverlayUpdatesForcedOff = true;
            Plugin.Debug(
                $"ObjectiveOverlayManager disabled at {GetPath(_objectiveOverlayManager.transform)}");
        }
        else
        {
            if (_objectiveOverlayManager != null)
                _objectiveOverlayManager.enabled = _savedObjectiveOverlayEnabled;
            _objectiveOverlayUpdatesForcedOff = false;
            _objectiveOverlayManager = null;
            Plugin.Debug("ObjectiveOverlayManager restored");
        }
    }
    
    // F7
    private void ToggleHudCanvases()
    {
        var rootCanvas = GetFlightHudCanvas();
        if (rootCanvas == null)
            return;
        
        if (!_hudCanvasesForcedOff)
        {
            _hudCanvasStates = CaptureCanvasStates(rootCanvas.gameObject);
            SetCanvasesEnabled(_hudCanvasStates, false);
            _hudCanvasesForcedOff = true;
            Plugin.Debug($"Disabled {_hudCanvasStates.Length} FlightHud Canvas component(s)");
        }
        else
        {
            RestoreCanvasStates(_hudCanvasStates);
            _hudCanvasStates = [];
            _hudCanvasesForcedOff = false;
            Plugin.Debug("FlightHud Canvas components restored");
        }
    }
    
    // F9
    private void ToggleMapCanvases()
    {
        var map = SceneSingleton<DynamicMap>.i;
        if (map == null)
            return;
        
        if (!_mapCanvasesForcedOff)
        {
            _mapCanvasStates = CaptureCanvasStates(map.gameObject);
            SetCanvasesEnabled(_mapCanvasStates, false);
            _mapCanvasesForcedOff = true;
            Plugin.Debug($"Disabled {_mapCanvasStates.Length} DynamicMap Canvas component(s) " +
                         "(DynamicMap logic remains active)");
        }
        else
        {
            RestoreCanvasStates(_mapCanvasStates);
            _mapCanvasStates = [];
            _mapCanvasesForcedOff = false;
            Plugin.Debug("DynamicMap Canvas components restored");
        }
    }
    
    // F10
    private void ToggleFlightHud()
    {
        var canvas = GetFlightHudCanvas();
        if (canvas == null)
            return;
        
        if (!_flightHudForcedOff)
        {
            _savedFlightHudActive = canvas.gameObject.activeSelf;
            FlightHud.EnableCanvas(false);
            _flightHudForcedOff = true;
            Plugin.Debug("FlightHud object and canvas forced off");
        }
        else
        {
            FlightHud.EnableCanvas(_savedFlightHudActive);
            _flightHudForcedOff = false;
            Plugin.Debug("FlightHud object and canvas restored");
        }
    }
    
    // F11
    private void ToggleDynamicMap()
    {
        var map = SceneSingleton<DynamicMap>.i;
        if (map == null)
            return;
        
        if (!_dynamicMapForcedOff)
        {
            _savedDynamicMapActive = map.gameObject.activeSelf;
            DynamicMap.EnableCanvas(false);
            _dynamicMapForcedOff = true;
            Plugin.Debug("DynamicMap object and canvas forced off");
        }
        else
        {
            DynamicMap.EnableCanvas(_savedDynamicMapActive);
            _dynamicMapForcedOff = false;
            Plugin.Debug("DynamicMap object and canvas restored");
        }
    }
    
    private static T? FindSceneComponent<T>() where T : Component
    {
        var components = Resources.FindObjectsOfTypeAll<T>();
        T? fallback = null;
        foreach (var component in components)
        {
            if (component == null || !component.gameObject.scene.IsValid())
                continue;
            
            if (component.gameObject.activeInHierarchy)
                return component;
            
            fallback ??= component;
        }
        
        return fallback;
    }
    
    private static CanvasState[] CaptureCanvasStates(GameObject root)
    {
        var canvases = root.GetComponentsInChildren<Canvas>(true);
        var states = new CanvasState[canvases.Length];
        for (var i = 0; i < canvases.Length; i++)
        {
            var canvas = canvases[i];
            states[i] = new CanvasState(canvas, canvas != null && canvas.enabled);
        }
        
        return states;
    }
    
    private static void SetCanvasesEnabled(CanvasState[] states, bool enabled)
    {
        for (var i = 0; i < states.Length; i++)
        {
            var canvas = states[i].Canvas;
            if (canvas != null)
                canvas.enabled = enabled;
        }
    }
    
    private static void RestoreCanvasStates(CanvasState[] states)
    {
        foreach (var state in states)
            if (state.Canvas != null)
                state.Canvas.enabled = state.Enabled;
    }
    
    private static string GetPath(Transform transform)
    {
        if (transform == null)
            return "<null>";
        
        var path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        
        return path;
    }
    
    private readonly struct CanvasState
    {
        internal readonly Canvas Canvas;
        internal readonly bool Enabled;
        
        internal CanvasState(Canvas canvas, bool enabled)
        {
            Canvas = canvas;
            Enabled = enabled;
        }
    }
}