using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using Il2CppInterop.Runtime;
using RedLoader;
using SonsSdk;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore;
using ULocale = UnityEngine.Localization.Locale;
using UObject = UnityEngine.Object;

[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v6.0", FrameworkDisplayName = "")]

namespace ThaiLanguage
{
    public class ThaiLanguageMod : SonsMod
    {
        internal const string Version = "1.0.0";

        internal static ThaiLanguageMod Instance;

        private Config _cfg = new Config();
        private string _dir;

        // table -> key -> Thai text (already word-broken)
        private readonly Dictionary<string, Dictionary<string, string>> _thai = new Dictionary<string, Dictionary<string, string>>();
        // table -> key -> original English text (learned while applying)
        private readonly Dictionary<string, Dictionary<string, string>> _english = new Dictionary<string, Dictionary<string, string>>();
        // exact English UI text -> Thai, for texts that never go through the localization system
        private readonly Dictionary<string, string> _reverse = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _reverseUpper = new Dictionary<string, string>();

        private const string StaticTable = "StaticTexts";

        private readonly Dictionary<string, IntPtr> _appliedTables = new Dictionary<string, IntPtr>();
        private readonly HashSet<string> _untranslated = new HashSet<string>();
        private readonly SortedSet<string> _missing = new SortedSet<string>();

        private TMP_FontAsset _font;
        private string _fontPath;
        private bool _fontFailed;
        private bool _localizationReady;
        private bool _untranslatedDirty;
        private float _nextFontCheck, _nextTableCheck, _nextScan, _nextReport;
        private float _debugAt = -1f;
        private int _debugStep;

        private static readonly HashSet<string> LoggedErrors = new HashSet<string>();

        public ThaiLanguageMod()
        {
            HarmonyPatchAll = false;
            OnUpdateCallback = OnUpdate;
        }

        protected override void OnInitializeMod()
        {
            Instance = this;
            _dir = DataPath.Path;
            try { _cfg = Config.Load(Path.Combine(_dir, "config.json")); }
            catch (Exception e) { RLog.Error("[ThaiLanguage] config.json อ่านไม่ได้ ใช้ค่าเริ่มต้นแทน: " + e.Message); }

            if (!_cfg.Enabled)
            {
                RLog.Msg("[ThaiLanguage] ปิดใช้งานอยู่ (config.json: enabled = false)");
                return;
            }

            LoadTranslations(Path.Combine(_dir, "th.json"));
            InstallPatches();
        }

        protected override void OnSdkInitialized()
        {
            if (!_cfg.Enabled) return;
            _localizationReady = true;
            EnsureFont();
            ApplyAllTables(refreshUi: true);
            RefreshAllTexts();
            if (_cfg.Debug) _debugAt = Time.unscaledTime + 4f;
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (!_cfg.Enabled) return;
            // UI of the new scene builds over the next frames; scan it shortly after.
            _nextScan = Time.unscaledTime + 1.5f;
            _nextTableCheck = Time.unscaledTime + 1f;
        }

        private void OnUpdate()
        {
            if (!_cfg.Enabled) return;
            float now = Time.unscaledTime;

            if (now >= _nextFontCheck)
            {
                _nextFontCheck = now + 2f;
                EnsureFont();
            }

            if (!_localizationReady) return;

            if (now >= _nextTableCheck)
            {
                _nextTableCheck = now + 2f;
                ApplyAllTables(refreshUi: true);
            }

            if (_cfg.TranslateStaticTexts && now >= _nextScan)
            {
                _nextScan = now + 3f;
                ScanTexts(force: false);
            }

            if (_cfg.WriteReports && now >= _nextReport)
            {
                _nextReport = now + 30f;
                WriteUntranslatedReport();
            }

            if (_debugAt >= 0f && now >= _debugAt) DebugStep();
        }

        // ------------------------------------------------------------------ translations

        private void LoadTranslations(string path)
        {
            if (!File.Exists(path))
            {
                RLog.Error("[ThaiLanguage] ไม่พบไฟล์คำแปล: " + path);
                return;
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                int count = 0;
                foreach (var table in doc.RootElement.EnumerateObject())
                {
                    if (table.Name.StartsWith("_") || table.Value.ValueKind != JsonValueKind.Object) continue;
                    // "StaticTexts" is not a game table: English UI label -> Thai, for labels typed into prefabs.
                    bool isStatic = table.Name == StaticTable;
                    var map = new Dictionary<string, string>();
                    foreach (var e in table.Value.EnumerateObject())
                    {
                        if (e.Value.ValueKind != JsonValueKind.String) continue;
                        var text = e.Value.GetString();
                        if (string.IsNullOrEmpty(text)) continue;
                        text = _cfg.WordBreak ? ThaiText.InsertWordBreaks(text) : text;
                        count++;
                        if (isStatic)
                        {
                            _reverse[e.Name.Trim()] = text;
                            _reverseUpper[e.Name.Trim().ToUpperInvariant()] = text;
                        }
                        else map[e.Name] = text;
                    }
                    if (!isStatic) _thai[table.Name] = map;
                }
                RLog.Msg($"[ThaiLanguage] โหลดคำแปล {count} ข้อความ จาก {_thai.Count} ตาราง");
            }
            catch (Exception e)
            {
                RLog.Error("[ThaiLanguage] th.json ผิดรูปแบบ: " + e.Message);
            }
        }

        private bool TryGetThai(string table, string key, out string thai)
        {
            thai = null;
            if (key == null || !_thai.TryGetValue(table, out var map)) return false;
            if (map.TryGetValue(key, out thai)) return true;
            // Thai has no plural / grammatical gender: fall back to the singular / masculine form.
            foreach (var alt in AlternateKeys(key))
                if (map.TryGetValue(alt, out thai)) return true;
            return false;
        }

        private static IEnumerable<string> AlternateKeys(string key)
        {
            if (key.EndsWith("_PLURAL"))
            {
                var stem = key.Substring(0, key.Length - "_PLURAL".Length);
                yield return stem;
                yield return stem + "_SINGULAR";
                if (stem.EndsWith("_F")) yield return stem.Substring(0, stem.Length - 2) + "_M_SINGULAR";
            }
            if (key.EndsWith("_F_SINGULAR")) yield return key.Substring(0, key.Length - "_F_SINGULAR".Length) + "_M_SINGULAR";
        }

        private static bool IsEnglish(string code) =>
            code != null && (code.Equals("en", StringComparison.OrdinalIgnoreCase) || code.StartsWith("en-", StringComparison.OrdinalIgnoreCase));

        private void RememberEnglish(string table, string key, string english)
        {
            if (string.IsNullOrEmpty(english) || ThaiText.ContainsThai(english)) return;
            if (!_english.TryGetValue(table, out var map)) _english[table] = map = new Dictionary<string, string>();
            if (map.ContainsKey(key)) return;
            map[key] = english;

            if (!TryGetThai(table, key, out var thai)) return;
            var trimmed = english.Trim();
            if (trimmed.Length < 2 || trimmed.Contains('{') || !ThaiText.HasLatinWord(trimmed)) return;
            if (!_reverse.ContainsKey(trimmed)) _reverse[trimmed] = thai;
            var upper = trimmed.ToUpperInvariant();
            if (!_reverseUpper.ContainsKey(upper)) _reverseUpper[upper] = thai;
        }

        private void ApplyAllTables(bool refreshUi)
        {
            if (_thai.Count == 0) return;
            ULocale locale;
            try { locale = LocalizationSettings.SelectedLocale; }
            catch (Exception e) { LogOnce("SelectedLocale", e); return; }
            if (locale == null || !IsEnglish(locale.Identifier.Code)) return;

            if (_cfg.RenameEnglishLocale)
            {
                try { if (locale.LocaleName != "ภาษาไทย (Thai)") locale.LocaleName = "ภาษาไทย (Thai)"; }
                catch (Exception e) { LogOnce("LocaleName", e); }
            }

            var db = LocalizationSettings.StringDatabase;
            int changedTables = 0, changedEntries = 0;
            foreach (var tableName in _thai.Keys.ToList())
            {
                StringTable table;
                try { table = db.GetTable(tableName, locale); }
                catch (Exception e) { LogOnce("GetTable " + tableName, e); continue; }
                if (table == null) continue;

                if (_appliedTables.TryGetValue(tableName, out var known) && known == table.Pointer && TableStillThai(tableName, table))
                    continue;

                changedEntries += ApplyTable(tableName, table);
                _appliedTables[tableName] = table.Pointer;
                changedTables++;
            }

            if (changedTables == 0) return;
            RLog.Msg($"[ThaiLanguage] แปลแล้ว {changedEntries} ข้อความ ใน {changedTables} ตาราง");
            if (_cfg.WriteReports) WriteMissingReport();

            if (refreshUi && changedEntries > 0)
            {
                // Make every LocalizeStringEvent / LocalizedString re-read its text.
                try { LocalizationSettings.Instance.InvokeSelectedLocaleChanged(locale); }
                catch (Exception e) { LogOnce("InvokeSelectedLocaleChanged", e); }
                _nextScan = Time.unscaledTime + 0.5f;
            }
        }

        private int ApplyTable(string tableName, StringTable table)
        {
            int changed = 0;
            var entries = table.SharedData.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var shared = entries[i];
                var key = shared.Key;
                var entry = table.GetEntry(shared.Id);
                if (entry == null) continue;
                var current = entry.Value;
                if (!TryGetThai(tableName, key, out var thai))
                {
                    if (!string.IsNullOrWhiteSpace(current) && ThaiText.HasLatinWord(current) && !ThaiText.ContainsThai(current))
                        _missing.Add($"{tableName}\t{key}\t{current.Replace("\n", "\\n").Replace("\r", "\\r")}");
                    continue;
                }
                if (current == thai) continue;
                RememberEnglish(tableName, key, current);
                entry.Value = thai;
                changed++;
            }
            return changed;
        }

        private bool TableStillThai(string tableName, StringTable table)
        {
            // Spot-check one translated entry: if the game reloaded the table from disk it is English again.
            if (!_thai.TryGetValue(tableName, out var map)) return true;
            foreach (var kv in map)
            {
                var e = table.GetEntry(kv.Key);
                if (e == null) continue;
                return e.Value == kv.Value;
            }
            return true;
        }

        // ------------------------------------------------------------------ font

        private void EnsureFont()
        {
            try
            {
                if (_font != null && _font.Pointer != IntPtr.Zero && !_font.WasCollected)
                {
                    RegisterFallback(_font);
                    return;
                }
                if (_fontFailed) return;
                if (TMP_Settings.instance == null) return; // TMP not loaded yet, try again later

                foreach (var path in _cfg.ResolveFonts(_dir))
                {
                    if (!File.Exists(path)) continue;
                    _font = CreateFont(path);
                    if (_font == null) continue;
                    _fontPath = path;
                    break;
                }
                if (_font == null)
                {
                    _fontFailed = true;
                    RLog.Error("[ThaiLanguage] สร้างฟอนต์ไทยไม่สำเร็จ — ตัวอักษรไทยอาจแสดงเป็นสี่เหลี่ยม (ใส่ไฟล์ font.ttf ไว้ใน Mods\\ThaiLanguage เพื่อใช้ฟอนต์อื่น)");
                    return;
                }
                RegisterFallback(_font);
                RLog.Msg($"[ThaiLanguage] ฟอนต์ไทย: {_fontPath} (glyphs {_font.glyphTable.Count}, mark-to-base {_font.fontFeatureTable.MarkToBaseAdjustmentRecords.Count}, mark-to-mark {_font.fontFeatureTable.MarkToMarkAdjustmentRecords.Count})");
                RefreshAllTexts();
            }
            catch (Exception e)
            {
                LogOnce("EnsureFont", e);
                _fontFailed = true;
            }
        }

        private TMP_FontAsset CreateFont(string path)
        {
            try
            {
                // CreateFontAsset needs the "TextMeshPro/Mobile/Distance Field" shader. If the game build
                // does not include it under that name, borrow the shader of one of the game's own fonts.
                if (ShaderUtilities.ShaderRef_MobileSDF == null)
                {
                    var donor = FindDonorFont();
                    if (donor != null && donor.material != null) ShaderUtilities.k_ShaderRef_MobileSDF = donor.material.shader;
                }

                var font = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048);
                if (font == null) return null;
                font.name = "ThaiLanguage " + Path.GetFileNameWithoutExtension(path);
                font.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (font.material != null) font.material.hideFlags = HideFlags.DontUnloadUnusedAsset;
                font.getFontFeatures = true;
                try { TMP_Settings.instance.m_GetFontFeaturesAtRuntime = true; } catch (Exception e) { LogOnce("getFontFeaturesAtRuntime", e); }

                if (Math.Abs(_cfg.FontScale - 1f) > 0.001f)
                {
                    var face = font.faceInfo;
                    face.scale = _cfg.FontScale;
                    font.faceInfo = face;
                }

                font.TryAddCharacters(ThaiText.AllThaiCharacters(), true);
                foreach (var tex in font.atlasTextures)
                    if (tex != null) tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return font;
            }
            catch (Exception e)
            {
                RLog.Error($"[ThaiLanguage] โหลดฟอนต์ {path} ไม่ได้: {e.Message}");
                return null;
            }
        }

        private static TMP_FontAsset FindDonorFont()
        {
            var def = TMP_Settings.defaultFontAsset;
            if (def != null) return def;
            var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>());
            for (int i = 0; i < all.Length; i++)
            {
                var f = all[i].TryCast<TMP_FontAsset>();
                if (f != null && f.material != null) return f;
            }
            return null;
        }

        private void RegisterFallback(TMP_FontAsset font)
        {
            var list = TMP_Settings.fallbackFontAssets;
            if (list == null)
            {
                list = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                TMP_Settings.fallbackFontAssets = list;
            }
            if (!list.Contains(font))
            {
                list.Add(font);
                RLog.Msg("[ThaiLanguage] เพิ่มฟอนต์ไทยเป็นฟอนต์สำรองของ TextMeshPro แล้ว");
            }

            if (_cfg.MarkPositioning)
            {
                var features = TMP_Settings.fontFeatures; // used by text objects created at runtime
                if (features != null)
                {
                    if (!features.Contains(OTL_FeatureTag.mark)) features.Add(OTL_FeatureTag.mark);
                    if (!features.Contains(OTL_FeatureTag.mkmk)) features.Add(OTL_FeatureTag.mkmk);
                }
            }
        }

        internal static void EnsureMarkFeatures(TMP_Text text)
        {
            var features = text.m_ActiveFontFeatures;
            if (features == null) return;
            if (!features.Contains(OTL_FeatureTag.mark)) features.Add(OTL_FeatureTag.mark);
            if (!features.Contains(OTL_FeatureTag.mkmk)) features.Add(OTL_FeatureTag.mkmk);
        }

        // ------------------------------------------------------------------ UI texts

        private static List<TMP_Text> ActiveTexts()
        {
            var result = new List<TMP_Text>();
            var all = UObject.FindObjectsOfType(Il2CppType.Of<TMP_Text>());
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i].TryCast<TMP_Text>();
                if (t != null) result.Add(t);
            }
            return result;
        }

        /// <summary>Re-layout every visible text so glyphs that were missing before the font existed show up.</summary>
        private void RefreshAllTexts()
        {
            try
            {
                foreach (var t in ActiveTexts())
                {
                    if (_cfg.MarkPositioning) EnsureMarkFeatures(t);
                    if (ThaiText.ContainsThai(t.text)) t.ForceMeshUpdate(false, true);
                }
            }
            catch (Exception e) { LogOnce("RefreshAllTexts", e); }
        }

        /// <summary>
        /// Some UI labels are typed straight into the game's prefabs instead of using the localization tables.
        /// If such a label exactly matches an English string we know, swap in the Thai version.
        /// </summary>
        private void ScanTexts(bool force)
        {
            if (!_cfg.TranslateStaticTexts && !_cfg.WriteReports) return;
            try
            {
                var locale = LocalizationSettings.SelectedLocale;
                if (locale == null || !IsEnglish(locale.Identifier.Code)) return;

                foreach (var t in ActiveTexts())
                {
                    var s = t.text;
                    if (string.IsNullOrEmpty(s) || ThaiText.ContainsThai(s)) continue;
                    var key = s.Trim();
                    if (key.Length < 2) continue;

                    if (_cfg.TranslateStaticTexts &&
                        (_reverse.TryGetValue(key, out var thai) || _reverseUpper.TryGetValue(key.ToUpperInvariant(), out thai)))
                    {
                        if (t.GetComponentInParent(Il2CppType.Of<TMP_InputField>()) != null) continue;
                        t.text = thai;
                        continue;
                    }

                    if (_cfg.WriteReports && ThaiText.HasLatinWord(key) && _untranslated.Count < 3000 && _untranslated.Add(UiPath(t) + "\t" + key.Replace("\n", "\\n")))
                        _untranslatedDirty = true;
                }
            }
            catch (Exception e) { LogOnce("ScanTexts", e); }
        }

        private static string UiPath(TMP_Text t)
        {
            try
            {
                var names = new List<string>();
                var tr = t.transform;
                for (int i = 0; i < 4 && tr != null; i++, tr = tr.parent) names.Add(tr.name);
                names.Reverse();
                return string.Join("/", names);
            }
            catch { return "?"; }
        }

        // ------------------------------------------------------------------ reports

        private void WriteMissingReport()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# ข้อความภาษาอังกฤษในเกมที่ยังไม่มีคำแปลใน th.json (ตาราง<TAB>คีย์<TAB>ข้อความเดิม)");
                sb.AppendLine("# เพิ่มคำแปลได้ใน th.json ใต้ชื่อตารางเดียวกัน แล้วเปิดเกมใหม่");
                foreach (var line in _missing) sb.AppendLine(line);
                File.WriteAllText(System.IO.Path.Combine(_dir, "missing.txt"), sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e) { LogOnce("missing.txt", e); }
        }

        private void WriteUntranslatedReport()
        {
            if (!_untranslatedDirty) return;
            _untranslatedDirty = false;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# ข้อความภาษาอังกฤษที่พบบนหน้าจอ แต่ไม่ได้มาจากระบบแปลของเกม (ตำแหน่ง UI<TAB>ข้อความ)");
                sb.AppendLine("# ใช้ดูว่ายังเหลือข้อความไหนที่ mod ยังแปลไม่ได้");
                foreach (var line in _untranslated.OrderBy(x => x)) sb.AppendLine(line);
                File.WriteAllText(System.IO.Path.Combine(_dir, "untranslated.txt"), sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e) { LogOnce("untranslated.txt", e); }
        }

        internal static void LogOnce(string where, Exception e)
        {
            if (LoggedErrors.Add(where)) RLog.Error($"[ThaiLanguage] {where}: {e}");
        }

        // ------------------------------------------------------------------ debug (config.json: "debug": true)

        private GameObject _debugCanvas;

        private void DebugStep()
        {
            try
            {
                switch (_debugStep++)
                {
                    case 0:
                        _debugAt = Time.unscaledTime + 1f;
                        CreateDebugOverlay();
                        break;
                    case 1:
                        _debugAt = Time.unscaledTime + 1f;
                        var dir = System.IO.Path.Combine(_dir, "debug");
                        Directory.CreateDirectory(dir);
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "title.png"));
                        DumpTexts(System.IO.Path.Combine(dir, "texts.txt"));
                        break;
                    default:
                        _debugAt = -1f;
                        if (_debugCanvas != null) UObject.Destroy(_debugCanvas);
                        break;
                }
            }
            catch (Exception e) { LogOnce("Debug", e); _debugAt = -1f; }
        }

        private void CreateDebugOverlay()
        {
            TMP_FontAsset gameFont = null;
            foreach (var t in ActiveTexts()) { if (t.font != null) { gameFont = t.font; break; } }

            _debugCanvas = new GameObject("ThaiLanguageDebug");
            UObject.DontDestroyOnLoad(_debugCanvas);
            var canvas = _debugCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            var go = new GameObject("Sample");
            go.transform.SetParent(_debugCanvas.transform, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (gameFont != null) text.font = gameFont;
            text.fontSize = 40;
            text.color = new Color(1f, 0.85f, 0.2f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.text = ThaiText.InsertWordBreaks("ทดสอบ: ที่นี่ ชื่อ ป่า ญี่ปุ่น น้ำ ปั้น ผู้ใหญ่ สิ่งที่ ฎีกา ฐาน ปู่ กำลัง\n" +
                                                  "กดค้างเพื่อเปิดเมนูเลือกด่วน ความหนาวจะจำกัดค่าพลังกายสูงสุดของคุณ");
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0.05f, 0.02f);
            rt.anchorMax = new Vector2(0.95f, 0.25f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void DumpTexts(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"font={_fontPath} selected={LocalizationSettings.SelectedLocale?.Identifier.Code}");
            foreach (var t in ActiveTexts())
            {
                var f = t.m_ActiveFontFeatures;
                sb.AppendLine($"{UiPath(t)}\tfont={(t.font != null ? t.font.name : "null")}\tfeatures={(f != null ? f.Count : -1)}\t{t.text?.Replace("\n", "\\n")}");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        // ------------------------------------------------------------------ harmony

        private void InstallPatches()
        {
            // Translations are written straight into the loaded string tables (ApplyAllTables), so the game's
            // localization code itself is never patched. Only TMP needs a hook, to enable Thai mark positioning.
            var harmony = new HarmonyLib.Harmony("ThaiLanguage");
            if (_cfg.MarkPositioning)
            {
                TryPatch(harmony, typeof(TextMeshProUGUI), nameof(TextMeshProUGUI.GenerateTextMesh), nameof(Patches.UguiGenerateTextMeshPrefix));
                TryPatch(harmony, typeof(TextMeshPro), nameof(TextMeshPro.GenerateTextMesh), nameof(Patches.WorldGenerateTextMeshPrefix));
            }
        }

        private static void TryPatch(HarmonyLib.Harmony harmony, Type type, string method, string prefix)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null) { RLog.Warning($"[ThaiLanguage] ไม่พบเมธอด {type.Name}.{method}"); return; }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(Patches), prefix));
            }
            catch (Exception e)
            {
                RLog.Error($"[ThaiLanguage] patch {type.Name}.{method} ไม่สำเร็จ: {e.Message}");
            }
        }
    }
}
