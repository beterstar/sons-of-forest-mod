using Construction;
using Sons.Ai.Vail;
using Sons.Crafting.Structures;
using Sons.Gameplay;
using Sons.Rocks;
using UnityEngine;

namespace PickaxeStone;

/// <summary>
/// "Is this collider a natural rock in the world?" Only static scenery whose surface
/// the game itself classifies as rock counts. Everything built, carried, alive or
/// already mineable by the game is excluded - most importantly the player's own stone
/// structures, which would otherwise turn into an endless stone duplicator.
/// </summary>
internal static class RockFilter
{
    /// <param name="reason">Why it was rejected (or "rock"), for the diagnostics log.</param>
    internal static bool IsNaturalRock(Collider c, out string reason)
    {
        var rejected = Reject(c);
        reason = rejected ?? "rock";
        return rejected == null;
    }

    private static string Reject(Collider c)
    {
        if (c == null || c.isTrigger || !c.gameObject.activeInHierarchy) return "trigger/inactive";

        // The ground: pickaxe ground attacks must not mine the terrain.
        if (c.TryCast<TerrainCollider>() != null) return "terrain";

        // Anything that moves (dropped stones, logs, props).
        var body = c.attachedRigidbody;
        if (body != null && !body.isKinematic) return "moving object";

        var surface = UnderfootSurfaceDetector.GetSurfaceType(c);
        if (surface != UnderfootSurfaceDetector.SurfaceType.Rock) return "surface " + surface;

        return OwnerOf(c);
    }

    private static string OwnerOf(Collider c)
    {
        if (c.GetComponentInParent<Structure>() != null || c.GetComponentInParent<StructureElement>() != null)
            return "player-built structure";
        if (c.GetComponentInParent<ScrewStructure>() != null) return "crafted object";
        if (c.GetComponentInParent<RockDestructionController>() != null) return "game's own mineable rock";
        if (c.GetComponentInParent<PickUp>() != null) return "pickup";
        if (c.GetComponentInParent<VailActor>() != null) return "actor";
        if (c.GetComponentInParent<BoltEntity>() != null) return "networked object";
        return null;
    }
}
