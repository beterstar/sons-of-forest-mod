using System;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using RedLoader;
using Sons.Crafting;
using TheForest.Utils;
using UnityEngine;

namespace SniperRifle
{
    /// <summary>
    /// Development self-test (only when Mods\SniperRifle\explore.flag exists): starts a throw-away game,
    /// gives and equips the sniper, checks the recipe and takes screenshots. Nothing is ever saved.
    /// </summary>
    internal sealed class DevTest
    {
        private readonly string _dir;
        private readonly StringBuilder _log = new StringBuilder();
        private float _at = -1f;
        private int _step;

        /// <summary>ammotest.flag: stop after the crafting checks (sniper, leaves, rock).</summary>
        private readonly bool AmmoOnly;

        internal DevTest(string dir)
        {
            _dir = Path.Combine(dir, "debug"); Directory.CreateDirectory(_dir);
            AmmoOnly = File.Exists(Path.Combine(dir, "ammotest.flag"));
        }

        internal void OnTitle()
        {
            _at = Time.unscaledTime + 3f;
            _step = 0;
        }

        internal void OnGameStart()
        {
            _at = Time.unscaledTime + 20f;
            _step = 10;
        }

        private void Log(string s) { _log.AppendLine(s); RLog.Msg("[SniperRifle/test] " + s); Flush(); }

        private void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(_dir, name));

        internal void Update(float now)
        {
            if (_at < 0f || now < _at) return;
            _at = -1f;
            try { Step(); }
            catch (Exception e) { Log($"step {_step} failed: {e}"); Flush(); }
        }

        private void Next(float delay) { _step++; _at = Time.unscaledTime + delay; }

        private void Step()
        {
            var inv = LocalPlayer._instance != null ? LocalPlayer.Inventory : null;
            switch (_step)
            {
                case 0:
                    Explorer.AutoLoadLatestSave();
                    _step = 9;
                    return;

                case 10:
                    Log($"registered={SniperItem.IsRegistered} itemName={SniperItem.Data?._uiData?._title}");
                    Log($"add sniper {inv.AddItem(SniperItem.Id, 1, true, false, null)}");
                    Log($"add 9mm {inv.AddItem(SniperItem.PistolAmmoId, 30, true, false, null)}");
                    Log($"add sticks {inv.AddItem(SniperItem.StickId, 10, true, false, null)} rocks {inv.AddItem(SniperItem.SmallRockId, 10, true, false, null)}");
                    var store = inv._itemInstanceManager;
                    Log($"store count={store?.GetItemCount(SniperItem.Id)} amountOf={inv.AmountOf(SniperItem.Id, true, false)} max={inv.GetMaxAmountOf(SniperItem.Id)} owns={inv.Owns(SniperItem.Id)} canEquipRight={inv.CanEquipItemInRightHand(SniperItem.Id)}");
                    Log($"equip {inv.TryEquip(SniperItem.Id, false, false, false)}");
                    inv.AddItem(SniperItem.RifleId, 1, true, false, null); // to compare inventory slots
                    Next(16f);
                    return;

                case 11:
                    var ri = inv.RightHandItem;
                    Log($"right hand = {ri?._itemID} object={(ri?.ItemObject != null ? Dump.PathOf(ri.ItemObject.transform) : "null")}");
                    if (ri?.ItemObject != null)
                    {
                        File.WriteAllText(Path.Combine(_dir, "held_hierarchy.txt"), Dump.Hierarchy(ri.ItemObject.transform, 8), new UTF8Encoding(true));
                        var rw = ri.ItemObject.GetComponent(Il2CppType.Of<Sons.Weapon.RangedWeapon>())?.TryCast<Sons.Weapon.RangedWeapon>();
                        if (rw != null) Log($"weapon id={rw._weaponItemId} ammoType={rw._ammo?._type} props={rw._ammo?._properties?.name} damage={rw._ammo?._properties?.ProjectileInfo?.muzzleDamage} max={rw._ammo?._maxCount} cur={rw._ammo?._currentCount}");
                    }
                    Shot("held.png");
                    Next(1.5f);
                    return;

                case 12:
                    Scope.DevForce = true;
                    Next(2.5f);
                    return;

                case 13:
                    Log($"scoped={Scope.IsScoped} fov={LocalPlayer.MainCam.fieldOfView} near={LocalPlayer.MainCam.nearClipPlane}");
                    Shot("scoped.png");
                    Next(1.5f);
                    return;

                case 14:
                    Scope.DevForce = false;
                    var cs = SonsSdk.GameState.CraftingSystem;
                    var recipes = cs._recipeDatabase._recipes;
                    var sniperRecipe = FindRecipe(cs, SniperItem.RecipeName);
                    var leafRecipe = FindRecipe(cs, SniperItem.LeafAmmoRecipeName);
                    var leafArmour = FindRecipe(cs, "LeafArmourRecipe");
                    var la = new System.Collections.Generic.List<(int, int)>();
                    if (leafArmour != null) for (int j = 0; j < leafArmour._ingredients.Count; j++) la.Add((leafArmour._ingredients[j].ItemId, leafArmour._ingredients[j].Count));
                    Log($"leaf armour ingredients: {string.Join(" ", la)}");
                    Log($"CHECK sniper 10 stick 10 small rock -> {Satisfied(cs, (392, 10), (476, 10))}");
                    Log($"CHECK 1 rock -> {Satisfied(cs, (393, 1))}");
                    Log($"CHECK 3 leaves -> {Satisfied(cs, (484, 3))}");
                    Log($"CHECK leaf armour set -> {Safe(() => Satisfied(cs, (484, 10), (415, 1)))}");
                    Log($"CHECK repair tool (rope stick rock) -> {Safe(() => Satisfied(cs, (403, 1), (392, 1), (393, 1)))}");
                    Log($"CHECK arrows -> {Safe(() => Satisfied(cs, (392, 2), (479, 2), (476, 4)))}");
                    Log($"CHECK bow -> {Safe(() => Satisfied(cs, (392, 2), (403, 1), (419, 1)))}");
                    Log($"CHECK torch -> {Safe(() => Satisfied(cs, (392, 1), (415, 1)))}");

                    Log($"inventory slot={ItemTools_Get(SniperItem.Id)} craft result slot={(SonsSdk.ItemTools.GetCraftingResultLayoutItemGroup(SniperItem.Id) != null)}");
                    inv.UnequipItemAtSlot(Sons.Items.Core.EquipmentSlot.RightHand, false, true, false, true, false, false);
                    inv.RemoveItem(SniperItem.Id, 1, false, false, false, null, false);
                    inv.AddItem(SniperItem.LeafId, 13, true, false, null);
                    inv.AddItem(415, 1, true, false, null); // cloth, for the leaf armour check
                    Log($"removed sniper, amount={inv.AmountOf(SniperItem.Id, true, false)} leaves={inv.AmountOf(SniperItem.LeafId, true, false)}");
                    inv.Open();
                    Next(4f);
                    return;

                case 15:
                    _cs = SonsSdk.GameState.CraftingSystem;
                    Log($"(inventory open) canHoldMoreSticks={_cs.CanHoldMoreOfIngredient(SniperItem.StickId)} rocks={_cs.CanHoldMoreOfIngredient(SniperItem.SmallRockId)} leaves={_cs.CanHoldMoreOfIngredient(SniperItem.LeafId)}");
                    Log($"REAL mat sniper: {PutOnMat(inv, (SniperItem.StickId, 10), (SniperItem.SmallRockId, 10))}");
                    Shot("mat_sniper.png");
                    Log($"REAL combine sniper: {_cs.TryCombineIngredients()}");
                    Next(8f);
                    return;

                case 16:
                    _cs.StashAllCraftingResultItems();
                    Next(2f);
                    return;

                case 17:
                    Log($"REAL after craft+stash: sniper amount={inv.AmountOf(SniperItem.Id, true, false)} sticks={inv.AmountOf(SniperItem.StickId, true, false)} smallRocks={inv.AmountOf(SniperItem.SmallRockId, true, false)} matLeft={_cs.CurrentIngredients.Count}");
                    _cs.StashAllIngredients();
                    Log($"REAL mat leaf armour: {PutOnMat(inv, (SniperItem.LeafId, 10), (415, 1))}");
                    _cs.StashAllIngredients();
                    Log($"REAL mat 3 leaves: {PutOnMat(inv, (SniperItem.LeafId, 3))}");
                    _ammoBefore = inv.AmountOf(SniperItem.PistolAmmoId, true, false);
                    Log($"9mm max carry = {inv.GetMaxAmountOf(SniperItem.PistolAmmoId)}, have {_ammoBefore}");
                    Log("ammo before: " + AmmoList(inv));
                    Log($"REAL combine leaves: {_cs.TryCombineIngredients()}");
                    Next(8f);
                    return;

                case 18:
                    _polls++;
                    Log($"poll {_polls}: last={_cs._lastCraftedRecipe?.name} matIngredients={_cs.CurrentIngredients.Count} leavesOnMat={_cs.GetIngredientCount(SniperItem.LeafId)} 9mm={inv.AmountOf(SniperItem.PistolAmmoId, true, false)} store9mm={inv._itemInstanceManager.GetItemCount(SniperItem.PistolAmmoId)} resultsOnMat9mm={_cs.GetCountOfResultingItemWithId(SniperItem.Data != null ? Sons.Items.Core.ItemDatabaseManager.ItemById(SniperItem.PistolAmmoId) : null)}");
                    if (_polls < 8) { _at = Time.unscaledTime + 2f; return; }
                    _cs.StashAllCraftingResultItems();
                    Next(2f);
                    return;
                case 19:
                    Log($"REAL after leaf craft+stash: 9mm {_ammoBefore} -> {inv.AmountOf(SniperItem.PistolAmmoId, true, false)}");
                    Log("ammo after: " + AmmoList(inv));
                    _cs.StashAllIngredients();
                    if (AmmoOnly) { _step = 40; _at = Time.unscaledTime + 1f; return; }
                    _group = SonsSdk.ItemTools.GetInventoryLayoutItemGroup(SniperItem.Id);
                    _rifleGroup = SonsSdk.ItemTools.GetInventoryLayoutItemGroup(SniperItem.RifleId);
                    _icam = LocalPlayer.InventoryCam;
                    _camSavedPos = _icam.transform.position;
                    _camSavedRot = _icam.transform.rotation;
                    _icam.transform.position = _rifleGroup.transform.position + Vector3.up * 2.2f;
                    _icam.transform.LookAt(_rifleGroup.transform.position);
                    _variant = 0;
                    Next(0.5f);
                    return;

                case 20:
                {
                    var offsets = new[] { SniperRifleMod.Cfg.InventoryOffset };
                    if (_variant < offsets.Length)
                    {
                        _group.transform.localPosition = _rifleGroup.transform.localPosition + offsets[_variant];
                        _at = Time.unscaledTime + 1.2f;
                        _step = 21;
                        return;
                    }
                    _icam.transform.position = _camSavedPos;
                    _icam.transform.rotation = _camSavedRot;
                    inv.Close(true);
                    _at = Time.unscaledTime + 2f;
                    _step = 23;
                    return;
                }

                case 21:
                    Log($"variant {_variant}: sniper viewport={_icam.WorldToViewportPoint(_group.transform.position)} rifle viewport={_icam.WorldToViewportPoint(_rifleGroup.transform.position)} sniperItemsActive={ActiveChildren(_group.transform)} rifleItemsActive={ActiveChildren(_rifleGroup.transform)}");
                    Shot($"slot_{_variant}.png");
                    _variant++;
                    _at = Time.unscaledTime + 1f;
                    _step = 20;
                    return;

                case 23:
                    inv.UnequipItemAtSlot(Sons.Items.Core.EquipmentSlot.RightHand, false, true, false, true, false, false);
                    Next(1.5f);
                    return;

                case 24:
                    Log($"first-look equip {inv.TryEquip(SniperItem.Id, false, false, true)}");
                    _polls = 0;
                    Next(1.2f);
                    return;

                case 25:
                    Shot($"firstlook_{_polls}.png");
                    var heldGo = inv.RightHandItem?.ItemObject;
                    var node = heldGo != null ? FindDeep(heldGo.transform, "RifleScopeModel") : null;
                    if (node != null)
                    {
                        var vp = LocalPlayer.MainCam.WorldToViewportPoint(node.position);
                        var rs = node.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true);
                        var vis = new StringBuilder();
                        for (int i = 0; i < rs.Length; i++) { var r = rs[i].Cast<Renderer>(); vis.Append(r.isVisible ? "V" : "-"); }
                        var r0 = rs.Length > 0 ? rs[0].Cast<Renderer>() : null;
                        Log($"scope node vp={vp} up(world)={node.up} camUp={LocalPlayer.MainCamTr.up} visible={vis} layer={node.gameObject.layer} gunLayer={heldGo.layer} shader={r0?.sharedMaterial?.shader?.name} active={node.gameObject.activeInHierarchy}");
                    }
                    else Log("scope node missing");
                    _polls++;
                    if (_polls < 5) { _at = Time.unscaledTime + 1.0f; return; }
                    Next(1f);
                    return;
                // ---- ammo from a big rock
                case 40:
                    inv.AddItem(SniperItem.RockId, 1, true, false, null);
                    Log($"REAL mat 1 rock: {PutOnMat(inv, (SniperItem.RockId, 1))}");
                    _ammoBefore = inv.AmountOf(SniperItem.PistolAmmoId, true, false);
                    Log($"REAL combine rock: {_cs.TryCombineIngredients()}");
                    _step = 41; _at = Time.unscaledTime + 8f;
                    return;
                case 41:
                    Log($"rock craft: results on mat 9mm={_cs.GetCountOfResultingItemWithId(Sons.Items.Core.ItemDatabaseManager.ItemById(SniperItem.PistolAmmoId))}");
                    _cs.StashAllCraftingResultItems();
                    _step = 42; _at = Time.unscaledTime + 2f;
                    return;
                case 42:
                    Log($"REAL after rock craft+stash: 9mm {_ammoBefore} -> {inv.AmountOf(SniperItem.PistolAmmoId, true, false)} (max {inv.GetMaxAmountOf(SniperItem.PistolAmmoId)}); rocks in inventory now {inv.AmountOf(SniperItem.RockId, true, false)} (1 = given back)");
                    Shot("ammo_crafted.png");
                    _step = 43; _at = Time.unscaledTime + 0.5f;
                    return;
                case 43: // right away again: 3 leaves
                    Log($"REAL mat 3 leaves again: {PutOnMat(inv, (SniperItem.LeafId, 3))}");
                    _ammoBefore = inv.AmountOf(SniperItem.PistolAmmoId, true, false);
                    Log($"REAL combine leaves again: {_cs.TryCombineIngredients()}");
                    _step = 44; _at = Time.unscaledTime + 8f;
                    return;
                case 44:
                    _cs.StashAllCraftingResultItems();
                    _step = 45; _at = Time.unscaledTime + 2f;
                    return;
                case 45:
                    Log($"REAL after 2nd leaf craft: 9mm {_ammoBefore} -> {inv.AmountOf(SniperItem.PistolAmmoId, true, false)}; mat ingredients left={_cs.CurrentIngredients.Count} leaves left={inv.AmountOf(SniperItem.LeafId, true, false)}");
                    _step = 26; _at = Time.unscaledTime + 1f;
                    return;

                case 26:
                    Flush();
                    Log("done");
                    Flush();
                    return;
            }
        }


        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }
        private int _variant;
        private Sons.Inventory.InventoryLayoutItemGroup _group, _rifleGroup;
        private Camera _icam;

        private static string ActiveChildren(Transform t)
        {
            int on = 0, all = 0;
            var items = t.GetComponentsInChildren(Il2CppType.Of<Sons.Inventory.InventoryLayoutItem>(), true);
            for (int i = 0; i < items.Length; i++) { all++; if (items[i].Cast<Component>().gameObject.activeInHierarchy) on++; }
            return $"{on}/{all}";
        }
        private Vector3 _camSavedPos;
        private Quaternion _camSavedRot;        private CraftingSystem _cs;
        private int _polls;
        private int _ammoBefore;

        /// <summary>Moves real item instances from the inventory onto the crafting mat and reports the recipe the cog offers.</summary>
        private string PutOnMat(TheForest.Items.Inventory.PlayerInventory inv, params (int id, int count)[] items)
        {
            var store = inv._itemInstanceManager;
            var sb = new StringBuilder();
            foreach (var (id, count) in items)
            {
                var all = store.GetAllItemsOfType(id);
                int added = 0;
                // Same as dragging onto the mat: the instance leaves the inventory store and goes onto the mat.
                var picked = new System.Collections.Generic.List<Sons.Inventory.ItemInstance>();
                for (int i = 0; i < all.Count && picked.Count < count; i++) picked.Add(all[i]);
                foreach (var inst in picked) { store.TryRemoveItem(inst); _cs.AddIngredient(inst); added++; }
                sb.Append($"{id}:{added}/{all.Count}(mat {_cs.GetIngredientCount(id)}) ");
            }
            CraftingRecipe r = null;
            bool ok = _cs.HasSatisfiedRecipeWithCurrentIngredients(out r);
            sb.Append("=> ").Append(ok ? r?.name : "none");
            return sb.ToString();
        }

        private static Il2CppSystem.Collections.Generic.List<int> List(params int[] ids)
        {
            var l = new Il2CppSystem.Collections.Generic.List<int>();
            foreach (var i in ids) l.Add(i);
            return l;
        }

        private static string Complete(CraftingSystem cs, Il2CppSystem.Collections.Generic.List<int> ids)
        {
            var complete = cs._recipeDatabase.FindCompleteRecipesWitItems(ids);
            var names = new StringBuilder();
            for (int i = 0; i < complete.Count; i++) names.Append(complete[i].name).Append($"({complete[i]._resultingItems.Count}) ");
            return names.ToString();
        }

        /// <summary>Which recipe would the crafting cog offer for this mat content? (mat is restored afterwards)</summary>
        private static string Satisfied(CraftingSystem cs, params (int id, int count)[] mat)
        {
            var current = cs.CurrentIngredients;
            var saved = new System.Collections.Generic.List<CraftingIngredient>();
            for (int i = 0; i < current.Count; i++) saved.Add(current[i]);
            current.Clear();
            foreach (var (id, count) in mat) current.Add(new CraftingIngredient { ItemId = id, Count = count, IsReusable = false });
            string result;
            try
            {
                cs.RefreshAvailableRecipes(false);
                CraftingRecipe r = null;
                bool ok = cs.HasSatisfiedRecipeWithCurrentIngredients(out r);
                result = ok ? r?.name : "none";
            }
            finally
            {
                current.Clear();
                foreach (var s in saved) current.Add(s);
                cs.RefreshAvailableRecipes(false);
            }
            return result;
        }

        private static string AmmoList(TheForest.Items.Inventory.PlayerInventory inv)
        {
            var sb = new StringBuilder();
            foreach (int id in CraftBonus.AllAmmoIds())
            {
                var d = Sons.Items.Core.ItemDatabaseManager.ItemById(id);
                sb.Append($" {id}:{d?._name}={inv.AmountOf(id, true, false)}/{inv.GetMaxAmountOf(id)}");
            }
            return sb.ToString();
        }

        private static string Safe(Func<string> f) { try { return f(); } catch (Exception e) { return "ERR " + e.GetType().Name; } }

        private static CraftingRecipe FindRecipe(CraftingSystem cs, string name)
        {
            var rs = cs._recipeDatabase._recipes;
            for (int i = 0; i < rs.Count; i++) if (rs[i] != null && rs[i].name == name) return rs[i];
            return null;
        }

        private static string Available(CraftingSystem cs, params (int id, int count)[] mat)
        {
            var current = cs.CurrentIngredients;
            var saved = new System.Collections.Generic.List<CraftingIngredient>();
            for (int i = 0; i < current.Count; i++) saved.Add(current[i]);
            current.Clear();
            foreach (var (id, count) in mat) current.Add(new CraftingIngredient { ItemId = id, Count = count, IsReusable = false });
            var sb = new StringBuilder();
            try
            {
                cs.RefreshAvailableRecipes(false);
                var av = cs._availableRecipes;
                for (int i = 0; i < av.Count; i++) sb.Append(av[i]?.name).Append(", ");
            }
            finally { current.Clear(); foreach (var s in saved) current.Add(s); cs.RefreshAvailableRecipes(false); }
            return sb.ToString();
        }

        private static string ItemTools_Get(int id)
        {
            var g = SonsSdk.ItemTools.GetInventoryLayoutItemGroup(id);
            return g == null ? "none" : $"{Dump.PathOf(g.transform)} lp={g.transform.localPosition} active={g.gameObject.activeInHierarchy}";
        }

        private void Flush() => File.WriteAllText(Path.Combine(_dir, "devtest.txt"), _log.ToString(), new UTF8Encoding(true));
    }
}
