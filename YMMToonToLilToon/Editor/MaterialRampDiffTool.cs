using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MaterialRampDiffTool
{
    [MenuItem("Tools/Debug/Compare Selected Materials Ramp State")]
    private static void CompareSelectedMaterials()
    {
        var materials = Selection.objects.OfType<Material>().ToArray();
        if (materials.Length != 2)
        {
            Debug.LogError("比較したい Material を2つ選択してください。1つ目=手動Realistic、2つ目=自動生成を想定しています。");
            return;
        }

        Compare(materials[0], materials[1]);
    }

    private static void Compare(Material a, Material b)
    {
        Debug.Log($"=== Compare Materials ===");
        Debug.Log($"A: {AssetDatabase.GetAssetPath(a)} / {a.name} / shader={a.shader?.name}");
        Debug.Log($"B: {AssetDatabase.GetAssetPath(b)} / {b.name} / shader={b.shader?.name}");

        DumpBasic(a, "A");
        DumpBasic(b, "B");

        CompareKeywords(a, b);
        CompareSavedProperties(a, b, "m_TexEnvs", "Texture", IsInterestingName);
        CompareSavedProperties(a, b, "m_Floats", "Float", IsInterestingName);
        CompareSavedProperties(a, b, "m_Colors", "Color", IsInterestingName);
    }

    private static void DumpBasic(Material m, string label)
    {
        Debug.Log($"--- {label} Basic ---");
        Debug.Log($"{label} VRCFallback = {m.GetTag("VRCFallback", false, "<none>")}");
        Debug.Log($"{label} Has _Ramp = {m.HasProperty("_Ramp")}");
        Debug.Log($"{label} _Ramp = {m.GetTexture("_Ramp")}");
        Debug.Log($"{label} _Ramp path = {AssetDatabase.GetAssetPath(m.GetTexture("_Ramp"))}");
        Debug.Log($"{label} renderQueue = {m.renderQueue}");
        Debug.Log($"{label} enableInstancing = {m.enableInstancing}");
        Debug.Log($"{label} globalIlluminationFlags = {m.globalIlluminationFlags}");
    }

    private static bool IsInterestingName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var n = name.ToLowerInvariant();

        return n.Contains("ramp")
            || n.Contains("fallback")
            || n.Contains("shadow")
            || n.Contains("shade")
            || n.Contains("toon")
            || n.Contains("receive")
            || n.Contains("light");
    }

    private static void CompareKeywords(Material a, Material b)
    {
        Debug.Log("--- Keywords diff ---");

        var ak = new HashSet<string>(a.shaderKeywords ?? new string[0]);
        var bk = new HashSet<string>(b.shaderKeywords ?? new string[0]);

        foreach (var k in ak.Except(bk).OrderBy(x => x))
        {
            Debug.Log($"Only A keyword: {k}");
        }

        foreach (var k in bk.Except(ak).OrderBy(x => x))
        {
            Debug.Log($"Only B keyword: {k}");
        }

        if (ak.SetEquals(bk))
        {
            Debug.Log("Keywords: no diff");
        }
    }

    private static void CompareSavedProperties(
        Material a,
        Material b,
        string propertyArrayName,
        string label,
        System.Func<string, bool> filter)
    {
        Debug.Log($"--- {label} diff: {propertyArrayName} ---");

        var ad = ReadSavedProperties(a, propertyArrayName, label, filter);
        var bd = ReadSavedProperties(b, propertyArrayName, label, filter);

        var keys = new HashSet<string>(ad.Keys);
        keys.UnionWith(bd.Keys);

        var any = false;
        foreach (var key in keys.OrderBy(x => x))
        {
            ad.TryGetValue(key, out var av);
            bd.TryGetValue(key, out var bv);

            if (av == bv) continue;

            any = true;
            Debug.Log($"{label} {key}\n  A = {av}\n  B = {bv}");
        }

        if (!any)
        {
            Debug.Log($"{label}: no interesting diff");
        }
    }

    private static Dictionary<string, string> ReadSavedProperties(
        Material material,
        string propertyArrayName,
        string label,
        System.Func<string, bool> filter)
    {
        var result = new Dictionary<string, string>();
        var so = new SerializedObject(material);
        var prop = so.FindProperty($"m_SavedProperties.{propertyArrayName}");

        if (prop == null || !prop.isArray)
        {
            return result;
        }

        for (var i = 0; i < prop.arraySize; i++)
        {
            var elem = prop.GetArrayElementAtIndex(i);
            var name = elem.FindPropertyRelative("first")?.stringValue;
            if (!filter(name)) continue;

            var second = elem.FindPropertyRelative("second");
            result[name] = SerializeSecondValue(second, label);
        }

        return result;
    }

    private static string SerializeSecondValue(SerializedProperty second, string label)
    {
        if (second == null) return "<null>";

        if (label == "Texture")
        {
            var tex = second.FindPropertyRelative("m_Texture")?.objectReferenceValue;
            var scale = second.FindPropertyRelative("m_Scale")?.vector2Value ?? Vector2.zero;
            var offset = second.FindPropertyRelative("m_Offset")?.vector2Value ?? Vector2.zero;

            return tex == null
                ? "null"
                : $"{tex.name} [{AssetDatabase.GetAssetPath(tex)}], scale={scale}, offset={offset}";
        }

        if (label == "Float")
        {
            return second.floatValue.ToString("R");
        }

        if (label == "Color")
        {
            return second.colorValue.ToString();
        }

        return second.ToString();
    }
}