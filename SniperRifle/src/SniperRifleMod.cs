using System;
using System.Collections.Generic;
using System.IO;
using RedLoader;
using SonsSdk;
using UnityEngine;

[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v6.0", FrameworkDisplayName = "")]

namespace SniperRifle
{
    public class SniperRifleMod : SonsMod
    {
        internal static Config Cfg = new Config();
        private static readonly HashSet<string> LoggedErrors = new HashSet<string>();

        private bool _registered;
        private bool _playerSetupPending;
        private float _nextTry;
        private DevTest _devTest;
        private FireTest _fireTest;

        public SniperRifleMod()
        {
            HarmonyPatchAll = false;
            OnUpdateCallback = OnUpdate;
            OnLateUpdateCallback = OnLateUpdate;
        }

        protected override void OnInitializeMod()
        {
            Explorer.Dir = DataPath.Path;
            try { Cfg = Config.Load(Path.Combine(DataPath.Path, "config.json")); }
            catch (Exception e) { RLog.Error("[SniperRifle] config.json อ่านไม่ได้ ใช้ค่าเริ่มต้น: " + e.Message); }
            Unstuck.Configure(Cfg);
        }

        protected override void OnSdkInitialized()
        {
            TryRegister();
            var harmony = new HarmonyLib.Harmony("SniperRifle");
            try { AimFix.Install(harmony); }
            catch (Exception e) { LogOnce("AimFix.Install", e); }
            try { CraftBonus.Install(harmony); }
            catch (Exception e) { LogOnce("CraftBonus.Install", e); }
            if (Explorer.Active) _devTest = new DevTest(Explorer.Dir);
            if (Explorer.Active && File.Exists(Path.Combine(Explorer.Dir, "firetest.flag"))) _fireTest = new FireTest(Explorer.Dir);
            _devTest?.OnTitle();
        }

        protected override void OnGameStart()
        {
            TryRegister(); // as early as possible, before the player's inventory reads the database
            _playerSetupPending = true;
            _nextTry = 0f;
            if (_fireTest != null) _fireTest.OnGameStart();
            else _devTest?.OnGameStart();
        }

        private void TryRegister()
        {
            if (_registered) return;
            try { _registered = SniperItem.TryRegister(Cfg); }
            catch (Exception e) { LogOnce("Register", e); _registered = true; /* don't retry a hard failure every frame */ }
        }

        private void OnUpdate()
        {
            float now = Time.unscaledTime;
            if (!_registered) TryRegister();
            if (now >= _nextTry && _playerSetupPending)
            {
                _nextTry = now + 1f;
                TryRegister();
                if (_registered && _playerSetupPending)
                {
                    try { if (SniperItem.TrySetupPlayer(Cfg)) { _playerSetupPending = false; RLog.Msg("[SniperRifle] พร้อมใช้งานในโลกนี้"); } }
                    catch (Exception e) { LogOnce("SetupPlayer", e); _playerSetupPending = false; }
                }
            }

            AnimFix.Apply();
            Unstuck.Update();
            Scope.Update(Cfg);
            ScopeModel.Update(Cfg);
            _devTest?.Update(now);
            _fireTest?.Update(now);
        }

        private void OnLateUpdate()
        {
            AnimFix.Apply();
            Scope.LateUpdate();
        }

        internal static void LogOnce(string where, Exception e)
        {
            if (LoggedErrors.Add(where)) RLog.Error($"[SniperRifle] {where}: {e}");
        }
    }
}
