using System;
using System.IO;
using RedLoader;
using SonsSdk;

namespace PickaxeStone;

/// <summary>Hit a natural rock with the pickaxe and big stones drop next to it.</summary>
public class PickaxeStoneMod : SonsMod
{
    internal static PickaxeStoneMod Instance { get; private set; }
    internal ModConfig Cfg { get; private set; } = new();
    internal HitCounter Counter { get; private set; }

    public PickaxeStoneMod()
    {
        HarmonyPatchAll = true;
    }

    protected override void OnInitializeMod()
    {
        try
        {
            Cfg = ModConfig.Load(Path.Combine(DataPath.Path, "config.json"));
        }
        catch (Exception ex)
        {
            RLog.Error("[PickaxeStone] config.json invalid, using defaults: " + ex.Message);
        }
        Counter = new HitCounter(Cfg.HitsPerDrop, Cfg.MinSecondsBetweenHits);
        Instance = this;
        RLog.Msg($"[PickaxeStone] enabled={Cfg.Enabled}, {Cfg.StonesPerDrop} stone(s) every {Cfg.HitsPerDrop} hit(s)");
    }

    // Fresh hit counts per world.
    protected override void OnGameStart() => Counter = new HitCounter(Cfg.HitsPerDrop, Cfg.MinSecondsBetweenHits);
}
