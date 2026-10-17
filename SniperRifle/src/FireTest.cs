using System;
using System.IO;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime;
using RedLoader;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SniperRifle
{
    /// <summary>
    /// Development-only (Mods\SniperRifle\firetest.flag). For each phase the mod prepares a situation and
    /// writes debug\ready_N.flag (its content is the input the outside script should send: "aim", "combo"
    /// or "harvest"); the script sends REAL mouse/keyboard input to the game window and writes
    /// debug\input_done_N.flag. The mod logs the state every 0.25 s meanwhile. Input reads are only observed.
    /// Phase kinds: Combo (aim in/out, fire twice, then switch to the pistol), Accuracy (one scoped shot at the
    /// ground a few dozen metres away; where the bullet lands vs the crosshair) and Harvest (spawn and kill a
    /// rabbit in front of the player, hold E on it).
    /// </summary>
    internal sealed class FireTest
    {
        private enum Kind { Combo, Accuracy, Harvest, Stash, Cave, Note, Switch, Pickup, Eat, Inv, Key }

        // name, kind, aim correction (-1 config / 0 off / 1 on), equip the sniper first
        private static readonly (string name, Kind kind, int fix, bool equip)[] Phases =
        {
            ("sniper: crawl into a cave while AIMING (gets stuck)", Kind.Cave, -1, true),
            ("press the unstuck key (F8)", Kind.Key, -1, false),
            ("pickup:393 big rock", Kind.Pickup, -1, true),
            ("finally: aim + fire", Kind.Combo, -1, true),        };

        private readonly string _dir;
        private readonly StringBuilder _log = new StringBuilder();
        private float _at = -1f;
        private int _step;
        private int _phase;
        private int _ammoBefore;
        private float _waitStart;
        private bool _dumpedFlip, _shotScoped;
        private static int _fireDownTrue, _firePerformedTrue;

        private Sons.Ai.Vail.VailActor _rabbit;
        private string _stashAwayState = "";
        private int _moved;
        private Sons.Animation.PlayerControl.CaveEntranceCutscene _cave;
        private Vector3 _caveStartPos;
        private GameObject _note;
        private int _pickId, _pickBefore;
        private readonly System.Collections.Generic.HashSet<IntPtr> _usedCaves = new System.Collections.Generic.HashSet<IntPtr>();
        private bool _caveWasEmpty;
        private int _meatId = -1, _meatBefore;

        // bullet tracking (accuracy phases)
        private float _trackAfter = float.MaxValue;
        private IntPtr _trackedVisual;
        private bool _tracking, _trackDone;
        private Vector3 _lastPos, _lastVel, _impact;
        private string _impactWhat = "";
        private int _trackFrames;
        private float _trackT0, _trackV0, _trackT1, _trackV1;
        private static readonly int RifleHeldHash = Animator.StringToHash("RifleHeld");

        internal FireTest(string dir) { _dir = Path.Combine(dir, "debug"); Directory.CreateDirectory(_dir); }

        internal void OnGameStart() { _at = Time.unscaledTime + 20f; _step = 0; }

        private void Log(string s) { _log.AppendLine($"[{Time.unscaledTime:0.00}] {s}"); RLog.Msg("[SniperRifle/fire] " + s); Flush(); }
        private void Flush() => File.WriteAllText(Path.Combine(_dir, "firetest.txt"), _log.ToString(), new UTF8Encoding(true));
        private void Wait(float s, int next) { _at = Time.unscaledTime + s; _step = next; }
        private void Ready(string mode) { File.WriteAllText(Path.Combine(_dir, $"ready_{_phase}.flag"), mode); _waitStart = Time.unscaledTime; }
        private bool InputDone => File.Exists(Path.Combine(_dir, $"input_done_{_phase}.flag"));

        internal void Update(float now)
        {
            try { Track(); } catch (Exception e) { Log("track failed: " + e.Message); _tracking = false; _trackDone = true; }
            if (_at < 0f || now < _at) return;
            _at = -1f;
            try { Step(); }
            catch (Exception e) { Log($"step {_step} failed: {e}"); }
        }

        // ------------------------------------------------------------------ helpers

        private static RangedWeapon Weapon()
        {
            var go = LocalPlayer.Inventory?.RightHandItem?.ItemObject;
            return go?.GetComponent(Il2CppType.Of<RangedWeapon>())?.TryCast<RangedWeapon>();
        }

        private static RangedWeaponController Controller()
        {
            var go = LocalPlayer.Inventory?.RightHandItem?.ItemObject;
            return go?.GetComponentInChildren(Il2CppType.Of<RangedWeaponController>(), true)?.TryCast<RangedWeaponController>();
        }

        private static string AnimatorParams(Animator a)
        {
            if (a == null) return "no animator";
            var sb = new StringBuilder();
            var ps = a.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                if (p.type == AnimatorControllerParameterType.Trigger) continue;
                string v = p.type == AnimatorControllerParameterType.Float ? a.GetFloat(p.nameHash).ToString("0.00")
                         : p.type == AnimatorControllerParameterType.Int ? a.GetInteger(p.nameHash).ToString()
                         : (a.GetBool(p.nameHash) ? "T" : "F");
                if (v == "0" || v == "0.00" || v == "F") continue; // only non-default values
                sb.Append(p.name).Append('=').Append(v).Append(' ');
            }
            return sb.ToString();
        }

        private static string Layers(Animator a)
        {
            if (a == null) return "";
            var sb = new StringBuilder("layers:");
            for (int l = 0; l < a.layerCount; l++)
            {
                if (a.GetLayerWeight(l) < 0.01f && l > 0) continue;
                var st = a.GetCurrentAnimatorStateInfo(l);
                sb.Append($" {l}:{st.shortNameHash}{(a.IsInTransition(l) ? "~" : "")}");
            }
            return sb.ToString();
        }

        private static string State()
        {
            var inv = LocalPlayer.Inventory;
            var an = LocalPlayer.Animator;
            var rw = Weapon();
            var c = Controller();
            string common = $"item={inv.RightHandItem?._itemID} prev={inv.PreviousRightHandItem?._itemID} animId={(an != null ? an.GetInteger(Animator.StringToHash("RightHandHeldItemId")) : -1)} " +
                            $"equip={inv._currentEquipState} empty={inv.IsRightHandEmpty()} blockInv={inv.BlockTogglingInventory} blockRanged={inv.blockRangedAttack} " +
                            $"rifleHeld={(an != null && an.GetBool(RifleHeldHash))} locked={LocalPlayer.FpCharacter?.Locked}/{LocalPlayer.FpCharacter?.MovementLocked} handsEmpty={Sons.Cutscenes.Cutscene.IsPlayerHandsEmpty()} pitch={CamPitchDown():0.0} scoped={Scope.IsScoped} drawnFov={Scope.LastRenderedFov:0.0}";
            if (rw == null) return common;
            return common + $" ammo={rw._ammo?._currentCount} aiming={Sons.Input.InputSystem.IsRangedWeaponAiming} w={Sons.Input.InputSystem.RangedWeaponAimingWeight:0.00} " +
                   $"canAim={c?.CanAim()} idle={c?.IsItemIdle()} reloading={c?.IsReloading()} fireDown={_fireDownTrue} focus={Application.isFocused}";
        }

        private static float CamPitchDown()
        {
            var f = LocalPlayer.MainCamTr.forward;
            return -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>Look down by <paramref name="deg"/> (negative = up) and turn right by <paramref name="yaw"/>.</summary>
        private static void Look(float deg, float yaw = 0f)
        {
            var cr = LocalPlayer.CamRotator;
            if (cr != null)
            {
                var t = cr._targetAngles; t.x = deg; cr._targetAngles = t;   // positive x = look down (test 4)
                var f = cr._followAngles; f.x = deg; cr._followAngles = f;
            }
            if (Mathf.Abs(yaw) > 0.01f && LocalPlayer.MainRotator != null)
            {
                var mr = LocalPlayer.MainRotator;
                var t = mr._targetAngles; t.y += yaw; mr._targetAngles = t;
                var f = mr._followAngles; f.y += yaw; mr._followAngles = f;
            }
        }

        private static bool RayFromCamera(float max, out RaycastHit best)
        {
            best = default;
            var tr = LocalPlayer.MainCamTr;
            var root = LocalPlayer._instance.transform.root;
            var hits = Physics.RaycastAll(tr.position, tr.forward, max, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.distance < 0.3f || h.distance >= bd) continue;
                if (h.collider != null && h.collider.transform.root == root) continue;
                bd = h.distance; best = h;
            }
            return bd < float.MaxValue;
        }

        /// <summary>A spot 40-250 m away (prefer ~90 m) that a level-ish shot would hit.</summary>
        private static bool FindFarTarget(out float pitchDown, out float yaw, out float dist)
        {
            pitchDown = yaw = dist = 0f;
            var tr = LocalPlayer.MainCamTr;
            var root = LocalPlayer._instance.transform.root;
            float camYaw = tr.eulerAngles.y, bestScore = float.MaxValue;
            float[] pitches = { -3f, -1.5f, 0f, 1.5f, 3f, 5f };
            for (int y = 0; y < 360; y += 15)
            {
                foreach (float p in pitches)
                {
                    var dir = Quaternion.Euler(p, camYaw + y, 0f) * Vector3.forward;
                    var hits = Physics.RaycastAll(tr.position, dir, 300f, ~0, QueryTriggerInteraction.Ignore);
                    float d = float.MaxValue;
                    for (int i = 0; i < hits.Length; i++)
                    {
                        var h = hits[i];
                        if (h.distance < 0.3f || h.distance >= d) continue;
                        if (h.collider != null && h.collider.transform.root == root) continue;
                        d = h.distance;
                    }
                    if (d < 40f || d > 250f) continue;
                    float score = Mathf.Abs(d - 90f) + Mathf.Abs(p) * 2f;
                    if (score >= bestScore) continue;
                    bestScore = score; pitchDown = p; yaw = y > 180 ? y - 360 : y; dist = d;
                }
            }
            return bestScore < float.MaxValue;
        }

        private void Skip(string why)
        {
            Log("  SKIPPED: " + why);
            Ready("skip");
            NextPhase();
        }

        private static string V(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";

        private void FindItems()
        {
            var sb = new StringBuilder("items:");
            var items = Sons.Items.Core.ItemDatabaseManager.Items;
            int knife = -1;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                string n = it?._name ?? "";
                if (n.IndexOf("knife", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("meat", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("rabbit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sb.Append($" {it._id}:{n}");
                    if (knife < 0 && n.Equals("Knife", StringComparison.OrdinalIgnoreCase)) knife = it._id;
                    if (_meatId < 0 && n.Equals("Meat", StringComparison.OrdinalIgnoreCase)) _meatId = it._id;
                }
            }
            Log(sb.ToString());
            if (knife > 0) Log($"add knife {knife}: {LocalPlayer.Inventory.AddItem(knife, 1, true, false, null)}");
        }

        // ------------------------------------------------------------------ bullet tracking

        private void Track()
        {
            if (_trackDone || AimFix.Last == null || AimFix.Last.Time < _trackAfter) return;
            var bh = Ballistics.BulletHandler.instance;
            var list = bh?.Bullets;
            if (list == null) return;
            if (!_tracking)
            {
                // newest bullet fired by the held sniper
                var rw = Weapon();
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var b = list[i];
                    if (rw != null && b.weapon != null && b.weapon.Pointer != rw.Pointer) continue;
                    _trackedVisual = b.visualBullet != null ? b.visualBullet.Pointer : IntPtr.Zero;
                    _lastPos = b.position; _lastVel = b.velocity;
                    _tracking = true; _trackFrames = 1;
                    _trackT0 = _trackT1 = Time.time; _trackV0 = _trackV1 = _lastVel.magnitude;
                    Log($"  tracking bullet: pos={V(_lastPos)} speed={_lastVel.magnitude:0.0} m/s maxSpeed={b.info?.maxSpeed:0.0} drag={b.info?.dragCoefficient:0.000} weight={b.info?.weight:0.000}");
                    return;
                }
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (_trackedVisual != IntPtr.Zero && (b.visualBullet == null || b.visualBullet.Pointer != _trackedVisual)) continue;
                if (_trackedVisual == IntPtr.Zero && i != list.Count - 1) continue;
                _lastPos = b.position; _lastVel = b.velocity; _trackFrames++;
                _trackT1 = Time.time; _trackV1 = _lastVel.magnitude;
                return;
            }
            // gone: it hit something between the last position and the next step
            _tracking = false; _trackDone = true;
            float step = Mathf.Max(_lastVel.magnitude * 0.1f, 2f);
            if (Physics.Raycast(_lastPos, _lastVel.normalized, out var hit, step, ~0, QueryTriggerInteraction.Ignore))
            { _impact = hit.point; _impactWhat = hit.collider != null ? hit.collider.name : "?"; }
            else { _impact = _lastPos; _impactWhat = "(no surface found, last position)"; }
            Log($"  bullet stopped after {_trackFrames} frames at {V(_impact)} on {_impactWhat}");
            if (_trackT1 - _trackT0 > 0.01f && _trackV1 > 1f && _trackV0 > 1f)
                Log($"  measured drag: speed {_trackV0:0.0} -> {_trackV1:0.0} m/s in {_trackT1 - _trackT0:0.000} s => k = {(1f / _trackV1 - 1f / _trackV0) / (_trackT1 - _trackT0):0.00000} (model k = {AimFix.Last?.Drag:0.00000})");
        }

        private void LogAccuracy()
        {
            var s = AimFix.Last;
            if (s == null || s.Time < _trackAfter) { Log("ACCURACY: no sniper shot recorded"); return; }
            var inv = Quaternion.Inverse(s.CamRot);
            Vector3 so = inv * (s.Spawn - s.CamPos);
            Vector3 od = inv * s.OrigDir, nd = inv * s.NewDir;
            float Deg(float a, float b) => Mathf.Atan2(a, b) * Mathf.Rad2Deg;
            Log($"  shot: aimHit={s.AimHit} aimDist={Vector3.Distance(s.CamPos, s.AimPoint):0.0} spawnFromCam(cam space)={V(so)} zeroAngle={s.OrigZero:0.000} " +
                $"origDir(up {Deg(od.y, od.z):0.000}° right {Deg(od.x, od.z):0.000}°) newDir(up {Deg(nd.y, nd.z):0.000}° right {Deg(nd.x, nd.z):0.000}°) applied={s.Applied} iter={s.Iterations} predictedMiss={s.PredictedMissDeg:0.000}° lift={s.LiftDeg:0.000}° speed={s.Speed:0} k={s.Drag:0.00000}");
            if (!_trackDone) { Log("ACCURACY: bullet not tracked to impact"); return; }
            Vector3 li = inv * (_impact - s.CamPos);
            float up = Deg(li.y, li.z), right = Deg(li.x, li.z);
            float pxPerDeg = Screen.height / Mathf.Max(1f, SniperRifleMod.Cfg.ScopeFov);
            Log($"ACCURACY phase {_phase} ({Phases[_phase].name}): impact vs crosshair: {(up >= 0 ? "above" : "below")} {Mathf.Abs(up):0.000}° / {(right >= 0 ? "right" : "left")} {Mathf.Abs(right):0.000}° " +
                $"= {up * pxPerDeg:0} px up, {right * pxPerDeg:0} px right in the scope; distance to aim point {Vector3.Distance(_impact, s.AimPoint):0.00} m (height {_impact.y - s.AimPoint.y:+0.00;-0.00} m)");
        }

        // ------------------------------------------------------------------ phases

        private void Step()
        {
            var inv = LocalPlayer.Inventory;
            var ph = _phase < Phases.Length ? Phases[_phase] : default;
            switch (_step)
            {
                case 0:
                    InstallObservers();
                    AnimFix.Observer = s => Log("  [anim] " + s);
                    Unstuck.Observer = s => Log("  [unstuck] " + s);
                    inv.AddItem(SniperItem.Id, 1, true, false, null);
                    inv.AddItem(SniperItem.PistolAmmoId, 40, true, false, null);
                    inv.AddItem(SniperItem.PistolId, 1, true, false, null);
                    inv.AddItem(SniperItem.RifleId, 1, true, false, null);
                    inv.AddItem(387, 20, true, false, null);
                    FindItems();
                    _phase = 0;
                    Wait(1f, 1);
                    return;

                case 1:
                    AimFix.ForceMode = ph.fix;
                    Log($"=== phase {_phase}: {ph.name}");
                    if (ph.equip)
                    {
                        inv.UnequipItemAtSlot(Sons.Items.Core.EquipmentSlot.RightHand, false, true, false, true, false, false);
                        int equipId = ph.name.StartsWith("VANILLA pistol") ? SniperItem.PistolId : ph.name.StartsWith("VANILLA") ? SniperItem.RifleId : SniperItem.Id;
                        Log($"  equip {equipId}: {inv.TryEquip(equipId, false, false, false)}");
                        Wait(9f, 2);
                    }
                    else Wait(0.5f, 2);
                    return;

                case 2:
                {
                    var rw = Weapon();
                    var c = Controller();
                    if (ph.equip && rw != null && c != null) c.ReloadAmmo(rw, false);
                    _dumpedFlip = _shotScoped = false;
                    if (ph.kind == Kind.Accuracy)
                    {
                        if (FindFarTarget(out float tp, out float ty, out float td)) { Look(tp, ty); Log($"  far target {td:0} m: look down {tp:0.0}°, turn {ty:0}°"); }
                        else { Look(0f); Log("  no far target found, looking level"); }
                        Wait(2f, 3);
                        return;
                    }
                    if (ph.kind == Kind.Harvest) { Look(45f); Wait(1.0f, 10); return; }
                    if (ph.kind == Kind.Cave) { Wait(0.5f, 30); return; }
                    if (ph.kind == Kind.Note) { Look(35f); Wait(1.0f, 50); return; }
                    if (ph.kind == Kind.Switch) { Ready("skip"); Wait(0.5f, 60); return; }
                    if (ph.kind == Kind.Pickup) { Ready("skip"); Look(35f); Wait(1.0f, 70); return; }
                    if (ph.kind == Kind.Eat) { Ready("skip"); Wait(0.5f, 80); return; }
                    if (ph.kind == Kind.Inv) { Ready("skip"); Wait(0.5f, 85); return; }
                    if (ph.kind == Kind.Key) { Log("  before the key: " + Unstuck.Snapshot()); Ready("f8"); Wait(0.25f, 90); return; }
                    if (ph.kind == Kind.Stash)
                    {
                        Ready("skip");
                        Log("  before: " + State());
                        inv.StashRightHandItem(false, true, false);
                        Log("  StashRightHandItem called");
                        _waitStart = Time.unscaledTime;
                        Wait(0.2f, 20);
                        return;
                    }
                    Wait(1.5f, 3);
                    return;
                }

                case 3: // ready for input
                {
                    _ammoBefore = Weapon()?._ammo?._currentCount ?? -1;
                    _fireDownTrue = _firePerformedTrue = 0;
                    if (ph.kind == Kind.Accuracy)
                    {
                        _trackAfter = Time.unscaledTime; _tracking = false; _trackDone = false; _trackedVisual = IntPtr.Zero;
                        Log(RayFromCamera(800f, out var h) ? $"  crosshair on {h.collider?.name} at {h.distance:0.0} m" : "  crosshair on nothing");
                    }
                    Log("ready: " + State());
                    Ready(ph.kind == Kind.Harvest ? "harvest" : ph.kind == Kind.Accuracy ? "aim" : "combo");
                    Wait(0.25f, 4);
                    return;
                }

                case 4: // the outside script is sending input now
                {
                    Log("  " + State() + (ph.kind == Kind.Harvest && _rabbit != null ? $" skinned={_rabbit.IsSkinned()} meat={(_meatId > 0 ? inv.AmountOf(_meatId, true, false) : -1)}" : ""));
                    if (!_shotScoped && Scope.IsScoped && Sons.Input.InputSystem.RangedWeaponAimingWeight > 0.99f) { _shotScoped = true; ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"scoped_{_phase}.png")); }
                    bool done = InputDone;
                    if (!done && Time.unscaledTime - _waitStart < 45f) { Wait(0.25f, 4); return; }
                    if (ph.kind == Kind.Accuracy && !_trackDone && Time.unscaledTime - _waitStart < 50f) { Wait(0.25f, 4); return; }
                    int after = Weapon()?._ammo?._currentCount ?? -1;
                    if (ph.kind == Kind.Harvest)
                    {
                        Log($"HARVEST RESULT: skinned={_rabbit?.IsSkinned()} meat {_meatBefore} -> {(_meatId > 0 ? inv.AmountOf(_meatId, true, false) : -1)} {(done ? "" : "(timeout: no input)")}");
                        ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"harvest_{_phase}.png"));
                        _waitStart = Time.unscaledTime;
                        Wait(0.5f, 6);
                        return;
                    }
                    Log($"RESULT phase {_phase} ({ph.name}): ammo {_ammoBefore} -> {after} {(after < _ammoBefore ? "FIRED" : "NOT FIRED")} {(done ? "" : "(timeout: no input)")}");
                    ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"fire_{_phase}.png"));
                    if (ph.kind == Kind.Accuracy) { LogAccuracy(); NextPhase(); return; }
                    Log($"switch to pistol: TryEquip={inv.TryEquip(SniperItem.PistolId, false, false, false)}");
                    _waitStart = Time.unscaledTime;
                    Wait(0.25f, 5);
                    return;
                }

                case 5: // after a combo: switching to the pistol must work
                {
                    Log("  switching: " + State());
                    if (Time.unscaledTime - _waitStart < 6f) { Wait(0.5f, 5); return; }
                    Log($"SWITCH RESULT phase {_phase}: right hand = {inv.RightHandItem?._itemID} {(inv.RightHandItem?._itemID == SniperItem.PistolId ? "OK" : "STUCK")}");
                    NextPhase();
                    return;
                }

                case 6: // after the harvest: watch what the hands do
                {
                    Log("  after harvest: " + State() + " " + Layers(LocalPlayer.Animator));
                    if (Time.unscaledTime - _waitStart < 6f) { Wait(0.5f, 6); return; }
                    Log("  PLAYER animator after harvest: " + AnimatorParams(LocalPlayer.Animator));
                    NextPhase();
                    return;
                }

                // ---- sniper -> pistol -> empty hands
                case 60:
                    Log("  holding the sniper: " + State());
                    Log($"  switch to pistol: {inv.TryEquip(SniperItem.PistolId, false, false, false)}");
                    _waitStart = Time.unscaledTime;
                    Wait(0.5f, 61);
                    return;
                case 61:
                    Log("  pistol: " + State());
                    if (Time.unscaledTime - _waitStart < 4f) { Wait(0.5f, 61); return; }
                    _stashAwayState = State();
                    inv.StashRightHandItem(false, true, false);
                    Log("  put the pistol away");
                    _waitStart = Time.unscaledTime;
                    Wait(0.5f, 62);
                    return;
                case 62:
                    Log("  empty: " + State());
                    if (Time.unscaledTime - _waitStart < 6f) { Wait(0.5f, 62); return; }
                    Log("  PLAYER animator, hands empty: " + AnimatorParams(LocalPlayer.Animator) + " " + Layers(LocalPlayer.Animator));
                    Log($"SWITCH-AWAY RESULT: with the pistol [{_stashAwayState}] | hands now: item={inv.RightHandItem?._itemID} animId={LocalPlayer.Animator.GetInteger(Animator.StringToHash("RightHandHeldItemId"))} rifleHeld={LocalPlayer.Animator.GetBool(RifleHeldHash)} handsEmpty={Sons.Cutscenes.Cutscene.IsPlayerHandsEmpty()}");
                    NextPhase();
                    return;

                // ---- pick up an item from the ground the way E does (PickUp.Take)
                case 70:
                {
                    _pickId = int.Parse(System.Text.RegularExpressions.Regex.Match(ph.name, @"pickup:(\d+)").Groups[1].Value);
                    var prefab = SonsSdk.ItemTools.GetPickupPrefab(_pickId);
                    if (prefab == null) { Skip($"item {_pickId} has no pickup prefab"); return; }
                    if (!RayFromCamera(10f, out var h)) { Skip("no ground in front"); return; }
                    _note = UnityEngine.Object.Instantiate(prefab.gameObject, h.point + Vector3.up * 0.15f, Quaternion.identity);
                    _note.SetActive(true);
                    _pickBefore = inv.AmountOf(_pickId, true, false);
                    Log($"  spawned pickup for {_pickId} ({_note.name}) {h.distance:0.0} m away; have {_pickBefore}");
                    Wait(1.5f, 71);
                    return;
                }
                case 71:
                {
                    var cam = LocalPlayer.MainCamTr;
                    Vector3 d = _note.transform.position - cam.position;
                    Look(Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg,
                         Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.forward, Vector3.up), Vector3.ProjectOnPlane(d, Vector3.up), Vector3.up));
                    var pick = _note.GetComponentInChildren(Il2CppType.Of<Sons.Gameplay.PickUp>(), true)?.TryCast<Sons.Gameplay.PickUp>();
                    if (pick == null) { Skip("no PickUp component"); return; }
                    Log($"  before take: fullBody={pick._needsFullBodyInteraction} blockedFullBody={pick.BlockedFullBodyInteraction()} canGather={pick.CanPlayerGather()} autoEquip={pick._forceAutoEquip}/{!pick._preventAutoEquip} " + State());
                    pick.Take();
                    Log("  PickUp.Take() called");
                    _waitStart = Time.unscaledTime;
                    Wait(0.25f, 72);
                    return;
                }
                case 72:
                    Log("  taking: " + State() + " " + Layers(LocalPlayer.Animator));
                    if (Time.unscaledTime - _waitStart < 6f) { Wait(0.25f, 72); return; }
                    Log($"PICKUP RESULT {_pickId}: amount {_pickBefore} -> {inv.AmountOf(_pickId, true, false)}, hand={inv.RightHandItem?._itemID} " + State());
                    Log("  PLAYER animator after pickup: " + AnimatorParams(LocalPlayer.Animator));
                    ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"pickup_{_phase}.png"));
                    Wait(0.1f, 73);
                    return;
                case 73:
                    if (inv.RightHandItem?._itemID != SniperItem.Id)
                        Log($"  take the sniper out: {inv.TryEquip(SniperItem.Id, false, false, false)}");
                    _waitStart = Time.unscaledTime;
                    Wait(0.5f, 74);
                    return;
                case 74:
                {
                    Log("  sniper again: " + State());
                    if (Time.unscaledTime - _waitStart < 4f) { Wait(0.5f, 74); return; }
                    var c = Controller();
                    bool ok = inv.RightHandItem?._itemID == SniperItem.Id && c != null && c.CanAim() && inv._currentEquipState.ToString() == "Idle"
                              && !(LocalPlayer.FpCharacter?.Locked ?? false);
                    Log($"AFTER {(ph.kind == Kind.Pickup ? "PICKUP " + _pickId : ph.name)}: {(ok ? "OK" : "STUCK")} " + State());
                    NextPhase();
                    return;
                }

                // ---- the outside script presses the unstuck key
                case 90:
                    if (!InputDone && Time.unscaledTime - _waitStart < 30f) { Wait(0.25f, 90); return; }
                    Wait(1.5f, 91);
                    return;
                case 91:
                    Log("  after the key: " + Unstuck.Snapshot());
                    Wait(0.1f, 73);
                    return;

                // ---- eat something from the inventory while holding the sniper
                case 80:
                {
                    int food = -1; string foodName = "";
                    string[] wanted = { "EnergyBar", "MRE", "CatFood", "Meat" };
                    foreach (var w in wanted)
                    {
                        int id = Sons.Items.Core.ItemDatabaseManager.ItemIdByName(w);
                        if (id > 0) { food = id; foodName = w; break; }
                    }
                    if (food < 0) { Skip("no food item found"); return; }
                    inv.AddItem(food, 2, true, false, null);
                    var action = LocalPlayer._instance.GetComponentInChildren(Il2CppType.Of<TheForest.Player.Actions.PlayerConsumeItemAction>(), true)?.TryCast<TheForest.Player.Actions.PlayerConsumeItemAction>();
                    var store = inv._itemInstanceManager;
                    var all = store.GetAllItemsOfType(food);
                    if (action == null || all == null || all.Count == 0) { Skip($"can't consume {foodName}: action={action != null} items={all?.Count}"); return; }
                    Log($"  eat {food}:{foodName} while holding the sniper: " + State());
                    action.ConsumeItem(all[0], true, null);
                    _waitStart = Time.unscaledTime;
                    Wait(0.25f, 81);
                    return;
                }
                case 81:
                    Log("  eating: " + State());
                    if (Time.unscaledTime - _waitStart < 6f) { Wait(0.25f, 81); return; }
                    Log("  PLAYER animator after eating: " + AnimatorParams(LocalPlayer.Animator));
                    Wait(0.1f, 73);
                    return;

                // ---- open and close the inventory while holding the sniper
                case 85:
                    Log("  open inventory: " + State());
                    inv.Open();
                    Wait(3f, 86);
                    return;
                case 86:
                    Log("  inventory open: " + State());
                    inv.Close(true);
                    _waitStart = Time.unscaledTime;
                    Wait(0.5f, 87);
                    return;
                case 87:
                    Log("  inventory closed: " + State());
                    if (Time.unscaledTime - _waitStart < 3f) { Wait(0.5f, 87); return; }
                    Wait(0.1f, 73);
                    return;

                // ---- pick up a note
                case 50:
                {
                    int noteId = -1; string noteName = "";
                    var items = Sons.Items.Core.ItemDatabaseManager.Items;
                    for (int i = 0; i < items.Count && noteId < 0; i++)
                    {
                        var it = items[i];
                        if (it != null && (it._name ?? "").IndexOf("Email", StringComparison.OrdinalIgnoreCase) >= 0 && SonsSdk.ItemTools.GetPickupPrefab(it._id) != null) { noteId = it._id; noteName = it._name; }
                    }
                    if (noteId < 0) { Skip("no email item with a pickup prefab"); return; }
                    if (!RayFromCamera(10f, out var h)) { Skip("no ground in front"); return; }
                    var prefab = SonsSdk.ItemTools.GetPickupPrefab(noteId);
                    var go = UnityEngine.Object.Instantiate(prefab.gameObject, h.point + Vector3.up * 0.1f, Quaternion.identity);
                    go.SetActive(true);
                    _note = go;
                    Log($"  spawned note pickup {noteId}:{noteName} ({go.name}) at {V(go.transform.position)} {h.distance:0.0} m away");
                    Wait(1.5f, 51);
                    return;
                }
                case 51:
                {
                    var cam = LocalPlayer.MainCamTr;
                    Vector3 d = _note.transform.position - cam.position;
                    float pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                    float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.forward, Vector3.up), Vector3.ProjectOnPlane(d, Vector3.up), Vector3.up);
                    Look(pitch, yaw);
                    var gui = _note.transform.Find("_PickupGui_");
                    Log($"  note at {V(_note.transform.position)} dist {d.magnitude:0.0} m, looking at it; prompt={(gui != null ? gui.gameObject.activeInHierarchy.ToString() : "n/a")}; " + State());
                    Wait(1.0f, 52);
                    return;
                }
                case 52:
                {
                    Ready("skip");
                    var pick = _note.GetComponentInChildren(Il2CppType.Of<Sons.Gameplay.PickUp>(), true)?.TryCast<Sons.Gameplay.PickUp>();
                    if (pick == null) { Log("  note has no PickUp component"); }
                    else { pick.Collect(); Log("  PickUp.Collect() called"); }
                    _waitStart = Time.unscaledTime;
                    Wait(0.25f, 53);
                    return;
                }
                case 53:
                {
                    var ri = LocalPlayer.Inventory.RightHandItem;
                    Log($"  note: held={ri?._itemID} noteObj={(_note != null ? _note.activeInHierarchy.ToString() : "gone")} " + State() + " " + Layers(LocalPlayer.Animator));
                    bool waiting = !InputDone || Time.unscaledTime - _waitStart < 10f;
                    if (waiting && Time.unscaledTime - _waitStart < 45f) { Wait(0.25f, 53); return; }
                    Log($"NOTE RESULT: right hand={LocalPlayer.Inventory.RightHandItem?._itemID} handsEmpty={Sons.Cutscenes.Cutscene.IsPlayerHandsEmpty()} " + State());
                    Log("  PLAYER animator after note: " + AnimatorParams(LocalPlayer.Animator));
                    ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"note_{_phase}.png"));
                    _waitStart = Time.unscaledTime;
                    Wait(0.5f, 54);
                    return;
                }
                case 54:
                    Log("  after note: " + State());
                    if (Time.unscaledTime - _waitStart < 5f) { Wait(0.5f, 54); return; }
                    NextPhase();
                    return;

                // ---- cave entrance cutscene
                case 30:
                {
                    _cave = null;
                    var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Sons.Animation.PlayerControl.CaveEntranceCutscene>());
                    var me = LocalPlayer.Transform.position;
                    float best = float.MaxValue;
                    var sb = new StringBuilder($"  cave entrance cutscenes: {all.Length}:");
                    for (int i = 0; i < all.Length; i++)
                    {
                        var c = all[i].TryCast<Sons.Animation.PlayerControl.CaveEntranceCutscene>();
                        if (c == null || !c.gameObject.scene.IsValid()) continue;
                        float d = Vector3.Distance(me, c.transform.position);
                        bool enabled = false, open = false;
                        try { enabled = c.CaveEntranceEnabled(); open = c.CaveIsOpen(); } catch { }
                        if (i < 12) sb.Append($" [{c.name} dir={c._direction} d={d:0} active={c.gameObject.activeInHierarchy} enabled={enabled} open={open}]");
                        if (c._direction.ToString().IndexOf("Exit", StringComparison.OrdinalIgnoreCase) >= 0 || c._isDemonBossCaveExit) continue;
                        if (_usedCaves.Contains(c.Pointer)) continue;
                        if (d < best) { best = d; _cave = c; }
                    }
                    Log(sb.ToString());
                    if (_cave == null) { Skip("no unused cave entrance found"); return; }
                    _usedCaves.Add(_cave.Pointer);
                    var at = _cave._alignmentTransform != null ? _cave._alignmentTransform : _cave.transform;
                    Log($"  using {_cave.name} dir={_cave._direction} at {V(at.position)} ({best:0} m away); hands empty now: {Sons.Cutscenes.Cutscene.IsPlayerHandsEmpty()}");
                    Vector3 to = at.position - at.forward * 1.5f + Vector3.up * 0.5f;
                    LocalPlayer.Transform.position = to;
                    if (LocalPlayer.Rigidbody != null) LocalPlayer.Rigidbody.position = to;
                    Log($"  teleported to {V(to)}");
                    Wait(8f, 35);
                    return;
                }
                case 35:
                    Ready(ph.name.Contains("AIMING") ? "aimhold" : "skip");
                    Wait(2.5f, 31);
                    return;
                case 31:
                {
                    Log("  at the cave: " + State() + $" inCaves={LocalPlayer.IsInCaves} canPlay={_cave.CanBePlayed()} enabled={_cave.CaveEntranceEnabled()} pos={V(LocalPlayer.Transform.position)}");
                    _caveStartPos = LocalPlayer.Transform.position;
                    _caveWasEmpty = false;
                    Log($"  Play() -> {_cave.Play()}");
                    _waitStart = Time.unscaledTime;
                    Wait(0.25f, 32);
                    return;
                }
                case 32:
                {
                    bool empty = Sons.Cutscenes.Cutscene.IsPlayerHandsEmpty();
                    _caveWasEmpty |= empty;
                    Log($"  cave: handsEmpty={empty} playing={_cave._isPlayingLocally} locked={LocalPlayer.FpCharacter?.Locked}/{LocalPlayer.FpCharacter?.MovementLocked} inCaves={LocalPlayer.IsInCaves} moved={Vector3.Distance(_caveStartPos, LocalPlayer.Transform.position):0.0} " + State());
                    if (Time.unscaledTime - _waitStart < 30f && !(LocalPlayer.IsInCaves && !_cave._isPlayingLocally && Time.unscaledTime - _waitStart > 3f)) { Wait(0.25f, 32); return; }
                    Log($"CAVE RESULT: inCaves={LocalPlayer.IsInCaves} handsEverEmpty={_caveWasEmpty} stillPlaying={_cave._isPlayingLocally} rightHand={LocalPlayer.Inventory.RightHandItem?._itemID}");
                    Log("  PLAYER animator after cave: " + AnimatorParams(LocalPlayer.Animator));
                    ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"cave_{_phase}.png"));
                    _waitStart = Time.unscaledTime;
                    Wait(4f, 33);
                    return;
                }
                case 33:
                    Log("  4 s later: " + State());
                    if (LocalPlayer.Inventory.RightHandItem == null && LocalPlayer.Inventory.PreviousRightHandItem != null)
                        Log($"  take the gun out again: {LocalPlayer.Inventory.TryEquip(Phases[_phase].name.StartsWith("VANILLA") ? SniperItem.RifleId : SniperItem.Id, false, false, false)}");
                    Wait(4f, 34);
                    return;
                case 34:
                    Log("  after re-equip: " + State());
                    NextPhase();
                    return;

                // ---- put away / take out with the game's own code
                case 20:
                    Log("  away: " + State());
                    if (Time.unscaledTime - _waitStart < 3f) { Wait(0.25f, 20); return; }
                    _stashAwayState = State();
                    inv.EquipPreviousRightHandItem(true);
                    Log("  EquipPreviousRightHandItem called");
                    _waitStart = Time.unscaledTime;
                    Wait(0.25f, 21);
                    return;
                case 21:
                    Log("  back: " + State());
                    if (Time.unscaledTime - _waitStart < 5f) { Wait(0.25f, 21); return; }
                    var an2 = LocalPlayer.Animator;
                    Log($"STASH RESULT: while away [{_stashAwayState}] | after taking it out: item={inv.RightHandItem?._itemID} animId={an2?.GetInteger(Animator.StringToHash("RightHandHeldItemId"))} canAim={Controller()?.CanAim()}");
                    NextPhase();
                    return;

                // ---- harvest preparation
                case 10:
                {
                    if (!RayFromCamera(15f, out var h)) { Skip($"rabbit: no ground in front (pitch {CamPitchDown():0.0})"); return; }
                    _rabbit = SonsSdk.ActorTools.Spawn(Sons.Ai.Vail.VailActorTypeId.Rabbit, h.point + Vector3.up * 0.2f, 0);
                    Log($"  rabbit spawned at {V(h.point)} ({h.distance:0.0} m): {(_rabbit != null ? _rabbit.name : "null")}");
                    if (_rabbit == null) { Skip("rabbit did not spawn"); return; }
                    Wait(0.15f, 11); // kill it before it runs off
                    return;
                }
                case 11:
                    _rabbit.ForceDeath();
                    Log($"  rabbit killed (ForceDeath) at {V(_rabbit.transform.position)}");
                    Wait(3f, 12);
                    return;
                case 12:
                {
                    var pick = _rabbit._skinningPickup;
                    Vector3 target = pick != null && pick.activeInHierarchy ? pick.transform.position : _rabbit.transform.position;
                    var cam = LocalPlayer.MainCamTr;
                    Vector3 d = target - cam.position;
                    float flat = new Vector2(d.x, d.z).magnitude;
                    float pitch = Mathf.Atan2(-d.y, flat) * Mathf.Rad2Deg;
                    float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.forward, Vector3.up), Vector3.ProjectOnPlane(d, Vector3.up), Vector3.up);
                    Look(pitch, yaw);
                    Log($"  corpse at {V(target)} dist {d.magnitude:0.0} m -> look down {pitch:0.0}°, turn {yaw:0.0}°; skinned={_rabbit.IsSkinned()} pitch now {CamPitchDown():0.0}");
                    if (d.magnitude > 2.2f)
                    {
                        // walk up to it: put the player 1.3 m from the corpse, same direction
                        var body = LocalPlayer._instance.transform;
                        Vector3 flatDir = Vector3.ProjectOnPlane(target - body.position, Vector3.up).normalized;
                        body.position = new Vector3(target.x, body.position.y, target.z) - flatDir * 1.3f;
                        Log($"  moved the player next to the corpse: {V(body.position)}");
                        Wait(1.0f, 12);
                        _moved++;
                        if (_moved < 3) return;
                    }
                    if (pick != null)
                    {
                        var sb = new StringBuilder($"  skinningPickup '{pick.name}' active={pick.activeInHierarchy} components:");
                        var comps = pick.GetComponentsInChildren(Il2CppType.Of<Component>(), true);
                        for (int i = 0; i < comps.Length && i < 40; i++) sb.Append($" {comps[i].gameObject.name}/{comps[i].GetIl2CppType().Name}");
                        Log(sb.ToString());
                    }
                    else Log("  no skinningPickup");
                    _meatBefore = _meatId > 0 ? inv.AmountOf(_meatId, true, false) : -1;
                    var gui = pick != null ? pick.transform.Find("_PickupGui_") : null;
                    Log($"  pickup prompt visible: {(gui != null ? gui.gameObject.activeInHierarchy.ToString() : "n/a")}");
                    Log("  PLAYER animator before harvest: " + AnimatorParams(LocalPlayer.Animator));
                    Wait(1.0f, 3);
                    return;
                }
            }
        }

        private void NextPhase()
        {
            _phase++;
            if (_phase < Phases.Length) { Wait(2f, 1); return; }
            AimFix.ForceMode = -1;
            AnimFix.Observer = null;
            Unstuck.Observer = null;
            Log("done");
        }

        // ------------------------------------------------------------------ observe (not alter) input reads

        private static void InstallObservers()
        {
            var h = new HarmonyLib.Harmony("SniperRifle.FireTest");
            try
            {
                h.Patch(AccessTools.Method(typeof(Sons.Input.InputSystem), nameof(Sons.Input.InputSystem.GetButtonDown)),
                    postfix: new HarmonyMethod(typeof(FireTest), nameof(AfterGetButtonDown)));
                h.Patch(AccessTools.Method(typeof(InputAction), nameof(InputAction.WasPerformedThisFrame)),
                    postfix: new HarmonyMethod(typeof(FireTest), nameof(AfterWasPerformed)));
            }
            catch (Exception e) { RLog.Error("[fire] observers: " + e.Message); }
        }

        public static void AfterGetButtonDown(Sons.Input.InputSystem.Action button, bool __result)
        {
            if (!__result) return;
            try { if (button?.GetName() == "PrimaryAction") _fireDownTrue++; } catch { }
        }

        public static void AfterWasPerformed(InputAction __instance, bool __result)
        {
            if (!__result) return;
            try { if (__instance?.name == "PrimaryAction") _firePerformedTrue++; } catch { }
        }
    }
}
