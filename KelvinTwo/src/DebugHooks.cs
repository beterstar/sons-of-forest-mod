using HarmonyLib;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Ai.Vail.StimuliTypes;
using Sons.Gameplay;
using UnityEngine;

namespace KelvinTwo;

/// <summary>
/// debugLog only: logs the game's own fetch / holder / drop decisions for both Kelvins
/// ("K1" original, "K2" clone), so a report like "he threw the sticks away" can be
/// traced to the exact step. Read-only: never changes what the game does.
/// </summary>
internal static class DebugHooks
{
    internal static bool Enabled;

    private static void Log(Robby robby, string what)
    {
        if (!Enabled || robby == null) return;
        try
        {
            var actor = robby.GetComponentInParent<VailActor>();
            if (actor == null) actor = robby.GetComponentInChildren<VailActor>();
            string who = actor != null && CloneMarker.IsClone(actor) ? "K2" : "K1";
            string carry = actor != null ? $"{actor.GetInventoryItemType()} x{actor.GetInventoryCountAll()}" : "?";
            string drop = "none";
            var d = robby._dropStimuliInstance;
            if (d != null && actor != null)
            {
                var target = d._dropTarget != null ? d._dropTarget.transform.position : d.transform.position;
                drop = $"{(d._targetIsHolder ? "holder" : "spot")} {Vector3.Distance(actor.transform.position, target):0} m";
            }
            RLog.Msg($"[KelvinTwo] {who} {what} | carrying {carry} | drop-off {drop}");
        }
        catch (System.Exception ex)
        {
            RLog.Msg($"[KelvinTwo] debug hook {what}: {ex.Message}");
        }
    }

    /// <summary>Structure recipe ids, for the console's "placestructure &lt;id&gt;" when testing fill-holder orders.</summary>
    internal static void LogHolderRecipes()
    {
        if (!Enabled) return;
        var mgr = Sons.Crafting.Structures.ScrewStructureManager._instance;
        if (mgr == null || mgr._database == null) return;
        var sb = new System.Text.StringBuilder();
        foreach (var r in mgr._database._recipes)
            if (r != null)
                sb.Append($" {r.Name}={r._id}");
        RLog.Msg($"[KelvinTwo] structure recipes:{sb} | hang glider item id {Sons.Items.Core.ItemDatabase.HangGliderId}");
    }

    /// <summary>
    /// Console test helper (debugLog only): "k2order get sticks fillholder" gives Kelvin 2 the same
    /// order the pad would, so K1 and K2 can be compared with identical orders.
    /// </summary>
    internal static void GiveOrder(bool clone, string args)
    {
        if (!Enabled || !KelvinTools.IsHost) { RLog.Msg("[KelvinTwo] k*order needs debugLog and host"); return; }
        var parts = (args ?? "").ToLowerInvariant().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        Robby robby = null;
        VailActor body = null;
        foreach (var a in KelvinTools.ActiveKelvins())
            if (CloneMarker.IsClone(a) == clone) { body = a; robby = a.GetComponentInChildren<Robby>(true); break; }
        if (robby == null || parts.Length == 0) { RLog.Msg("[KelvinTwo] k*order: no Kelvin body nearby or no order"); return; }

        var from = TheForest.Utils.LocalPlayer.Transform;
        switch (parts[0])
        {
            case "follow": robby.ServerGiveOrders(Robby.OrderType.Follow, 0, 0, from); break;
            case "stay": robby.ServerGiveOrders(Robby.OrderType.StayHere, 0, 0, from); break;
            // Reproduces the old warp freeze (route dropped mid-walk) to test the stuck guard.
            case "breakpath":
                body.ClearPath();
                RLog.Msg("[KelvinTwo] route cleared");
                break;
            case "get" when parts.Length >= 3:
                var orders = robby._orderData._getItemOrders;
                int index = -1;
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < orders.Count; i++)
                {
                    var o = orders[i];
                    names.Append($" {i}:{o._itemType}");
                    if (index < 0 && o._itemType != null && o._itemType.ToLowerInvariant().Contains(parts[1])) index = i;
                }
                if (index < 0 || !System.Enum.TryParse<Robby.DropLocationType>(parts[2], true, out var drop))
                {
                    RLog.Msg($"[KelvinTwo] k*order get <item> <DropHere|GiveToMe|FillHolder|FillLogSled>; items:{names}");
                    return;
                }
                var g = orders[index];
                RLog.Msg($"[KelvinTwo] giving {(clone ? "K2" : "K1")} get {g._itemType} -> {drop} (expires {g._expireTimeMinutes} min, " +
                         $"restart {g._restartAfterExpire}, needs {g._needsStimuli} within {g._maxStimuliRange} m)");
                robby.ServerGiveOrders(Robby.OrderType.Get, index, (int)drop, from);
                break;
            default: RLog.Msg("[KelvinTwo] k*order follow | stay | breakpath | get <item> <drop>"); break;
        }
    }

    [HarmonyPatch(typeof(Robby), nameof(Robby.DropItemsNotNeededForTask))]
    private static class DropNotNeeded
    {
        private static void Prefix(Robby __instance) => Log(__instance, "DropItemsNotNeededForTask");
    }

    [HarmonyPatch(typeof(Robby), nameof(Robby.DropNow))]
    private static class DropNow
    {
        private static void Prefix(Robby __instance) => Log(__instance, "DropNow");
    }

    [HarmonyPatch(typeof(Robby), nameof(Robby.FindValidHolders))]
    private static class FindValidHolders
    {
        private static void Postfix(Robby __instance, string vailItemType, bool setDropLocation, bool __result) =>
            Log(__instance, $"FindValidHolders({vailItemType}, set={setDropLocation}) = {__result}");
    }

    [HarmonyPatch(typeof(Robby), nameof(Robby.FindHoldersNeedingItems))]
    private static class FindHoldersNeedingItems
    {
        private static void Postfix(Robby __instance, bool setDropLocation, bool __result) =>
            Log(__instance, $"FindHoldersNeedingItems(set={setDropLocation}) = {__result}");
    }

    [HarmonyPatch(typeof(Robby), nameof(Robby.Sons_Ai_Vail_IRobbyCallbacks_OnAddedItemsToHolder))]
    private static class AddedToHolder
    {
        private static void Postfix(Robby __instance, bool holderMissing) =>
            Log(__instance, $"OnAddedItemsToHolder(holderMissing={holderMissing})");
    }

    [HarmonyPatch(typeof(Robby), nameof(Robby.Sons_Ai_Vail_IRobbyCallbacks_OnDroppedItems))]
    private static class DroppedItems
    {
        private static void Prefix(Robby __instance) => Log(__instance, "OnDroppedItems");
    }

    [HarmonyPatch(typeof(Robby), nameof(Robby.OnOrderCompleted))]
    private static class OrderCompleted
    {
        private static void Prefix(Robby __instance, bool restartAfterRest) =>
            Log(__instance, $"OnOrderCompleted(restartAfterRest={restartAfterRest})");
    }

    [HarmonyPatch(typeof(Robby), nameof(Robby.StartGetItemOrder))]
    private static class StartGet
    {
        private static void Postfix(Robby __instance, Robby.RobbyGetOrder getOrder, Robby.DropLocationType orderDropLocation) =>
            Log(__instance, $"StartGetItemOrder(drop at {orderDropLocation})");
    }

    [HarmonyPatch(typeof(RobbyDropStimuli), nameof(RobbyDropStimuli.OnDropEvent))]
    private static class DropEvent
    {
        private static void Postfix(RobbyDropStimuli __instance, VailActor actor, int __result)
        {
            if (!Enabled || actor == null) return;
            string who = CloneMarker.IsClone(actor) ? "K2" : "K1";
            var target = __instance._dropTarget != null ? __instance._dropTarget.transform.position : __instance.transform.position;
            RLog.Msg($"[KelvinTwo] {who} OnDropEvent = {__result} | holder={__instance._targetIsHolder} " +
                     $"dist {Vector3.Distance(actor.transform.position, target):0.0} m, stop {__instance.GetMoveStopDistance():0.0} m");
        }
    }
}
