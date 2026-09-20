using HarmonyLib;

namespace NO_OPT.Server;

internal sealed partial class HeadlessServerOptimisations
{
    [HarmonyPatch]
    private static class EnginePatches
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
}