using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace SniperRifle
{
    /// <summary>Debug helper: writes the serialized fields of IL2CPP objects and transform hierarchies as text.</summary>
    internal static class Dump
    {
        internal static string Fields(object obj, int depth = 1, string indent = "")
        {
            var sb = new StringBuilder();
            WriteFields(sb, obj, depth, indent, new HashSet<IntPtr>());
            return sb.ToString();
        }

        private static void WriteFields(StringBuilder sb, object obj, int depth, string indent, HashSet<IntPtr> seen)
        {
            if (obj == null) { sb.AppendLine(indent + "null"); return; }
            if (obj is Il2CppObjectBase ib)
            {
                if (!seen.Add(ib.Pointer)) { sb.AppendLine(indent + "(seen)"); return; }
            }
            var type = obj.GetType();
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                // interop exposes serialized fields as properties; skip Unity/Il2Cpp plumbing
                if (p.DeclaringType == typeof(Il2CppObjectBase) || p.DeclaringType == typeof(UObject) ||
                    p.DeclaringType == typeof(Component) || p.DeclaringType == typeof(Behaviour) ||
                    p.DeclaringType == typeof(MonoBehaviour) || p.DeclaringType == typeof(ScriptableObject) ||
                    p.DeclaringType?.FullName == "Il2CppSystem.Object") continue;
                if (!(p.Name.StartsWith("_") || p.Name.StartsWith("m_") || char.IsLower(p.Name[0]))) continue;
                object v;
                try { v = p.GetValue(obj); }
                catch (Exception e) { sb.AppendLine($"{indent}{p.Name} = <{e.InnerException?.GetType().Name ?? e.GetType().Name}>"); continue; }
                sb.AppendLine($"{indent}{p.Name} = {Describe(v)}");
                if (depth > 0 && v != null && IsExpandable(v)) WriteFields(sb, v, depth - 1, indent + "    ", seen);
                if (depth > 0 && v != null) WriteList(sb, v, depth - 1, indent + "    ", seen);
            }
        }

        private static void WriteList(StringBuilder sb, object v, int depth, string indent, HashSet<IntPtr> seen)
        {
            var t = v.GetType();
            var countProp = t.GetProperty("Count") ?? t.GetProperty("Length");
            var indexer = t.GetProperty("Item");
            if (countProp == null || indexer == null || !(v is Il2CppObjectBase)) return;
            int count;
            try { count = Convert.ToInt32(countProp.GetValue(v)); } catch { return; }
            for (int i = 0; i < Math.Min(count, 12); i++)
            {
                object e;
                try { e = indexer.GetValue(v, new object[] { i }); } catch { break; }
                sb.AppendLine($"{indent}[{i}] = {Describe(e)}");
                if (depth > 0 && e != null && IsExpandable(e)) WriteFields(sb, e, depth - 1, indent + "    ", seen);
            }
        }

        private static bool IsExpandable(object v)
        {
            if (!(v is Il2CppObjectBase)) return false;
            if (v is UObject) return v is ScriptableObject; // expand data assets, not scene objects
            var n = v.GetType().FullName ?? "";
            return !n.StartsWith("Il2CppSystem.Collections") && !n.StartsWith("Il2CppInterop");
        }

        internal static string Describe(object v)
        {
            if (v == null) return "null";
            try
            {
                if (v is UObject uo)
                {
                    if (uo == null) return "null(destroyed)";
                    var tn = uo.GetIl2CppType().FullName;
                    if (uo is Component c) return $"{tn} '{PathOf(c.transform)}'";
                    if (uo is GameObject go) return $"GameObject '{PathOf(go.transform)}' scene='{go.scene.name}' active={go.activeSelf}";
                    return $"{tn} '{uo.name}'";
                }
                if (v is Il2CppObjectBase ib)
                {
                    var t = v.GetType();
                    var cnt = t.GetProperty("Count") ?? t.GetProperty("Length");
                    if (cnt != null) return $"{t.Name} (count {cnt.GetValue(v)})";
                    return t.FullName;
                }
                return v.ToString();
            }
            catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        internal static string PathOf(Transform t)
        {
            var names = new List<string>();
            for (int i = 0; t != null && i < 12; i++, t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        internal static string Hierarchy(Transform root, int maxDepth)
        {
            var sb = new StringBuilder();
            WriteNode(sb, root, 0, maxDepth);
            return sb.ToString();
        }

        private static void WriteNode(StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            var comps = t.GetComponents(Il2CppType.Of<Component>());
            var names = new List<string>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                var n = c.GetIl2CppType().Name;
                if (n != "Transform") names.Add(n);
            }
            sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (off)")} [{string.Join(", ", names)}] lp={t.localPosition} lr={t.localEulerAngles} ls={t.localScale}");
            if (depth >= maxDepth) return;
            for (int i = 0; i < t.childCount; i++) WriteNode(sb, t.GetChild(i), depth + 1, maxDepth);
        }
    }
}
