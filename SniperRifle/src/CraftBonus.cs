using System;
using HarmonyLib;
using RedLoader;
using Sons.Crafting;
using TheForest.Utils;

namespace SniperRifle
{
    /// <summary>
    /// Ammo crafts that give many rounds at once. A recipe's results are one entry per item, but the crafting
    /// mat has a single display slot for 9 mm rounds: extra results are silently lost (a recipe with 3 results
    /// gave 1 round, or 0 when crafting again quickly) and a recipe with 20 results crashed the game as soon
    /// as it was added. So our ammo recipes list one round (for the recipe display) and when one is crafted
    /// the configured amount goes straight into the inventory instead of onto the mat.
    /// </summary>
    internal static class CraftBonus
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var m = AccessTools.Method(typeof(CraftingSystem), nameof(CraftingSystem.CraftResultingItems));
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(CraftBonus), nameof(BeforeCraftResultingItems)));
        }

        /// <returns>false = our ammo recipe, handled here (the game does not put anything on the mat)</returns>
        public static bool BeforeCraftResultingItems(CraftingRecipe recipe)
        {
            try
            {
                if (recipe == null || LocalPlayer._instance == null) return true;
                var cfg = SniperRifleMod.Cfg;
                int total = recipe.name == SniperItem.RockAmmoRecipeName ? cfg.AmmoRockOutput
                          : recipe.name == SniperItem.LeafAmmoRecipeName ? cfg.AmmoLeafOutput
                          : 0;
                if (total <= 0) return true;
                bool added = LocalPlayer.Inventory.AddItem(SniperItem.PistolAmmoId, total, true, false, null);
                if (cfg.Debug || !added) RLog.Msg($"[SniperRifle] {recipe.name}: +{total} rounds to the inventory ({(added ? "ok" : "inventory full?")})");
                return false;
            }
            catch (Exception e)
            {
                SniperRifleMod.LogOnce("CraftBonus", e);
                return true; // let the game do its normal thing
            }
        }
    }
}
