using System;
using Sons.Items.Core;
using TheForest.Utils;
using TheForest.Utils.Items;
using UnityEngine;

namespace SniperRifle
{
    /// <summary>
    /// The player's animator picks the held-weapon animations by the "RightHandHeldItemId" parameter.
    /// Our sniper has its own item id, which has no rifle aim/idle states, so without help the player never
    /// reaches the "aimed and ready" pose and the game refuses to fire. While the sniper is held we report the
    /// rifle's id to the animator instead.
    ///
    /// The game clears that parameter (and "RifleHeld") in ItemAnimatorHashHelper.ApplyAnimVars only while it
    /// still holds the put-away item's own id. Because it holds the rifle's id, putting the sniper away (e.g. the
    /// game swapping to the knife to skin an animal) left empty hands frozen in the rifle pose with nothing
    /// usable (1.0.2). So when the hands are empty after the sniper and the animator is still in the rifle
    /// pose, we put the sniper's id back and let the game's own ApplyAnimVars clear it.
    /// (Hooking ApplyAnimVars with Harmony crashes the game - it has a Nullable parameter - so we call it.)
    /// </summary>
    internal static class AnimFix
    {
        private static readonly int ItemIdParam = Animator.StringToHash("RightHandHeldItemId");
        private static readonly int ItemIdFloatParam = Animator.StringToHash("RightHandHeldItemIdFloat");
        private static readonly int RifleHeldParam = Animator.StringToHash("RifleHeld");
        private const float StaleAfter = 0.3f;

        /// <summary>Tests: notes about stale-pose clean-ups.</summary>
        internal static Action<string> Observer;

        private static bool _lastHeldWasSniper;
        private static float _emptySince = -1f;

        private static void SetId(Animator a, int id)
        {
            a.SetInteger(ItemIdParam, id);
            a.SetFloat(ItemIdFloatParam, id);
        }

        /// <summary>Every frame: keep the swap while the sniper is held, and undo a stale rifle pose.</summary>
        internal static void Apply()
        {
            try
            {
                if (LocalPlayer._instance == null) return;
                var animator = LocalPlayer.Animator;
                if (animator == null) return;
                var item = LocalPlayer.Inventory?.RightHandItem;
                int id = animator.GetInteger(ItemIdParam);

                if (item != null)
                {
                    _emptySince = -1f;
                    _lastHeldWasSniper = item._itemID == SniperItem.Id;
                    if (!_lastHeldWasSniper) return;
                    // Only translate our own id; any other value is the game changing weapons.
                    if (id == SniperItem.Id) SetId(animator, SniperItem.RifleId);
                    else if (id == SniperItem.RifleId && animator.GetFloat(ItemIdFloatParam) != SniperItem.RifleId)
                        animator.SetFloat(ItemIdFloatParam, SniperItem.RifleId);
                    return;
                }

                // Hands empty after the sniper but the animator still in the rifle pose: clear it.
                if (!_lastHeldWasSniper || id != SniperItem.RifleId) { _emptySince = -1f; return; }
                if (_emptySince < 0f) { _emptySince = Time.unscaledTime; return; }
                if (Time.unscaledTime - _emptySince < StaleAfter) return;
                _emptySince = -1f;
                _lastHeldWasSniper = false;
                ClearRiflePose(animator);
            }
            catch (Exception e) { SniperRifleMod.LogOnce("AnimFix", e); }
        }

        private static void ClearRiflePose(Animator animator)
        {
            string how = "game";
            try
            {
                var data = ItemDatabaseManager.ItemById(SniperItem.Id);
                SetId(animator, SniperItem.Id);
                if (data != null) ItemAnimatorHashHelper.ApplyAnimVars(data, false, animator, new Il2CppSystem.Nullable<EquipmentSlot>());
            }
            catch (Exception e) { SniperRifleMod.LogOnce("AnimFix.Clear", e); }
            int id = animator.GetInteger(ItemIdParam);
            if (id == SniperItem.Id || id == SniperItem.RifleId || animator.GetBool(RifleHeldParam))
            {
                // the game's own clean-up didn't do it: reset the hand by hand
                how = "manual";
                animator.SetBool(RifleHeldParam, false);
                SetId(animator, -1);
            }
            string note = $"cleared a stale rifle pose ({how}): RightHandHeldItemId={animator.GetInteger(ItemIdParam)} RifleHeld={animator.GetBool(RifleHeldParam)}";
            Observer?.Invoke(note);
            if (SniperRifleMod.Cfg.Debug) RedLoader.RLog.Msg("[SniperRifle] " + note);
        }
    }
}