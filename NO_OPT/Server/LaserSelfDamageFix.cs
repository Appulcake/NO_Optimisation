using UnityEngine;

namespace NO_OPT.Server;

public static class LaserSelfDamageFix
{
    // Re-implemented from GunSelfDamageFix from PRF to prevent possible desync self-damage
    // https://github.com/Appulcake/PauelsRandomFixes/blob/master/PRF/Fixes/GunSelfDamageFix.cs
    //
    // With PRF, the vanilla Laser.FixedUpdate() is already patched to use this
    
    private const int RaycastBufferSize = 64;
    private static readonly RaycastHit[] RaycastBuffer = new RaycastHit[RaycastBufferSize];
    
    public static bool LinecastIgnoringOwner(Vector3 start, Vector3 end, out RaycastHit hitInfo, int layerMask,
        Unit laserAttachedUnit)
    {
        var hit = Physics.Linecast(start, end, out hitInfo, layerMask);
        
        if (!hit || laserAttachedUnit == null || hitInfo.collider == null)
            return hit;
        
        if (!ColliderBelongsToOwner(hitInfo.collider, laserAttachedUnit))
            return true;
        
        return TryFindNearestNonOwnerHit(start, end, layerMask, laserAttachedUnit, out hitInfo);
    }
    
    private static bool TryFindNearestNonOwnerHit(Vector3 start, Vector3 end, int layerMask, Unit owner,
        out RaycastHit hitInfo)
    {
        hitInfo = default;
        
        var trace = end - start;
        var traceLength = trace.magnitude;
        
        if (traceLength <= Mathf.Epsilon)
            return false;
        
        var direction = trace / traceLength;
        
        var hitCount = Physics.RaycastNonAlloc(start, direction, RaycastBuffer, traceLength, layerMask,
            QueryTriggerInteraction.UseGlobal);
        
        var foundNonOwnerHit = false;
        var nearestDistance = float.PositiveInfinity;
        
        for (var i = 0; i < hitCount; i++)
        {
            var candidate = RaycastBuffer[i];
            
            if (candidate.collider == null || candidate.distance >= nearestDistance ||
                ColliderBelongsToOwner(candidate.collider, owner)) continue;
            
            nearestDistance = candidate.distance;
            hitInfo = candidate;
            foundNonOwnerHit = true;
        }
        
        return foundNonOwnerHit;
    }
    
    private static bool ColliderBelongsToOwner(Collider collider, Unit owner)
    {
        var damageable = collider.gameObject.GetComponent<IDamageable>();
        
        if (damageable != null && SameUnit(damageable.GetUnit(), owner))
            return true;
        
        var colliderTransform = collider.transform;
        var ownerTransform = owner.transform;
        
        if (colliderTransform == ownerTransform || colliderTransform.IsChildOf(ownerTransform))
            return true;
        
        if (owner.rb != null && collider.attachedRigidbody == owner.rb)
            return true;
        
        var parentUnit = collider.GetComponentInParent<Unit>();
        
        return SameUnit(parentUnit, owner);
    }
    
    private static bool SameUnit(Unit first, Unit second)
    {
        if (first == null || second == null)
            return false;
        
        if (first == second)
            return true;
        
        return !first.persistentID.Equals(PersistentID.None) && first.persistentID.Equals(second.persistentID);
    }
}