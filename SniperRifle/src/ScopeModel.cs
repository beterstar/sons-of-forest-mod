using System;
using Il2CppInterop.Runtime;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace SniperRifle
{
    /// <summary>
    /// Look of the homemade sniper: the game's rifle model already carries a small scope ("RifleScopeModel");
    /// we enlarge it into a long sniper scope and give the gun a warm wood tint so it is easy to tell apart
    /// from the normal rifle. Applied to the held gun and to the copies shown in the inventory / crafting mat.
    /// </summary>
    internal static class ScopeModel
    {
        private const string ScopeNode = "RifleScopeModel";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private static MaterialPropertyBlock _block;
        private static float _nextSlotCheck;
        internal static bool DisabledForTest;

        internal static void Update(Config cfg)
        {
            if (!cfg.ShowScopeModel || DisabledForTest) return;
            try
            {
                if (LocalPlayer._instance == null) return;
                var item = LocalPlayer.Inventory?.RightHandItem;
                if (item != null && item._itemID == SniperItem.Id && item.ItemObject != null)
                    Apply(item.ItemObject.transform, cfg);

                float now = Time.unscaledTime;
                if (now < _nextSlotCheck) return;
                _nextSlotCheck = now + 1f;
                var inv = ItemTools.GetInventoryLayoutItemGroup(SniperItem.Id);
                if (inv != null) Apply(inv.transform, cfg);
                var craft = ItemTools.GetCraftingResultLayoutItemGroup(SniperItem.Id);
                if (craft != null) Apply(craft.transform, cfg);
            }
            catch (Exception e) { SniperRifleMod.LogOnce("ScopeModel", e); }
        }

        private static void Apply(Transform root, Config cfg)
        {
            var renderers = root.GetComponentsInChildren(Il2CppType.Of<MeshRenderer>(), true);
            if (renderers.Length == 0) return; // model not loaded yet
            _block ??= new MaterialPropertyBlock();
            var tint = new Color(cfg.Tint.x, cfg.Tint.y, cfg.Tint.z, 1f);

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i].TryCast<MeshRenderer>();
                if (r == null) continue;
                var go = r.gameObject;

                if (go.name == ScopeNode)
                {
                    var target = cfg.ScopeScale;
                    if (go.transform.localScale != target)
                    {
                        go.transform.localScale = target;
                        if (!go.activeSelf) go.SetActive(true);
                    }
                    continue; // keep the scope dark
                }

                var mat = r.sharedMaterial;
                if (mat == null) continue;
                // Keep values the game already put in the block (blood/plating effects) and only add the tint.
                r.GetPropertyBlock(_block);
                if (mat.HasProperty(BaseColor)) _block.SetColor(BaseColor, tint);
                else if (mat.HasProperty(LegacyColor)) _block.SetColor(LegacyColor, tint);
                else continue;
                r.SetPropertyBlock(_block);
            }
        }
    }
}
