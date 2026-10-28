using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Gameplay;
using TheForest.Utils;
using UnityEngine;

namespace KelvinTwo;

/// <summary>
/// Applies the clone's look (tint + name tag) on every machine, and its upgrades
/// (speed, vision, immortality) on the host. Kelvin bodies are pooled and can be
/// handed to the other Kelvin at any moment, so every per-frame use re-checks that
/// the body still belongs to the clone, and the full set is recomputed every tick.
/// </summary>
internal sealed class CloneMarker
{
    private sealed class Marked
    {
        public VailActor Actor;
        public Robby Robby;
        public readonly List<(Renderer renderer, Il2CppReferenceArray<Material> shared, Il2CppReferenceArray<Material> instances)> Originals = new();
        public float BaseFocusRange, BasePeripheralRange, BaseMoveSpeedLite;
        public bool Buffed;
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private readonly ModConfig _cfg;
    private readonly Dictionary<IntPtr, Marked> _marked = new();
    private Func<VailActor, bool> _stillClone = _ => false;
    private GUIStyle _tagStyle;

    internal CloneMarker(ModConfig cfg) => _cfg = cfg;

    internal static CloneMarker Instance { get; private set; }

    internal void Activate() => Instance = this;

    internal static bool IsClone(VailActor actor) =>
        Instance != null && Instance._marked.TryGetValue(actor.Pointer, out var m) && Instance.IsLive(m);

    // A pooled body may have been given to the original Kelvin since the last tick.
    private bool IsLive(Marked m) => m.Actor != null && _stillClone(m.Actor);

    /// <summary>
    /// Marks exactly the bodies in <paramref name="clones"/> and restores everything else.
    /// <paramref name="stillClone"/> is re-checked every frame between ticks.
    /// </summary>
    internal void Sync(List<VailActor> clones, Func<VailActor, bool> stillClone)
    {
        _stillClone = stillClone;
        var keep = new HashSet<IntPtr>();
        foreach (var actor in clones)
        {
            keep.Add(actor.Pointer);
            if (!_marked.ContainsKey(actor.Pointer))
                _marked[actor.Pointer] = Mark(actor);
        }

        foreach (var ptr in new List<IntPtr>(_marked.Keys))
        {
            if (keep.Contains(ptr)) continue;
            Restore(_marked[ptr]);
            _marked.Remove(ptr);
        }

        if (!KelvinTools.IsHost) return;
        foreach (var m in _marked.Values)
            ApplyHostBuffs(m);
    }

    internal void Clear() => Sync(new List<VailActor>(), _ => false);

    /// <summary>
    /// Called every Update and LateUpdate so the game's own animator writes don't win.
    /// While Kelvin is dealing with a player (order pad, handing a note, being helped up)
    /// he plays animations synced with the player's, so he runs at normal speed then -
    /// a faster Kelvin there would drift out of sync and could get stuck.
    /// </summary>
    internal void KeepSpeed()
    {
        if (!KelvinTools.IsHost) return;
        foreach (var m in _marked.Values)
        {
            if (!IsLive(m)) continue;
            var anim = m.Actor._animator;
            if (anim == null || anim.speed <= 0f) continue;
            anim.speed = NeedsNormalSpeed(m.Robby) ? 1f : _cfg.SpeedMultiplier;
        }
    }

    // Without the Robby component we can't tell when he's with a player, so stay safe at 1x.
    private static bool NeedsNormalSpeed(Robby robby) =>
        robby == null
        || robby._interactingPlayer != null
        || robby._waitingForOrders
        || robby._localPadState != Robby.PadState.Closed;

    internal void DrawTags()
    {
        if (!_cfg.ShowNameTag || _marked.Count == 0 || LocalPlayer._instance == null) return;
        var cam = LocalPlayer.MainCam;
        if (cam == null) return;

        _tagStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold
        };
        _tagStyle.normal.textColor = Tint();

        foreach (var m in _marked.Values)
        {
            if (!IsLive(m) || !m.Actor.gameObject.activeInHierarchy) continue;
            var head = m.Actor.transform.position + Vector3.up * 2.2f;
            var sp = cam.WorldToScreenPoint(head);
            if (sp.z <= 0f || sp.z > 60f) continue;
            var rect = new Rect(sp.x - 100f, Screen.height - sp.y - 14f, 200f, 28f);
            GUI.Label(rect, _cfg.CloneName, _tagStyle);
        }
    }

    private Color Tint() => new(_cfg.TintRgb[0], _cfg.TintRgb[1], _cfg.TintRgb[2], 1f);

    private Marked Mark(VailActor actor)
    {
        var m = new Marked { Actor = actor };
        FindRobby(m);
        Recolor(m);
        RLog.Msg($"[KelvinTwo] marked clone body (sim id {KelvinTools.SimId(actor)})");
        return m;
    }

    private static void FindRobby(Marked m)
    {
        try
        {
            m.Robby = m.Actor.GetComponentInChildren<Robby>(true);
            if (m.Robby == null) m.Robby = m.Actor.GetComponentInParent<Robby>();
        }
        catch (Exception ex)
        {
            RLog.Error("[KelvinTwo] Robby component lookup failed: " + ex.Message);
        }
        if (m.Robby == null)
            RLog.Msg("[KelvinTwo] no Robby component on clone body - speed-up disabled for safety");
    }

    private void Recolor(Marked m)
    {
        var tint = Tint();
        try
        {
            foreach (var r in m.Actor.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || (r.TryCast<SkinnedMeshRenderer>() == null && r.TryCast<MeshRenderer>() == null)) continue;
                var shared = r.sharedMaterials;
                var instances = r.materials; // per-renderer copies; destroyed again in Restore
                m.Originals.Add((r, shared, instances));
                foreach (var mat in instances)
                {
                    if (mat == null) continue;
                    if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, mat.GetColor(BaseColorId) * tint);
                    else if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, mat.GetColor(ColorId) * tint);
                }
            }
        }
        catch (Exception ex)
        {
            RLog.Error("[KelvinTwo] tint failed: " + ex.Message);
        }
    }

    /// <summary>Idempotent: re-applied every tick in case the game re-initialised the body.</summary>
    private void ApplyHostBuffs(Marked m)
    {
        var a = m.Actor;
        if (a == null) return;

        if (!m.Buffed)
        {
            m.BaseFocusRange = a._visionFocusRange;
            m.BasePeripheralRange = a._visionPeripheralRange;
            m.BaseMoveSpeedLite = a._moveSpeedLite;
            m.Buffed = true;
        }
        a._visionFocusRange = m.BaseFocusRange * _cfg.VisionMultiplier;
        a._visionPeripheralRange = m.BasePeripheralRange * _cfg.VisionMultiplier;
        a._moveSpeedLite = m.BaseMoveSpeedLite * _cfg.SpeedMultiplier;
        a.SetDamageEnabled(false);
        KelvinTools.RefillStats(a);
    }

    private static void Restore(Marked m)
    {
        foreach (var (renderer, shared, instances) in m.Originals)
        {
            if (renderer != null) renderer.sharedMaterials = shared;
            foreach (var mat in instances)
                if (mat != null) UnityEngine.Object.Destroy(mat);
        }

        if (m.Actor == null || !m.Buffed) return;
        m.Actor._visionFocusRange = m.BaseFocusRange;
        m.Actor._visionPeripheralRange = m.BasePeripheralRange;
        m.Actor._moveSpeedLite = m.BaseMoveSpeedLite;
        m.Actor.SetDamageEnabled(true);
        if (m.Actor._animator != null) m.Actor._animator.speed = 1f;
    }
}
