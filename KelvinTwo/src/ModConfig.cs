using System;
using System.IO;
using System.Text.Json;

namespace KelvinTwo;

/// <summary>Settings read from Mods/KelvinTwo/config.json (JSON with // comments).</summary>
internal sealed class ModConfig
{
    public bool CloneEnabled = true;
    public string CloneName = "Kelvin 2";
    public float[] TintRgb = { 0.35f, 0.65f, 1.0f };
    public bool ShowNameTag = true;
    public float SpeedMultiplier = 1.6f;
    public float VisionMultiplier = 2.0f;
    public float SearchMultiplier = 5.0f;
    public bool WarpHome = true;
    public float WarpMinDistance = 60f;
    public float WarpCooldownSeconds = 60f;
    public float Sentiment = 100f;
    public bool AlwaysObedient = true;
    public bool GliderAutoPickup = true;
    public float GliderSearchRadius = 10f;

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
                case "cloneenabled": cfg.CloneEnabled = v.GetBoolean(); break;
                case "clonename": cfg.CloneName = v.GetString() ?? cfg.CloneName; break;
                case "tintrgb": cfg.TintRgb = ReadRgb(v, cfg.TintRgb); break;
                case "shownametag": cfg.ShowNameTag = v.GetBoolean(); break;
                case "speedmultiplier": cfg.SpeedMultiplier = Math.Clamp((float)v.GetDouble(), 0.5f, 3f); break;
                case "visionmultiplier": cfg.VisionMultiplier = Math.Clamp((float)v.GetDouble(), 1f, 5f); break;
                case "searchmultiplier": cfg.SearchMultiplier = Math.Clamp((float)v.GetDouble(), 1f, 10f); break;
                case "warphome": cfg.WarpHome = v.GetBoolean(); break;
                case "warpmindistance": cfg.WarpMinDistance = Math.Clamp((float)v.GetDouble(), 30f, 500f); break;
                case "warpcooldownseconds": cfg.WarpCooldownSeconds = Math.Clamp((float)v.GetDouble(), 20f, 600f); break;
                case "sentiment": cfg.Sentiment = Math.Clamp((float)v.GetDouble(), -100f, 100f); break;
                case "alwaysobedient": cfg.AlwaysObedient = v.GetBoolean(); break;
                case "gliderautopickup": cfg.GliderAutoPickup = v.GetBoolean(); break;
                case "glidersearchradius": cfg.GliderSearchRadius = Math.Clamp((float)v.GetDouble(), 3f, 100f); break;
            }
        }
        return cfg;
    }

    private static float[] ReadRgb(JsonElement v, float[] fallback)
    {
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3)
            return fallback;
        var rgb = new float[3];
        for (int i = 0; i < 3; i++)
            rgb[i] = Math.Clamp((float)v[i].GetDouble(), 0f, 1f);
        return rgb;
    }
}
