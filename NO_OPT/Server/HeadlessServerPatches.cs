using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using JetBrains.Annotations;
using NuclearOption.Effects;
using Rewired;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace NO_OPT.Server;

internal static class HeadlessServerPatches
{
    internal static void Apply(Harmony harmony)
    {
        Patch(harmony, typeof(NoOpPatches));
        Patch(harmony, typeof(EnginePatches));
        Patch(harmony, typeof(AudioSourcePatches));
        Patch(harmony, typeof(ParticlePatches));
        Patch(harmony, typeof(MiscPatches));
    }
    
    private static void Patch(Harmony harmony, Type patchType)
    {
        try
        {
            var patched = harmony.CreateClassProcessor(patchType).Patch();
            Plugin.Debug($"Applied {patchType.Name}: {patched?.Count ?? 0} patched method(s).");
        }
        catch (Exception ex)
        {
            Plugin.Debug($"Failed applying {patchType.Name} - continuing with remaining patch groups.\n{ex}",
                Plugin.DebugType.LogError);
        }
    }
    
    [HarmonyPatch]
    internal static class NoOpPatches
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
            Plugin.Debug($"Headless no-op target not found: {type.FullName}.{name}({signature})",
                Plugin.DebugType.LogError);
        }
        
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool SkipPrefix() => false;
    }
    
    [HarmonyPatch]
    internal static class EnginePatches
    {
        // This for some reason is run in Update() => PropAnimate() which is otherwise purely visual and thus no-opped
        // Run this once before FixedUpdate() as it expects this to be set from Update(), otherwise these props
        // don't have thrust on AI planes
        [HarmonyPatch(typeof(ConstantSpeedProp), nameof(ConstantSpeedProp.FixedUpdate))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static void ConstantSpeedPropFixedUpdatePrefix(ConstantSpeedProp __instance)
        {
            __instance.rpmRatio = __instance.RPM / __instance.rpmLimit;
        }
    }
    
    [HarmonyPatch]
    internal static class AudioSourcePatches
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
    
    [HarmonyPatch]
    internal static class ParticlePatches
    {
        private static readonly ParticleEffectManager.PrefabEffect EmptyEffect = new(null, null);
        
        [HarmonyPatch(typeof(VaporEffect), nameof(VaporEffect.OnEnable))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool VaporEffectOnEnablePrefix(VaporEffect __instance)
        {
            __instance.enabled = false;
            return false;
        }
        
        [HarmonyPatch(typeof(TrailEmitter), nameof(TrailEmitter.OnEnable))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool TrailEmitterOnEnablePrefix(TrailEmitter __instance)
        {
            __instance.trailSystem?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            __instance.enabled = false;
            return false;
        }
        
        [HarmonyPatch(typeof(Downwash), nameof(Downwash.Awake))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool DownwashAwakePrefix(Downwash __instance)
        {
            __instance.enabled = false;
            foreach (var system in __instance.GetComponentsInChildren<ParticleSystem>(true))
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (__instance.forceField != null)
                __instance.forceField.enabled = false;
            return false;
        }
        
        [HarmonyPatch(typeof(DownwashEffect), nameof(DownwashEffect.Start))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool DownwashEffectStartPrefix(DownwashEffect __instance)
        {
            __instance.enabled = false;
            return false;
        }
        
        [HarmonyPatch(typeof(ParticleEffectManager), nameof(ParticleEffectManager.GetPrefabEffect))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool GetPrefabEffectPrefix(ref ParticleEffectManager.PrefabEffect __result)
        {
            __result = EmptyEffect;
            return false;
        }
        
        [HarmonyPatch(typeof(DamageParticles), nameof(DamageParticles.SlowUpdate))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool DamageParticlesSlowUpdatePrefix(DamageParticles __instance)
        {
            if (__instance.fireLifetime <= 0f || __instance.fireDamage <= 0f)
                return true;
            
            if ((!__instance.snapToWater && __instance.transform.position.y < Datum.LocalSeaY) ||
                (__instance.snapToWater && __instance.transform.parent.position.y < Datum.LocalSeaY - 10f))
            {
                foreach (var behaviour in __instance.systemBehaviours) behaviour.Stop();
                __instance.ParentObjectCulled();
                Object.Destroy(__instance.gameObject, 60f);
                if (__instance.fireLight == null)
                    return false;
                
                __instance.fireLifetime = 0f;
                __instance.fireDamage = 0f;
                __instance.enabled = false;
                Object.Destroy(__instance.fireLight.gameObject);
                return false;
            }
            
            __instance.time++;
            var active = false;
            foreach (var behaviour in __instance.systemBehaviours) active |= behaviour.IsActive();
            if (!active)
                Object.Destroy(__instance.gameObject);
            if (__instance.time > __instance.fireLifetime)
            {
                __instance.fireLifetime = 0f;
                __instance.fireDamage = 0f;
                __instance.enabled = false;
                if (__instance.fireLight != null)
                    Object.Destroy(__instance.fireLight.gameObject);
            }
            
            if (__instance.fireDamage <= 0f)
                return false;
            
            var count = Physics.OverlapSphereNonAlloc(__instance.transform.position, __instance.fireRange,
                DamageParticles.fireColliders);
            for (var i = 0; i < count; i++)
                if (DamageParticles.fireColliders[i].TryGetComponent<IDamageable>(out var damageable))
                    damageable.TakeDamage(0f, 0f, 1f, __instance.fireDamage, 0f, PersistentID.None);
            return false;
        }
        
        [HarmonyPatch(typeof(DamageParticles), nameof(DamageParticles.Update))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool DamageParticlesUpdatePrefix(DamageParticles __instance)
        {
            if (__instance.snapToWater)
                __instance.transform.position = new Vector3(__instance.transform.position.x, Datum.LocalSeaY,
                    __instance.transform.position.z);
            return false;
        }
        
        [HarmonyPatch(typeof(Shockwave), nameof(Shockwave.Start))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool ShockwaveStartPrefix(Shockwave __instance)
        {
            __instance.blastPower = Mathf.Pow(__instance.yieldKilotons * 1000000f, 0.3333f);
            __instance.blastRadius = __instance.blastPower * 13f;
            __instance.blastPropagation = __instance.blastPower * 0.5f;
            if (!(__instance.yieldKilotons >= 0.0002f)) return false;
            var num = Physics.OverlapSphereNonAlloc(__instance.transform.position, __instance.blastRadius * 2f,
                Shockwave.colliderBuffer);
            for (var i = 0; i < num; i++)
            {
                var collider = Shockwave.colliderBuffer[i];
                var item = new Shockwave.InfluencedObject(collider);
                if (item.IsInteractable()) __instance.influencedObjects.Add(item);
            }
            
            if (__instance.influencedObjects.Count == 0)
            {
                __instance.enabled = false;
                Object.Destroy(__instance);
            }
            
            return false;
        }
        
        [HarmonyPatch(typeof(Shockwave), nameof(Shockwave.Update))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool ShockwaveUpdatePrefix(Shockwave __instance)
        {
            __instance.blastPropagation += 340f * Time.deltaTime;
            var num = Mathf.Max(__instance.blastPropagation / __instance.blastPower, 1f);
            var num2 = 25000f / (num * num * num);
            if (num2 > 0.5f)
            {
                for (var num3 = __instance.influencedObjects.Count - 1; num3 >= 0; num3--)
                    if (__instance.influencedObjects[num3].HasShockwaveReached(__instance.transform.position,
                            __instance.blastPropagation, num2, __instance.yieldKilotons * 1000000f,
                            __instance.blastPower, __instance.ownerID))
                        __instance.influencedObjects.RemoveAt(num3);
            }
            else
            {
                __instance.influencedObjects.Clear();
            }
            
            if (__instance.influencedObjects.Count == 0)
            {
                __instance.enabled = false;
                Object.Destroy(__instance);
            }
            
            return false;
        }
        
        [HarmonyPatch(typeof(MushroomCloud), nameof(MushroomCloud.Start))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool MushroomCloudStartPrefix(MushroomCloud __instance)
        {
            __instance.enabled = false;
            foreach (var system in __instance.GetComponentsInChildren<ParticleSystem>(true))
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var forceField in __instance.GetComponentsInChildren<ParticleSystemForceField>(true))
                forceField.enabled = false;
            if (__instance.lensFlare != null)
                __instance.lensFlare.enabled = false;
            return false;
        }
        
        [HarmonyPatch(typeof(Gun), nameof(Gun.Awake))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void GunAwakePostfix(Gun __instance)
        {
            foreach (var particles in __instance.muzzleParticles)
                if (particles != null)
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            __instance.muzzleParticles = [];
            __instance.ejectionTransform = null;
            __instance.sources = [];
        }
        
        [HarmonyPatch(typeof(Gun.Heat), nameof(Gun.Heat.Update))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool GunHeatUpdatePrefix(Gun.Heat __instance, float time)
        {
            if (!(__instance.heat > 0f)) return false;
            var num = (__instance.coolingPerSecond +
                       __instance.coolingPer100kph * __instance.gun.attachedUnit.speed * 0.036f) * time;
            if (__instance.heat > num)
            {
                __instance.heat -= num;
                var max = Mathf.Min(__instance.baseFireRate / __instance.firerateDegradation * 1.01f,
                    __instance.gun.info.muzzleVelocity / __instance.velocityDegradation);
                __instance.overheatFactor = Mathf.Clamp(__instance.heat / __instance.maxHeat - 1f, 0f, max);
                if (__instance.overheatFactor > 0f)
                {
                    __instance.gun.fireRate = __instance.baseFireRate -
                                              __instance.firerateDegradation * __instance.overheatFactor;
                    __instance.gun.fireInterval = 60f / __instance.gun.fireRate;
                    __instance.gun.bulletSpread = __instance.baseSpread +
                                                  __instance.accuracyDegradation * __instance.overheatFactor;
                    __instance.gun.muzzleVelocity = __instance.gun.info.muzzleVelocity -
                                                    __instance.velocityDegradation * __instance.overheatFactor;
                }
                else
                {
                    __instance.overheatFactor = 0f;
                    __instance.gun.fireRate = __instance.baseFireRate;
                    __instance.gun.fireInterval = 60f / __instance.baseFireRate;
                    __instance.gun.bulletSpread = __instance.baseSpread;
                    __instance.gun.muzzleVelocity = __instance.gun.info.muzzleVelocity;
                }
            }
            else
            {
                __instance.heat = 0f;
            }
            
            return false;
        }
        
        [HarmonyPatch(typeof(Gun.Heat), nameof(Gun.Heat.GunFired))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool GunHeatGunFiredPrefix(Gun.Heat __instance)
        {
            if (__instance.overheatFactor > 1f)
                __instance.heat += __instance.heatPerShot * (__instance.overheatFactor - 1f);
            __instance.heat += __instance.heatPerShot;
            return false;
        }
        
        [HarmonyPatch(typeof(Missile), nameof(Missile.Awake))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void MissileAwakePostfix(Missile __instance)
        {
            __instance.flightSound = null;
            foreach (var motor in __instance.motors)
            {
                foreach (var particles in motor.particleSystems)
                    if (particles != null)
                        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                motor.particleSystems = [];
                motor.audioSources = [];
                motor.startupSource = null;
                foreach (var light in motor.lights)
                    if (light != null)
                        light.enabled = false;
                motor.lights = [];
                motor.trailEmitters = [];
            }
        }
        
        [HarmonyPatch(typeof(MissileLauncher), nameof(MissileLauncher.OnEnable))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void MissileLauncherOnEnablePostfix(MissileLauncher __instance)
        {
            if (__instance.launchParticles != null)
                __instance.launchParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            __instance.launchParticles = null;
            __instance.launchSound = null;
        }
        
        [HarmonyPatch(typeof(Laser), nameof(Laser.Start))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool LaserStartPrefix(Laser __instance)
        {
            __instance.beamRenderer.enabled = false;
            __instance.beamTransform = __instance.beamRenderer.transform;
            __instance.beamScale = __instance.beamRenderer.transform.localScale.x;
            __instance.lastDamageTick = Time.timeSinceLevelLoad;
            __instance.sources = [];
            return false;
        }
        
        [HarmonyPatch(typeof(Laser), nameof(Laser.Fire))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool LaserFirePrefix(Laser __instance, Unit owner, Unit target, Vector3 inheritedVelocity,
            WeaponStation weaponStation, GlobalPosition aimpoint)
        {
            if (!__instance.enabled) __instance.enabled = true;
            __instance.fireCommanded = true;
            __instance.previousLastFired = __instance.lastFired;
            __instance.lastFired = Time.timeSinceLevelLoad;
            weaponStation.LastFiredTime = Time.timeSinceLevelLoad;
            return false;
        }
        
        [HarmonyPatch(typeof(Laser), nameof(Laser.LateUpdate))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool LaserLateUpdatePrefix(Laser __instance)
        {
            if (!Mathf.Approximately(__instance.lastFired, __instance.previousLastFired) &&
                !(Time.timeSinceLevelLoad > __instance.lastFired + 0.2f)) return false;
            __instance.fireCommanded = false;
            __instance.enabled = false;
            return false;
        }
        
        [HarmonyPatch(typeof(Laser), nameof(Laser.FixedUpdate))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool LaserFixedUpdatePrefix(Laser __instance)
        {
            var vector = __instance.currentTargetTransform != null
                ? __instance.currentTargetTransform.position
                : __instance.transform.position + __instance.transform.forward * 20000f;
            if (__instance.currentTarget != null &&
                !__instance.attachedUnit.NetworkHQ.IsTargetBeingTracked(__instance.currentTarget) &&
                __instance.attachedUnit.NetworkHQ.TryGetKnownPosition(__instance.currentTarget, out var knownPosition))
                vector = knownPosition.ToLocalPosition();
            var vector2 = vector - __instance.transform.position;
            __instance.directionTransform.rotation = Quaternion.LookRotation(
                Vector3.RotateTowards(__instance.transform.forward, vector2, __instance.maxAngle * (Mathf.PI / 180f),
                    0f));
            var num = Vector3.Angle(__instance.directionTransform.forward, vector2);
            if (num > 1f) vector = __instance.transform.position + __instance.directionTransform.forward * 20000f;
            if (__instance.fireCommanded && num < 1f)
            {
                var num2 = 1f;
                if (!__instance.vehicularPowerSupply)
                {
                    var powerRequested = __instance.fireTime < 0.2f ? __instance.power * 0.1f : __instance.power;
                    num2 = Mathf.Clamp01(__instance.powerSupply.DrawPower(powerRequested) / __instance.power);
                }
                
                // Prevent laser self damage to owner unit
                if (LaserSelfDamageFix.LinecastIgnoringOwner(__instance.directionTransform.position, vector,
                        out var hitInfo, ~PhysicsLayers.ExclusionZonesMask, __instance.attachedUnit))
                {
                    __instance.hitTransform = hitInfo.collider.transform;
                    __instance.hitOffset = __instance.hitTransform.InverseTransformPoint(hitInfo.point);
                    var component = hitInfo.collider.gameObject.GetComponent<IDamageable>();
                    if (component != null && Time.timeSinceLevelLoad - __instance.lastDamageTick > 0.2f)
                    {
                        __instance.lastDamageTick = Time.timeSinceLevelLoad;
                        var num3 = __instance.damageAtRange.Evaluate(hitInfo.distance) * num2;
                        component.TakeDamage(0f, __instance.blastDamage * num3 * 0.2f, 1f,
                            __instance.fireDamage * num3 * 0.2f, 0f, __instance.attachedUnit.persistentID);
                    }
                }
                
                __instance.fireTime += Time.deltaTime;
            }
            else
            {
                __instance.fireTime = 0f;
            }
            
            return false;
        }
        
        [HarmonyPatch(typeof(EscapeCapsule), nameof(EscapeCapsule.StartEjection))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool EscapeCapsuleStartEjectionPrefix(EscapeCapsule __instance)
        {
            if (__instance.launched || __instance.aircraft == null)
                return false;
            
            __instance.launched = true;
            __instance.aircraft = __instance.part.parentUnit as Aircraft;
            __instance.player = __instance.aircraft!.Player;
            __instance.unitName = __instance.aircraft.unitName;
            __instance.ID = __instance.aircraft.persistentID;
            __instance.HQ = __instance.aircraft.NetworkHQ;
            __instance.Owner = __instance.aircraft.Owner;
            __instance.part.DisableMaterialCleanup();
            __instance.StartSlowUpdateDelayed(1f, __instance.CheckRadarAlt);
            __instance.enabled = true;
            __instance.Eject().Forget();
            return false;
        }
        
        [HarmonyPatch(typeof(FuelTank), nameof(FuelTank.PunctureTank))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool FuelTankPunctureTankPrefix(FuelTank __instance, float leakRate)
        {
            __instance.isLeaking = true;
            __instance.leakRate = Mathf.Clamp(leakRate, __instance.leakRate, __instance.maxLeakRate);
            return false;
        }
        
        [HarmonyPatch(typeof(IRFlare), nameof(IRFlare.LaunchFlare))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool IRFlareLaunchFlarePrefix(
            // ReSharper disable once InconsistentNaming
            IRFlare __instance, Aircraft aircraft, Transform launchPoint, Vector3 launchVelocity)
        {
            __instance.IR = new IRSource(__instance.transform, 1f, true);
            __instance.transform.position = launchPoint.position - launchVelocity * Time.deltaTime;
            __instance.velocity = launchVelocity;
            __instance.aircraft = aircraft;
            aircraft.AddIRSource(__instance.IR);
            return false;
        }
        
        [HarmonyPatch(typeof(IRFlare), nameof(IRFlare.Update))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool IRFlareUpdatePrefix(
            // ReSharper disable once InconsistentNaming
            IRFlare __instance)
        {
            __instance.transform.position += __instance.velocity * Time.deltaTime;
            var relativeVelocity = __instance.velocity -
                                   NetworkSceneSingleton<LevelInfo>.i.GetWind(__instance.transform.GlobalPosition());
            var dragForce = relativeVelocity.normalized * relativeVelocity.sqrMagnitude * __instance.drag;
            __instance.velocity -= (dragForce + __instance.gravityVector) * Time.deltaTime;
            if (Physics.Linecast(__instance.transform.position,
                    __instance.transform.position + __instance.velocity * Time.deltaTime * 1.05f, out __instance.hit,
                    __instance.layermask))
            {
                __instance.transform.position = __instance.hit.point + __instance.hit.normal * 0.1f;
                __instance.velocity = Vector3.zero;
                __instance.gravityVector = Vector3.zero;
            }
            
            __instance.burnTime -= Time.deltaTime;
            if (__instance.transform.position.GlobalY() <= 0f)
            {
                __instance.burnTime = 0f;
                __instance.velocity = Vector3.zero;
            }
            
            if (__instance.burnTime > 0f)
            {
                __instance.CheckFlareDistance();
                return false;
            }
            
            __instance.IR.intensity = 0f;
            if (__instance.aircraft != null && __instance.nearAircraft)
            {
                __instance.aircraft.RemoveIRSource(__instance.IR);
                __instance.nearAircraft = false;
            }
            
            Object.Destroy(__instance.gameObject);
            return false;
        }
        
        [HarmonyPatch(typeof(IRFlare), nameof(IRFlare.OnEnable))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool IRFlareOnEnablePrefix(
            // ReSharper disable once InconsistentNaming
            IRFlare __instance)
        {
            __instance.checkTime = Time.timeSinceLevelLoad;
            __instance.flareParticles?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            __instance.smokeParticles?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (__instance.flareLight != null)
                __instance.flareLight.enabled = false;
            return false;
        }
        
        [HarmonyPatch(typeof(SpecialFlareEjector), nameof(SpecialFlareEjector.EjectFlare))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool SpecialFlareEjectorEjectFlarePrefix(ref UniTask __result)
        {
            __result = UniTask.CompletedTask;
            return false;
        }
    }
    
    [HarmonyPatch]
    internal static class MiscPatches
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