using System;
using System.Collections.Generic;
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
    /// usable (1.0.2). Switching from the sniper to another item (a note, the pistol, ...) left "RifleHeld" on as
    /// well, so the hands froze in the rifle pose once that item was put away (1.0.4). So once the sniper has
    /// left the hand: with empty hands we put its id back and let the game's own ApplyAnimVars clear it; with
    /// another item in the hand we switch off the sniper's pose flags that item doesn't use.
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

        // The sniper was in the right hand and its animator variables (RifleHeld) may still be set.
        private static bool _sniperVarsPending;
        private static float _settledSince = -1f;
        private static string[] _sniperVars;
        private static IntPtr _boolsOf;
        private static readonly HashSet<int> _bools = new HashSet<int>();

        private static void SetId(Animator a, int id)
        {
            a.SetInteger(ItemIdParam, id);
            a.SetFloat(ItemIdFloatParam, id);
        }

        /// <summary>Every frame: keep the swap while the sniper is held, and undo a stale rifle pose afterwards.</summary>
        internal static void Apply()
        {
            try
            {
                if (LocalPlayer._instance == null) return;
                var animator = LocalPlayer.Animator;
                if (animator == null) return;
                var inv = LocalPlayer.Inventory;
                var item = inv?.RightHandItem;
                int id = animator.GetInteger(ItemIdParam);

                if (item != null && item._itemID == SniperItem.Id)
                {
                    _sniperVarsPending = true;
                    _settledSince = -1f;
                    // Only translate our own id; any other value is the game changing weapons.
                    if (id == SniperItem.Id) SetId(animator, SniperItem.RifleId);
                    else if (id == SniperItem.RifleId && animator.GetFloat(ItemIdFloatParam) != SniperItem.RifleId)
                        animator.SetFloat(ItemIdFloatParam, SniperItem.RifleId);
                    return;
                }
                if (!_sniperVarsPending) return;

                // The sniper is gone from the hand. Once the game has finished swapping (or putting it away),
                // undo what its own clean-up skipped.
                if (inv._currentEquipState.ToString() != "Idle") { _settledSince = -1f; return; }
                float now = Time.unscaledTime;
                if (_settledSince < 0f) { _settledSince = now; return; }
                if (now - _settledSince < StaleAfter) return;
                _settledSince = -1f;
                _sniperVarsPending = false;

                if (item == null)
                {
                    if (id == SniperItem.RifleId || id == SniperItem.Id || animator.GetBool(RifleHeldParam)) ClearRiflePose(animator);
                }
                else if (item._itemID != SniperItem.RifleId)
                {
                    ClearSniperOnlyVars(animator, item._itemID);
                }
            }
            catch (Exception e) { SniperRifleMod.LogOnce("AnimFix", e); }
        }

        /// <summary>Hands empty: let the game clear the sniper's animator variables (needs its own id in place).</summary>
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
            Note($"cleared a stale rifle pose ({how}): RightHandHeldItemId={animator.GetInteger(ItemIdParam)} RifleHeld={animator.GetBool(RifleHeldParam)}");
        }

        /// <summary>Another item is in the hand now: switch off the sniper's pose flags that this item doesn't use.</summary>
        private static void ClearSniperOnlyVars(Animator animator, int currentId)
        {
            _sniperVars ??= VarNames(SniperItem.Id);
            var current = new HashSet<string>(VarNames(currentId));
            CacheBools(animator);
            var cleared = new List<string>();
            foreach (var v in _sniperVars)
            {
                if (current.Contains(v)) continue;
                int h = Animator.StringToHash(v);
                if (!_bools.Contains(h) || !animator.GetBool(h)) continue;
                animator.SetBool(h, false);
                cleared.Add(v);
            }
            if (cleared.Count > 0) Note($"switched from the sniper to item {currentId}: turned off {string.Join(", ", cleared)}");
        }

        private static string[] VarNames(int itemId)
        {
            var data = ItemDatabaseManager.ItemById(itemId);
            var vars = data?._equippedAnimVars;
            if (vars == null) return Array.Empty<string>();
            var names = new string[vars.Length];
            for (int i = 0; i < vars.Length; i++) names[i] = vars[i].ToString();
            return names;
        }

        private static void CacheBools(Animator animator)
        {
            if (_boolsOf == animator.Pointer) return;
            _boolsOf = animator.Pointer;
            _bools.Clear();
            var ps = animator.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].type == AnimatorControllerParameterType.Bool) _bools.Add(ps[i].nameHash);
        }

        private static void Note(string s)
        {
            Observer?.Invoke(s);
            if (SniperRifleMod.Cfg.Debug) RedLoader.RLog.Msg("[SniperRifle] " + s);
        }
    }
}
