using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using NO_OPT.Modules;
using UnityEngine;

namespace NO_OPT.Generic;

[OptimisationModule(ModuleScope.Any, "Generic", "Enable Missile Optimisation Patches", LiveToggle = false)]
internal sealed class MissileOptimisations : OptimisationModule
{
    private const float WindSampleInterval = 0.05f; // 20 UPS
    private const float AirDensitySampleInterval = 0.10f; // 10 UPS
    private const float LongRangeDistance = 15000f;
    private const float LongRangeDistanceSq = LongRangeDistance * LongRangeDistance;
    private const float LongRangeGuidanceInterval = 0.05f; // 20 UPS
    private const float MotorMassUpdateInterval = 0.05f; // 20 UPS
    private const float CruiseGuidanceInterval = 0.5f;
    private static ConfigEntry<bool> _groupByOwnerConfig = null!;
    private static ConfigEntry<float> _launchProximityConfig = null!;
    private static ConfigEntry<float> _etaWindowConfig = null!;
    private static ConfigEntry<bool> _cruiseRetargetingConfig = null!;
    private static ConfigEntry<float> _cruiseRetargetRangeConfig = null!;
    private static int _nextCruiseGuidanceBucket;
    private static readonly ConditionalWeakTable<Missile, MissileState> MissileStates = new();
    private static bool _groupByOwner;
    private static float _launchProximitySq;
    private static float _etaWindow;
    private static int _nextWindBucket;
    private static int _nextAirDensityBucket;
    private static int _nextGuidanceBucket;
    private static int _nextMassBucket;
    private static bool _cruiseRetargeting;
    private static float _cruiseRetargetRangeSq;
    
    protected override void Configure()
    {
        _groupByOwnerConfig = Config.Bind("Generic - Cruise Missile", "Group Formation By Owner", true);
        _launchProximityConfig = Config.Bind("Generic - Cruise Missile", "Group Formation Launch Distance", 10000f);
        _etaWindowConfig = Config.Bind("Generic - Cruise Missile", "Group Formation ETA Window", 15f);
        _cruiseRetargetingConfig = Config.Bind("Generic - Cruise Missile", "Retarget Destroyed Targets", false);
        _cruiseRetargetRangeConfig = Config.Bind("Generic - Cruise Missile", "Retarget Group Range", 7500f);
    }
    
    protected override void OnEnable()
    {
        CacheSettings();
    }
    
    private static void CacheSettings()
    {
        _groupByOwner = _groupByOwnerConfig.Value;
        var launchProximity = Mathf.Max(_launchProximityConfig.Value, 0f);
        _launchProximitySq = launchProximity * launchProximity;
        _etaWindow = Mathf.Max(_etaWindowConfig.Value, 0f);
        _cruiseRetargeting = _cruiseRetargetingConfig.Value;
        var retargetRange = Mathf.Max(_cruiseRetargetRangeConfig.Value, 0f);
        _cruiseRetargetRangeSq = retargetRange * retargetRange;
    }
    
    private static MissileState CreateState(Missile _) => new();
    private static MissileState GetState(Missile missile) => MissileStates.GetValue(missile, CreateState);
    
    private static float GetFirstStaggeredUpdateTime(float interval, ref int nextBucket)
    {
        if (interval <= 0f)
            return Time.timeSinceLevelLoad;
        
        var bucketCount = Mathf.Max(1, Mathf.RoundToInt(interval / Time.fixedDeltaTime));
        var bucket = nextBucket++ % bucketCount;
        var phase = bucket * (interval / bucketCount);
        return Time.timeSinceLevelLoad + interval + phase;
    }
    
    [HarmonyPatch]
    internal static class CruiseMissileFormationPatches
    {
        private static int GetCruiseGuidanceBuckets() =>
            Mathf.Max(1, Mathf.RoundToInt(CruiseGuidanceInterval / Time.fixedDeltaTime));
        
        [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), nameof(OpticalSeekerCruiseMissile.Initialize))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void CruiseMissileInitializePostfix(OpticalSeekerCruiseMissile __instance)
        {
            var bucketCount = GetCruiseGuidanceBuckets();
            var bucket = _nextCruiseGuidanceBucket++ % bucketCount;
            var phase = bucket * (CruiseGuidanceInterval / bucketCount);
            __instance.lastTerminalCheck =
                Time.timeSinceLevelLoad + __instance.guidanceDelay - CruiseGuidanceInterval + phase;
            if (_launchProximitySq <= 0f && _etaWindow <= 0f)
                return;
            
            var missile = __instance.missile;
            var launchPosition = missile.rb.position.ToGlobalPosition();
            var state = GetState(missile);
            if (_launchProximitySq > 0f)
            {
                state.LaunchPosition = launchPosition;
                state.HasLaunchPosition = true;
            }
            
            if (!(_etaWindow > 0f))
                return;
            
            var targetDelta = __instance.knownPos - launchPosition;
            var distance = targetDelta.magnitude;
            var estimatedSpeed = Mathf.Max(missile.GetWeaponInfo().maxSpeed, 100f);
            state.EstimatedArrivalTime = Time.timeSinceLevelLoad + __instance.guidanceDelay + distance / estimatedSpeed;
            state.HasEstimatedArrivalTime = true;
        }
        
        [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), nameof(OpticalSeekerCruiseMissile.TerrainWaypoint))]
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static bool OpticalSeekerCruiseMissileTerrainWaypointPrefix(OpticalSeekerCruiseMissile __instance,
            // ReSharper disable once InconsistentNaming
            ref GlobalPosition __result, GlobalPosition destination)
        {
            var selfMissile = __instance.missile;
            var selfRb = selfMissile.rb;
            var selfPosition = selfRb.position;
            var selfForward = __instance.transform.forward;
            var selfGlobalPosition = selfPosition.ToGlobalPosition();
            var formationSpacingSq = __instance.formationSpacing * __instance.formationSpacing;
            var selfOwnerId = selfMissile.ownerID;
            var selfWeaponInfo = selfMissile.GetWeaponInfo();
            MissileState? selfState = null;
            if (_launchProximitySq > 0f || _etaWindow > 0f)
                MissileStates.TryGetValue(selfMissile, out selfState);
#pragma warning disable Harmony003
            destination.y = Mathf.Max(destination.y, Datum.LocalSeaY + __instance.altitudeTarget);
#pragma warning restore Harmony003
            var target = destination - selfGlobalPosition;
            var velocity = selfMissile.rb.velocity;
            velocity.y = 0f;
            target = Vector3.RotateTowards(velocity.normalized, target, 0.17453292f, 0f);
            var throttle = 1f;
            if (selfMissile.NetworkHQ != null)
                foreach (var otherMissile in selfMissile.NetworkHQ.GetCruiseMissiles())
                {
                    if (otherMissile == null || otherMissile == selfMissile)
                        continue;
                    
                    // Always require the same cruise missile type
                    if (!ReferenceEquals(otherMissile.GetWeaponInfo(), selfWeaponInfo))
                        continue;
                    
                    MissileState? otherState = null;
                    
                    // Launch-source grouping
                    var sourceCompatible = (!_groupByOwner && _launchProximitySq <= 0f)
                                           || (_groupByOwner && otherMissile.ownerID == selfOwnerId);
                    if (!sourceCompatible && _launchProximitySq > 0f && selfState is { HasLaunchPosition: true })
                        if (MissileStates.TryGetValue(otherMissile, out otherState) && otherState.HasLaunchPosition)
                        {
                            var launchDelta = selfState.LaunchPosition - otherState.LaunchPosition;
                            if (launchDelta.sqrMagnitude <= _launchProximitySq)
                                sourceCompatible = true;
                        }
                    
                    if (!sourceCompatible)
                        continue;
                    
                    // ETA grouping
                    if (_etaWindow > 0f)
                    {
                        if (selfState == null || !selfState.HasEstimatedArrivalTime)
                            continue;
                        
                        if (otherState == null && !MissileStates.TryGetValue(otherMissile, out otherState))
                            continue;
                        
                        if (!otherState.HasEstimatedArrivalTime)
                            continue;
                        
                        if (Mathf.Abs(otherState.EstimatedArrivalTime - selfState.EstimatedArrivalTime) > _etaWindow)
                            continue;
                    }
                    
                    var otherMissilePosition = otherMissile.rb.position;
                    var separation = selfPosition - otherMissilePosition;
                    var distanceSq = separation.sqrMagnitude;
                    if (distanceSq > 5000f * 5000f)
                        continue;
                    
                    var normalized = separation.normalized;
                    var spacing = formationSpacingSq / distanceSq;
                    spacing = Mathf.Min(spacing, 0.03f);
                    separation.y = Mathf.Max(separation.y, 0f);
                    target += separation.normalized * spacing;
                    var alignment = Vector3.Dot(-normalized, selfForward);
                    throttle += alignment * 0.03f;
                }
            
            selfMissile.SetThrottle(Mathf.Clamp(throttle, 0.8f, 1f));
            var num3 = Mathf.Max(selfMissile.speed, 100f) * 6f;
            target.y = 0f;
            var vector2 = selfPosition + target.normalized * num3;
            vector2.y = Datum.LocalSeaY;
            if (Physics.Linecast(vector2 + Vector3.up * 5000f, vector2 - Vector3.up * 5000f, out var hitInfo,
                    PhysicsLayers.StaticsMask | PhysicsLayers.ExclusionZonesMask))
            {
                vector2 = hitInfo.point;
                vector2.y = Mathf.Max(vector2.y, Datum.LocalSeaY);
            }
            else
            {
                vector2.y = Datum.LocalSeaY;
            }
            
            vector2 += Vector3.up * __instance.altitudeTarget;
            if (selfMissile.radarAlt < __instance.altitudeTarget * 2f)
            {
                var num4 = __instance.altitudeTarget - (selfMissile.radarAlt + selfMissile.rb.velocity.y * 4f);
                __instance.altitudeTrim += num4;
                __instance.altitudeTrim = Mathf.Max(__instance.altitudeTrim, 0f);
                vector2 += __instance.altitudeTrim * Vector3.up;
            }
            else
            {
                __instance.altitudeTrim = 0f;
            }
            
            var vector3 = Vector3.Lerp(__instance.terrainClearVector, vector2 - selfPosition, 0.8f);
            if (!Physics.Linecast(selfPosition - Vector3.up * 2f, selfPosition + vector3 * 0.9f,
                    PhysicsLayers.StaticsMask | PhysicsLayers.ExclusionZonesMask))
                __instance.terrainClearVector = vector3;
            else
                __instance.terrainClearVector = Vector3.Lerp(__instance.terrainClearVector, Vector3.up * 1000f, 0.1f);
            var num5 = selfPosition.y - Datum.LocalSeaY;
            __instance.terrainClearVector.y =
                Mathf.Max(__instance.terrainClearVector.y, 0f - (num5 - __instance.altitudeTarget));
            
            __result = selfGlobalPosition + __instance.terrainClearVector;
            return false;
        }
        
        [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), nameof(OpticalSeekerCruiseMissile.UpdateTargetParameters))]
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static bool CruiseMissileUpdateTargetParametersPrefix(OpticalSeekerCruiseMissile __instance)
        {
            if (__instance.terminalMode)
                return true;
            
            return Time.timeSinceLevelLoad - __instance.lastTerminalCheck >= CruiseGuidanceInterval;
        }
    }
    
    [HarmonyPatch]
    internal static class MissileEnvironmentPatches
    {
        [HarmonyPatch(typeof(Missile), nameof(Missile.ApplyAero))]
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static bool MissileApplyAeroPrefix(Missile __instance)
        {
            if (WindSampleInterval <= 0f)
#pragma warning disable CS0162 // Unreachable code detected
                return true;
#pragma warning restore CS0162 // Unreachable code detected
            
            var state = GetState(__instance);
            var now = Time.timeSinceLevelLoad;
            var rb = __instance.rb;
            if (!state.HasWind)
            {
                state.Wind = NetworkSceneSingleton<LevelInfo>.i.GetWind();
                state.HasWind = true;
                state.NextWindUpdate = GetFirstStaggeredUpdateTime(WindSampleInterval, ref _nextWindBucket);
            }
            else if (now >= state.NextWindUpdate)
            {
                state.Wind = NetworkSceneSingleton<LevelInfo>.i.GetWind(rb.position.ToGlobalPosition());
                state.NextWindUpdate = now + WindSampleInterval;
            }
            
            var xform = __instance.transform;
            var forward = xform.forward;
            var relativeAirVelocity = rb.velocity - state.Wind;
            var sqrMagnitude = relativeAirVelocity.sqrMagnitude;
            var normalized =
                Vector3.Cross(Vector3.Cross(forward, relativeAirVelocity), relativeAirVelocity).normalized;
            var angle = Mathf.PI / 180f * Vector3.Angle(forward, relativeAirVelocity);
            var liftCoef = __instance.liftCurve.Evaluate(angle);
            var drag = __instance.dragCurve.Evaluate(angle) * __instance.airDensity * sqrMagnitude * 0.5f *
                       __instance.currentFinArea;
            var lift = liftCoef * __instance.airDensity * sqrMagnitude * -0.5f * __instance.currentFinArea;
            var dragForce = -relativeAirVelocity.normalized * drag;
            if (__instance.supersonicDrag > 0f)
            {
                var speedOfSound = LevelInfo.GetSpeedOfSound(rb.position.GlobalY());
                const float transonicRange = 0.1f;
                if (__instance.speed > (1f + transonicRange) * speedOfSound)
                {
                    dragForce *= 1f + __instance.supersonicDrag;
                }
                else if (__instance.speed > (1f - transonicRange) * speedOfSound)
                {
                    var peakDrag = __instance.supersonicDrag + 0.15f;
                    var distance = Mathf.Min(Mathf.Abs((speedOfSound - __instance.speed) / speedOfSound),
                        transonicRange);
                    var ratio = (transonicRange - distance) / transonicRange;
                    dragForce *= 1f + ratio * ratio * ratio * peakDrag;
                }
            }
            
            var liftForce = normalized * lift;
            rb.AddForce(liftForce + dragForce);
            var torque = __instance.inputs * __instance.torque;
            if (__instance.maxTurnRate > 0f || __instance.gLimit > 0f)
            {
                var maxRate = Mathf.Min(__instance.maxTurnRate * (Mathf.PI / 180f),
                    9.81f * __instance.gLimit / Mathf.Max(__instance.speed, 1f));
                var nextAngularVelocity = __instance.localAngularVel + torque * Time.fixedDeltaTime;
                var pitchExcess = Mathf.Max(Mathf.Abs(nextAngularVelocity.x) - maxRate, 0f);
                var yawExcess = Mathf.Max(Mathf.Abs(nextAngularVelocity.y) - maxRate, 0f);
                torque -= new Vector3(Mathf.Sign(torque.x) * pitchExcess / Time.fixedDeltaTime,
                    Mathf.Sign(torque.y) * yawExcess / Time.fixedDeltaTime, 0f);
            }
            
            rb.AddRelativeTorque(torque, ForceMode.Acceleration);
            return false;
        }
        
        [HarmonyPatch(typeof(Missile), nameof(Missile.ServerFixedUpdate))]
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static bool MissileServerFixedUpdatePrefix(Missile __instance)
        {
            var state = GetState(__instance);
            var now = Time.timeSinceLevelLoad;
            if (!state.HasAirDensity)
            {
                state.AirDensity = LevelInfo.GetAirDensity(__instance.rb.position.GlobalY());
                state.HasAirDensity = true;
                state.NextAirDensityUpdate =
                    GetFirstStaggeredUpdateTime(AirDensitySampleInterval, ref _nextAirDensityBucket);
            }
            else if (now >= state.NextAirDensityUpdate)
            {
                state.AirDensity = LevelInfo.GetAirDensity(__instance.rb.position.GlobalY());
                state.NextAirDensityUpdate = now + AirDensitySampleInterval;
            }
            
            __instance.airDensity = state.AirDensity;
            __instance.seeker?.Seek();
            __instance.Steering();
            __instance.ApplyAero();
            __instance.DetectCollisions();
            return false;
        }
    }
    
    [HarmonyPatch]
    internal static class MissileLongRangeGuidancePatches
    {
        [HarmonyPatch(typeof(OpticalSeekerShell), nameof(OpticalSeekerShell.SlowChecks))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void OpticalShellSlowChecksPostfix(OpticalSeekerShell __instance)
        {
            var state = GetState(__instance.missile);
            var delta = __instance.knownPos - __instance.missile.GlobalPosition();
            SetFarGuidance(state, delta.sqrMagnitude > LongRangeDistanceSq);
        }
        
        private static void SetFarGuidance(MissileState state, bool far)
        {
            if (state.FarGuidance == far)
                return;
            
            state.FarGuidance = far;
            state.LongGuidanceScheduled = false;
            state.HasCachedAimPoint = false;
        }
        
        [HarmonyPatch(typeof(OpticalSeekerShell), nameof(OpticalSeekerShell.SendTargetInfo))]
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static bool OpticalShellSendTargetInfoPrefix(OpticalSeekerShell __instance)
        {
            var missile = __instance.missile;
            var state = GetState(missile);
            if (!state.FarGuidance)
                return true;
            
            if (__instance.hasVisual && missile.targetID.NotValid) missile.SetTarget(__instance.targetUnit);
            if (!__instance.hasVisual && missile.targetID.IsValid) missile.SetTarget(null);
            if (!state.HasCachedAimPoint || ShouldRefreshLongGuidance(state))
            {
                var ballisticAimPoint = Kinematics.GetBallisticAimPoint(missile, __instance.knownPos,
                    __instance.timeToTarget, __instance.maxTargetSpeed, __instance.trajectoryError,
                    __instance.knownVel);
                ballisticAimPoint -= __instance.timeToTarget * 0.5f * __instance.measuredWind;
                state.CachedAimPoint = ballisticAimPoint;
                state.HasCachedAimPoint = true;
            }
            
            if (PlayerSettings.debugVis && __instance.aimpointDebug != null)
                __instance.aimpointDebug.transform.localPosition = state.CachedAimPoint.AsVector3();
            __instance.timeToTarget -= Time.fixedDeltaTime;
            if (!missile.IsTangible() && missile.owner != null &&
                !FastMath.InRange(missile.owner.GlobalPosition(), missile.GlobalPosition(), 15f))
                missile.SetTangible(true);
            missile.SetAimpoint(state.CachedAimPoint, __instance.knownVel);
            return false;
        }
        
        private static bool ShouldRefreshLongGuidance(MissileState state)
        {
            var now = Time.timeSinceLevelLoad;
            if (!state.LongGuidanceScheduled)
            {
                state.LongGuidanceScheduled = true;
                state.NextLongGuidanceUpdate =
                    GetFirstStaggeredUpdateTime(LongRangeGuidanceInterval, ref _nextGuidanceBucket);
                return true;
            }
            
            if (now < state.NextLongGuidanceUpdate)
                return false;
            
            state.NextLongGuidanceUpdate = now + LongRangeGuidanceInterval;
            return true;
        }
        
        [HarmonyPatch(typeof(OpticalSeeker), nameof(OpticalSeeker.SlowChecks))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void OpticalSeekerSlowChecksPostfix(OpticalSeeker __instance)
        {
            var state = GetState(__instance.missile);
            var delta = __instance.knownPos - __instance.missile.GlobalPosition();
            SetFarGuidance(state, delta.sqrMagnitude > LongRangeDistanceSq);
        }
        
        [HarmonyPatch(typeof(OpticalSeeker), nameof(OpticalSeeker.GetTargetParameters))]
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static bool OpticalSeekerGetTargetParametersPrefix(OpticalSeeker __instance)
        {
            var state = GetState(__instance.missile);
            if (!state.FarGuidance)
                return true;
            
            if (__instance.targetUnit == null || __instance.targetTransform == null)
                return false;
            
            if (Time.timeSinceLevelLoad - __instance.lastOpticalCheck > 0.25f)
            {
                __instance.OpticalCheck();
                __instance.missile.SetTarget(__instance.hasVisual ? __instance.targetUnit : null);
            }
            
            if (__instance.hasVisual)
            {
                if (ShouldRefreshLongGuidance(state))
                    TargetCalc.GetLeadFromMaxTargetSpeed(__instance.targetUnit, __instance.targetTransform,
                        __instance.transform, __instance.knownPos, __instance.maxTargetSpeed, out __instance.knownPos,
                        out __instance.knownVel);
                else
                    __instance.knownPos += __instance.knownVel * Time.fixedDeltaTime;
            }
            else
            {
                __instance.knownPos += __instance.knownVel * Time.fixedDeltaTime;
            }
            
            return false;
        }
    }
    
    [HarmonyPatch]
    internal static class MissileMotorPatches
    {
        [HarmonyPatch(typeof(Missile.Motor), nameof(Missile.Motor.Thrust))]
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static bool MissileMotorThrustPrefix(Missile.Motor __instance, Missile missile, bool localSim,
            // ReSharper disable once InconsistentNaming
            Vector3 inputs, float throttle, ref float __result)
        {
            if (MotorMassUpdateInterval <= 0f)
#pragma warning disable CS0162 // Unreachable code detected
                return true;
#pragma warning restore CS0162 // Unreachable code detected
            
            if (__instance.delayTimer > 0f)
            {
                __instance.delayTimer -= Time.deltaTime;
                if (__instance.startupSource != null && !__instance.startupSource.isPlaying)
                    __instance.startupSource.Play();
                
                __result = 0f;
                return false;
            }
            
            if (!__instance.activated)
            {
                __instance.activated = true;
                __instance.Activate(missile);
            }
            
            var burnedMass = __instance.burnRate * Time.deltaTime;
            __instance.fuelMass -= burnedMass;
            var state = GetState(missile);
            state.PendingMotorMassLoss += burnedMass;
            if (!state.MotorMassScheduled)
            {
                state.MotorMassScheduled = true;
                state.NextMotorMassUpdate = GetFirstStaggeredUpdateTime(MotorMassUpdateInterval, ref _nextMassBucket);
            }
            
            var burnedOut = __instance.fuelMass <= 0f;
            if (burnedOut || Time.timeSinceLevelLoad >= state.NextMotorMassUpdate)
            {
                missile.rb.mass -= state.PendingMotorMassLoss;
                state.PendingMotorMassLoss = 0f;
                if (burnedOut)
                    state.MotorMassScheduled = false;
                else
                    state.NextMotorMassUpdate = Time.timeSinceLevelLoad + MotorMassUpdateInterval;
            }
            
            if (burnedOut)
                __instance.Burnout(false);
            if (__instance.thrustVectoring > 0f)
                foreach (var particles in __instance.particleSystems)
#pragma warning disable Harmony003
                    particles.transform.localEulerAngles = new Vector3(inputs.x * __instance.thrustVectoring,
                        180f - inputs.y * __instance.thrustVectoring, 0f);
#pragma warning restore Harmony003
            if (localSim && missile.speed < __instance.topSpeed)
                missile.rb.AddForce(__instance.thrust * throttle * missile.transform.forward);
            __result = __instance.thrust;
            return false;
        }
    }
    
    [HarmonyPatch]
    internal static class CruiseMissileRetargetPatches
    {
        private static readonly Dictionary<Unit, RetargetCandidate> RetargetCandidates = new(16);
        
        [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), nameof(OpticalSeekerCruiseMissile.SlowChecks))]
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static void CruiseMissileSlowChecksPrefix(OpticalSeekerCruiseMissile __instance)
        {
            if (!_cruiseRetargeting)
                return;
            
            var missile = __instance.missile;
            if (missile == null || missile.disabled || __instance.terminalMode)
                return;
            
            if (__instance.targetUnit != null && !__instance.targetUnit.disabled)
                return;
            
            TryRetargetCruiseMissile(__instance);
        }
        
        private static bool TryRetargetCruiseMissile(OpticalSeekerCruiseMissile seeker)
        {
            var missile = seeker.missile;
            var hq = missile.NetworkHQ;
            if (hq == null || _cruiseRetargetRangeSq <= 0f)
                return false;
            
            var selfPosition = missile.rb.position;
            var selfOwnerId = missile.ownerID;
            var selfWeaponInfo = missile.GetWeaponInfo();
            RetargetCandidates.Clear();
            foreach (var otherMissile in hq.GetCruiseMissiles())
            {
                if (otherMissile == null || otherMissile == missile || otherMissile.disabled)
                    continue;
                
                if (otherMissile.ownerID != selfOwnerId)
                    continue;
                
                if (!ReferenceEquals(otherMissile.GetWeaponInfo(), selfWeaponInfo))
                    continue;
                
                var separation = otherMissile.rb.position - selfPosition;
                if (separation.sqrMagnitude > _cruiseRetargetRangeSq)
                    continue;
                
                if (otherMissile.seeker is not OpticalSeekerCruiseMissile otherSeeker || otherSeeker.terminalMode)
                    continue;
                
                var target = otherSeeker.targetUnit;
                if (target == null || target.disabled || target.NetworkHQ == null)
                    continue;
                
                if (target.NetworkHQ == hq)
                    continue;
                
                if (seeker.targetHQAtLaunch != null && target.NetworkHQ != seeker.targetHQAtLaunch)
                    continue;
                
                if (RetargetCandidates.TryGetValue(target, out var candidate))
                {
                    candidate.MissileCount++;
                    RetargetCandidates[target] = candidate;
                }
                else
                {
                    RetargetCandidates.Add(target, new RetargetCandidate
                    {
                        MissileCount = 1,
                        KnownPosition = otherSeeker.knownPos
                    });
                }
            }
            
            if (RetargetCandidates.Count == 0)
                return false;
            
            Unit? chosenTarget = null;
            var chosenCandidate = default(RetargetCandidate);
            var lowestMissileCount = int.MaxValue;
            foreach (var pair in RetargetCandidates)
            {
                if (pair.Value.MissileCount >= lowestMissileCount)
                    continue;
                
                lowestMissileCount = pair.Value.MissileCount;
                chosenTarget = pair.Key;
                chosenCandidate = pair.Value;
            }
            
            if (chosenTarget == null)
                return false;
            
            ApplyCruiseRetarget(seeker, chosenTarget, chosenCandidate.KnownPosition);
            return true;
        }
        
        private static void ApplyCruiseRetarget(OpticalSeekerCruiseMissile seeker, Unit newTarget,
            GlobalPosition knownPosition)
        {
            var missile = seeker.missile;
            var previousKnownPosition = seeker.knownPos;
            var headingToFinalTarget = FastMath.InRange(seeker.aimPos, previousKnownPosition, 10f);
            seeker.targetUnit = newTarget;
            seeker.targetHQAtLaunch = newTarget.NetworkHQ;
            seeker.knownPos = knownPosition;
            seeker.knownVel = Vector3.zero;
            if (headingToFinalTarget) seeker.aimPos = knownPosition;
            missile.SetTarget(newTarget);
        }
        
        private struct RetargetCandidate
        {
            public int MissileCount;
            public GlobalPosition KnownPosition;
        }
    }
    
    private sealed class MissileState
    {
        // I wanted to structure this but Rider code clean-up said no
        public float AirDensity;
        public GlobalPosition CachedAimPoint;
        public float EstimatedArrivalTime;
        public bool FarGuidance;
        public bool HasAirDensity;
        public bool HasCachedAimPoint;
        public bool HasEstimatedArrivalTime;
        public bool HasLaunchPosition;
        public bool HasWind;
        public GlobalPosition LaunchPosition;
        public bool LongGuidanceScheduled;
        public bool MotorMassScheduled;
        public float NextAirDensityUpdate;
        public float NextLongGuidanceUpdate;
        public float NextMotorMassUpdate;
        public float NextWindUpdate;
        public float PendingMotorMassLoss;
        public Vector3 Wind;
    }
}