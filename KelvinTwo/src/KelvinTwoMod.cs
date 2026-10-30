using System;
using System.IO;
using RedLoader;
using Sons.Ai.Vail;
using SonsSdk;
using SonsSdk.Attributes;
using SonsSdk.Networking;
using TheForest.Utils;
using UnityEngine;

namespace KelvinTwo;

public class KelvinTwoMod : SonsMod
{
    private const float WorldReadyDelay = 15f;
    private const float WorldReadyRetry = 2f;

    private ModConfig _cfg = new();
    private CloneMarker _marker;
    private CloneManager _clones;
    private GliderAutoPickup _glider;
    private float _worldReadyAt = -1f;
    private bool _netRegistered;

    public KelvinTwoMod()
    {
        HarmonyPatchAll = true;
        OnUpdateCallback = OnUpdate;
        OnLateUpdateCallback = OnLateUpdate;
        OnGUICallback = OnGui;
    }

    protected override void OnInitializeMod()
    {
        var dir = DataPath.Path;
        var cfgPath = Path.Combine(dir, "config.json");
        try
        {
            _cfg = ModConfig.Load(cfgPath);
        }
        catch (Exception ex)
        {
            RLog.Error("[KelvinTwo] config.json invalid, using defaults: " + ex.Message);
        }

        CloneGuards.IgnoreHits = _cfg.AlwaysObedient;
        DebugHooks.Enabled = _cfg.DebugLog;
        SearchWidening.Multiplier = _cfg.CloneEnabled ? _cfg.SearchMultiplier : 1f;
        _marker = new CloneMarker(_cfg);
        _marker.Activate();
        _clones = new CloneManager(_cfg, new CloneStore(Path.Combine(dir, "clones.json")), _marker);
        _glider = new GliderAutoPickup(_cfg);
    }

    protected override void OnSdkInitialized() => RegisterNetEvent();

    private void RegisterNetEvent()
    {
        if (_netRegistered || _clones == null) return;
        try
        {
            Packets.Register(_clones.NetEvent);
            _netRegistered = true;
        }
        catch (Exception ex)
        {
            RLog.Error("[KelvinTwo] network registration failed (co-op tint/name tag off): " + ex.Message);
        }
    }

    protected override void OnGameStart()
    {
        RegisterNetEvent();
        ResetSession();
        _worldReadyAt = _cfg.CloneEnabled ? Time.unscaledTime + WorldReadyDelay : -1f;
    }

    protected override void OnSonsSceneInitialized(ESonsScene sonsScene)
    {
        if (sonsScene != ESonsScene.Title) return;
        _worldReadyAt = -1f;
        ResetSession();
    }

    // Per-world state, including features switched off by an error last session.
    private void ResetSession()
    {
        _speedFailed = _tagFailed = false;
        _clones?.Reset();
        _glider?.Reset();
    }

    private void OnUpdate()
    {
        _glider?.Update();
        if (!_cfg.CloneEnabled || _clones == null) return;

        if (_worldReadyAt > 0f && Time.unscaledTime >= _worldReadyAt)
        {
            if (LocalPlayer._instance == null || !LocalPlayer.IsInWorld || VailWorldSimulation._instance == null)
            {
                _worldReadyAt = Time.unscaledTime + WorldReadyRetry;
            }
            else
            {
                _worldReadyAt = -1f;
                try { DebugHooks.LogHolderRecipes(); }
                catch (Exception ex) { RLog.Msg("[KelvinTwo] recipe list: " + ex.Message); }
                try { _clones.OnWorldReady(); }
                catch (Exception ex) { RLog.Error("[KelvinTwo] world ready: " + ex); }
            }
        }

        _clones.Tick();
        KeepSpeed();
    }

    private void OnLateUpdate() => KeepSpeed();

    private void KeepSpeed() => EveryFrame(ref _speedFailed, "speed", () => _marker?.KeepSpeed());

    private void OnGui() => EveryFrame(ref _tagFailed, "name tag", () => _marker?.DrawTags());

    private bool _speedFailed, _tagFailed;

    // Test helpers for the F1 console, active only with "debugLog": true.
    [DebugCommand("k1order")]
    private void K1Order(string args) => DebugHooks.GiveOrder(false, args);

    [DebugCommand("k2order")]
    private void K2Order(string args) => DebugHooks.GiveOrder(true, args);

    /// <summary>Per-frame work: on the first error, log it and switch that feature off.</summary>
    private static void EveryFrame(ref bool failed, string what, Action action)
    {
        if (failed) return;
        try { action(); }
        catch (Exception ex)
        {
            failed = true;
            RLog.Error($"[KelvinTwo] {what} disabled after error: {ex}");
        }
    }
}
