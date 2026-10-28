using System;
using System.IO;
using System.Text;
using RedLoader;
using Sons.Ai.Vail;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;
using SaveState = Sons.Save.GameState;

[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v6.0", FrameworkDisplayName = "")]

namespace CompanionRevive
{
    /// <summary>
    /// Brings Virginia (and optionally Kelvin) back in a save where they died: when that save is loaded by the
    /// host, the game's "is dead" flag is cleared and the companion is spawned next to the player with the
    /// game's own VailWorldSimulation.SpawnCompanion. Saving the game afterwards stores them normally.
    /// </summary>
    public class CompanionReviveMod : SonsMod
    {
        private Config _cfg = new Config();
        private string _dir;
        private float _checkAt = -1f;
        private bool _test;

        public CompanionReviveMod()
        {
            HarmonyPatchAll = false;
            OnUpdateCallback = OnUpdate;
        }

        protected override void OnInitializeMod()
        {
            _dir = DataPath.Path;
            try { _cfg = Config.Load(Path.Combine(_dir, "config.json")); }
            catch (Exception e) { RLog.Error("[CompanionRevive] config.json อ่านไม่ได้: " + e.Message); }
            _test = File.Exists(Path.Combine(_dir, "test.flag"));
            string slotFlag = Path.Combine(_dir, "savetest.flag");
            if (File.Exists(slotFlag)) uint.TryParse(File.ReadAllText(slotFlag).Trim(), out _testSlot);
        }

        protected override void OnSdkInitialized()
        {
            if (_testSlot != 0) _loadAt = Time.unscaledTime + 3f;
        }

        protected override void OnGameStart() => _checkAt = Time.unscaledTime + 15f;

        private void OnUpdate()
        {
            if (_loadAt > 0f && Time.unscaledTime >= _loadAt) { _loadAt = -1f; LoadTestSlot(); }
            if (_followUpAt > 0f && Time.unscaledTime >= _followUpAt) { _followUpAt = -1f; TestFollowUp(); }
            if (_recheckAt > 0f && Time.unscaledTime >= _recheckAt) { _recheckAt = -1f; TestRecheck(); }
            if (_shotAt > 0f && Time.unscaledTime >= _shotAt) { _shotAt = -1f; TestShot(); }
            if (_checkAt < 0f || Time.unscaledTime < _checkAt) return;
            if (LocalPlayer._instance == null || !LocalPlayer.IsInWorld || VailWorldSimulation._instance == null)
            {
                _checkAt = Time.unscaledTime + 2f;
                return;
            }
            _checkAt = -1f;
            try
            {
                if (_test) RunTest();
                else Check();
            }
            catch (Exception e) { RLog.Error("[CompanionRevive] " + e); }
        }

        private void Check()
        {
            uint id = Sons.Gameplay.GameSetup.GameSetupManager.GetSelectedSaveId();
            if (id == 0) id = GameState.LastLoadedSaveId;
            string saveId = id.ToString();
            bool host = !BoltNetwork.isRunning || BoltNetwork.isServer;
            RLog.Msg($"[CompanionRevive] save {saveId} ({Sons.Gameplay.GameSetup.GameSetupManager.GetSaveGameType()}), host={host}, Virginia dead={SaveState.IsVirginiaDead()}, Kelvin dead={SaveState.IsRobbyDead()}");
            bool testSlot = _testSlot != 0 && id == _testSlot;
            if (!host || !(_cfg.AppliesTo(saveId) || testSlot)) return;
            if (_cfg.ReviveVirginia && SaveState.IsVirginiaDead()) Revive(VailActorTypeId.Virginia);
            if (_cfg.ReviveKelvin && SaveState.IsRobbyDead()) Revive(VailActorTypeId.Robby);
            if (testSlot)
            {
                _testLog = new StringBuilder();
                _testType = VailActorTypeId.Robby;
                TL($"save test {saveId}: virginiaDead={SaveState.IsVirginiaDead()} kelvinDead={SaveState.IsRobbyDead()}");
                _followUpAt = Time.unscaledTime + 6f;
            }
        }

        private string Revive(VailActorTypeId type)
        {
            bool virginia = type == VailActorTypeId.Virginia;
            string name = virginia ? "เวอร์จิเนีย" : "เคลวิน";
            if (virginia) SaveState.SetIsVirginiaDead(false);
            else SaveState.SetIsRobbyDead(false);

            // Virginia's record disappears when she dies, but Kelvin's stays in the world sim as a corpse
            // (State Dead), which would only ever load as a body. Drop dead records and spawn a fresh one.
            var sim = VailWorldSimulation._instance;
            WorldSimActor existing = null;
            var dead = new System.Collections.Generic.List<WorldSimActor>();
            foreach (var a in ActorsOf(type))
            {
                RLog.Msg($"[CompanionRevive] {type} actor {a.UniqueId}: state={a.State} dead={a.IsDead()} pos={a._position}");
                if (a.IsDead()) dead.Add(a);
                else existing ??= a;
            }
            foreach (var a in dead)
            {
                sim.RemoveActor(a);
                RLog.Msg($"[CompanionRevive] removed dead {type} actor {a.UniqueId}");
            }
            string result;
            if (existing != null)
            {
                result = $"{name} ยังอยู่ในโลก: ลบสถานะ 'ตายแล้ว' ออกให้แล้ว — เซฟเกมเพื่อบันทึก";
            }
            else
            {
                var p = LocalPlayer.Transform;
                Vector3 forward = Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized;
                Vector3 pos = p.position + forward * 4f;
                VailWorldSimulation.SpawnCompanion(type, pos, Quaternion.LookRotation(-forward), _cfg.Sentiment);
                result = $"{name} กลับมาแล้ว อยู่ข้างหน้าคุณ — เซฟเกมเพื่อบันทึก";
            }
            RLog.Msg("[CompanionRevive] " + result);
            SonsTools.ShowMessage(result, 10f);
            return result;
        }

        private static System.Collections.Generic.List<WorldSimActor> ActorsOf(VailActorTypeId type)
        {
            var found = new System.Collections.Generic.List<WorldSimActor>();
            var all = VailWorldSimulation._instance._actors;
            for (int i = 0; all != null && i < all.Count; i++)
                if (all[i] != null && all[i].TypeId == type) found.Add(all[i]);
            return found;
        }

        // ---------------------------------------------------------------- dev self-test (Mods\CompanionRevive\test.flag)

        private float _followUpAt = -1f;
        private float _recheckAt = -1f;
        private float _loadAt = -1f;
        private uint _testSlot;  // Mods\CompanionRevive\savetest.flag = single-player slot (a COPY of a save) to load and revive in
        private VailActorTypeId _testType = VailActorTypeId.Virginia;
        private StringBuilder _testLog;

        private void TL(string s)
        {
            _testLog.AppendLine($"[{Time.unscaledTime:0.00}] {s}");
            RLog.Msg("[CompanionRevive/test] " + s);
            File.WriteAllText(Path.Combine(_dir, "test.txt"), _testLog.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// A new world already has a (far away) Virginia, unlike the damaged save where her record is gone.
        /// So: mark her dead, run the normal revive (clears the flag), then spawn one next to the player with the
        /// same call the revive uses when she is missing, and check she really appears.
        /// </summary>
        private void RunTest()
        {
            _testLog = new StringBuilder();
            var sim = VailWorldSimulation._instance;
            TL($"start: save={GameState.LastLoadedSaveId} virginiaDead={SaveState.IsVirginiaDead()} virginiaActors={sim._virginiaActors?.Count}");
            SaveState.SetIsVirginiaDead(true);
            TL($"pretend dead: virginiaDead={SaveState.IsVirginiaDead()}");
            TL("revive (normal path): " + Revive(VailActorTypeId.Virginia));
            TL($"flag after revive: virginiaDead={SaveState.IsVirginiaDead()}");

            var p = LocalPlayer.Transform;
            Vector3 forward = Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized;
            Vector3 pos = p.position + forward * 4f;
            VailWorldSimulation.SpawnCompanion(VailActorTypeId.Virginia, pos, Quaternion.LookRotation(-forward), _cfg.Sentiment);
            TL($"SpawnCompanion(Virginia) at {pos}: virginiaActors={sim._virginiaActors?.Count}");
            _followUpAt = Time.unscaledTime + 6f;
        }

        /// <summary>Loads the single-player slot named in savetest.flag (a throw-away copy, never a real save).</summary>
        private void LoadTestSlot()
        {
            // (LoadMenu.LoadSlotActivated on the title screen only sets the mode and never starts the load)
            RLog.Msg($"[CompanionRevive/test] loading test slot {_testSlot}");
            Sons.Gameplay.GameSetup.GameSetupManager.SetSaveId(_testSlot);
            Sons.Gameplay.GameSetup.GameSetupManager.SetSaveGameType(Sons.Save.SaveGameType.SinglePlayer);
            UnityEngine.SceneManagement.SceneManager.LoadScene("SonsMainLoading");
        }

        private WorldSimActor LogActors(string when)
        {
            var me = LocalPlayer.Transform.position;
            WorldSimActor near = null;
            foreach (var a in ActorsOf(_testType))
            {
                VailActor body = null;
                try { body = ActorTools.GetVailActor(a); } catch { }
                TL($"  {when} {_testType} actor {a.UniqueId}: state={a.State} dead={a.IsDead()} dist={Vector3.Distance(a._position, me):0.0} body={(body != null ? $"{body.name} active={body.gameObject.activeInHierarchy} dead={body.IsDead()} hp={body.GetHealth():0}/{body.GetMaxHealth():0}" : "-")}");
                if (!a.IsDead() && (near == null || Vector3.Distance(a._position, me) < Vector3.Distance(near._position, me))) near = a;
            }
            foreach (var b in ActorTools.GetActors(_testType))
                if (b != null) TL($"  {when} scene body {b.name}: active={b.gameObject.activeInHierarchy} dead={b.IsDead()} dist={Vector3.Distance(b.transform.position, me):0.0}");
            return near;
        }

        private void TestFollowUp()
        {
            try
            {
                var me = LocalPlayer.Transform.position;
                var near = LogActors("after revive");
                var va = near != null ? ActorTools.GetVailActor(near) : null;
                TL($"nearest living {_testType}: {(near != null ? $"id={near.UniqueId} dist={Vector3.Distance(near._position, me):0.0}" : "none")} spawned body: {(va != null ? $"{va.name} active={va.gameObject.activeInHierarchy} dist={Vector3.Distance(va.transform.position, me):0.0}" : "no")}");
                if (va != null)
                {
                    var cam = LocalPlayer.MainCamTr;
                    Vector3 d = va.transform.position + Vector3.up * 1.2f - cam.position;
                    float pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                    float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.forward, Vector3.up), Vector3.ProjectOnPlane(d, Vector3.up), Vector3.up);
                    var cr = LocalPlayer.CamRotator;
                    if (cr != null) { var t = cr._targetAngles; t.x = pitch; cr._targetAngles = t; var f = cr._followAngles; f.x = pitch; cr._followAngles = f; }
                    var mr = LocalPlayer.MainRotator;
                    if (mr != null) { var t = mr._targetAngles; t.y += yaw; mr._targetAngles = t; var f = mr._followAngles; f.y += yaw; mr._followAngles = f; }
                }
                _shotAt = Time.unscaledTime + 1.5f;
            }
            catch (Exception e) { TL("follow-up failed: " + e); TL("done"); }
        }

        private float _shotAt = -1f;

        private void TestShot()
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"test_{_testType}.png".ToLowerInvariant()));
            TL("screenshot taken");
            if (_testType == VailActorTypeId.Virginia) TL("done");
            else _recheckAt = Time.unscaledTime + 20f; // make sure he stays alive and the corpse stays gone
        }

        private void TestRecheck()
        {
            try
            {
                LogActors("20 s later");
                TL($"kelvinDead={SaveState.IsRobbyDead()}");
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "test_robby_later.png"));
            }
            catch (Exception e) { TL("recheck failed: " + e); }
            TL("done");
        }
    }
}
