using HarmonyLib;
using NO_OPT.Modules;

namespace NO_OPT.Client;

[OptimisationModule(ModuleScope.Client, "--- Client ---", "4. Datalink Target Search Optimisation", 
    true,"Removes redundant Turret.DatalinkTargetSearch on remote clients, which causes clients to "
         + "do LoS checks for all units' turrets on map, not just your own, lowering FPS for no reason.")]
internal sealed class ClientDatalinkOptimisation : OptimisationModule
{
    [HarmonyPatch]
    private static class DatalinkPatches
    {
        // Turret.Turret_OnInitialize() properly restricts TargetAcquisitionMode.DatalinkTargetSearch to server only
        // But Turret.DatalinkTargetSearch() can still call it on client and lower FPS running turret checks like
        // LoS, despite not doing anything further with that information
        
        [HarmonyPatch(typeof(Turret), nameof(Turret.DatalinkTargetSearch))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool TurretDatalinkTargetSearchPrefix(Turret __instance)
        {
            var unit = __instance.GetAttachedUnit();
            return unit?.IsServer == true;
        }
    }
}