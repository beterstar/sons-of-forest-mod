using System;
using System.IO;
using System.Text.Json;

namespace PickaxeStone;

/// <summary>Settings read from Mods/PickaxeStone/config.json (JSON with // comments).</summary>
internal sealed class ModConfig
{
    public bool Enabled = true;
    public int HitsPerDrop = 1;
    public int StonesPerDrop = 1;
    public float MinSecondsBetweenHits = 0.4f;
    public int DropItemId;          // 0 = big stone (ItemDatabase.StoneId)

    internal static ModConfig Load(string path)
    {
        var cfg = new ModConfig();
        if (!File.Exists(path))
            return cfg;

        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var v = prop.Value;
            switch (prop.Name.ToLowerInvariant())
            {
                case "enabled": cfg.Enabled = v.GetBoolean(); break;
                case "hitsperdrop": cfg.HitsPerDrop = Math.Clamp(v.GetInt32(), 1, 50); break;
                case "stonesperdrop": cfg.StonesPerDrop = Math.Clamp(v.GetInt32(), 1, 5); break;
                case "minsecondsbetweenhits": cfg.MinSecondsBetweenHits = Math.Clamp((float)v.GetDouble(), 0.1f, 5f); break;
                case "dropitemid": cfg.DropItemId = Math.Max(0, v.GetInt32()); break;
            }
        }
        return cfg;
    }
}
