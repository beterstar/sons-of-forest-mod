using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Ai.Vail.StimuliQueries;
using Sons.Ai.Vail.StimuliTypes;

namespace KelvinTwo;

/// <summary>
/// Makes Kelvin 2 look much farther for things to fetch, so "get logs/sticks/stones..."
/// still works when nothing is left nearby. The AI asks each search query how far to look
/// (<c>GetDistance(actor, ...)</c>); for the clone we multiply that - but only:
/// <list type="bullet">
/// <item>for "search" queries (Far / Distance / World). Near, VeryClose and NotWithinFar are
///   left alone: they decide things like "close enough to grab", and stretching those would
///   make him think he has arrived when he hasn't (the classic stuck Kelvin).</item>
/// <item>for things to collect (every pickup, trees to chop, food). Drop-off spots such as
///   log holders keep their normal range, so he still delivers where he normally would.</item>
/// </list>
/// Host only, clone only - the original Kelvin is untouched.
/// </summary>
internal static class SearchWidening
{
    private const int DiagnosticLines = 6;

    internal static float Multiplier = 3f;

    private static readonly Dictionary<IntPtr, bool> FetchTypeCache = new();
    private static int _diagnosticsLeft = DiagnosticLines;
    private static bool _loggedError;

    [HarmonyPatch(typeof(StimuliQueryFar), nameof(StimuliQueryFar.GetDistance))]
    private static class Far
    {
        private static void Postfix(StimuliQueryTargetMonoBehaviour __instance, VailActor actor, ref float __result) =>
            Widen(__instance, actor, ref __result, "Far");
    }

    [HarmonyPatch(typeof(StimuliQueryDistance), nameof(StimuliQueryDistance.GetDistance))]
    private static class Distance
    {
        private static void Postfix(StimuliQueryTargetMonoBehaviour __instance, VailActor actor, ref float __result) =>
            Widen(__instance, actor, ref __result, "Distance");
    }

    [HarmonyPatch(typeof(StimuliQueryWorld), nameof(StimuliQueryWorld.GetDistance))]
    private static class World
    {
        private static void Postfix(StimuliQueryTargetMonoBehaviour __instance, VailActor actor, ref float __result) =>
            Widen(__instance, actor, ref __result, "World");
    }

    private static void Widen(StimuliQueryTargetMonoBehaviour query, VailActor actor, ref float distance, string kind)
    {
        // Hot path for every AI in the game: cheapest checks first.
        if (Multiplier <= 1f || !CloneGuards.Applies(actor)) return;
        try
        {
            var type = query._stimuliType;
            if (type == null || !IsFetchTarget(type) || distance <= 0f) return;

            float before = distance;
            distance = before * Multiplier;
            if (_diagnosticsLeft <= 0) return;
            _diagnosticsLeft--;
            RLog.Msg($"[KelvinTwo] Kelvin 2 searches farther for {type.Name} ({kind}): {before:0} -> {distance:0} m");
        }
        catch (Exception ex)
        {
            if (_loggedError) return;
            _loggedError = true;
            RLog.Error("[KelvinTwo] search widening failed (further errors hidden): " + ex);
        }
    }

    private static bool IsFetchTarget(Il2CppSystem.Type type)
    {
        if (FetchTypeCache.TryGetValue(type.Pointer, out var known)) return known;

        bool fetch = Il2CppType.Of<PickupStimuli>().IsAssignableFrom(type)   // logs, sticks, stones, rocks, arrows...
                     || Il2CppType.Of<TreeStimuli>().IsAssignableFrom(type)  // trees he fells for logs
                     || Il2CppType.Of<BerryBushStimuli>().IsAssignableFrom(type)
                     || Il2CppType.Of<PlantFoodStimuli>().IsAssignableFrom(type)
                     || Il2CppType.Of<ConsumableStimuli>().IsAssignableFrom(type)
                     || Il2CppType.Of<MeatStimuli>().IsAssignableFrom(type);
        FetchTypeCache[type.Pointer] = fetch;
        return fetch;
    }
}
