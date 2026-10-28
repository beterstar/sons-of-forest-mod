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
/// </summary>
internal sealed class CloneWarp
{
    private const float LandShortOfDropOff = 6f;
    private const float MaxSnapDistance = 8f;

    private readonly ModConfig _cfg;
    private readonly WarpDecider _decider;

    internal CloneWarp(ModConfig cfg)
    {
        _cfg = cfg;
        _decider = new WarpDecider(cfg.WarpMinDistance, cfg.WarpCooldownSeconds);
    }

    internal void Reset() => _decider.Reset();

    internal void Tick(VailActor actor, Robby robby)
    {
        if (!_cfg.WarpHome || actor == null || robby == null || !actor.gameObject.activeInHierarchy
            || CloneMarker.NeedsNormalSpeed(robby) || !TryGetDropOff(robby, out var dropOff))
        {
            _decider.Reset();
            return;
        }

        bool carrying = actor.GetInventoryCountAll() > 0;
        var from = actor.transform.position;
        if (!_decider.Update(carrying, Vector3.Distance(from, dropOff), Time.time)) return;

        if (!TryFindLanding(from, dropOff, out var landing))
        {
            RLog.Msg("[KelvinTwo] warp home skipped: no walkable spot near the drop-off, Kelvin 2 keeps walking");
            return;
        }
        WarpTo(actor, landing, dropOff);
        RLog.Msg($"[KelvinTwo] Kelvin 2 warped home with his load: {Vector3.Distance(from, landing):0} m");
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
        actor.transform.position = landing;
        if (face.sqrMagnitude > 0.01f) actor.transform.rotation = Quaternion.LookRotation(face);

        var sim = actor.GetWorldSimActor();
        if (sim != null) sim._position = landing;

        // Drop the old route so he re-plans from here instead of walking back along it.
        actor.ClearPath();
    }
}
