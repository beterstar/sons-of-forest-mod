using System;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Gameplay;
using UnityEngine;

namespace KelvinTwo;

/// <summary>
/// Host only: when Kelvin 2 is carrying a load back from far away, warp him to a walkable
/// spot a few meters short of the drop-off instead of making him walk the whole way (or
/// speeding him up, which looks odd). He then walks the last bit and drops it the normal
/// way. <see cref="WarpDecider"/> keeps it fair: only while heading back, only when far,
/// with a cooldown. If no safe landing spot is found he simply keeps walking.
/// Also watches for him freezing with a load (<see cref="StuckDetector"/>) and tells the AI
/// he is stuck when that happens, warp or not.
/// </summary>
internal sealed class CloneWarp
{
    private const float LandShortOfDropOff = 6f;
    private const float MaxSnapDistance = 8f;
    // He walks only a few meters in the second before the check; a failed warp leaves him 30+ m away.
    private const float WarpTookTolerance = 10f;

    private readonly ModConfig _cfg;
    private readonly WarpDecider _decider;
    private readonly StuckDetector _stuck = new();
    private bool _checkWarp;
    private Vector3 _warpLanding;
    private bool _warpRefused;

    internal CloneWarp(ModConfig cfg)
    {
        _cfg = cfg;
        _decider = new WarpDecider(cfg.WarpMinDistance, cfg.WarpCooldownSeconds);
    }

    internal void Reset()
    {
        _decider.Reset();
        _stuck.Reset();
        _checkWarp = false;
    }

    /// <summary>New world: the game gets another chance to accept a warp.</summary>
    internal void ResetSession()
    {
        Reset();
        _warpRefused = false;
    }

    internal void Tick(VailActor actor, Robby robby)
    {
        if (actor == null || robby == null || !actor.gameObject.activeInHierarchy
            || CloneMarker.NeedsNormalSpeed(robby) || !TryGetDropOff(robby, out var dropOff))
        {
            Reset();
            return;
        }

        if (_checkWarp) CheckWarpTook(actor);

        bool carrying = actor.GetInventoryCountAll() > 0;
        var from = actor.transform.position;
        float distance = Vector3.Distance(from, dropOff);
        if (_stuck.Update(carrying, distance, from.x, from.z, Time.time))
        {
            Unstick(actor);
            RLog.Msg($"[KelvinTwo] Kelvin 2 stood still with his load {distance:0} m from the drop-off - told him he is stuck");
        }

        if (!_cfg.WarpHome || _warpRefused || !_decider.Update(carrying, distance, Time.time)) return;

        if (!TryFindLanding(from, dropOff, out var landing))
        {
            RLog.Msg("[KelvinTwo] warp home skipped: no walkable spot near the drop-off, Kelvin 2 keeps walking");
            return;
        }
        WarpTo(actor, landing, dropOff);
        _warpLanding = landing;
        _checkWarp = true;
        _stuck.Reset();
        RLog.Msg($"[KelvinTwo] Kelvin 2 warped home with his load: {Vector3.Distance(from, landing):0} m");
    }

    // One tick after a warp: if the game put him back where he was, stop warping this
    // session and let him walk (re-planning his route so he doesn't stand there).
    private void CheckWarpTook(VailActor actor)
    {
        _checkWarp = false;
        float off = Vector3.Distance(actor.transform.position, _warpLanding);
        if (off <= WarpTookTolerance) return;
        _warpRefused = true;
        Unstick(actor);
        RLog.Msg($"[KelvinTwo] warp home did not take ({off:0} m from the landing spot) - warp home off until you reload, Kelvin 2 walks");
    }

    private static bool TryGetDropOff(Robby robby, out Vector3 dropOff)
    {
        dropOff = default;
        var drop = robby._dropStimuliInstance;
        if (drop == null) return false;
        var holder = drop._dropTarget;   // log holder / sled / player, when there is one
        dropOff = holder != null ? holder.transform.position : drop.transform.position;
        return true;
    }

    // A walkable point on the AI's own navigation graph, on the side he is coming from.
    private static bool TryFindLanding(Vector3 from, Vector3 dropOff, out Vector3 landing)
    {
        var dir = from - dropOff;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
        var wanted = dropOff + dir * LandShortOfDropOff;
        landing = wanted;

        var astar = AstarPath.active;
        if (astar == null) return false;
        var nearest = astar.GetNearest(wanted);
        if (nearest.node == null || Vector3.Distance(nearest.position, wanted) > MaxSnapDistance) return false;
        landing = nearest.position;
        return true;
    }

    private static void WarpTo(VailActor actor, Vector3 landing, Vector3 dropOff)
    {
        var face = dropOff - landing;
        face.y = 0f;
        var rotation = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : actor.transform.rotation;

        // The actor's own move keeps its internal position in step; setting the transform
        // alone gets undone by the game on the next frame.
        actor.SetPositionAndRotation(landing, rotation, false);
        var sim = actor.GetWorldSimActor();
        if (sim != null) sim._position = landing;

        // Plan a fresh route from here (clearing the old one without a new one froze him).
        try { actor.FindPath(); }
        catch (Exception) { Unstick(actor); }
    }

    // The game's own reaction to a blocked walk: drop the current step and pick the next
    // one again (here: carry the load to the drop-off). Without a route, a fresh FindPath
    // throws, and only a hit used to get him out of it.
    private static void Unstick(VailActor actor) => actor.InterruptCurrentAction(InterruptReason.MovementStuck);
}
