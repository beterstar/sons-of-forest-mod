using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;

namespace SniperRifle
{
    /// <summary>
    /// Makes sniper shots land on the scope crosshair. The game (built for the rifle's iron sights) sends the
    /// bullet about 2 degrees below the screen centre and lets the slow 9 mm round drop under gravity; through
    /// a 6x scope both show up as hits below the crosshair. We aim each sniper shot from its spawn point at
    /// whatever is under the crosshair, raised just enough (by flying the bullet with the same gravity and
    /// drag the game uses) to cancel the drop at that distance.
    /// </summary>
    internal static class AimFix
    {
        /// <summary>Tests: -1 = use config, 0 = off, 1 = on.</summary>
        internal static int ForceMode = -1;
        /// <summary>Tests: details of the last sniper shot.</summary>
        internal static ShotInfo Last;

        internal sealed class ShotInfo
        {
            public float Time;
            public Vector3 CamPos, Spawn, OrigDir, NewDir, AimPoint;
            public Quaternion CamRot;
            public float OrigZero, PredictedMissDeg, LiftDeg, Speed, Drag;
            public string AimHit = "";
            public bool Applied;
            public int Iterations;
        }

        private const float MaxAimDistance = 800f;
        private const float NoHitDistance = 300f;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var shoot = AccessTools.Method(typeof(RangedWeapon), nameof(RangedWeapon.Shoot), new[] { typeof(Vector3), typeof(float), typeof(int) });
            harmony.Patch(shoot, prefix: new HarmonyMethod(typeof(AimFix), nameof(BeforeShoot)));
        }

        public static void BeforeShoot(RangedWeapon __instance, ref Vector3 direction, ref float zeroAngle)
        {
            try
            {
                if (!IsHeldSniper(__instance)) return;
                var cam = LocalPlayer.MainCam;
                if (cam == null) return;
                var camTr = cam.transform;
                var info = new ShotInfo
                {
                    Time = UnityEngine.Time.unscaledTime,
                    CamPos = camTr.position,
                    CamRot = camTr.rotation,
                    OrigDir = direction,
                    NewDir = direction,
                    OrigZero = zeroAngle,
                    Spawn = SpawnPosition(__instance),
                };
                info.AimPoint = AimPoint(__instance, camTr, out info.AimHit);
                Last = info;

                // The sniper can only fire while aimed (and the game reports "not aiming" for a moment while the
                // shot goes off), so every sniper shot is a scoped shot.
                bool on = ForceMode >= 0 ? ForceMode == 1 : SniperRifleMod.Cfg.ScopeAimCorrection;
                if (!on) return;

                // The game sends the bullet about 2 degrees below the screen centre (where the rifle's iron
                // sights are); send it at the crosshair instead.
                Vector3 dir = (info.AimPoint - info.Spawn).normalized;
                if (dir.sqrMagnitude < 0.5f) return;
                direction = dir;
                zeroAngle = 0f;
                info.NewDir = dir;
                info.Applied = true;

                // then lift it just enough to cancel the drop at that distance
                var straight = dir;
                try
                {
                    TryBallistics(__instance, out info.Speed, out info.Drag, out _);
                    for (int i = 0; i < 4; i++)
                    {
                        if (!TryMissAngle(__instance, info.Spawn, dir, info.AimPoint, out float missDeg)) break;
                        info.Iterations = i + 1;
                        info.PredictedMissDeg = missDeg;
                        if (Mathf.Abs(missDeg) < 0.005f) break;
                        var axis = Vector3.Cross(dir, Vector3.up);
                        if (axis.sqrMagnitude < 1e-6f) break;
                        dir = Quaternion.AngleAxis(-missDeg, axis.normalized) * dir; // positive angle about dir x up = upwards
                    }
                    direction = dir;
                    info.NewDir = dir;
                    info.LiftDeg = Vector3.Angle(straight, dir);
                }
                catch (Exception e) { SniperRifleMod.LogOnce("AimFix.Drop", e); }
            }
            catch (Exception e) { SniperRifleMod.LogOnce("AimFix", e); }
        }

        private static bool IsHeldSniper(RangedWeapon rw)
        {
            if (rw == null || LocalPlayer._instance == null) return false;
            var item = LocalPlayer.Inventory?.RightHandItem;
            if (item == null || item._itemID != SniperItem.Id || item.ItemObject == null) return false;
            var held = item.ItemObject.GetComponent(Il2CppType.Of<RangedWeapon>());
            return held != null && held.Pointer == rw.Pointer;
        }

        private static Vector3 SpawnPosition(RangedWeapon rw)
        {
            if (rw.physicalSpawnPoint != null) return rw.physicalSpawnPoint.position;
            if (rw._projectilePhysicalSpawnPoint != null) return rw._projectilePhysicalSpawnPoint.position;
            var t = rw.GetProjectileTransform();
            return t != null ? t.position : rw.transform.position;
        }

        /// <summary>The first thing under the crosshair that a bullet would hit (ignoring the player).</summary>
        private static Vector3 AimPoint(RangedWeapon rw, Transform camTr, out string what)
        {
            Vector3 origin = camTr.position, fwd = camTr.forward;
            what = "nothing";
            Vector3 best = origin + fwd * NoHitDistance;
            float bestDist = float.MaxValue;
            var playerRoot = LocalPlayer._instance.transform.root;
            var hits = Physics.RaycastAll(origin, fwd, MaxAimDistance, rw.GetHitMask().value, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.distance < 0.3f || h.distance >= bestDist) continue;
                var col = h.collider;
                if (col != null && col.transform.root == playerRoot) continue;
                bestDist = h.distance;
                best = h.point;
                what = col != null ? $"{col.name} @{h.distance:0.0}m" : $"? @{h.distance:0.0}m";
            }
            return best;
        }

        /// <summary>
        /// Flies the bullet the way the game's ballistics do (gravity plus air drag that grows with speed squared)
        /// and returns how far, in degrees seen from the spawn point, it passes above (+) or below (-) the aim
        /// point at the aim point's distance. (RangedWeapon.SimulateTrajectory can't be used: it throws for
        /// weapons without a trajectory preview.)
        /// </summary>
        private static bool TryMissAngle(RangedWeapon rw, Vector3 spawn, Vector3 dir, Vector3 aim, out float missDeg)
        {
            missDeg = 0f;
            if (!TryBallistics(rw, out float speed, out float drag, out Vector3 gravity)) return false;
            float target = Flat(aim - spawn);
            if (target < 1f) return false; // (almost) straight up/down: nothing to correct
            Vector3 p = spawn, v = dir * speed;
            const float dt = 0.002f;
            for (int i = 0; i < 5000; i++)
            {
                Vector3 prev = p;
                v += (gravity - drag * v.magnitude * v) * dt;
                p += v * dt;
                float d = Flat(p - spawn);
                if (d >= target)
                {
                    Vector3 at = Vector3.Lerp(prev, p, Mathf.InverseLerp(Flat(prev - spawn), d, target));
                    missDeg = Elevation(at - spawn) - Elevation(aim - spawn);
                    return true;
                }
                if (v.sqrMagnitude < 1f) break;
            }
            return false;
        }

        /// <summary>Muzzle speed, drag factor k (a = -k |v| v) and gravity used for the sniper's bullets.</summary>
        internal static bool TryBallistics(RangedWeapon rw, out float speed, out float drag, out Vector3 gravity)
        {
            speed = drag = 0f;
            gravity = Physics.gravity;
            var info = rw.bulletInfo ?? rw._ammo?._properties?.ProjectileInfo;
            if (info == null || info.maxSpeed < 1f) return false;
            speed = info.maxSpeed;
            var settings = Ballistics.BulletHandler.instance?.GetSettings();
            if (settings != null && !settings.BulletDrop) gravity = Vector3.zero;
            if (settings == null || settings.BulletDrag)
            {
                float pre = info.precalculatedDrag;
                if (pre <= 0f && info.weight > 0f)
                    pre = info.dragCoefficient * Mathf.PI * info.diameter * info.diameter * 0.25f / (2f * info.weight);
                drag = Mathf.Max(0f, pre * (settings != null ? settings.AirDensity : 1.225f));
            }
            return true;
        }
        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        private static float Elevation(Vector3 v) => Mathf.Atan2(v.y, Flat(v)) * Mathf.Rad2Deg;
    }
}
