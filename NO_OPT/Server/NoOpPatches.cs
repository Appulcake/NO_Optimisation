using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using NuclearOption.Effects;
using Rewired;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NO_OPT.Server;

internal sealed partial class HeadlessServerOptimisations
{
    [HarmonyPatch]
    private static class NoOpPatches
    {
        [HarmonyTargetMethods]
        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var targets = new List<MethodBase>();
            
            // UI / Rewired
            AddTarget(targets, typeof(DynamicMap), nameof(DynamicMap.Update));
            AddTarget(targets, typeof(DynamicMap), nameof(DynamicMap.UpdateIcons));
            AddTarget(targets, typeof(DynamicMap), nameof(DynamicMap.UpdateMap));
            
            AddTarget(targets, typeof(InputManager_Base), nameof(InputManager_Base.Update));
            AddTarget(targets, typeof(InputManager_Base), nameof(InputManager_Base.FixedUpdate));
            AddTarget(targets, typeof(InputManager_Base), nameof(InputManager_Base.LateUpdate));
            
            // Engines
            AddTarget(targets, typeof(ConstantSpeedProp), nameof(ConstantSpeedProp.Update));
            AddTarget(targets, typeof(DuctedFan), nameof(DuctedFan.Update));
            AddTarget(targets, typeof(PropFan), nameof(PropFan.Update));
            AddTarget(targets, typeof(RotorShaft), nameof(RotorShaft.Update));
            AddTarget(targets, typeof(TurbineEngine), nameof(TurbineEngine.Animate), typeof(bool));
            AddTarget(targets, typeof(Turbofan), nameof(Turbofan.Animate));
            AddTarget(targets, typeof(Turbojet), nameof(Turbojet.Animate));
            AddTarget(targets, typeof(JetNozzle.Afterburner), nameof(JetNozzle.Afterburner.Audio),
                typeof(float), typeof(float));
            AddTarget(targets, typeof(JetNozzle.Afterburner), nameof(JetNozzle.Afterburner.EnableAudio));
            AddTarget(targets, typeof(JetNozzle.JetParticleParameters),
                nameof(JetNozzle.JetParticleParameters.UpdateParticles),
                typeof(float), typeof(float), typeof(float));
            AddTarget(targets, typeof(JetNozzle), nameof(JetNozzle.AudioEffects));
            
            // Audio
            AddTarget(targets, typeof(ExplosionAudio), nameof(ExplosionAudio.Start));
            
            AddTarget(targets, typeof(Unit), nameof(Unit.RegisterDopplerSound), typeof(AudioSource));
            AddTarget(targets, typeof(Unit), nameof(Unit.DeregisterDopplerSound), typeof(AudioSource));
            AddTarget(targets, typeof(Unit), nameof(Unit.SetDoppler), typeof(bool));
            AddTarget(targets, typeof(Unit), nameof(Unit.SetSoundsMuted), typeof(bool));
            
            AddTarget(targets, typeof(AudioSource), nameof(AudioSource.PlayClipAtPoint),
                typeof(AudioClip), typeof(Vector3), typeof(float));
            
            AddTarget(targets, typeof(Laser), nameof(Laser.LaserSound));
            
            AddTarget(targets, typeof(Gun), nameof(Gun.ShotSound));
            AddTarget(targets, typeof(Gun), nameof(Gun.LoopSounds));
            
            // Particles / Effects
            AddTarget(targets, typeof(Aircraft), nameof(Aircraft.ThrowSparks), typeof(Vector3), typeof(Vector3));
            AddTarget(targets, typeof(BlastManager), nameof(BlastManager.AddBlast),
                typeof(GlobalPosition), typeof(float));
            AddTarget(targets, typeof(EffectManager), nameof(EffectManager.Start));
            AddTarget(targets, typeof(EffectManager), nameof(EffectManager.ImpactDust),
                typeof(float), typeof(GlobalPosition), typeof(Quaternion));
            AddTarget(targets, typeof(DecalSpawner), nameof(DecalSpawner.Start));
            AddTarget(targets, typeof(DecalSpawner), nameof(DecalSpawner.DecalFadeIn), typeof(DecalProjector));
            AddTarget(targets, typeof(ParticleEffectManager), nameof(ParticleEffectManager.EmitParticles),
                typeof(int), typeof(int), typeof(GlobalPosition), typeof(Vector3), typeof(float), typeof(float),
                typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float));
            AddTarget(targets, typeof(SmokeEmitter), nameof(SmokeEmitter.Initialize));
            AddTarget(targets, typeof(SmokeEmitter), nameof(SmokeEmitter.Emit),
                typeof(bool), typeof(float), typeof(Vector3));
            AddTarget(targets, typeof(SpecialSmokeEjector), nameof(SpecialSmokeEjector.Update));
            AddTarget(targets, typeof(TrailEmitter), nameof(TrailEmitter.StartTrail));
            AddTarget(targets, typeof(SetGlobalParticles), nameof(SetGlobalParticles.Start));
            AddTarget(targets, typeof(AeroPart), nameof(AeroPart.OnCollisionStay), typeof(Collision));
            AddTarget(targets, typeof(Ship), nameof(Ship.UpdateParticles));
            AddTarget(targets, typeof(EjectionSeat), nameof(EjectionSeat.FireEffects));
            AddTarget(targets, typeof(IRFlare), nameof(IRFlare.Emit), typeof(float), typeof(Vector3));
            
            return targets;
        }
        
        private static void AddTarget(ICollection<MethodBase> targets, Type type, string name, params Type[] parameters)
        {
            MethodBase method = AccessTools.Method(type, name, parameters);
            if (method != null)
            {
                targets.Add(method);
                return;
            }
            
            var signature = string.Join(", ", Array.ConvertAll(parameters, p => p.Name));
            Plugin.LogError($"Headless no-op target not found: {type.FullName}.{name}({signature})");
        }
        
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool SkipPrefix() => false;
    }
}