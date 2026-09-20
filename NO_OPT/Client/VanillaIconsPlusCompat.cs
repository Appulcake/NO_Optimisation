using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NO_OPT.Client;

internal struct VanillaIconsPlusHudState
{
    internal Transform? LabelTransform;
    internal Behaviour? LabelBehaviour;
    internal Vector3 Offset;
    internal bool Visible;
}

internal static class VanillaIconsPlusCompat
{
    private static bool _resolved;
    private static MethodInfo? _getHudLabel;
    
    private static void Resolve()
    {
        if (_resolved)
            return;
        
        _resolved = true;
        var extensionsType = AccessTools.TypeByName("VanillaIconsPLUS.HUDUnitMarkerExtensions");
        if (extensionsType == null)
            return;
        
        _getHudLabel = AccessTools.Method(extensionsType, "GetLabel", [typeof(HUDUnitMarker)]);
        if (_getHudLabel != null)
            Plugin.Log("Vanilla Icons PLUS HUD compatibility enabled.");
    }
    
    internal static void CaptureState(HUDUnitMarker marker, ref VanillaIconsPlusHudState state)
    {
        if (marker.image == null || marker.unit is not Aircraft aircraft || aircraft.Player == null)
            return;
        
        if (state.LabelTransform == null)
        {
            Resolve();
            if (_getHudLabel == null)
                return;
            
            var result = _getHudLabel.Invoke(null, [marker]);
            if (result is not Component component)
                return;
            
            state.LabelTransform = component.transform;
            state.LabelBehaviour = component as Behaviour;
        }
        
        var labelTransform = state.LabelTransform;
        if (labelTransform == null)
            return;
        
        state.Offset = labelTransform.position - marker.image.transform.position;
        if (state.LabelBehaviour != null)
            state.Visible = state.LabelBehaviour.enabled;
    }
    
    internal static void SyncVisual(HUDUnitMarker marker, ref VanillaIconsPlusHudState state)
    {
        var labelTransform = state.LabelTransform;
        if (labelTransform == null || marker.image == null)
            return;
        
        labelTransform.position = marker.image.transform.position + state.Offset;
        if (state.LabelBehaviour == null)
            return;
        
        var visible = state.Visible && marker.image.enabled && marker.image.gameObject.activeInHierarchy;
        if (state.LabelBehaviour.enabled != visible)
            state.LabelBehaviour.enabled = visible;
    }
    
    internal static void Hide(ref VanillaIconsPlusHudState state)
    {
        if (state.LabelBehaviour != null && state.LabelBehaviour.enabled)
            state.LabelBehaviour.enabled = false;
    }
    
    internal static void Restore(ref VanillaIconsPlusHudState state)
    {
        if (state.LabelBehaviour != null && state.LabelBehaviour.enabled != state.Visible)
            state.LabelBehaviour.enabled = state.Visible;
    }
}