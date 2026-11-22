using HarmonyLib;

namespace StandsOfValheim
{
    /// <summary>
    /// Prevents CharacterAnimEvent callbacks (Awake, Speed, TrailOn, TrailOff, Hit, GPower) from crashing
    /// on Aurvandil's Fylgja's cloned visual model, which does not have a Character or ZNetView root component.
    /// </summary>
    [HarmonyPatch(typeof(CharacterAnimEvent))]
    internal static class AnimEventPatches
    {
        private static bool IsStandVisual(CharacterAnimEvent instance) =>
            instance != null && instance.GetComponentInParent<StandFollower>() != null;

        [HarmonyPatch("Awake")]
        [HarmonyPrefix]
        private static bool Awake_Prefix(CharacterAnimEvent __instance) => !IsStandVisual(__instance);

        [HarmonyPatch("Speed")]
        [HarmonyPrefix]
        private static bool Speed_Prefix(CharacterAnimEvent __instance) => !IsStandVisual(__instance);

        [HarmonyPatch("TrailOn")]
        [HarmonyPrefix]
        private static bool TrailOn_Prefix(CharacterAnimEvent __instance) => !IsStandVisual(__instance);

        [HarmonyPatch("TrailOff")]
        [HarmonyPrefix]
        private static bool TrailOff_Prefix(CharacterAnimEvent __instance) => !IsStandVisual(__instance);

        [HarmonyPatch("Hit")]
        [HarmonyPrefix]
        private static bool Hit_Prefix(CharacterAnimEvent __instance) => !IsStandVisual(__instance);

        [HarmonyPatch("GPower")]
        [HarmonyPrefix]
        private static bool GPower_Prefix(CharacterAnimEvent __instance) => !IsStandVisual(__instance);
    }
}
