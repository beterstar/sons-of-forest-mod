using System;
using System.IO;
using Il2CppInterop.Runtime;
using RedLoader;
using Sons.Gameplay.GameSetup;
using Sons.Gui;
using Sons.Save;
using SonsSdk;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SniperRifle
{
    /// <summary>
    /// Development helpers, active only when Mods\SniperRifle\explore.flag exists (never in normal play).
    /// </summary>
    internal static class Explorer
    {
        internal static string Dir;
        internal static bool Active => Dir != null && File.Exists(Path.Combine(Dir, "explore.flag"));

        /// <summary>Starts a throw-away Peaceful game. Never loads the player's own saves.</summary>
        internal static void AutoLoadLatestSave()
        {
            uint id = 0; // (loading the newest save was removed: the tests must never touch real worlds)

            if (id == 0)
            {
                GameSetupManager.ClearSelectedLoadSlot();
                GameSetupManager.SetSaveGameType(SaveGameType.SinglePlayer);
                GameSetupManager.SetMode("Peaceful");
                RLog.Msg("[SniperRifle/dev] starting a throw-away Peaceful game");
                SceneManager.LoadScene("SonsMainLoading");
                return;
            }

            try
            {
                var menus = Resources.FindObjectsOfTypeAll(Il2CppType.Of<LoadMenu>());
                if (menus.Length > 0)
                {
                    var data = SaveGameManager.GetData<GameStateData>(SaveGameType.SinglePlayer, (int)id);
                    menus[0].Cast<LoadMenu>().LoadSlotActivated((int)id, data);
                    return;
                }
            }
            catch (Exception e) { RLog.Error("[SniperRifle/dev] LoadMenu path failed: " + e); }

            GameSetupManager.SetSaveId(id);
            GameSetupManager.SetSaveGameType(SaveGameType.SinglePlayer);
            SceneManager.LoadScene("SonsMainLoading");
        }
    }
}
