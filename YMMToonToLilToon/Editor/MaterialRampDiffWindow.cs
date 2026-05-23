using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class MaterialRampDiffWindow : EditorWindow
{
    private Material _manualMaterial;
    private Material _generatedMaterial;
    private bool _showAllTextures;
    private bool _showAllFloats;
    private bool _showAllColors;

    [MenuItem("Tools/Debug/Material Ramp Diff Window")]
    private static void Open()
    {
        var window = GetWindow<MaterialRampDiffWindow>("Material Ramp Diff");
        window.minSize = new Vector2(420f, 220f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Compare lilToon / Toon Standard Fallback State", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        _manualMaterial = (Material)EditorGUILayout.ObjectField(
            "Manual / Reference",
            _manualMaterial,
            typeof(Material),
            false);

        _generatedMaterial = (Material)EditorGUILayout.ObjectField(
            "Generated / Target",
            _generatedMaterial,
            typeof(Material),
            false);

        EditorGUILayout.Space(8f);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Diff Options", EditorStyles.boldLabel);
            _showAllTextures = EditorGUILayout.ToggleLeft("Show all texture properties", _showAllTextures);
            _showAllFloats = EditorGUILayout.ToggleLeft("Show all float properties", _showAllFloats);
            _showAllColors = EditorGUILayout.ToggleLeft("Show all color properties", _showAllColors);
        }

        EditorGUILayout.Space(8f);

        using (new EditorGUI.DisabledScope(_manualMaterial == null || _generatedMaterial == null))
        {
            if (GUILayout.Button("Compare", GUILayout.Height(28f)))
            {
                Compare(_manualMaterial, _generatedMaterial);
            }

            if (GUILayout.Button("Dump Manual Only"))
            {
                DumpSingle(_manualMaterial, "Manual");
            }

            if (GUILayout.Button("Dump Generated Only"))
            {
                DumpSingle(_generatedMaterial, "Generated");
            }
        }

        EditorGUILayout.Space(8f);

        EditorGUILayout.HelpBox(
            "Manual / Reference に手動で Realistic を設定した Material、Generated / Target にツール生成 Material を指定してください。結果は Console に出ます。",
            MessageType.Info);
    }

    private void Compare(Material a, Material b)
    {
        Debug.Log("=== Compare Materials ===");
        Debug.Log($"A Manual: {DescribeMaterial(a)}");
        Debug.Log($"B Generated: {DescribeMaterial(b)}");

        DumpBasic(a, "A");
        DumpBasic(b, "B");

        CompareKeywords(a, b);

        CompareSavedProperties(
            a,
            b,
            "m_TexEnvs",
            "Texture",
            _showAllTextures ? (_ => true) : IsInterestingName);

        CompareSavedProperties(
            a,
            b,
            "m_Floats",
            "Float",
            _showAllFloats ? (_ => true) : IsInterestingName);

        CompareSavedProperties(
            a,
            b,
            "m_Colors",
            "Color",
            _showAllColors ? (_ => true) : IsInterestingName);
    }

    private void DumpSingle(Material material, string label)
    {
        Debug.Log($"=== Dump {label} Material ===");
        Debug.Log($"{label}: {DescribeMaterial(material)}");

        DumpBasic(material, label);
        DumpKeywords(material, label);

        DumpSavedProperties(
            material,
            label,
            "m_TexEnvs",
            "Texture",
            _showAllTextures ? (_ => true) : IsInterestingName);

        DumpSavedProperties(
            material,
            label,
            "m_Floats",
            "Float",
            _showAllFloats ? (_ => true) : IsInterestingName);

        DumpSavedProperties(
            material,
            label,
            "m_Colors",
            "Color",
            _showAllColors ? (_ => true) : IsInterestingName);
    }

    private static string DescribeMaterial(Material m)
    {
        if (m == null) return "<null>";

        return $"{AssetDatabase.GetAssetPath(m)} / {m.name} / shader={m.shader?.name}";
    }

    private static void DumpBasic(Material m, string label)
    {
        if (m == null)
        {
            Debug.Log($"{label}: <null>");
            return;
        }

        Debug.Log($"--- {label} Basic ---");
        Debug.Log($"{label} VRCFallback = {m.GetTag("VRCFallback", false, "<none>")}");
        Debug.Log($"{label} Has _Ramp = {m.HasProperty("_Ramp")}");
        Debug.Log($"{label} _Ramp = {m.GetTexture("_Ramp")}");
        Debug.Log($"{label} _Ramp path = {AssetDatabase.GetAssetPath(m.GetTexture("_Ramp"))}");
        Debug.Log($"{label} renderQueue = {m.renderQueue}");
        Debug.Log($"{label} enableInstancing = {m.enableInstancing}");
        Debug.Log($"{label} globalIlluminationFlags = {m.globalIlluminationFlags}");
        Debug.Log($"{label} instanceID = {m.GetInstanceID()}");
        Debug.Log($"{label} assetPath = {AssetDatabase.GetAssetPath(m)}");
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
            || n.Contains("light")
            || n.Contains("custom")
            || n.Contains("safety")
            || n.Contains("vrc");
    }

    private static void CompareKeywords(Material a, Material b)
    {
        Debug.Log("--- Keywords diff ---");

        var ak = new HashSet<string>(a != null && a.shaderKeywords != null ? a.shaderKeywords : new string[0]);
        var bk = new HashSet<string>(b != null && b.shaderKeywords != null ? b.shaderKeywords : new string[0]);

        var any = false;

        foreach (var k in ak.Except(bk).OrderBy(x => x))
        {
            any = true;
            Debug.Log($"Only A keyword: {k}");
        }

        foreach (var k in bk.Except(ak).OrderBy(x => x))
        {
            any = true;
            Debug.Log($"Only B keyword: {k}");
        }

        if (!any)
        {
            Debug.Log("Keywords: no diff");
        }
    }

    private static void DumpKeywords(Material material, string label)
    {
        Debug.Log($"--- {label} Keywords ---");

        if (material == null || material.shaderKeywords == null || material.shaderKeywords.Length == 0)
        {
            Debug.Log($"{label} Keywords: none");
            return;
        }

        foreach (var keyword in material.shaderKeywords.OrderBy(x => x))
        {
            Debug.Log($"{label} Keyword: {keyword}");
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
            Debug.Log($"{label} {key}\n  A = {av ?? "<missing>"}\n  B = {bv ?? "<missing>"}");
        }

        if (!any)
        {
            Debug.Log($"{label}: no diff");
        }
    }

    private static void DumpSavedProperties(
        Material material,
        string ownerLabel,
        string propertyArrayName,
        string label,
        System.Func<string, bool> filter)
    {
        Debug.Log($"--- {ownerLabel} {label}: {propertyArrayName} ---");

        var values = ReadSavedProperties(material, propertyArrayName, label, filter);

        if (values.Count == 0)
        {
            Debug.Log($"{ownerLabel} {label}: no entries");
            return;
        }

        foreach (var kv in values.OrderBy(kv => kv.Key))
        {
            Debug.Log($"{ownerLabel} {label} {kv.Key} = {kv.Value}");
        }
    }

    private static Dictionary<string, string> ReadSavedProperties(
        Material material,
        string propertyArrayName,
        string label,
        System.Func<string, bool> filter)
    {
        var result = new Dictionary<string, string>();

        if (material == null) return result;

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
                ? $"null, scale={scale}, offset={offset}"
                : $"{tex.name} [{AssetDatabase.GetAssetPath(tex)}], scale={scale}, offset={offset}";
        }

        if (label == "Float")
        {
            return second.floatValue.ToString("R");
        }

        if (label == "Color")
        {
            var c = second.colorValue;
            return $"RGBA({c.r:R}, {c.g:R}, {c.b:R}, {c.a:R})";
        }

        return second.ToString();
    }
}