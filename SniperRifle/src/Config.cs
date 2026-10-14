using System.IO;
using System.Text.Json;
using UnityEngine;

namespace SniperRifle
{
    /// <summary>Settings from Mods\SniperRifle\config.json (all optional).</summary>
    internal sealed class Config
    {
        public string ItemName = "ปืนสไนเปอร์ไม้";
        public string ItemDescription = "สไนเปอร์ทำเองจากกิ่งไม้และก้อนหิน ใช้กระสุน 9 มม. (ดาเมจเท่าปืนพก) คลิกขวาค้างเพื่อส่องกล้อง";
        public int Sticks = 10;
        public int SmallRocks = 10;
        public int MagazineSize = 5;
        public int AmmoRockInput = 1;          // ก้อนหิน (ก้อนใหญ่) กี่ก้อน
        public int AmmoRockOutput = 20;        // ได้กระสุน 9 มม. กี่นัด (0 = ปิดสูตรนี้)
        public int AmmoLeafInput = 3;          // ใบไม้กี่ใบ
        public int AmmoLeafOutput = 20;        // ได้กระสุน 9 มม. กี่นัด (0 = ปิดสูตรนี้)
        public float ScopeFov = 12f;           // องศา ยิ่งน้อยยิ่งซูมไกล (ปกติเกมประมาณ 70)
        public float ScopeNearClip = 1.2f;     // ซ่อนแขน/ตัวปืนที่อยู่ใกล้กล้องตอนส่อง
        public bool ScopeAimCorrection = true; // ส่องกล้องแล้วกระสุนไปตรงกลางเป้า (ชดเชยกระสุนตก)
        public bool ShowScopeModel = true;
        public Vector3 ScopeScale = new Vector3(1.25f, 1.25f, 1.8f); // ขยายกล้องบนตัวปืน (กว้าง, สูง, ยาว)
        public Vector3 Tint = new Vector3(0.80f, 0.62f, 0.42f);     // สีโทนไม้ของตัวปืน (1,1,1 = สีเดิม)
        public Vector3 InventoryOffset = new Vector3(-0.52f, 0f, 0f);
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
                var v = p.Value;
                switch (p.Name.ToLowerInvariant())
                {
                    case "itemname": cfg.ItemName = v.GetString(); break;
                    case "itemdescription": cfg.ItemDescription = v.GetString(); break;
                    case "sticks": cfg.Sticks = Mathf.Clamp(v.GetInt32(), 1, 50); break;
                    case "smallrocks": cfg.SmallRocks = Mathf.Clamp(v.GetInt32(), 0, 50); break;
                    case "magazinesize": cfg.MagazineSize = Mathf.Clamp(v.GetInt32(), 1, 30); break;
                    case "ammorockinput": cfg.AmmoRockInput = Mathf.Clamp(v.GetInt32(), 0, 20); break;
                    case "ammorockoutput": cfg.AmmoRockOutput = Mathf.Clamp(v.GetInt32(), 0, 100); break;
                    case "ammoleafinput": cfg.AmmoLeafInput = Mathf.Clamp(v.GetInt32(), 0, 20); break;
                    case "ammoleafoutput": cfg.AmmoLeafOutput = Mathf.Clamp(v.GetInt32(), 0, 100); break;
                    case "scopefov": cfg.ScopeFov = Mathf.Clamp((float)v.GetDouble(), 2f, 60f); break;
                    case "scopenearclip": cfg.ScopeNearClip = Mathf.Clamp((float)v.GetDouble(), 0.01f, 5f); break;
                    case "scopeaimcorrection": cfg.ScopeAimCorrection = v.GetBoolean(); break;
                    case "showscopemodel": cfg.ShowScopeModel = v.GetBoolean(); break;
                    case "scopescale": cfg.ScopeScale = Vec(v, cfg.ScopeScale); break;
                    case "tint": cfg.Tint = Vec(v, cfg.Tint); break;
                    case "inventoryoffset": cfg.InventoryOffset = Vec(v, cfg.InventoryOffset); break;
                    case "debug": cfg.Debug = v.GetBoolean(); break;
                }
            }
            return cfg;
        }

        private static Vector3 Vec(JsonElement v, Vector3 fallback) =>
            v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3
                ? new Vector3((float)v[0].GetDouble(), (float)v[1].GetDouble(), (float)v[2].GetDouble())
                : fallback;
    }
}
