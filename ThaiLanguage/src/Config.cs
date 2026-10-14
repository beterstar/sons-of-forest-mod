using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ThaiLanguage
{
    /// <summary>Settings read from Mods\ThaiLanguage\config.json (all optional).</summary>
    internal sealed class Config
    {
        public bool Enabled = true;

        // First existing file wins. {mod} = Mods\ThaiLanguage, {windows} = C:\Windows
        public List<string> Fonts = new List<string>
        {
            "{mod}/font.ttf",
            "{mod}/font.otf",
            "{windows}/Fonts/LeelawUI.ttf",
            "{windows}/Fonts/leelawad.ttf",
            "{windows}/Fonts/tahoma.ttf",
        };

        public float FontScale = 1f;
        public bool WordBreak = true;
        public bool MarkPositioning = true;
        public bool TranslateStaticTexts = true;
        public bool RenameEnglishLocale = true;
        public bool WriteReports = true;
        public bool Debug = false;

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
                switch (p.Name.ToLowerInvariant())
                {
                    case "enabled": cfg.Enabled = p.Value.GetBoolean(); break;
                    case "fontscale": cfg.FontScale = (float)p.Value.GetDouble(); break;
                    case "wordbreak": cfg.WordBreak = p.Value.GetBoolean(); break;
                    case "markpositioning": cfg.MarkPositioning = p.Value.GetBoolean(); break;
                    case "translatestatictexts": cfg.TranslateStaticTexts = p.Value.GetBoolean(); break;
                    case "renameenglishlocale": cfg.RenameEnglishLocale = p.Value.GetBoolean(); break;
                    case "writereports": cfg.WriteReports = p.Value.GetBoolean(); break;
                    case "debug": cfg.Debug = p.Value.GetBoolean(); break;
                    case "fonts":
                        var list = new List<string>();
                        foreach (var f in p.Value.EnumerateArray())
                            if (f.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(f.GetString())) list.Add(f.GetString());
                        if (list.Count > 0) cfg.Fonts = list;
                        break;
                }
            }
            return cfg;
        }

        internal IEnumerable<string> ResolveFonts(string modDir)
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var f in Fonts)
            {
                var p = f.Replace("{mod}", modDir, StringComparison.OrdinalIgnoreCase)
                         .Replace("{windows}", windows, StringComparison.OrdinalIgnoreCase)
                         .Replace('/', Path.DirectorySeparatorChar);
                if (!Path.IsPathRooted(p)) p = Path.Combine(modDir, p);
                yield return p;
            }
        }
    }
}
