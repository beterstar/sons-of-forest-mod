using System;
using HarmonyLib;
using RedLoader;
using Sons.Crafting;
using SonsSdk;
using TheForest.Utils;

namespace SniperRifle
{
    /// <summary>
    /// Ammo crafts that give many rounds at once. A recipe's results are one entry per item, but the crafting
    /// mat has a single display slot for 9 mm rounds: extra results are silently lost (a recipe with 3 results
    /// gave 1 round, or 0 when crafting again quickly) and a recipe with 20 results crashed the game as soon
    /// as it was added. So our ammo recipes list one round (for the recipe display) and when one is crafted
    /// the configured amount goes straight into the inventory instead of onto the mat - as much as the player
    /// can still carry. If they can't carry any more, the ingredients are given back.
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

                var inv = LocalPlayer.Inventory;
                int have = inv.AmountOf(SniperItem.PistolAmmoId, true, false);
                int max = inv.GetMaxAmountOf(SniperItem.PistolAmmoId);
                int give = max > 0 ? Math.Min(total, Math.Max(0, max - have)) : total;

                if (give <= 0)
                {
                    Refund(inv, recipe);
                    Show($"กระสุน 9 มม. เต็มแล้ว (พกได้สูงสุด {max} นัด) คืนวัตถุดิบให้แล้ว");
                    return false;
                }
                bool added = inv.AddItem(SniperItem.PistolAmmoId, give, true, false, null);
                if (give < total) Show($"เติมกระสุน 9 มม. จนเต็มแล้ว +{give} นัด (พกได้สูงสุด {max} นัด)");
                if (cfg.Debug || !added) RLog.Msg($"[SniperRifle] {recipe.name}: +{give}/{total} rounds (had {have}, max {max}) {(added ? "ok" : "AddItem failed")}");
                return false;
            }
            catch (Exception e)
            {
                SniperRifleMod.LogOnce("CraftBonus", e);
                return true; // let the game do its normal thing
            }
        }

        private static void Refund(TheForest.Items.Inventory.PlayerInventory inv, CraftingRecipe recipe)
        {
            var ingredients = recipe._ingredients;
            for (int i = 0; ingredients != null && i < ingredients.Count; i++)
            {
                var ing = ingredients[i];
                if (ing.Count > 0 && !ing.IsReusable) inv.AddItem(ing.ItemId, ing.Count, true, false, null);
            }
        }

        private static void Show(string message)
        {
            try { SonsTools.ShowMessage(message, 4f); } catch { }
            RLog.Msg("[SniperRifle] " + message);
        }
    }
}
