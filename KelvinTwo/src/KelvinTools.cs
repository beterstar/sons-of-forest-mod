using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Sons.Ai.Vail;
using Sons.Ai.Vail.Influences;
using Sons.Ai.Vail.Memory;
using Sons.Ai.Vail.StimuliTypes;
using Sons.StatSystem;
using UnityEngine;

namespace KelvinTwo;

/// <summary>Small helpers around the game's Kelvin ("Robby") actors.</summary>
internal static class KelvinTools
{
    // Stats that should sit at their maximum (alive, rested, fed, never tired).
    private static readonly HashSet<string> FillStats = new()
    {
        nameof(HealthStat), nameof(EnergyStat), nameof(RestedStat), nameof(StaminaStat),
        nameof(FullnessStat), nameof(HydrationStat), nameof(VitalityStat), nameof(MoraleStat),
        nameof(AffectionStat)
    };

    // Stats that should sit at their minimum (never scared, sick or angry).
    private static readonly HashSet<string> EmptyStats = new()
    {
        nameof(FearStat), nameof(SicknessStat), nameof(AngerStat)
    };

    internal static bool IsHost => !BoltNetwork.isRunning || BoltNetwork.isServer;

    internal static List<VailActor> ActiveKelvins()
    {
        var result = new List<VailActor>();
        var actors = VailActorManager.GetActiveActors();
        if (actors == null)
            return result;
        foreach (var actor in actors)
        {
            if (actor != null && actor.TypeId == VailActorTypeId.Robby && !actor._isDead)
                result.Add(actor);
        }
        return result;
    }

    internal static List<WorldSimActor> WorldKelvins(VailWorldSimulation sim)
    {
        var result = new List<WorldSimActor>();
        var list = sim._robbyActors;
        if (list != null && list.Count > 0)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) result.Add(list[i]);
            return result;
        }

        var all = sim._actors;
        for (int i = 0; all != null && i < all.Count; i++)
        {
            var a = all[i];
            if (a != null && a.TypeId == VailActorTypeId.Robby)
                result.Add(a);
        }
        return result;
    }

    /// <summary>World-sim id of a body, or -1 if it has none (never matches the clone).</summary>
    internal static int SimId(VailActor actor)
    {
        var sim = actor.GetWorldSimActor();
        return sim != null ? sim.UniqueId : -1;
    }

    internal static BoltEntity Entity(VailActor actor)
    {
        var e = actor.GetComponent<BoltEntity>();
        return e != null ? e : actor.GetComponentInParent<BoltEntity>();
    }

    /// <summary>Tops up good stats and zeroes bad ones so Kelvin never tires, sleeps, starves or panics.</summary>
    internal static void RefillStats(StatsManager stats)
    {
        var list = stats?._stats;
        for (int i = 0; list != null && i < list.Count; i++)
        {
            var stat = list[i];
            if (stat == null) continue;
            var name = stat.GetIl2CppType().Name;
            if (FillStats.Contains(name))
                stat._currentValue = stat.GetMax();
            else if (EmptyStats.Contains(name))
                stat._currentValue = stat.GetMin();
        }
    }

    internal static void RefillStats(VailActor actor)
    {
        RefillStats(actor._statsManager?._statsManager);
        RefillStats(actor.GetWorldSimActor()?._statsManager);
    }

    /// <summary>
    /// Kelvin remembers how the players treated him (sentiment / anger / fear toward
    /// <see cref="PlayerStimuli"/>). Hitting him drops sentiment and he starts refusing
    /// orders. Pin those to "best friend" so the clone always obeys.
    /// </summary>
    internal static void PinPlayerTrust(int simId)
    {
        var playerType = Il2CppType.Of<PlayerStimuli>();
        if (!ActorMemoryManager.TryEvaluate(simId, playerType, out var values) || values == null)
            return;

        Pin<SentimentInfluence>(simId, playerType, values._sentiment, toMax: true);
        Pin<AngerInfluence>(simId, playerType, values._anger, toMax: false);
        Pin<FearInfluence>(simId, playerType, values._fear, toMax: false);
    }

    private static bool _adjustUnavailable;

    private static void Pin<T>(int simId, Il2CppSystem.Type playerType, Influence current, bool toMax) where T : Influence
    {
        if (current == null) return;
        float target = toMax ? current.GetMax() : current.GetMin();
        float delta = target - current._currentValue;
        if (Mathf.Abs(delta) < 0.01f) return;

        // Prefer the game's own path (keeps its bookkeeping/save right). If this generic
        // method isn't available in the game build, fall back to writing the value only.
        if (!_adjustUnavailable)
        {
            try { ActorMemoryManager.Adjust<T>(simId, playerType, delta); }
            catch (System.Exception ex)
            {
                _adjustUnavailable = true;
                RedLoader.RLog.Msg("[KelvinTwo] ActorMemoryManager.Adjust unavailable, writing trust directly: " + ex.Message);
            }
        }
        current._currentValue = target;
    }

    internal static Vector3 SpawnPointInFrontOfPlayer(Transform player, float distance, out Quaternion facing)
    {
        var forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
        facing = Quaternion.LookRotation(-forward);
        return player.position + forward * distance;
    }
}
