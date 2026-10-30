using System;
using System.Collections.Generic;
using RedLoader;
using Sons.Gameplay;
using Sons.Items.Core;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace KelvinTwo;

/// <summary>
/// After the local player lands from a hang glider: if there is no glider in the
/// inventory, pick up the one that was just dropped. Uses the game's own
/// <see cref="PickUp.Take"/> so co-op ownership/networking is handled by the game.
/// Each player runs this for themselves.
/// </summary>
internal sealed class GliderAutoPickup
{
    private const int FallbackGliderId = 626;
    // Check almost at once and keep checking fast: in co-op the dropped glider arrives from the
    // host a moment later and may still be sliding, so it can take a few polls to show up.
    private const float FirstCheckDelay = 0.15f;
    private const float PollInterval = 0.25f;
    private const float VerifyDelay = 0.4f;
    private const float GiveUpAfter = 4f;
    private const float FindActionInterval = 2f;
    private const float NotDroppedRadius = 3f;
    // A glider that appeared after take-off is ours (it was in our hands while flying),
    // so it may be taken from farther away than one that was already lying around.
    private const float NewGliderRadius = 30f;
    private const float LandingDebounce = 1f;

    private readonly ModConfig _cfg;
    private PlayerHangGliderAction _action;
    private float _nextFind;
    private bool _wasGliding;
    private float _landedAt = float.NegativeInfinity;
    private Vector3 _landPos;
    private bool _droppedOnLanding;
    private float _checkAt = -1f, _verifyAt = -1f, _giveUpAt;
    private int _attempts;
    private PickUp _target;
    private readonly HashSet<IntPtr> _glidersAtTakeOff = new();
    private bool _haveTakeOffSnapshot;

    internal GliderAutoPickup(ModConfig cfg) => _cfg = cfg;

    private static int GliderId => ItemDatabase.HangGliderId > 0 ? ItemDatabase.HangGliderId : FallbackGliderId;

    internal void Reset()
    {
        _action = null;
        _wasGliding = false;
        _checkAt = _verifyAt = -1f;
        _target = null;
        _haveTakeOffSnapshot = false;
    }

    internal void Update()
    {
        if (!_cfg.GliderAutoPickup || LocalPlayer._instance == null || !LocalPlayer.IsInWorld) return;
        try
        {
            WatchLanding();
            if (_checkAt > 0f && Time.unscaledTime >= _checkAt) TryPickup();
            if (_verifyAt > 0f && Time.unscaledTime >= _verifyAt) Verify();
        }
        catch (Exception ex)
        {
            _checkAt = _verifyAt = -1f;
            RLog.Error("[KelvinTwo] glider: " + ex);
        }
    }

    private void WatchLanding()
    {
        if (_action == null && Time.unscaledTime >= _nextFind)
        {
            _nextFind = Time.unscaledTime + FindActionInterval;
            // Explicit Unity null checks: '?.' / '??' don't see destroyed game objects.
            var root = LocalPlayer.GameObject;
            if (root != null) _action = root.GetComponentInChildren<PlayerHangGliderAction>(true);
            var special = LocalPlayer.SpecialActions;
            if (_action == null && special != null) _action = special.GetComponentInChildren<PlayerHangGliderAction>(true);
        }
        if (_action == null) return;

        bool gliding = _action.IsGliding;
        if (!_wasGliding && gliding && Time.unscaledTime - _landedAt >= LandingDebounce) SnapshotGliders();
        if (_wasGliding && !gliding)
        {
            // IsGliding flickers off/on/off within a fraction of a second on landing:
            // treat a repeat inside the debounce window as the same landing.
            bool repeat = Time.unscaledTime - _landedAt < LandingDebounce;
            _landedAt = Time.unscaledTime;
            _landPos = LocalPlayer.Transform.position;
            bool dropped = _action.GliderWasDroppedOnLastUse;
            _droppedOnLanding = repeat ? _droppedOnLanding | dropped : dropped;
            if (!repeat)
            {
                _attempts = 0;
                _checkAt = Time.unscaledTime + FirstCheckDelay;
                _giveUpAt = Time.unscaledTime + GiveUpAfter;
                RLog.Msg($"[KelvinTwo] landed (game says glider dropped: {dropped})");
            }
        }
        _wasGliding = gliding;
    }

    // Gliders lying in the world when we took off can't be the one in our hands.
    private void SnapshotGliders()
    {
        _glidersAtTakeOff.Clear();
        foreach (var p in AllGliderPickups())
            _glidersAtTakeOff.Add(p.Pointer);
        _haveTakeOffSnapshot = true;
    }

    private static IEnumerable<PickUp> AllGliderPickups()
    {
        int id = GliderId;
        foreach (var obj in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<PickUp>()))
        {
            var p = obj != null ? obj.TryCast<PickUp>() : null;
            if (p != null && p._itemId == id) yield return p;
        }
    }

    private static bool HasGlider() => LocalPlayer.Inventory != null && LocalPlayer.Inventory.AmountOf(GliderId) > 0;

    private bool TimeLeft => Time.unscaledTime < _giveUpAt;

    private void TryPickup()
    {
        _checkAt = -1f;
        _attempts++;
        if (_action != null) _droppedOnLanding |= _action.GliderWasDroppedOnLastUse;
        if (HasGlider())
        {
            if (_attempts == 1) RLog.Msg("[KelvinTwo] landed, glider already in inventory - nothing to do");
            return;
        }

        _target = FindDroppedGlider();
        if (_cfg.DebugLog) LogGliders();
        if (_target != null && _target.IsWaitingToActivate()) SkipSpawnDelay(_target);
        if (_target == null || _target.IsWaitingToActivate())
        {
            if (TimeLeft) _checkAt = Time.unscaledTime + PollInterval;
            else GiveUp();
            return;
        }

        _target.Take();
        _verifyAt = Time.unscaledTime + VerifyDelay;
    }

    // A freshly dropped item can't be picked up for a moment (so a player doesn't grab it
    // straight back by accident). Grabbing it straight back is exactly what we want here.
    private static void SkipSpawnDelay(PickUp p) => p._enableTime = Time.time - p._pickupDelayOnSpawn - 1f;

    private void Verify()
    {
        _verifyAt = -1f;
        if (HasGlider())
        {
            RLog.Msg($"[KelvinTwo] glider picked up ({Time.unscaledTime - _landedAt:0.0} s after landing)");
            SonsTools.ShowMessage("เก็บเครื่องร่อนเข้ากระเป๋าให้แล้ว", 3f);
            return;
        }

        // In co-op the host is still handing it to us: wait for it instead of asking again.
        if (_target != null && (_target._isCollected || _target.IsPendingDestroyThroughBolt()))
        {
            if (TimeLeft) _verifyAt = Time.unscaledTime + PollInterval;
            else GiveUp();
            return;
        }

        // Take() was refused (e.g. needs the player to look at it). Single player only:
        // add it ourselves and remove the world copy. In co-op we don't touch the
        // networked object directly, to avoid duplicating gliders.
        // Guard against a late-but-successful Take(): if the pickup is already collected
        // or gone, adding another glider here would duplicate it.
        if (!BoltNetwork.isRunning && _target != null && _target.gameObject.activeInHierarchy
            && !_target._isCollected && !_target.IsBeingDestroyed()
            && LocalPlayer.Inventory.HasRoomFor(GliderId)
            && LocalPlayer.Inventory.AddItem(GliderId, 1, true))
        {
            UnityEngine.Object.Destroy(_target._destroyTarget != null ? _target._destroyTarget : _target.gameObject);
            RLog.Msg("[KelvinTwo] glider picked up (added directly)");
            SonsTools.ShowMessage("เก็บเครื่องร่อนเข้ากระเป๋าให้แล้ว", 3f);
            return;
        }

        if (TimeLeft) _checkAt = Time.unscaledTime + PollInterval;
        else GiveUp();
    }

    // Say where it is when we found it but couldn't take it, so the player can walk over.
    private void GiveUp()
    {
        if (_target == null)
        {
            RLog.Msg("[KelvinTwo] landed without glider, but no dropped glider found nearby");
            return;
        }
        float d = Vector3.Distance(_target.transform.position, LocalPlayer.Transform.position);
        RLog.Msg($"[KelvinTwo] could not auto-pickup the glider {d:0} m away (inventory full?)");
        SonsTools.ShowMessage($"เก็บเครื่องร่อนให้ไม่ได้ ตกอยู่ห่าง {d:0} ม.", 4f);
    }

    /// <summary>debugLog: every glider pickup in the loaded world and why it was or wasn't taken.</summary>
    private void LogGliders()
    {
        var here = LocalPlayer.Transform.position;
        var sb = new System.Text.StringBuilder();
        int n = 0;
        foreach (var p in AllGliderPickups())
        {
            n++;
            var pos = p.transform.position;
            sb.Append($" [{Vector3.Distance(pos, here):0.0} m from you, {Vector3.Distance(pos, _landPos):0.0} m from landing, " +
                      $"new={IsNew(p)} active={p.gameObject.activeInHierarchy} collected={p._isCollected} " +
                      $"waiting={p.IsWaitingToActivate()} delay={p._pickupDelayOnSpawn:0.0}]");
        }
        float radius = _droppedOnLanding ? _cfg.GliderSearchRadius : NotDroppedRadius;
        RLog.Msg($"[KelvinTwo] glider attempt {_attempts} at {Time.unscaledTime - _landedAt:0.00} s: radius {radius:0} m " +
                 $"(new ones {NewGliderRadius:0} m), picked {(_target != null ? "one" : "none")}, {n} glider pickup(s) loaded:{sb}");
    }

    private bool IsNew(PickUp p) => _haveTakeOffSnapshot && !_glidersAtTakeOff.Contains(p.Pointer);

    private PickUp FindDroppedGlider()
    {
        var here = LocalPlayer.Transform.position;
        PickUp best = null;
        float bestDist = NewGliderRadius;

        // Ours for sure: appeared after take-off. Taken even if it slid or flew on a bit.
        if (_haveTakeOffSnapshot)
        {
            foreach (var p in AllGliderPickups())
                if (IsNew(p)) Consider(p, here, ref best, ref bestDist);
            if (best != null) return best;
        }

        // Otherwise the closest one around the landing spot. If the game says it didn't drop
        // our glider, only one lying right at our feet, so a glider placed on the ground on
        // purpose (or a friend's) isn't grabbed.
        bestDist = _droppedOnLanding ? _cfg.GliderSearchRadius : NotDroppedRadius;
        foreach (var center in new[] { _landPos, here })
            foreach (var col in Physics.OverlapSphere(center, bestDist, ~0, QueryTriggerInteraction.Collide))
            {
                var p = col != null ? col.GetComponentInParent<PickUp>() : null;
                if (p != null && p._itemId == GliderId) Consider(p, here, ref best, ref bestDist);
            }
        if (best != null) return best;

        // Some gliders have no collider the sphere can hit.
        foreach (var p in AllGliderPickups())
            Consider(p, here, ref best, ref bestDist);
        return best;
    }

    // Distance to the closer of the landing spot and where the player stands now
    // (the glider can come down a bit away from where gliding ended).
    private void Consider(PickUp p, Vector3 here, ref PickUp best, ref float bestDist)
    {
        if (!p.gameObject.activeInHierarchy || p._isCollected || p.IsBeingDestroyed() || p.IsPendingDestroyThroughBolt())
            return;
        var pos = p.transform.position;
        float d = Mathf.Min(Vector3.Distance(pos, _landPos), Vector3.Distance(pos, here));
        if (d < bestDist)
        {
            bestDist = d;
            best = p;
        }
    }
}
