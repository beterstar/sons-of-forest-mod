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
        }

        protected override void OnGameStart() => _checkAt = Time.unscaledTime + 15f;

        private void OnUpdate()
        {
            if (_followUpAt > 0f && Time.unscaledTime >= _followUpAt) { _followUpAt = -1f; TestFollowUp(); }
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
            if (!host || !_cfg.AppliesTo(saveId)) return;
            if (_cfg.ReviveVirginia && SaveState.IsVirginiaDead()) Revive(VailActorTypeId.Virginia);
            if (_cfg.ReviveKelvin && SaveState.IsRobbyDead()) Revive(VailActorTypeId.Robby);
        }

        private string Revive(VailActorTypeId type)
        {
            bool virginia = type == VailActorTypeId.Virginia;
            string name = virginia ? "เวอร์จิเนีย" : "เคลวิน";
            if (virginia) SaveState.SetIsVirginiaDead(false);
            else SaveState.SetIsRobbyDead(false);

            var sim = VailWorldSimulation._instance;
            var existing = virginia ? sim.FindVirginiaActor() : sim.FindRobbyActor();
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

        // ---------------------------------------------------------------- dev self-test (Mods\CompanionRevive\test.flag)

        private float _followUpAt = -1f;
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

        private void TestFollowUp()
        {
            try
            {
                var sim = VailWorldSimulation._instance;
                var me = LocalPlayer.Transform.position;
                WorldSimActor near = null;
                var list = sim._virginiaActors;
                for (int i = 0; list != null && i < list.Count; i++)
                {
                    var a = list[i];
                    TL($"  virginia actor {a.UniqueId}: dist={Vector3.Distance(a._position, me):0.0}");
                    if (near == null || Vector3.Distance(a._position, me) < Vector3.Distance(near._position, me)) near = a;
                }
                var va = near != null ? ActorTools.GetVailActor(near) : null;
                TL($"nearest Virginia: {(near != null ? $"id={near.UniqueId} dist={Vector3.Distance(near._position, me):0.0}" : "none")} spawned body: {(va != null ? $"{va.name} active={va.gameObject.activeInHierarchy} dist={Vector3.Distance(va.transform.position, me):0.0}" : "no")}");
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
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "test_virginia.png"));
            TL("screenshot taken");
            TL("done");
        }
    }
}
