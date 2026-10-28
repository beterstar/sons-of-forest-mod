using System;
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
    private const float FirstCheckDelay = 1.2f;
    private const float RetryDelay = 1f;
    private const float VerifyDelay = 1f;
    private const int MaxAttempts = 4;
    private const float FindActionInterval = 2f;
    private const float NotDroppedRadius = 3f;

    private readonly ModConfig _cfg;
    private PlayerHangGliderAction _action;
    private float _nextFind;
    private bool _wasGliding;
    private Vector3 _landPos;
    private bool _droppedOnLanding;
    private float _checkAt = -1f, _verifyAt = -1f;
    private int _attempts;
    private PickUp _target;

    internal GliderAutoPickup(ModConfig cfg) => _cfg = cfg;

    private static int GliderId => ItemDatabase.HangGliderId > 0 ? ItemDatabase.HangGliderId : FallbackGliderId;

    internal void Reset()
    {
        _action = null;
        _wasGliding = false;
        _checkAt = _verifyAt = -1f;
        _target = null;
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
        if (_wasGliding && !gliding)
        {
            _landPos = LocalPlayer.Transform.position;
            _attempts = 0;
            _checkAt = Time.unscaledTime + FirstCheckDelay;
            _droppedOnLanding = _action.GliderWasDroppedOnLastUse;
            RLog.Msg($"[KelvinTwo] landed (game says glider dropped: {_droppedOnLanding})");
        }
        _wasGliding = gliding;
    }

    private static bool HasGlider() => LocalPlayer.Inventory != null && LocalPlayer.Inventory.AmountOf(GliderId) > 0;

    private void TryPickup()
    {
        _checkAt = -1f;
        if (_action != null) _droppedOnLanding |= _action.GliderWasDroppedOnLastUse;
        if (HasGlider())
        {
            RLog.Msg("[KelvinTwo] landed, glider already in inventory - nothing to do");
            return;
        }

        _target = FindDroppedGlider();
        if (_target == null || _target.IsWaitingToActivate())
        {
            if (++_attempts < MaxAttempts) _checkAt = Time.unscaledTime + RetryDelay;
            else RLog.Msg("[KelvinTwo] landed without glider, but no dropped glider found nearby");
            return;
        }

        _target.Take();
        _verifyAt = Time.unscaledTime + VerifyDelay;
    }

    private void Verify()
    {
        _verifyAt = -1f;
        if (HasGlider())
        {
            SonsTools.ShowMessage("เก็บเครื่องร่อนเข้ากระเป๋าให้แล้ว", 3f);
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
            SonsTools.ShowMessage("เก็บเครื่องร่อนเข้ากระเป๋าให้แล้ว", 3f);
            return;
        }

        if (++_attempts < MaxAttempts) _checkAt = Time.unscaledTime + RetryDelay;
        else RLog.Msg("[KelvinTwo] could not auto-pickup the glider (inventory full?)");
    }

    private PickUp FindDroppedGlider()
    {
        int id = GliderId;
        PickUp best = null;
        // If the game says it didn't drop our glider, only take one lying right at our feet,
        // so a glider placed on the ground on purpose (or a friend's) isn't grabbed.
        float radius = _droppedOnLanding ? _cfg.GliderSearchRadius : NotDroppedRadius;
        float bestDist = radius;
        var here = LocalPlayer.Transform.position;

        foreach (var center in new[] { _landPos, here })
            foreach (var col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide))
                Consider(col != null ? col.GetComponentInParent<PickUp>() : null, id, here, ref best, ref bestDist);

        if (best != null) return best;

        foreach (var obj in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<PickUp>()))
            Consider(obj != null ? obj.TryCast<PickUp>() : null, id, here, ref best, ref bestDist);
        return best;
    }

    // Distance to the closer of the landing spot and where the player stands now
    // (the glider can come down a bit away from where gliding ended).
    private void Consider(PickUp p, int id, Vector3 here, ref PickUp best, ref float bestDist)
    {
        if (p == null || p._itemId != id || !p.gameObject.activeInHierarchy || p._isCollected
            || p.IsBeingDestroyed() || p.IsPendingDestroyThroughBolt())
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
