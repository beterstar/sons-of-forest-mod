using System;
using System.Text;
using Il2CppInterop.Runtime;
using RedLoader;
using Sons.Cutscenes;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UObject = UnityEngine.Object;

namespace SniperRifle
{
    /// <summary>
    /// Safety net for hand/animation lock-ups this mod can't reproduce offline (e.g. in hosted multiplayer):
    /// - the unstuck key (config "unstuckKey", default F8) stops cutscenes that are still playing, puts away
    ///   whatever is in the right hand at once and clears the rifle pose;
    /// - when the player looks stuck for a few seconds (empty hands still in the rifle pose, or the hands
    ///   busy swapping for a long time) the full state is written to _Redloader\Latest.log once, so the cause
    ///   can be found afterwards.
    /// </summary>
    internal static class Unstuck
    {
        private const float WatchEvery = 0.5f;
        private const float ReportAfter = 4f;       // empty hands still in the rifle pose
        private const float ReportBusyAfter = 10f;  // swapping items (the first equip after loading can take a while)
        private static readonly int ItemIdParam = Animator.StringToHash("RightHandHeldItemId");
        private static readonly int RifleHeldParam = Animator.StringToHash("RifleHeld");

        /// <summary>Tests: notes about what was done.</summary>
        internal static Action<string> Observer;

        private static Key _key = Key.F8;
        private static bool _keyEnabled = true;
        private static float _nextWatch, _suspectSince = -1f;
        private static bool _reported;

        internal static void Configure(Config cfg)
        {
            string k = (cfg.UnstuckKey ?? "").Trim();
            _keyEnabled = k.Length > 0 && !k.Equals("none", StringComparison.OrdinalIgnoreCase) && Enum.TryParse(k, true, out _key);
            if (k.Length > 0 && !_keyEnabled && !k.Equals("none", StringComparison.OrdinalIgnoreCase))
                RLog.Warning($"[SniperRifle] unstuckKey \"{k}\" is not a key name (e.g. F8, F9, Home): the unstuck key is off");
        }

        internal static void Update()
        {
            try
            {
                if (LocalPlayer._instance == null || !LocalPlayer.IsInWorld) return;
                if (_keyEnabled)
                {
                    var kb = Keyboard.current;
                    if (kb != null && kb[_key].wasPressedThisFrame) Run();
                }
                Watch();
            }
            catch (Exception e) { SniperRifleMod.LogOnce("Unstuck", e); }
        }

        private static void Run()
        {
            RLog.Msg("[SniperRifle] unstuck key pressed. Before: " + Snapshot());
            int stopped = 0;
            var all = UObject.FindObjectsOfType(Il2CppType.Of<Cutscene>());
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i]?.TryCast<Cutscene>();
                if (c == null || !c._isPlayingLocally) continue;
                try { c.Stop(); } catch (Exception e) { RLog.Warning("[SniperRifle] stopping cutscene " + c.name + ": " + e.Message); }
                if (c._isPlayingLocally) c.SetIsPlayingLocally(false);
                stopped++;
            }
            var inv = LocalPlayer.Inventory;
            if (inv?.RightHandItem != null) inv.StashRightHandItem(false, true, true);
            var animator = LocalPlayer.Animator;
            if (animator != null) AnimFix.ForceClear(animator);
            string after = $"stopped {stopped} cutscene(s). After: " + Snapshot();
            RLog.Msg("[SniperRifle] unstuck: " + after);
            Observer?.Invoke(after);
        }

        private static void Watch()
        {
            float now = Time.unscaledTime;
            if (now < _nextWatch) return;
            _nextWatch = now + WatchEvery;
            var inv = LocalPlayer.Inventory;
            var animator = LocalPlayer.Animator;
            if (inv == null || animator == null) return;
            int id = animator.GetInteger(ItemIdParam);
            bool rifleNoGun = inv.RightHandItem == null && (animator.GetBool(RifleHeldParam) || id == SniperItem.RifleId || id == SniperItem.Id);
            bool handsBusy = inv._currentEquipState.ToString() != "Idle";
            if (!rifleNoGun && !handsBusy) { _suspectSince = -1f; _reported = false; return; }
            if (_suspectSince < 0f) { _suspectSince = now; return; }
            if (_reported || now - _suspectSince < (rifleNoGun ? ReportAfter : ReportBusyAfter)) return;
            _reported = true;
            string s = $"looks stuck ({(rifleNoGun ? "empty hands in the rifle pose" : "hands busy for a long time")}): " + Snapshot();
            RLog.Warning("[SniperRifle] " + s);
            Observer?.Invoke(s);
        }

        internal static string Snapshot()
        {
            var sb = new StringBuilder();
            var inv = LocalPlayer.Inventory;
            var a = LocalPlayer.Animator;
            var fp = LocalPlayer.FpCharacter;
            sb.Append($"right={inv?.RightHandItem?._itemID} prev={inv?.PreviousRightHandItem?._itemID} equip={inv?._currentEquipState} ");
            sb.Append($"handsEmpty={Cutscene.IsPlayerHandsEmpty()} locked={fp?.Locked}/{fp?.MovementLocked} inInventory={LocalPlayer.IsInInventory} inCaves={LocalPlayer.IsInCaves} ");
            sb.Append($"multiplayer={BoltNetwork.isRunning} server={BoltNetwork.isServer} ");
            if (a != null)
            {
                sb.Append("anim:");
                var ps = a.parameters;
                for (int i = 0; i < ps.Length; i++)
                {
                    var p = ps[i];
                    if (p.type == AnimatorControllerParameterType.Bool && a.GetBool(p.nameHash) && p.name.IndexOf("Held", StringComparison.OrdinalIgnoreCase) >= 0) sb.Append(' ').Append(p.name);
                    else if (p.type == AnimatorControllerParameterType.Int && p.name.IndexOf("HeldItemId", StringComparison.OrdinalIgnoreCase) >= 0) sb.Append($" {p.name}={a.GetInteger(p.nameHash)}");
                }
                sb.Append(" layers:");
                for (int l = 0; l < a.layerCount; l++)
                {
                    if (l > 0 && a.GetLayerWeight(l) < 0.01f) continue;
                    sb.Append($" {l}:{a.GetCurrentAnimatorStateInfo(l).shortNameHash}");
                }
            }
            var all = UObject.FindObjectsOfType(Il2CppType.Of<Cutscene>());
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i]?.TryCast<Cutscene>();
                if (c != null && c._isPlayingLocally) sb.Append($" playingCutscene={c.name}");
            }
            return sb.ToString();
        }
    }
}
