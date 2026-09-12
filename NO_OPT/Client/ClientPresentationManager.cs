using System.Runtime.CompilerServices;
using UnityEngine;

namespace NO_OPT.Client;

internal static class ClientPresentationManager
{
    private const float TargetSweepInterval = 0.5f;
    private const int MaxChecksPerFrame = 128;
    private static readonly ConditionalWeakTable<Unit, PresentationState> States = new();
    private static readonly ConditionalWeakTable<AudioSource, Unit> AudioOwners = new();
    private static int _nextUnitIndex;
    private static bool _wasEnabled;
    
    private static PresentationState CreateState(Unit _) => new();
    
    internal static void Tick()
    {
        var enabled = Plugin.ClientPresentationOptimisationEnabled.Value;
        if (!enabled)
        {
            if (_wasEnabled)
                RestoreAll();
            _wasEnabled = false;
            return;
        }
        
        _wasEnabled = true;
        var cameraState = SceneSingleton<CameraStateManager>.i;
        if (cameraState == null)
            return;
        
        var units = UnitRegistry.allUnits;
        if (units == null || units.Count == 0)
        {
            _nextUnitIndex = 0;
            return;
        }
        
        // Goal: complete unit check every ~0.5s, based on FPS and unit count
        var checks =
            Mathf.Clamp(Mathf.CeilToInt(units.Count * Mathf.Max(Time.unscaledDeltaTime, 0.001f) / TargetSweepInterval),
                1, Mathf.Min(MaxChecksPerFrame, units.Count));
        
        for (var i = 0; i < checks; i++)
        {
            if (units.Count == 0)
            {
                _nextUnitIndex = 0;
                return;
            }
            
            if (_nextUnitIndex >= units.Count)
                _nextUnitIndex = 0;
            var unit = units[_nextUnitIndex++];
            if (unit == null)
                continue;
            
            UpdateUnit(unit);
        }
    }
    
    private static void UpdateUnit(Unit unit)
    {
        States.TryGetValue(unit, out var state);
        var currentlySleeping = state?.Sleeping == true;
        var shouldSleep = ClientActivity.ShouldSleepPresentation(unit, currentlySleeping);
        if (shouldSleep)
        {
            state ??= States.GetValue(unit, CreateState);
            state.Sleep(unit);
            return;
        }
        
        if (currentlySleeping)
            state!.Wake();
    }
    
    internal static bool IsSleeping(Unit unit)
    {
        if (unit == null || !Plugin.ClientPresentationOptimisationEnabled.Value)
            return false;
        
        return States.TryGetValue(unit, out var state) && state.Sleeping;
    }
    
    internal static void Wake(Unit unit)
    {
        if (unit == null)
            return;
        
        if (States.TryGetValue(unit, out var state) && state.Sleeping)
            state.Wake();
    }
    
    internal static void RestoreAll()
    {
        var units = UnitRegistry.allUnits;
        
        if (units != null)
            foreach (var unit in units)
            {
                if (unit == null)
                    continue;
                
                if (States.TryGetValue(unit, out var state) && state.Sleeping)
                    state.Wake();
            }
        
        _nextUnitIndex = 0;
        _wasEnabled = false;
    }
    
    internal static bool ShouldBlockAudioPlayback(AudioSource source)
    {
        if (source == null || !Plugin.ClientPresentationOptimisationEnabled.Value)
            return false;
        
        if (!AudioOwners.TryGetValue(source, out var unit) || unit == null)
            return false;
        
        return States.TryGetValue(unit, out var state) && state.Sleeping;
    }
    
    private static void RegisterAudioSource(AudioSource source, Unit unit)
    {
        if (source == null || unit == null)
            return;
        
        AudioOwners.Remove(source);
        AudioOwners.Add(source, unit);
    }
    
    private sealed class PresentationState
    {
        internal bool Sleeping;
        private AudioState[] _audioSources = [];
        private ForceFieldState[] _forceFields = [];
        private LightState[] _lights = [];
        private ParticleState[] _particles = [];
        private RendererState[] _renderers = [];
        private TrailState[] _trails = [];
        
        internal void Sleep(Unit unit)
        {
            if (Sleeping)
                return;
            
            Capture(unit);
            for (var i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i].Component;
                if (renderer != null)
                    renderer.forceRenderingOff = true;
            }
            
            for (var i = 0; i < _trails.Length; i++)
            {
                var trail = _trails[i].Component;
                if (trail != null)
                    trail.emitting = false;
            }
            
            for (var i = 0; i < _particles.Length; i++)
            {
                var system = _particles[i].Component;
                if (system != null && system.isPlaying)
                    system.Pause(true);
            }
            
            for (var i = 0; i < _audioSources.Length; i++)
            {
                var source = _audioSources[i].Component;
                if (source == null)
                    continue;
                
                if (source.isPlaying)
                    source.Pause();
                source.enabled = false;
            }
            
            for (var i = 0; i < _lights.Length; i++)
            {
                var light = _lights[i].Component;
                if (light != null)
                    light.enabled = false;
            }
            
            for (var i = 0; i < _forceFields.Length; i++)
            {
                var forceField = _forceFields[i].Component;
                if (forceField != null)
                    forceField.enabled = false;
            }
            
            Sleeping = true;
        }
        
        internal void Wake()
        {
            if (!Sleeping)
                return;
            
            foreach (var state in _renderers)
                if (state.Component != null)
                    state.Component.forceRenderingOff = state.ForceRenderingOff;
            
            foreach (var state in _trails)
                if (state.Component != null)
                    state.Component.emitting = state.Emitting;
            
            foreach (var state in _particles)
            {
                if (state.Component == null)
                    continue;
                
                if (state.WasPlaying)
                    state.Component.Play(true);
            }
            
            foreach (var state in _audioSources)
            {
                var source = state.Component;
                if (source == null)
                    continue;
                
                source.enabled = state.Enabled;
                if (!state.Enabled)
                    continue;
                
                if (state.WakeMode == AudioWakeMode.LetControllerRestart)
                {
                    // Stop paused playbacks to prevent a no longer active sound from playing when unit is woken up
                    // E.g. laser buzzing sounds whenever coming back in range of a laser
                    source.Stop();
                    continue;
                }
                
                if (state.WasPlaying)
                    source.UnPause();
            }
            
            foreach (var state in _lights)
                if (state.Component != null)
                    state.Component.enabled = state.Enabled;
            foreach (var state in _forceFields)
                if (state.Component != null)
                    state.Component.enabled = state.Enabled;
            Sleeping = false;
            ClearCapture();
        }
        
        private void Capture(Unit unit)
        {
            var renderers = unit.GetComponentsInChildren<Renderer>(true);
            _renderers = new RendererState[renderers.Length];
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                _renderers[i] = new RendererState(renderer, renderer != null && renderer.forceRenderingOff);
            }
            
            var trails = unit.GetComponentsInChildren<TrailRenderer>(true);
            _trails = new TrailState[trails.Length];
            for (var i = 0; i < trails.Length; i++)
            {
                var trail = trails[i];
                _trails[i] = new TrailState(trail, trail != null && trail.emitting);
            }
            
            var particles = unit.GetComponentsInChildren<ParticleSystem>(true);
            _particles = new ParticleState[particles.Length];
            for (var i = 0; i < particles.Length; i++)
            {
                var particle = particles[i];
                _particles[i] = new ParticleState(particle, particle != null && particle.isPlaying);
            }
            
            var audioSources = unit.GetComponentsInChildren<AudioSource>(true);
            _audioSources = new AudioState[audioSources.Length];
            for (var i = 0; i < audioSources.Length; i++)
            {
                var source = audioSources[i];
                if (source == null)
                    continue;
                
                RegisterAudioSource(source, unit);
                var wakeMode = source.GetComponent<Laser>() != null
                    ? AudioWakeMode.LetControllerRestart
                    : AudioWakeMode.ResumeCapturedPlayback;
                _audioSources[i] = new AudioState(source, source != null && source.enabled,
                    source != null && source.isPlaying, wakeMode);
            }
            
            var lights = unit.GetComponentsInChildren<Light>(true);
            _lights = new LightState[lights.Length];
            for (var i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                _lights[i] = new LightState(light, light != null && light.enabled);
            }
            
            var forceFields = unit.GetComponentsInChildren<ParticleSystemForceField>(true);
            _forceFields = new ForceFieldState[forceFields.Length];
            for (var i = 0; i < forceFields.Length; i++)
            {
                var forceField = forceFields[i];
                _forceFields[i] = new ForceFieldState(forceField, forceField != null && forceField.enabled);
            }
        }
        
        private void ClearCapture()
        {
            _renderers = [];
            _trails = [];
            _particles = [];
            _audioSources = [];
            _lights = [];
            _forceFields = [];
        }
    }
    
    private readonly struct RendererState
    {
        internal readonly Renderer Component;
        internal readonly bool ForceRenderingOff;
        
        internal RendererState(Renderer component, bool forceRenderingOff)
        {
            Component = component;
            ForceRenderingOff = forceRenderingOff;
        }
    }
    
    private readonly struct TrailState
    {
        internal readonly TrailRenderer Component;
        internal readonly bool Emitting;
        
        internal TrailState(TrailRenderer component, bool emitting)
        {
            Component = component;
            Emitting = emitting;
        }
    }
    
    private readonly struct ParticleState
    {
        internal readonly ParticleSystem Component;
        internal readonly bool WasPlaying;
        
        internal ParticleState(ParticleSystem component, bool wasPlaying)
        {
            Component = component;
            WasPlaying = wasPlaying;
        }
    }
    
    private enum AudioWakeMode : byte
    {
        ResumeCapturedPlayback,
        LetControllerRestart
    }
    
    private readonly struct AudioState
    {
        internal readonly AudioSource Component;
        internal readonly bool Enabled;
        internal readonly bool WasPlaying;
        internal readonly AudioWakeMode WakeMode;
        
        internal AudioState(AudioSource component, bool enabled, bool wasPlaying, AudioWakeMode wakeMode)
        {
            Component = component;
            Enabled = enabled;
            WasPlaying = wasPlaying;
            WakeMode = wakeMode;
        }
    }
    
    private readonly struct LightState
    {
        internal readonly Light Component;
        internal readonly bool Enabled;
        
        internal LightState(Light component, bool enabled)
        {
            Component = component;
            Enabled = enabled;
        }
    }
    
    private readonly struct ForceFieldState
    {
        internal readonly ParticleSystemForceField Component;
        internal readonly bool Enabled;
        
        internal ForceFieldState(ParticleSystemForceField component, bool enabled)
        {
            Component = component;
            Enabled = enabled;
        }
    }
}