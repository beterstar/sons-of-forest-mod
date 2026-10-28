using HarmonyLib;
using Sons.Ai.Vail;

namespace KelvinTwo;

/// <summary>Switches for the patches below, set from config at startup.</summary>
internal static class CloneGuards
{
    internal static bool IgnoreHits = true;

    // Only the host simulates Kelvin. Clients leave hits alone so the game's own
    // network path is untouched; the host decides what actually happens.
    internal static bool Applies(VailActor actor) => KelvinTools.IsHost && CloneMarker.IsClone(actor);
}

/// <summary>Backup for immortality: the clone ignores direct damage calls.</summary>
[HarmonyPatch(typeof(VailActor), nameof(VailActor.DamageActor))]
internal static class DamageActorPatch
{
    private static bool Prefix(VailActor __instance, ref bool __result)
    {
        if (!CloneGuards.Applies(__instance)) return true;
        __result = false;
        return false;
    }
}

/// <summary>
/// Getting hit is what makes Kelvin upset with the player and refuse orders.
/// The clone simply doesn't register hits (no anger, fear or sentiment loss).
/// </summary>
[HarmonyPatch(typeof(VailActor), nameof(VailActor.ReceivedDamage))]
internal static class ReceivedDamagePatch
{
    private static bool Prefix(VailActor __instance) =>
        !(CloneGuards.IgnoreHits && CloneGuards.Applies(__instance));
}

[HarmonyPatch(typeof(VailActor), nameof(VailActor.UpdateDamageTakenStats))]
internal static class UpdateDamageTakenStatsPatch
{
    private static bool Prefix(VailActor __instance) =>
        !(CloneGuards.IgnoreHits && CloneGuards.Applies(__instance));
}
