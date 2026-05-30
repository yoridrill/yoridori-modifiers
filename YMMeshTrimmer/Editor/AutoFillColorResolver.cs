using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.MeshTrimmer
{
public static class AutoFillColorResolver
{
    private const string ToolName = "YM Mesh Trimmer";
    private const string DefaultConfigGuid = "913b6d45eecf30e8e5727b508b630201";
    private const string UserConfigPath = "Assets/NDMF-VRoid-Mesh-Trimmer-Setting.json";
    private const float AlphaThreshold = 0.05f;

    [Serializable]
    private class FillColorConfig
    {
        public int version;
        public FillColorRule[] fillColors;
        public PreSubdivideRule[] preSubdivide;
        public string trimAlgorithm;
    }

    [Serializable]
    private class FillColorRule
    {
        public string[] target;
        public string[] source;
        public float[] uv;
    }

    [Serializable]
    private class PreSubdivideRule
    {
        public string[] target;
        public int level;
        public bool quadAware;
    }

    private enum ConfigSource
    {
        None,
        User,
        Default
    }

    private struct ConfigLoadResult
    {
        public FillColorConfig config;
        public ConfigSource source;
        public string sourcePath;
    }

    public static void Apply(MeshTrimmerComponent trimmer, List<MeshTrimmerComponent.TextureTargetSettings> targets)
    {
        if (trimmer == null || targets == null || targets.Count == 0) return;
        bool verbose = trimmer.debugEdgeCrossingRoutes;

        var loadResult = LoadConfig();
        var config = loadResult.config;
        if (config == null)
        {
            LogUtility.Verbose(ToolName, verbose, "AutoConfig", "Config is empty or unavailable.");
            return;
        }

        LogUtility.Verbose(ToolName, verbose, "AutoConfig", $"FillColorConfigSource={loadResult.source}, Path={loadResult.sourcePath}");

        var materialMap = BuildMaterialMap(trimmer);
        var appliedTargets = new HashSet<MeshTrimmerComponent.TextureTargetSettings>();

        ApplyTrimAlgorithmRule(config, trimmer);

        ApplyPreSubdivideRules(config, targets, verbose);

        if (config.fillColors == null || config.fillColors.Length == 0)
        {
            LogUtility.Verbose(ToolName, verbose, "AutoConfig", "Fill-color config is empty or unavailable.");
            return;
        }

        foreach (var rule in config.fillColors)
        {
            if (rule == null || appliedTargets.Count == targets.Count) continue;
            if (!TryGetUV(rule, out var uv)) continue;

            if (!TryFindFirstTarget(targets, rule.target, appliedTargets, out var target)) continue;
            if (!TryFindFirstMaterial(materialMap, rule.source, out var sourceMaterial)) continue;
            if (!MaterialMainTextureResolver.TryGetMainTexture(sourceMaterial, out var sourceTexture, out _)) continue;
            if (!TrySampleFillColor(sourceTexture, uv, out var fillColor)) continue;

            target.texturePostProcessMode = MeshTrimmerComponent.TexturePostProcessMode.FillColor;
            target.fillColor = fillColor;
            appliedTargets.Add(target);

            string targetName = GetFirstUsageMaterialName(target);
            string texName = target.mainTexture != null ? target.mainTexture.name : "(None)";
            LogUtility.Verbose(ToolName, verbose, "AutoConfig", $"FillColorApplied TargetMaterial={targetName}, TargetTexture={texName}, SourceMaterial={sourceMaterial.name}, UV=({uv.x:F3}, {uv.y:F3}), Color=RGBA({fillColor.r:F3}, {fillColor.g:F3}, {fillColor.b:F3}, {fillColor.a:F3})");
        }
    }

    private static void ApplyPreSubdivideRules(FillColorConfig config, List<MeshTrimmerComponent.TextureTargetSettings> targets, bool verbose)
    {
        if (config.preSubdivide == null) return;
        int matched = 0;
        foreach (var target in targets)
        {
            if (target == null || target.usages == null) continue;
            target.enablePreSubdivide = false;
            target.preSubdivideLevel = 0;
            target.preSubdivideQuadAware = false;
            foreach (var rule in config.preSubdivide)
            {
                if (rule == null || rule.target == null) continue;
                if (!TryMatchTarget(target, rule.target)) continue;
                target.enablePreSubdivide = rule.level > 0;
                target.preSubdivideLevel = Mathf.Clamp(rule.level, 0, 2);
                target.preSubdivideQuadAware = rule.quadAware;
                matched++;
                LogUtility.Verbose(ToolName, verbose, "AutoConfig", $"PreSubdivideMatched TargetMaterial={GetFirstUsageMaterialName(target)}, Level={target.preSubdivideLevel}, QuadAware={target.preSubdivideQuadAware}");
                break;
            }
        }

        LogUtility.Verbose(ToolName, verbose, "AutoConfig", $"PreSubdivideRulesProcessed RuleCount={config.preSubdivide.Length}, MatchedTargetCount={matched}");
    }

    private static void ApplyTrimAlgorithmRule(FillColorConfig config, MeshTrimmerComponent trimmer)
    {
        if (trimmer == null || string.IsNullOrWhiteSpace(config.trimAlgorithm)) return;
        string mode = Normalize(config.trimAlgorithm);
        if (mode == "legacyinsidepoint")
        {
            trimmer.trimAlgorithm = MeshTrimmerComponent.TrimAlgorithm.LegacyInsidePoint;
        }
        else
        {
            trimmer.trimAlgorithm = MeshTrimmerComponent.TrimAlgorithm.EdgeCrossing;
        }
        LogUtility.Verbose(ToolName, trimmer.debugEdgeCrossingRoutes, "AutoConfig", $"TrimAlgorithm={trimmer.trimAlgorithm}");
    }

    private static bool TryMatchTarget(MeshTrimmerComponent.TextureTargetSettings settings, string[] targetCandidates)
    {
        foreach (var usage in settings.usages)
        {
            if (usage == null || usage.material == null) continue;
            string materialName = Normalize(usage.material.name);
            for (int i = 0; i < targetCandidates.Length; i++)
            {
                string candidate = Normalize(targetCandidates[i]);
                if (!string.IsNullOrEmpty(candidate) && materialName.Contains(candidate)) return true;
            }
        }
        return false;
    }

    private static Dictionary<string, Material> BuildMaterialMap(MeshTrimmerComponent trimmer)
    {
        var map = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var searchRoot = trimmer.GetComponentsInChildren<Renderer>(true).Length > 0
            ? trimmer.gameObject
            : PreviewCoordinator.FindAvatarRoot(trimmer.gameObject) ?? trimmer.gameObject;
        var renderers = searchRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            var mats = renderer.sharedMaterials;
            if (mats == null) continue;
            foreach (var mat in mats)
            {
                if (mat == null) continue;
                string key = Normalize(mat.name);
                if (string.IsNullOrEmpty(key) || map.ContainsKey(key)) continue;
                map[key] = mat;
            }
        }

        return map;
    }

    private static bool TryFindFirstMaterial(Dictionary<string, Material> map, string[] candidates, out Material material)
    {
        material = null;
        if (candidates == null) return false;
        foreach (var candidate in candidates)
        {
            string key = Normalize(candidate);
            if (string.IsNullOrEmpty(key)) continue;

            foreach (var pair in map)
            {
                if (pair.Key.Contains(key))
                {
                    material = pair.Value;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindFirstTarget(
        List<MeshTrimmerComponent.TextureTargetSettings> targets,
        string[] targetCandidates,
        HashSet<MeshTrimmerComponent.TextureTargetSettings> alreadyApplied,
        out MeshTrimmerComponent.TextureTargetSettings target)
    {
        target = null;
        if (targetCandidates == null) return false;

        foreach (var settings in targets)
        {
            if (settings == null || alreadyApplied.Contains(settings) || settings.usages == null) continue;
            foreach (var usage in settings.usages)
            {
                if (usage == null || usage.material == null) continue;
                string materialName = Normalize(usage.material.name);
                for (int i = 0; i < targetCandidates.Length; i++)
                {
                    if (materialName.Contains(Normalize(targetCandidates[i])))
                    {
                        target = settings;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TryGetUV(FillColorRule rule, out Vector2 uv)
    {
        uv = new Vector2(0.5f, 0.5f);
        if (rule.uv == null || rule.uv.Length != 2) return false;
        uv.x = rule.uv[0];
        uv.y = rule.uv[1];
        return true;
    }

    private static bool TrySampleFillColor(Texture2D texture, Vector2 uv, out Color fillColor)
    {
        fillColor = Color.black;
        Color[] pixels;
        Texture2D readable = null;
        try
        {
            readable = TextureReadUtility.ToReadableTexture(texture);
            pixels = readable.GetPixels();
        }
        catch (Exception ex) when (ex is UnityException || ex is ArgumentException)
        {
            return false;
        }
        finally
        {
            if (readable != null && readable != texture)
            {
                UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        int width = texture.width;
        int height = texture.height;
        if (width <= 0 || height <= 0 || pixels == null || pixels.Length != width * height) return false;

        int x = WrapToPixelIndex(uv.x, width, texture.wrapModeU, texture.wrapMode);
        int y = WrapToPixelIndex(uv.y, height, texture.wrapModeV, texture.wrapMode);
        int index = y * width + x;
        var sampled = pixels[index];

        if (sampled.a < AlphaThreshold)
        {
            if (!TryFindNearestOpaquePixel(pixels, width, height, x, y, out sampled))
            {
                return false;
            }
        }

        sampled.a = 1f;
        fillColor = sampled;
        return true;
    }

    private static int WrapToPixelIndex(float coord, int size, TextureWrapMode axisWrap, TextureWrapMode fallback)
    {
        var wrap = axisWrap;
#if !UNITY_2021_1_OR_NEWER
        wrap = fallback;
#endif
        float normalized;
        if (wrap == TextureWrapMode.Repeat || wrap == TextureWrapMode.Mirror)
        {
            normalized = coord - Mathf.Floor(coord);
        }
        else
        {
            normalized = Mathf.Clamp01(coord);
        }

        return Mathf.Clamp(Mathf.FloorToInt(normalized * (size - 1)), 0, size - 1);
    }

    private static bool TryFindNearestOpaquePixel(Color[] pixels, int width, int height, int sx, int sy, out Color color)
    {
        color = default;
        float bestDistance = float.MaxValue;
        bool found = false;

        for (int y = 0; y < height; y++)
        {
            int dy = y - sy;
            for (int x = 0; x < width; x++)
            {
                var c = pixels[y * width + x];
                if (c.a < AlphaThreshold) continue;
                int dx = x - sx;
                float dist = dx * dx + dy * dy;
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    color = c;
                    found = true;
                }
            }
        }

        return found;
    }

    private static ConfigLoadResult LoadConfig()
    {
        var result = new ConfigLoadResult { source = ConfigSource.None, sourcePath = "(none)", config = null };

        string json = TryLoadUserJson(out string userPath);
        if (!string.IsNullOrWhiteSpace(json))
        {
            result.source = ConfigSource.User;
            result.sourcePath = userPath;
            result.config = ParseConfig(json, userPath);
            return result;
        }

        json = TryLoadDefaultJson(out string defaultPath);
        if (!string.IsNullOrWhiteSpace(json))
        {
            result.source = ConfigSource.Default;
            result.sourcePath = defaultPath;
            result.config = ParseConfig(json, defaultPath);
            return result;
        }

        LogUtility.Warning(ToolName, "AutoConfig", "Fill-color config not found. Checked user and default JSON.");
        return result;
    }

    private static FillColorConfig ParseConfig(string json, string path)
    {
        FillColorConfig config = null;
        try
        {
            config = JsonUtility.FromJson<FillColorConfig>(json);
        }
        catch (Exception ex)
        {
            LogUtility.Warning(ToolName, "AutoConfig", "Failed to parse fill-color config JSON: " + ex.Message + " (" + path + ")");
            return null;
        }

        if (config == null || config.fillColors == null)
        {
            LogUtility.Warning(ToolName, "AutoConfig", "Failed to parse fill-color config JSON: deserialized object was null or missing fillColors. (" + path + ")");
            return null;
        }

        return config;
    }

    private static string TryLoadUserJson(out string path)
    {
        path = UserConfigPath;
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(UserConfigPath);
        return asset != null ? asset.text : null;
    }

    private static string TryLoadDefaultJson(out string path)
    {
        path = AssetDatabase.GUIDToAssetPath(DefaultConfigGuid);
        if (string.IsNullOrWhiteSpace(path)) return null;
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        return asset != null ? asset.text : null;
    }

    private static string GetFirstUsageMaterialName(MeshTrimmerComponent.TextureTargetSettings target)
    {
        if (target == null || target.usages == null) return "(Unknown)";
        foreach (var usage in target.usages)
        {
            if (usage != null && usage.material != null) return usage.material.name;
        }

        return "(Unknown)";
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}

}
