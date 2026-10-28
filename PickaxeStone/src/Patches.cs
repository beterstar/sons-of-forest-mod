using System;
using HarmonyLib;
using RedLoader;
using Sons.Items;
using Sons.Items.Core;
using TheForest.Utils;
using UnityEngine;

namespace PickaxeStone;

/// <summary>
/// Hooks the held weapon's impact callbacks. Three of them, because which one the game
/// calls for a rock surface isn't known for sure; <see cref="HitCounter"/> makes sure one
/// swing still counts once. All hooked methods take only a Collider (hooking methods with
/// Nullable parameters crashes the game) and every handler swallows its own errors so the
/// weapon itself can never break.
/// </summary>
internal static class WeaponHooks
{
    private const int DiagnosticHits = 12; // ~4 swings: one swing can fire up to 3 callbacks

    private static int _swing = -1;   // -1 until the game tells us about a swing
    private static int _pickaxeId;
    private static bool _pickaxeIdResolved;
    private static bool _loggedError;
    private static int _diagnosticsLeft = DiagnosticHits;

    [HarmonyPatch(typeof(weaponInfo), nameof(weaponInfo.OnNewMeleeAttack))]
    private static class NewSwing
    {
        private static void Postfix() => _swing = _swing < 0 ? 0 : _swing + 1;
    }

    [HarmonyPatch(typeof(weaponInfo), nameof(weaponInfo.DoImpact))]
    private static class Impact
    {
        private static void Postfix(weaponInfo __instance, Collider other) => OnSurfaceHit(__instance, other);
    }

    [HarmonyPatch(typeof(weaponInfo), nameof(weaponInfo.PlaySurfaceImpactEffect))]
    private static class ImpactEffect
    {
        private static void Postfix(weaponInfo __instance, Collider other) => OnSurfaceHit(__instance, other);
    }

    [HarmonyPatch(typeof(weaponInfo), nameof(weaponInfo.PlaySurfaceHitAudio), new[] { typeof(Collider) })]
    private static class ImpactAudio
    {
        private static void Postfix(weaponInfo __instance, Collider other) => OnSurfaceHit(__instance, other);
    }

    private static void OnSurfaceHit(weaponInfo weapon, Collider other)
    {
        var mod = PickaxeStoneMod.Instance;
        if (mod == null || !mod.Cfg.Enabled) return;
        try
        {
            if (!IsLocalPickaxe(weapon)) return;
            bool isRock = RockFilter.IsNaturalRock(other, out var reason);
            LogDiagnostics(other, reason);
            if (!isRock) return;
            if (!mod.Counter.RegisterHit(other.GetInstanceID(), _swing, Time.time)) return;

            var player = LocalPlayer.Transform;
            if (player == null) return;
            var impact = other.bounds.ClosestPoint(weapon.GetWeaponImpactPosition());
            StoneDropper.Drop(StoneDropper.ResolveItemId(mod.Cfg.DropItemId), mod.Cfg.StonesPerDrop, impact, player.position);
        }
        catch (Exception ex)
        {
            if (_loggedError) return;
            _loggedError = true;
            RLog.Error("[PickaxeStone] hit handling failed (further errors hidden): " + ex);
        }
    }

    private static bool IsLocalPickaxe(weaponInfo weapon)
    {
        if (weapon == null || weapon.remotePlayer) return false;

        var inv = weapon.PlayerInv;
        var local = LocalPlayer.Inventory;
        if (inv != null && local != null && inv != local) return false;

        if (!_pickaxeIdResolved)
        {
            _pickaxeIdResolved = true;
            _pickaxeId = ItemDatabaseManager.ItemIdByName("Pickaxe");
            RLog.Msg($"[PickaxeStone] pickaxe item id = {_pickaxeId}");
        }
        if (_pickaxeId > 0 && weapon._itemID == _pickaxeId) return true;
        return weapon.GetComponentInParent<PickaxeItemController>() != null;
    }

    // The mod can't be tested in-game while it's written, so the first few pickaxe hits
    // say what was hit and why it did or didn't count - enough to fix it from the log.
    private static void LogDiagnostics(Collider other, string reason)
    {
        if (_diagnosticsLeft <= 0) return;
        _diagnosticsLeft--;
        var name = other != null ? other.gameObject.name : "null";
        RLog.Msg($"[PickaxeStone] pickaxe hit '{name}': {reason}");
    }
}
