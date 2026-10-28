using System;
using System.Collections.Generic;
using System.Linq;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Gameplay.GameSetup;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace KelvinTwo;

/// <summary>
/// Host: makes sure the save has exactly one extra Kelvin and remembers which one it is.
/// Clients: follow the host's broadcast. Both: feed the current clone body to <see cref="CloneMarker"/>.
/// </summary>
internal sealed class CloneManager
{
    private const float TickSeconds = 1f;
    private const float BroadcastSeconds = 3f;
    private const float SpawnTimeoutSeconds = 20f;
    private const float SpawnDistance = 4f;
    private const float ConfirmDelaySeconds = 5f;
    private const int MaxKelvins = 2;

    private readonly ModConfig _cfg;
    private readonly CloneStore _store;
    private readonly CloneMarker _marker;
    private readonly CloneNetEvent _net;
    private readonly CloneWarp _warp;

    private string _saveKey;
    private int _cloneSimId = -1;
    private HashSet<int> _idsBeforeSpawn;
    private Vector3 _spawnPos;
    private float _spawnDeadline = -1f;
    private List<int> _firstSample;
    private float _confirmAt = -1f;
    private float _nextTick, _nextBroadcast;
    private bool _ready;
    private ulong _clientCloneNetId;
    private bool _hasClientClone;

    internal CloneManager(ModConfig cfg, CloneStore store, CloneMarker marker)
    {
        _cfg = cfg;
        _store = store;
        _marker = marker;
        _net = new CloneNetEvent(OnCloneAnnounced);
        _warp = new CloneWarp(cfg);
    }

    internal CloneNetEvent NetEvent => _net;

    internal void Reset()
    {
        _ready = false;
        _cloneSimId = -1;
        _spawnDeadline = -1f;
        _confirmAt = -1f;
        _firstSample = null;
        _idsBeforeSpawn = null;
        _hasClientClone = false;
        _warp.Reset();
        _marker.Clear();
    }

    /// <summary>Called once the player is in the world and the AI simulation exists.</summary>
    internal void OnWorldReady()
    {
        _ready = true;
        if (!KelvinTools.IsHost)
        {
            RLog.Msg("[KelvinTwo] client: waiting for host to say which Kelvin is the clone");
            return;
        }

        uint save = GameSetupManager.GetSelectedSaveId();
        if (save == 0) save = SonsSdk.GameState.LastLoadedSaveId;
        // Single player and multiplayer saves live in different folders and can share a number.
        _saveKey = $"{GameSetupManager.GetSaveGameType()}:{save}";

        var ids = CurrentKelvinIds();
        RLog.Msg($"[KelvinTwo] host, save {_saveKey}: Kelvins in world = [{string.Join(", ", ids)}]");
        if (TryAdoptExisting(ids)) return;

        // The world may still be loading actors. Only spawn if a second look, a few
        // seconds later, sees the same Kelvins - otherwise we could add a 3rd Kelvin.
        _firstSample = ids;
        _confirmAt = Time.unscaledTime + ConfirmDelaySeconds;
    }

    private static List<int> CurrentKelvinIds() =>
        KelvinTools.WorldKelvins(VailWorldSimulation._instance).Select(k => k.UniqueId).OrderBy(i => i).ToList();

    private bool TryAdoptExisting(List<int> ids)
    {
        if (_store.TryGet(_saveKey, out var stored) && ids.Contains(stored))
        {
            _cloneSimId = stored;
            RLog.Msg($"[KelvinTwo] found saved clone {stored}");
            return true;
        }
        if (ids.Count >= MaxKelvins)
        {
            _cloneSimId = ids.Max();
            _store.Set(_saveKey, _cloneSimId);
            RLog.Msg($"[KelvinTwo] adopting existing extra Kelvin {_cloneSimId} as clone");
            return true;
        }
        return false;
    }

    private void ConfirmAndSpawn()
    {
        _confirmAt = -1f;
        var ids = CurrentKelvinIds();
        if (TryAdoptExisting(ids)) return;

        if (!ids.SequenceEqual(_firstSample))
        {
            RLog.Msg("[KelvinTwo] Kelvin list still changing, checking again");
            _firstSample = ids;
            _confirmAt = Time.unscaledTime + ConfirmDelaySeconds;
            return;
        }

        _spawnPos = KelvinTools.SpawnPointInFrontOfPlayer(LocalPlayer.Transform, SpawnDistance, out var rot);
        _idsBeforeSpawn = new HashSet<int>(ids);
        _spawnDeadline = Time.unscaledTime + SpawnTimeoutSeconds;
        VailWorldSimulation.SpawnCompanion(VailActorTypeId.Robby, _spawnPos, rot, _cfg.Sentiment);
        RLog.Msg($"[KelvinTwo] spawned a new Kelvin at {_spawnPos}");
    }

    internal void Tick()
    {
        if (!_ready || Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + TickSeconds;
        if (VailWorldSimulation._instance == null || LocalPlayer._instance == null || !LocalPlayer.IsInWorld) return;

        if (KelvinTools.IsHost) HostTick();
        else Safe("client sync", ClientTick);
    }

    private void HostTick()
    {
        if (_confirmAt > 0f && Time.unscaledTime >= _confirmAt) Safe("spawn", ConfirmAndSpawn);
        if (_spawnDeadline > 0f) Safe("find spawned", ResolvePendingSpawn);
        if (_cloneSimId < 0) return;

        // Each step isolated: one failing (e.g. after a game update) must not stop the others.
        List<VailActor> bodies = null;
        Safe("mark", () =>
        {
            int cloneId = _cloneSimId;
            bool IsCloneBody(VailActor a) => KelvinTools.SimId(a) == cloneId;
            bodies = KelvinTools.ActiveKelvins().Where(IsCloneBody).ToList();
            _marker.Sync(bodies, IsCloneBody);
        });
        Safe("off-screen stats", () =>
        {
            // Off-screen Kelvin is simulated without a body: keep his stats topped up there too.
            var sim = KelvinTools.WorldKelvins(VailWorldSimulation._instance).FirstOrDefault(k => k.UniqueId == _cloneSimId);
            if (sim != null) KelvinTools.RefillStats(sim._statsManager);
        });
        if (_cfg.AlwaysObedient) Safe("trust", () => KelvinTools.PinPlayerTrust(_cloneSimId));
        Safe("warp home", () =>
        {
            if (_marker.TryGetLiveClone(out var actor, out var robby)) _warp.Tick(actor, robby);
            else _warp.Reset();
        });

        if (BoltNetwork.isRunning && bodies is { Count: > 0 } && Time.unscaledTime >= _nextBroadcast)
        {
            _nextBroadcast = Time.unscaledTime + BroadcastSeconds;
            Safe("broadcast", () => _net.Broadcast(KelvinTools.Entity(bodies[0])));
        }
    }

    private void ResolvePendingSpawn()
    {
        // Pick the new Kelvin closest to where we spawned him, in case the game adds one at the same time.
        var fresh = KelvinTools.WorldKelvins(VailWorldSimulation._instance)
            .Where(k => !_idsBeforeSpawn.Contains(k.UniqueId))
            .OrderBy(k => Vector3.Distance(k._position, _spawnPos))
            .FirstOrDefault();

        if (fresh != null)
        {
            _cloneSimId = fresh.UniqueId;
            _store.Set(_saveKey, _cloneSimId);
            _spawnDeadline = -1f;
            RLog.Msg($"[KelvinTwo] clone is sim actor {_cloneSimId}");
            SonsTools.ShowMessage($"{_cfg.CloneName} มาแล้ว! (เดินไปหาแล้วสั่งงานได้เหมือนเคลวิน)", 8f);
        }
        else if (Time.unscaledTime > _spawnDeadline)
        {
            _spawnDeadline = -1f;
            RLog.Error("[KelvinTwo] spawned Kelvin never showed up in the world simulation");
            SonsTools.ShowMessage("KelvinTwo: เรียกเคลวินตัวที่ 2 ไม่สำเร็จ ดู _Redloader\\Latest.log", 8f);
        }
    }

    private void ClientTick()
    {
        if (!_hasClientClone) return;
        ulong cloneNetId = _clientCloneNetId;
        bool IsCloneBody(VailActor a) =>
            KelvinTools.Entity(a) is { } e && e != null && e.isAttached && e.networkId.PackedValue == cloneNetId;
        _marker.Sync(KelvinTools.ActiveKelvins().Where(IsCloneBody).ToList(), IsCloneBody);
    }

    private void OnCloneAnnounced(BoltEntity entity)
    {
        _clientCloneNetId = entity.networkId.PackedValue;
        _hasClientClone = true;
    }

    private static readonly HashSet<string> LoggedFailures = new();

    /// <summary>Runs one step; logs its first failure only, so a broken step can't flood the log every second.</summary>
    private static void Safe(string step, Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            if (LoggedFailures.Add(step)) RLog.Error($"[KelvinTwo] {step} failed (further errors hidden): {ex}");
        }
    }
}
