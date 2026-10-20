using System;
using Il2CppInterop.Runtime;
using RedLoader;
using Sons.Crafting;
using Sons.Gameplay;
using Sons.Inventory;
using Sons.Items.Core;
using Sons.Weapon;
using SonsSdk;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace SniperRifle
{
    /// <summary>
    /// The homemade sniper is a new item cloned from the game's rifle: same animations and model,
    /// but it fires 9mm (pistol) rounds, so every hit does exactly the pistol's damage.
    /// </summary>
    internal static class SniperItem
    {
        internal const int Id = 8701;
        internal const int RifleId = 361;
        internal const int PistolId = 355;
        internal const int PistolAmmoId = 362;
        internal const int StickId = 392;
        internal const int SmallRockId = 476;
        internal const string RecipeName = "HomemadeSniperRecipe";

        internal static ItemData Data;
        private static GameObject _prefabRoot;

        internal static bool IsRegistered => ItemDatabaseManager.ItemById(Id) != null;

        /// <summary>Registers the item in the item database. Needs the database (title screen or later).</summary>
        internal static bool TryRegister(Config cfg)
        {
            if (ItemDatabaseManager._instance == null) return false;
            if (IsRegistered) return true;

            var rifle = ItemDatabaseManager.ItemById(RifleId);
            var ammo = ItemDatabaseManager.ItemById(PistolAmmoId);
            if (rifle == null || ammo == null || rifle._heldPrefab == null) return false;

            if (_prefabRoot == null)
            {
                // Inactive holder: clones parented here never wake up, so they behave like prefabs.
                _prefabRoot = new GameObject("SniperRiflePrefabs");
                _prefabRoot.SetActive(false);
                UObject.DontDestroyOnLoad(_prefabRoot);
                _prefabRoot.hideFlags = HideFlags.HideAndDontSave;
            }

            var data = UObject.Instantiate(rifle);
            data.name = "HomemadeSniperData";
            data.hideFlags = HideFlags.DontUnloadUnusedAsset;
            data._id = Id;
            data._name = "HomemadeSniper";
            data._editorName = "HomemadeSniper";
            // Not droppable: a dropped pickup would be networked as the game's own rifle.
            data._type = data._type & ~Sons.Items.Core.Types.Droppable;
            data._dropsOnDeath = false;
            data._canBePlated = false;
            data._platingUpgradeModifierData = null;
            data._canBeSpawned = true;
            if (data._compatibleAmmo != null && data._compatibleAmmo.Count > 0)
                data._compatibleAmmo[0]._itemId = PistolAmmoId;

            var ui = data._uiData;
            ui._itemId = Id;
            ui._title = cfg.ItemName;
            ui._titlePlural = cfg.ItemName;
            ui._translationKey = null;
            ui._description = cfg.ItemDescription;
            ui._descriptionShowFor = rifle._uiData._descriptionShowFor;

            // Held object (what the player holds): clone of the rifle with our id and pistol ammo.
            var held = UObject.Instantiate(rifle._heldPrefab.gameObject, _prefabRoot.transform);
            held.name = "HomemadeSniperHeld";
            var rw = held.GetComponent(Il2CppType.Of<RangedWeapon>())?.TryCast<RangedWeapon>();
            if (rw != null)
            {
                rw._weaponItemId = Id;
                if (rw._ammo != null)
                {
                    rw._ammo._type = PistolAmmoId;
                    rw._ammo._properties = ammo._ammoProperties;
                    rw._ammo._maxCount = cfg.MagazineSize;
                    rw._ammo._currentCount = 0;
                }
            }
            var hid = held.GetComponent(Il2CppType.Of<HeldItemIdentifier>())?.TryCast<HeldItemIdentifier>();
            if (hid != null) hid._itemId = Id;
            data._heldPrefab = held.transform;

            if (rifle._pickupPrefab != null)
            {
                var pickup = UObject.Instantiate(rifle._pickupPrefab.gameObject, _prefabRoot.transform);
                pickup.name = "HomemadeSniperPickup";
                var pu = pickup.GetComponent(Il2CppType.Of<PickUp>())?.TryCast<PickUp>();
                if (pu != null) pu._itemId = Id;
                data._pickupPrefab = pickup.transform;
            }

            ItemTools.RegisterItem(data, true);
            Data = data;
            RLog.Msg($"[SniperRifle] ลงทะเบียนไอเทม {Id} '{cfg.ItemName}' แล้ว");
            return true;
        }

        /// <summary>Per-world setup on the local player: hand locator, inventory slot, crafting result and recipe.</summary>
        internal static bool TrySetupPlayer(Config cfg)
        {
            if (LocalPlayer._instance == null || LocalPlayer.Inventory == null || LocalPlayer.RightHandTransform == null) return false;
            if (!IsRegistered) return false;
            var crafting = SonsSdk.GameState.CraftingSystem;
            if (crafting == null) return false;

            // The player's item store sizes its slots from the database when the player spawns; our item may
            // have been registered a moment later, so give it its slot explicitly.
            var store = LocalPlayer.Inventory._itemInstanceManager;
            if (store != null) store.SetItemMaxCount(Id, Data != null ? Data._maxAmount : 1);

            SetupHeldLocator();
            SetupInventoryLayout(cfg);
            SetupCraftingResult(crafting);
            SetupRecipes(cfg, crafting);
            crafting.RefreshAvailableRecipes(false);
            return true;
        }

        /// <summary>Adds <paramref name="item"/> to every cached list (of the given element type) that contains <paramref name="like"/>.</summary>
        private static void AddNextTo<T>(Il2CppSystem.Collections.Generic.List<T> list, T like, T item) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            if (list == null) return;
            bool hasLike = false, hasItem = false;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null) continue;
                if (e.Pointer == like.Pointer) hasLike = true;
                if (e.Pointer == item.Pointer) hasItem = true;
            }
            if (hasLike && !hasItem) list.Add(item);
        }

        private static void SetupHeldLocator()
        {
            var tags = LocalPlayer.RightHandTransform.GetComponentsInChildren(Il2CppType.Of<ItemLocatorTag>(), true);
            ItemLocatorTag rifleTag = null;
            for (int i = 0; i < tags.Length; i++)
            {
                var t = tags[i].TryCast<ItemLocatorTag>();
                if (t == null) continue;
                if (t._itemId == Id) return; // already there
                if (t._itemId == RifleId && rifleTag == null) rifleTag = t;
            }
            if (rifleTag == null) { RLog.Warning("[SniperRifle] ไม่พบตำแหน่งถือไรเฟิลในมือผู้เล่น"); return; }

            var clone = CloneInactive(rifleTag.gameObject, rifleTag.transform.parent);
            clone.name = "HomemadeSniperHeldLocator";
            var tag = clone.GetComponent(Il2CppType.Of<ItemLocatorTag>()).TryCast<ItemLocatorTag>();
            tag._itemId = Id;
            tag._name = "HomemadeSniper";
            clone.SetActive(rifleTag.gameObject.activeSelf);
            RegisterPropDefinition(clone.transform);
        }

        /// <summary>
        /// Equipping looks the item up in InventoryProps' prop definitions (item id -> hand locator).
        /// Without an entry TryEquip refuses the item.
        /// </summary>
        private static void RegisterPropDefinition(Transform locator)
        {
            var props = LocalPlayer.Inventory.InventoryProps;
            if (props == null) { RLog.Warning("[SniperRifle] ไม่พบ InventoryProps"); return; }
            var defs = props._propDefinitions;
            Sons.Player.InventoryProps.PropDefinition rifleDef = null;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null) continue;
                if (d._itemId == Id) return;
                if (d._itemId == RifleId) rifleDef = d;
            }
            var def = new Sons.Player.InventoryProps.PropDefinition();
            def._itemId = Id;
            def._heldLocator = locator != null ? locator : rifleDef?._heldLocator;
            defs.Add(def);
        }

        private static void SetupInventoryLayout(Config cfg)
        {
            if (ItemTools.GetInventoryLayoutItemGroup(Id) != null) return;
            var rifleGroup = ItemTools.GetInventoryLayoutItemGroup(RifleId);
            if (rifleGroup == null) { RLog.Warning("[SniperRifle] ไม่พบช่องไรเฟิลในกระเป๋า"); return; }

            var clone = CloneInactive(rifleGroup.gameObject, rifleGroup.transform.parent);
            clone.name = "HomemadeSniperLayoutGroup";
            var group = clone.GetComponent(Il2CppType.Of<InventoryLayoutItemGroup>()).TryCast<InventoryLayoutItemGroup>();
            group._itemId = Id;
            group._startDisabled = false;
            clone.transform.localPosition = rifleGroup.transform.localPosition + cfg.InventoryOffset;
            clone.SetActive(true);

            // The inventory opening animation and the enable/disable helpers keep their own group lists.
            var cutscenes = UObject.FindObjectsOfType(Il2CppType.Of<Sons.Cutscenes.InventoryCutscene>(), true);
            for (int i = 0; i < cutscenes.Length; i++)
            {
                var c = cutscenes[i].TryCast<Sons.Cutscenes.InventoryCutscene>();
                AddNextTo(c._inventoryLayoutItemGroups, rifleGroup, group);
                AddNextTo(c._creativeModeLayoutItemGroups, rifleGroup, group);
            }
            var enablers = UObject.FindObjectsOfType(Il2CppType.Of<InventoryEnableLayoutGroups>(), true);
            for (int i = 0; i < enablers.Length; i++)
                AddNextTo(enablers[i].TryCast<InventoryEnableLayoutGroups>()._layoutGroups, rifleGroup, group);
        }

        private static void SetupCraftingResult(CraftingSystem crafting)
        {
            var existing = ItemTools.GetCraftingResultLayoutItemGroup(Id);
            if (existing != null) { AddNextTo(crafting._craftingResultLayoutItemGroups, ItemTools.GetCraftingResultLayoutItemGroup(PistolId), existing); return; }
            var source = ItemTools.GetCraftingResultLayoutItemGroup(PistolId);
            var rifleInv = ItemTools.GetInventoryLayoutItemGroup(RifleId);
            if (source == null || rifleInv == null) { RLog.Warning("[SniperRifle] ไม่พบช่องผลลัพธ์การคราฟต้นแบบ"); return; }

            // The model shown on the crafting mat: the rifle's inventory model.
            var rifleRenderable = rifleInv.GetComponentInChildren(Il2CppType.Of<ItemRenderable>(), true)?.TryCast<ItemRenderable>();

            var clone = CloneInactive(source.gameObject, source.transform.parent);
            clone.name = "HomemadeSniperCraftingResultLayoutGroup";
            var group = clone.GetComponent(Il2CppType.Of<CraftingResultLayoutItemGroup>()).TryCast<CraftingResultLayoutItemGroup>();
            group._itemId = Id;

            var tags = clone.GetComponentsInChildren(Il2CppType.Of<ItemLocatorTag>(), true);
            for (int i = 0; i < tags.Length; i++)
            {
                var t = tags[i].TryCast<ItemLocatorTag>();
                t._itemId = Id;
                t._name = "HomemadeSniper";
            }
            if (rifleRenderable != null)
            {
                var renderables = clone.GetComponentsInChildren(Il2CppType.Of<ItemRenderable>(), true);
                for (int i = 0; i < renderables.Length; i++)
                {
                    var r = renderables[i].TryCast<ItemRenderable>();
                    r._itemId = RifleId;
                    r._baseRenderable = rifleRenderable._baseRenderable;
                    r._hasVariations = false;
                    r._useVariations = false;
                }
            }
            clone.SetActive(source.gameObject.activeSelf);
            AddNextTo(crafting._craftingResultLayoutItemGroups, source, group);
        }

        internal const string RockAmmoRecipeName = "HomemadeSniperRockAmmoRecipe";
        internal const string LeafAmmoRecipeName = "HomemadeSniperLeafAmmoRecipe";
        internal const int LeafId = 484;
        internal const int RockId = 393;

        private static void SetupRecipes(Config cfg, CraftingSystem crafting)
        {
            AddRecipe(crafting, RecipeName, 443, new[] { (StickId, cfg.Sticks), (SmallRockId, cfg.SmallRocks) }, Id, 1,
                $"กิ่งไม้ x{cfg.Sticks} + ก้อนหินเล็ก x{cfg.SmallRocks} -> {cfg.ItemName}");
            if (cfg.AmmoRockInput > 0 && cfg.AmmoRockOutput > 0)
                // Big rock, not small rock: a recipe made only of small rocks would always win over the sniper
                // recipe (sticks + small rocks) when both are on the crafting mat.
                // (one listed result for the recipe display; CraftBonus puts the real amount in the inventory)
                AddRecipe(crafting, RockAmmoRecipeName, 362, new[] { (RockId, cfg.AmmoRockInput) }, PistolAmmoId, 1,
                    $"ก้อนหิน x{cfg.AmmoRockInput} -> กระสุน 9 มม. x{cfg.AmmoRockOutput}");
            if (cfg.AmmoLeafInput > 0 && cfg.AmmoLeafOutput > 0)
                AddRecipe(crafting, LeafAmmoRecipeName, 362, new[] { (LeafId, cfg.AmmoLeafInput) }, PistolAmmoId, 1,
                    cfg.AmmoLeafFillsAll ? $"ใบไม้ x{cfg.AmmoLeafInput} -> กระสุนทุกชนิดจนเต็ม" : $"ใบไม้ x{cfg.AmmoLeafInput} -> กระสุน 9 มม. x{cfg.AmmoLeafOutput}");
        }

        private static void AddRecipe(CraftingSystem crafting, string name, int templateResultId, (int id, int count)[] inputs, int resultId, int resultCount, string description)
        {
            var recipes = crafting._recipeDatabase._recipes;
            CraftingRecipe template = null;
            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                if (r == null) continue;
                if (r.name == name) return; // already added this session (the database lives across worlds)
                if (template == null && r._resultingItems.Count > 0 && r._resultingItems[0].Id == templateResultId) template = r;
            }
            template ??= recipes[0];

            var recipe = UObject.Instantiate(template);
            recipe.name = name;
            recipe.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var ingredients = new Il2CppSystem.Collections.Generic.List<CraftingIngredient>();
            foreach (var (id, count) in inputs)
                ingredients.Add(new CraftingIngredient { ItemId = id, Count = count, IsReusable = false });
            var results = new Il2CppSystem.Collections.Generic.List<CraftingRecipe.ResultingItem>();
            for (int i = 0; i < resultCount; i++) results.Add(new CraftingRecipe.ResultingItem { Id = resultId });
            recipe._ingredients = ingredients;
            recipe._resultingItems = results;
            recipe._generatedResultingItems = new Il2CppSystem.Collections.Generic.List<CraftingRecipe.ResultingItem>();
            recipe._recipeType = CraftingRecipe.Type.CraftNewItem;
            recipe._weaponMod = null;
            recipe._useContainerDataForResultingItems = false;
            recipe._containerItemData = null;
            recipes.Add(recipe);
            RLog.Msg("[SniperRifle] เพิ่มสูตรคราฟ: " + description);
        }

        /// <summary>Instantiate a copy that has not run Awake/OnEnable yet (so ids can be changed first).</summary>
        internal static GameObject CloneInactive(GameObject source, Transform parent)
        {
            bool wasActive = source.activeSelf;
            source.SetActive(false);
            var clone = UObject.Instantiate(source, parent);
            source.SetActive(wasActive);
            return clone;
        }
    }
}
