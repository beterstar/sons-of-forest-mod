using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CompanionRevive
{
    /// <summary>Settings from Mods\CompanionRevive\config.json.</summary>
    internal sealed class Config
    {
        public bool ReviveVirginia = true;
        public bool ReviveKelvin = true;
        public string[] SaveIds = Array.Empty<string>(); // empty = every save
        public float Sentiment = 0f;                     // starting trust towards the player (-100..100)

        internal bool AppliesTo(string saveId) => SaveIds.Length == 0 || SaveIds.Contains(saveId);

        internal static Config Load(string path)
        {
            var cfg = new Config();
            if (!File.Exists(path)) return cfg;
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var v = p.Value;
                switch (p.Name.ToLowerInvariant())
                {
                    case "revivevirginia": cfg.ReviveVirginia = v.GetBoolean(); break;
                    case "revivekelvin": cfg.ReviveKelvin = v.GetBoolean(); break;
                    case "saveids":
                        cfg.SaveIds = v.ValueKind == JsonValueKind.Array
                            ? v.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                            : Array.Empty<string>();
                        break;
                    case "sentiment": cfg.Sentiment = Math.Clamp((float)v.GetDouble(), -100f, 100f); break;
                }
            }
            return cfg;
        }
    }
}
