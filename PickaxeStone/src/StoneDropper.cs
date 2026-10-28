using RedLoader;
using Sons.Items.Core;
using TheForest.Items.Utils;
using UnityEngine;

namespace PickaxeStone;

/// <summary>Drops stones next to the rock, on the player's side, using the game's own item spawner.</summary>
internal static class StoneDropper
{
    private const int FallbackStoneId = 640;
    private const float OutFromRock = 0.9f;
    private const float Lift = 0.6f;
    private const float Spread = 0.35f;

    private static bool _loggedFirst;

    internal static int ResolveItemId(int configured) =>
        configured > 0 ? configured : (ItemDatabase.StoneId > 0 ? ItemDatabase.StoneId : FallbackStoneId);

    internal static void Drop(int itemId, int count, Vector3 impact, Vector3 playerPos)
    {
        var data = ItemDatabaseManager.ItemById(itemId);
        if (data == null)
        {
            RLog.Error($"[PickaxeStone] item {itemId} not found - check dropItemId in config.json");
            return;
        }

        var away = playerPos - impact;
        away.y = 0f;
        away = away.sqrMagnitude > 0.001f ? away.normalized : Vector3.forward;

        for (int i = 0; i < count; i++)
        {
            var jitter = new Vector3(Random.Range(-Spread, Spread), 0f, Random.Range(-Spread, Spread));
            var pos = impact + away * OutFromRock + Vector3.up * (Lift + i * 0.4f) + jitter;
            var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var velocity = away * 1.5f + Vector3.up;

            // Pass empty Nullables, never null: a null Il2Cpp Nullable can crash the native call.
            ItemUtils.SpawnItemPickup(data, pos, rot, velocity,
                false, true, true, false, null, null, 1,
                new Il2CppSystem.Nullable<Vector3>(), new Il2CppSystem.Nullable<Vector3>(), true, false);
        }

        if (_loggedFirst) return;
        _loggedFirst = true;
        RLog.Msg($"[PickaxeStone] first drop: {count} x item {itemId} at {impact}");
    }
}
