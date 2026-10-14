using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RedLoader;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace SniperRifle
{
    /// <summary>
    /// Sniper scope view: while the sniper is aimed, zoom the camera, clip away the gun/arms close to the
    /// camera and draw a scope overlay (black ring, crosshair, mil-dots).
    /// The zoom is set in LateUpdate (after the game's camera scripts, before rendering) and undone as soon
    /// as the player camera has drawn the frame, so the game's own field-of-view logic never sees it.
    /// (1.0.1 registered with the game's FovManager, which keeps a single active source that the rifle's
    /// own aim zoom also uses: aiming in/out could leave the camera stuck zoomed in.)
    /// Setting it in beginCameraRendering is too late: HDRP has already set the camera up by then.
    /// </summary>
    internal static class Scope
    {
        internal static bool DevForce;
        internal static bool SuppressForTest;
        internal static bool IsScoped => _scoped;
        /// <summary>Field of view the last frame was actually drawn with (for tests).</summary>
        internal static float LastRenderedFov { get; private set; }

        private static bool _scoped;
        private static GameObject _canvasGo;
        private static RectTransform _lens, _left, _right;
        private static float _targetFov;
        private static float _nearClip;

        private static bool _hooked;
        private static Camera _applied;
        private static float _restoreFov, _restoreNear;

        /// <summary>Runs after the game's own camera/FOV scripts: zoom for this frame's rendering only.</summary>
        internal static void LateUpdate()
        {
            try
            {
                if (_applied != null) Restore(); // last frame's end-of-render callback did not run
                if (!_scoped) return;
                var cam = LocalPlayer._instance != null ? LocalPlayer.MainCam : null;
                if (cam == null) return;
                Hook();
                _restoreFov = cam.fieldOfView;
                _restoreNear = cam.nearClipPlane;
                _applied = cam;
                cam.fieldOfView = _targetFov;
                cam.nearClipPlane = Mathf.Max(_restoreNear, _nearClip);
            }
            catch (Exception e) { SniperRifleMod.LogOnce("Scope.LateUpdate", e); }
        }

        private static void Restore()
        {
            var cam = _applied;
            _applied = null;
            if (cam == null) return;
            cam.fieldOfView = _restoreFov;
            cam.nearClipPlane = _restoreNear;
        }

        internal static void Update(Config cfg)
        {
            bool want = false;
            try { want = ShouldScope(); } catch (Exception e) { SniperRifleMod.LogOnce("Scope.ShouldScope", e); }
            if (want && !_scoped) Enter(cfg);
            else if (!want && _scoped) Exit();
            if (_scoped) Layout();
        }

        private static bool ShouldScope()
        {
            if (DevForce) return LocalPlayer._instance != null;
            if (SuppressForTest) return false;
            if (LocalPlayer._instance == null || !LocalPlayer.IsInWorld || LocalPlayer.IsInInventory) return false;
            var item = LocalPlayer.Inventory?.RightHandItem;
            if (item == null || item._itemID != SniperItem.Id) return false;
            // Only while the game itself has the gun aimed (so the scope never shows when a shot would be refused,
            // e.g. during reload).
            var go = item.ItemObject;
            if (go == null) return false;
            var rw = go.GetComponent(Il2CppType.Of<RangedWeapon>())?.TryCast<RangedWeapon>();
            if (rw == null) return false;
            if (rw.IsAiming) return true;
            var ctrl = rw._rangedWeaponController ?? go.GetComponentInChildren(Il2CppType.Of<RangedWeaponController>(), true)?.TryCast<RangedWeaponController>();
            return ctrl != null && ctrl.IsAiming;
        }

        internal static void Exit()
        {
            if (!_scoped) return;
            _scoped = false;
            try { Restore(); if (_canvasGo != null) _canvasGo.SetActive(false); }
            catch (Exception e) { SniperRifleMod.LogOnce("Scope.Exit", e); }
        }

        private static void Enter(Config cfg)
        {
            try
            {
                EnsureOverlay();
                _targetFov = cfg.ScopeFov;
                _nearClip = cfg.ScopeNearClip;
                _canvasGo.SetActive(true);
                Layout();
                _scoped = true;
            }
            catch (Exception e) { SniperRifleMod.LogOnce("Scope.Enter", e); }
        }

        // ------------------------------------------------------------------ undo the zoom after rendering

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            RenderPipelineManager.add_beginCameraRendering(new Action<ScriptableRenderContext, Camera>(OnBeginCamera));
            RenderPipelineManager.add_endCameraRendering(new Action<ScriptableRenderContext, Camera>(OnEndCamera));
        }

        private static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            try
            {
                var main = LocalPlayer._instance != null ? LocalPlayer.MainCam : null;
                if (main != null && cam != null && cam.Pointer == main.Pointer) LastRenderedFov = cam.fieldOfView;
            }
            catch (Exception e) { SniperRifleMod.LogOnce("Scope.BeginCamera", e); }
        }

        private static void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            try
            {
                if (_applied == null || cam == null || cam.Pointer != _applied.Pointer) return;
                Restore();
            }
            catch (Exception e) { _applied = null; SniperRifleMod.LogOnce("Scope.EndCamera", e); }
        }

        private static void Layout()
        {
            float h = Screen.height, w = Screen.width;
            _lens.sizeDelta = new Vector2(h, h);
            float side = Mathf.Max(0f, (w - h) / 2f) + 2f;
            _left.sizeDelta = new Vector2(side, h + 4f);
            _right.sizeDelta = new Vector2(side, h + 4f);
        }

        private static void EnsureOverlay()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SniperScopeOverlay");
            UObject.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250;

            _lens = MakeImage("Lens", BuildScopeTexture(1024), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            // Texture2D.blackTexture is transparent, so tint a white texture black for the side bars.
            _left = MakeImage("Left", Texture2D.whiteTexture, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Color.black);
            _right = MakeImage("Right", Texture2D.whiteTexture, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Color.black);
            _canvasGo.SetActive(false);
        }

        private static RectTransform MakeImage(string name, Texture tex, Vector2 anchor, Vector2 pivot, Color? color = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvasGo.transform, false);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            if (color.HasValue) img.color = color.Value;
            var rt = img.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>Transparent lens with a soft black edge, thin crosshair and mil-dots; black outside the circle.</summary>
        private static Texture2D BuildScopeTexture(int size)
        {
            var px = new Color32[size * size];
            float c = (size - 1) / 2f, r = size * 0.485f, edge = size * 0.035f;
            float line = Mathf.Max(1.5f, size / 600f);
            float thick = line * 3.2f;
            float gap = size * 0.015f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (d >= r) a = 1f;
                    else
                    {
                        a = Mathf.Clamp01((d - (r - edge)) / edge); // dark rim fading into the lens
                        a = a * a * 0.85f;
                        float adx = Mathf.Abs(dx), ady = Mathf.Abs(dy);
                        // thick posts from the rim, thin hairs in the middle, small gap at the centre
                        bool hair = (adx <= line && ady > gap) || (ady <= line && adx > gap);
                        bool post = (adx <= thick && ady > r * 0.45f) || (ady <= thick && adx > r * 0.45f);
                        if (hair || post) a = 1f;
                        // mil-dots
                        for (int k = 1; k <= 4; k++)
                        {
                            float off = r * 0.09f * k;
                            float dot = line * 2.4f;
                            if ((Sq(dx) + Sq(Mathf.Abs(dy) - off) <= dot * dot) || (Sq(Mathf.Abs(dx) - off) + Sq(dy) <= dot * dot)) a = 1f;
                        }
                        if (d <= line * 1.6f) a = 1f; // centre dot
                    }
                    byte al = (byte)Mathf.RoundToInt(a * 255f);
                    px[y * size + x] = new Color32(0, 0, 0, al);
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels32(new Il2CppStructArray<Color32>(px));
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        private static float Sq(float v) => v * v;
    }
}
