using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.MToonToLilToon
{
    internal static class MToonToLilToonProcessor
    {
        private const string ToolName = "YM MToon to lilToon";

        internal enum ConversionRoute
        {
            Preview,
            Build,
        }

        private static GameObject ResolveProcessingRoot(MToonToLilToonComponent component)
        {
            if (component == null) return null;

            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            return avatarRoot != null ? avatarRoot : component.gameObject;
        }

        internal static void ApplyGlobalOverridesToConvertedMaterials(
            MToonToLilToonComponent component,
            LilToonGlobalOverrides overrides,
            bool disableShadowReceiveForFace = false,
            bool disableRimShadeForFace = false,
            bool disableBacklightStrengthForFace = false)
        {
            if (component == null || overrides == null) return;

            var materials = ResolveProcessingRoot(component).GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer != null ? renderer.sharedMaterials : System.Array.Empty<Material>())
                .Where(material => material != null
                    && material.shader != null
                    && material.shader.name.IndexOf("liltoon", System.StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct()
                .ToList();

            for (var i = 0; i < materials.Count; i++)
            {
                MToonToLilToonMapper.ApplyGlobalOverridesToMaterial(materials[i], overrides);
            }

            ApplyBacklightExclusionToMouthMaterials(materials);

            if (!disableShadowReceiveForFace && !disableRimShadeForFace && !disableBacklightStrengthForFace) return;

            var faceMaterial = ResolveCurrentMaterialReference(component.faceShadowFaceMaterial, materials);
            ApplyFaceGlobalExclusionSettings(
                faceMaterial,
                disableShadowReceiveForFace,
                disableRimShadeForFace,
                disableBacklightStrengthForFace);
        }

        internal static void ApplyOnBuild(
            MToonToLilToonComponent component,
            System.Action<string> onProgress = null,
            ConversionRoute route = ConversionRoute.Build,
            BuildContext buildContext = null,
            AnimatorServicesContext animatorServices = null)
        {
            if (component == null) return;
            var processingRoot = ResolveProcessingRoot(component);
            if (processingRoot == null) return;
            var currentMaterials = CollectCurrentMaterials(processingRoot);
            var faceShadowFaceMaterial = ResolveCurrentMaterialReference(component.faceShadowFaceMaterial, currentMaterials);
            var silhouetteClothingMaterial = ResolveCurrentMaterialReference(component.silhouetteClothingMaterial, currentMaterials);
            var silhouetteBodyMaterial = ResolveCurrentMaterialReference(component.silhouetteBodyMaterial, currentMaterials);

            if (component.isPreviewing)
            {
                component.isPreviewing = false;
                if (component.verboseLog)
                {
                    LogUtility.Warning(ToolName, "Preview state was active on this component and has been reset before conversion.", component);
                }
            }

            var report = new ConversionReport();
            var lilToonShader = ResolveLilToonShader(component);
            if (lilToonShader == null)
            {
                component.warnings = new List<string> { "lilToon shader was not found in this project. Conversion skipped." };
                component.scannedMaterialCount = 0;
                component.convertedMaterialCount = 0;
                component.skippedMaterialCount = 0;
                component.unsupportedProperties = new List<string>();
                if (component.verboseLog)
                {
                    LogUtility.Warning(ToolName, "lilToon shader was not found. Conversion skipped.", component);
                }
                return;
            }

            var convertedBySource = new Dictionary<Material, Material>();
            onProgress?.Invoke("Converting materials...");
            foreach (var renderer in processingRoot.GetComponentsInChildren<Renderer>(true))
            {
                ProcessRenderer(
                    renderer,
                    lilToonShader,
                    component.globalOverrides,
                    component.useToonStandardFallback,
                    convertedBySource,
                    component.verboseLog,
                    report,
                    buildContext);
            }

            var resolvedFaceShadowMaterial = faceShadowFaceMaterial != null
                ? (convertedBySource.TryGetValue(faceShadowFaceMaterial, out var convertedFaceShadow)
                    ? convertedFaceShadow
                    : faceShadowFaceMaterial)
                : null;

            if (resolvedFaceShadowMaterial != null && component.enableFaceShadowTuning)
            {
                ApplyFaceShadowMaskSettings(
                    resolvedFaceShadowMaterial,
                    component.faceShadowSdfTexture,
                    component.faceShadowMaskType,
                    component.shadowStrengthMaskLod);
            }

            if (component.disableShadowReceiveForFace || component.disableRimShadeForFace || component.disableBacklightStrengthForFace)
            {
                ApplyFaceGlobalExclusionSettings(
                    resolvedFaceShadowMaterial,
                    component.disableShadowReceiveForFace,
                    component.disableRimShadeForFace,
                    component.disableBacklightStrengthForFace);
            }

            if (component.useToonStandardFallback)
            {
                ApplyToonStandardFallbackRampToMaterials(convertedBySource.Values);
            }

            ApplyBacklightExclusionToMouthMaterials(convertedBySource.Values);

            var shouldApplySilhouetteTransparency = component.enableSilhouetteTransparency
                && (route == ConversionRoute.Preview
                    || MToonToLilToonBuildTargetUtility.IsPcBuildTarget(processingRoot));
            if (shouldApplySilhouetteTransparency)
            {
                var resolvedClothingMaterial = ResolveConvertedMaterial(silhouetteClothingMaterial, convertedBySource);
                var resolvedBodyMaterial = ResolveConvertedMaterial(silhouetteBodyMaterial, convertedBySource);
                var canModifyBodyMaterial = resolvedBodyMaterial != null
                    && resolvedBodyMaterial != silhouetteBodyMaterial;
                onProgress?.Invoke("Creating silhouette transparency materials...");
                SilhouetteTransparencyProcessor.Apply(
                    processingRoot,
                    resolvedClothingMaterial,
                    resolvedBodyMaterial,
                    canModifyBodyMaterial,
                    component.silhouetteShadowColor,
                    component.silhouetteOpacity,
                    component.useSilhouetteRefractionBlur,
                    component.silhouetteBlur,
                    report,
                    buildContext,
                    animatorServices);
            }

            component.scannedMaterialCount = report.ScannedMaterialCount;
            component.convertedMaterialCount = report.ConvertedMaterialCount;
            component.skippedMaterialCount = report.SkippedMaterialCount;
            component.warnings = report.Warnings.Select(w => w.Message).ToList();
            component.unsupportedProperties = report.UnsupportedPropertySummary.Select(kv => $"{kv.Key}:{kv.Value}").ToList();
            ValidateRendererMaterialTextureReferencesBeforeAao(component, report);
            LogVerboseReportIfNeeded(component, report);
        }

        private static IReadOnlyList<Material> CollectCurrentMaterials(GameObject processingRoot)
        {
            if (processingRoot == null) return System.Array.Empty<Material>();

            return processingRoot.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer != null ? renderer.sharedMaterials : System.Array.Empty<Material>())
                .Where(material => material != null)
                .Distinct()
                .ToList();
        }

        private static Material ResolveCurrentMaterialReference(Material configuredMaterial, IReadOnlyList<Material> currentMaterials)
        {
            if (configuredMaterial == null || currentMaterials == null) return configuredMaterial;
            if (currentMaterials.Count == 0 || currentMaterials.Contains(configuredMaterial)) return configuredMaterial;

            var configuredName = configuredMaterial.name;
            if (string.IsNullOrEmpty(configuredName)) return configuredMaterial;

            return currentMaterials.FirstOrDefault(material => IsCurrentVersionOfConfiguredMaterial(material, configuredName))
                ?? configuredMaterial;
        }

        private static Material ResolveConvertedMaterial(
            Material source,
            IReadOnlyDictionary<Material, Material> convertedBySource)
        {
            if (source == null || convertedBySource == null) return source;
            return convertedBySource.TryGetValue(source, out var converted) ? converted : source;
        }

        private static bool IsCurrentVersionOfConfiguredMaterial(Material candidate, string sourceName)
        {
            if (candidate == null || string.IsNullOrEmpty(sourceName)) return false;

            return candidate.name == sourceName
                || candidate.name.StartsWith($"{sourceName}_", System.StringComparison.Ordinal)
                || candidate.name.StartsWith($"{sourceName} ", System.StringComparison.Ordinal)
                || candidate.name.StartsWith($"{sourceName}(", System.StringComparison.Ordinal);
        }

        private static void LogVerboseReportIfNeeded(MToonToLilToonComponent component, ConversionReport report)
        {
            if (component == null || report == null || !component.verboseLog) return;

            var unsupportedSummary = report.UnsupportedPropertySummary.Count > 0
                ? string.Join(", ", report.UnsupportedPropertySummary.Select(kv => $"{kv.Key}:{kv.Value}"))
                : "none";
            var warnings = report.Warnings.Count > 0
                ? string.Join(" | ", report.Warnings.Select(w => w.Message))
                : "none";

            LogUtility.Info(
                ToolName,
                $"scanned={report.ScannedMaterialCount}, converted={report.ConvertedMaterialCount}, skipped={report.SkippedMaterialCount}, warnings={warnings}, unsupported={unsupportedSummary}",
                component);
        }

        private static void ProcessRenderer(
            Renderer renderer,
            Shader lilToonShader,
            LilToonGlobalOverrides globalOverrides,
            bool useToonStandardFallback,
            IDictionary<Material, Material> convertedBySource,
            bool verboseLog,
            ConversionReport report,
            BuildContext buildContext)
        {
            if (renderer == null) return;

            var original = renderer.sharedMaterials;
            var result = new List<Material>(original.Length);
            var resultSourceIndices = new List<int>(original.Length);
            var transparentRanks = BuildTransparentRanks(original);
            report.ScannedMaterialCount += original.Length;

            for (var i = 0; i < original.Length; i++)
            {
                var source = original[i];
                if (source == null)
                {
                    result.Add(null);
                    resultSourceIndices.Add(i);
                    report.SkippedMaterialCount++;
                    continue;
                }

                if (convertedBySource != null && convertedBySource.TryGetValue(source, out var cached))
                {
                    result.Add(cached);
                    resultSourceIndices.Add(i);
                    if (cached != source)
                    {
                        report.ConvertedMaterialCount++;
                    }
                    else
                    {
                        report.SkippedMaterialCount++;
                    }
                    continue;
                }

                if (MToonToLilToonMapper.TryConvert(source, lilToonShader, globalOverrides, useToonStandardFallback, out var converted, report))
                {
                    NdmfObjectRegistry.RegisterReplacement(source, converted);
                    buildContext?.AssetSaver.SaveAsset(converted);
                    ApplyMToon10ShadingShiftStrengthMask(source, converted, report);
                    result.Add(converted);
                    resultSourceIndices.Add(i);
                    report.ConvertedMaterialCount++;
                    if (convertedBySource != null && source != null && !convertedBySource.ContainsKey(source))
                    {
                        convertedBySource[source] = converted;
                    }
                }
                else
                {
                    result.Add(source);
                    resultSourceIndices.Add(i);
                    report.SkippedMaterialCount++;
                    report.Warnings.Add(new ConversionWarning($"{source.name}: skipped (not convertible)"));
                    if (convertedBySource != null && source != null && !convertedBySource.ContainsKey(source))
                    {
                        convertedBySource[source] = source;
                    }
                }
            }
            ReindexTransparentQueues(result, resultSourceIndices, transparentRanks);
            renderer.sharedMaterials = result.ToArray();
        }

        private static Shader ResolveLilToonShader(MToonToLilToonComponent component)
        {
            if (component.lilToonShader != null) return component.lilToonShader;

            var resolved = Shader.Find("lilToon");
            if (resolved == null)
            {
                var guids = AssetDatabase.FindAssets("lilToon t:Shader");
                resolved = guids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<Shader>)
                    .FirstOrDefault(shader => shader != null && shader.name == "lilToon")
                    ?? guids
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Select(AssetDatabase.LoadAssetAtPath<Shader>)
                        .FirstOrDefault(shader => shader != null && shader.name.IndexOf("liltoon", System.StringComparison.OrdinalIgnoreCase) >= 0);
            }

            component.lilToonShader = resolved;
            return resolved;
        }

        private static Dictionary<int, int> BuildTransparentRanks(IReadOnlyList<Material> materials)
        {
            var ranked = new List<(int index, int queue)>();
            for (var i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material == null) continue;
                if (RenderTypeResolver.ResolveFromMaterial(material) != RenderType.Transparent) continue;
                ranked.Add((i, material.renderQueue));
            }

            ranked = ranked.OrderBy(pair => pair.queue).ThenBy(pair => pair.index).ToList();
            var result = new Dictionary<int, int>(ranked.Count);
            for (var i = 0; i < ranked.Count; i++)
            {
                result[ranked[i].index] = i;
            }
            return result;
        }

        private static void ReindexTransparentQueues(IReadOnlyList<Material> materials, IReadOnlyList<int> sourceIndices, IReadOnlyDictionary<int, int> transparentRanks)
        {
            for (var i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material == null) continue;
                if (i >= sourceIndices.Count) continue;
                var sourceIndex = sourceIndices[i];
                if (sourceIndex < 0) continue;
                if (!transparentRanks.TryGetValue(sourceIndex, out var rank)) continue;
                material.renderQueue = 2460 + rank;
            }
        }

        private static void SetFloatIfAnyExists(Material material, IReadOnlyList<string> propertyNames, float value)
        {
            if (material == null || propertyNames == null) return;

            for (var i = 0; i < propertyNames.Count; i++)
            {
                var propertyName = propertyNames[i];
                if (!material.HasProperty(propertyName)) continue;
                material.SetFloat(propertyName, value);
            }
        }

        private static void SetTextureIfAnyExists(Material material, IReadOnlyList<string> propertyNames, Texture texture)
        {
            if (material == null || propertyNames == null) return;

            for (var i = 0; i < propertyNames.Count; i++)
            {
                var propertyName = propertyNames[i];
                if (!material.HasProperty(propertyName)) continue;
                material.SetTexture(propertyName, texture);
            }
        }

        private static void ApplyFaceShadowMaskSettings(
            Material faceMaterial,
            Texture sdfTexture,
            MToonToLilToonComponent.FaceShadowMaskType maskType,
            float shadowStrengthMaskLod)
        {
            if (faceMaterial == null) return;

            SetFloatIfAnyExists(faceMaterial, new[] { "_UseShadowMask", "_UseShadowStrengthMask" }, 1f);
            var shadowMaskTypeValue = maskType switch
            {
                MToonToLilToonComponent.FaceShadowMaskType.Strength => 0f,
                MToonToLilToonComponent.FaceShadowMaskType.Flat => 1f,
                MToonToLilToonComponent.FaceShadowMaskType.Sdf => 2f,
                _ => 1f
            };
            SetFloatIfAnyExists(faceMaterial, new[] { "_ShadowMaskType" }, shadowMaskTypeValue);
            SetTextureIfAnyExists(faceMaterial, new[] { "_ShadowStrengthMask" }, sdfTexture);
            SetFloatIfAnyExists(faceMaterial, new[] { "_ShadowStrengthMaskLOD" }, Mathf.Clamp01(shadowStrengthMaskLod));
        }

        private static void ApplyFaceGlobalExclusionSettings(
            Material faceMaterial,
            bool disableShadowReceiveForFace,
            bool disableRimShadeForFace,
            bool disableBacklightStrengthForFace)
        {
            if (faceMaterial == null) return;
            if (disableShadowReceiveForFace)
            {
                SetFloatIfAnyExists(faceMaterial, new[] { "_ShadowReceive" }, 0f);
            }

            if (disableRimShadeForFace)
            {
                SetFloatIfAnyExists(faceMaterial, new[] { "_UseRimShade" }, 0f);
            }

            if (disableBacklightStrengthForFace)
            {
                DisableBacklightOnMaterial(faceMaterial);
            }
        }

        private static void ApplyBacklightExclusionToMouthMaterials(IEnumerable<Material> materials)
        {
            if (materials == null) return;

            foreach (var material in materials)
            {
                if (material == null) continue;
                if (material.name.IndexOf("mouth", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                DisableBacklightOnMaterial(material);
            }
        }

        private static void DisableBacklightOnMaterial(Material material)
        {
            if (material == null) return;

            SetFloatIfAnyExists(material, new[] { "_UseBacklight", "_BacklightMainStrength" }, 0f);
            if (!material.HasProperty("_BacklightColor")) return;

            var backlightColor = material.GetColor("_BacklightColor");
            backlightColor.a = 0f;
            material.SetColor("_BacklightColor", backlightColor);
        }

        private static void ApplyToonStandardFallbackRampToMaterials(IEnumerable<Material> materials)
        {
            if (materials == null) return;

            var path = AssetDatabase.GUIDToAssetPath("348500adef1d2da428abc7b720b8b699");
            var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (ramp == null)
            {
                LogUtility.Warning(ToolName, "Toon Standard Realistic ramp was not found.");
                return;
            }

            foreach (var material in materials)
            {
                if (material == null) continue;
                if (!material.HasProperty("_Ramp")) continue;

                material.SetTexture("_Ramp", ramp);
                EditorUtility.SetDirty(material);
            }
        }

        private static bool IsLikelyDummyTexture(Texture texture)
        {
            if (texture == null) return false;
            return texture.width <= 8 && texture.height <= 8;
        }

        private static void ApplyMToon10ShadingShiftStrengthMask(Material source, Material destination, ConversionReport report)
        {
            if (!IsMToon10Material(source) || destination == null) return;
            if (!source.HasProperty("_ShadingShiftTex") || !destination.HasProperty("_ShadowStrengthMask")) return;

            var texture = source.GetTexture("_ShadingShiftTex");
            if (texture == null || IsLikelyDummyTexture(texture)) return;

            var readable = TextureReadUtility.ToReadableTextureWithTransform(
                texture,
                source.GetTextureScale("_ShadingShiftTex"),
                source.GetTextureOffset("_ShadingShiftTex"));
            if (readable == null)
            {
                report?.Warnings.Add(new ConversionWarning($"{source.name}: _ShadingShiftTex could not be baked as an inverted _ShadowStrengthMask."));
                return;
            }

            InvertRgb(readable);
            destination.SetTexture("_ShadowStrengthMask", CompressGeneratedAtlas(readable, "_ShadowStrengthMask"));
            destination.SetTextureScale("_ShadowStrengthMask", Vector2.one);
            destination.SetTextureOffset("_ShadowStrengthMask", Vector2.zero);
            SetFloatIfAnyExists(destination, new[] { "_UseShadowMask", "_UseShadowStrengthMask" }, 1f);
            SetFloatIfAnyExists(destination, new[] { "_ShadowMaskType" }, 0f);
        }

        private static bool IsMToon10Material(Material material)
        {
            if (material == null || material.shader == null) return false;
            var shaderName = material.shader.name;
            return shaderName == "VRM10/MToon10"
                || shaderName == "VRM10/Universal Render Pipeline/MToon10";
        }

        private static void InvertRgb(Texture2D texture)
        {
            if (texture == null) return;
            var pixels = texture.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                pixels[i] = new Color(1f - p.r, 1f - p.g, 1f - p.b, p.a);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
        }

        private static Texture2D CompressGeneratedAtlas(Texture2D atlas, string propertyName, BuildTarget? buildTarget = null)
        {
            var isNormal = string.Equals(propertyName, "_BumpMap", System.StringComparison.OrdinalIgnoreCase);
            return GeneratedTextureUtility.CompressGeneratedTexture(atlas, propertyName, isNormal, buildTarget);
        }

        private static void ValidateRendererMaterialTextureReferencesBeforeAao(MToonToLilToonComponent component, ConversionReport report)
        {
            if (component == null) return;
            var verbose = component.verboseLog;

            foreach (var renderer in ResolveProcessingRoot(component).GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials ?? System.Array.Empty<Material>();
                var mesh = renderer switch
                {
                    SkinnedMeshRenderer skinned => skinned.sharedMesh,
                    MeshRenderer meshRenderer => meshRenderer.GetComponent<MeshFilter>()?.sharedMesh,
                    _ => null
                };

                var hasDitherClothingOverflow = mesh != null
                    && materials.Length == mesh.subMeshCount + 1
                    && materials[materials.Length - 1] != null
                    && materials[materials.Length - 1].name.EndsWith("_SilhouetteDither", System.StringComparison.Ordinal);
                var hasBodyStencilOverflow = mesh != null
                    && materials.Length == mesh.subMeshCount + 1
                    && materials[materials.Length - 1] != null
                    && materials[materials.Length - 1].name.EndsWith("_SilhouetteBodyStencil", System.StringComparison.Ordinal);
                var hasRefractionClothingOverflow = mesh != null
                    && materials.Length == mesh.subMeshCount + 2
                    && materials[materials.Length - 2] != null
                    && materials[materials.Length - 1] != null
                    && materials[materials.Length - 2].name.EndsWith("_SilhouetteClothingOverlay", System.StringComparison.Ordinal)
                    && materials[materials.Length - 1].name.EndsWith("_SilhouetteRefractionBlur", System.StringComparison.Ordinal);
                var isExpectedSilhouetteOverflow = hasDitherClothingOverflow
                    || hasBodyStencilOverflow
                    || hasRefractionClothingOverflow;
                if (mesh != null && mesh.subMeshCount != materials.Length && !isExpectedSilhouetteOverflow)
                {
                    var message = $"{renderer.name}: subMeshCount({mesh.subMeshCount}) != sharedMaterials.Length({materials.Length})";
                    report?.Warnings.Add(new ConversionWarning(message));
                    if (verbose)
                    {
                        LogUtility.Warning(ToolName, "AAO-precheck", message, renderer);
                    }
                }
                else if (verbose)
                {
                    var suffix = isExpectedSilhouetteOverflow ? " (expected silhouette overflow)" : string.Empty;
                    LogUtility.Info(ToolName, "AAO-precheck", $"{renderer.name}: subMeshCount/materialCount OK ({materials.Length}){suffix}", renderer);
                }

                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null)
                    {
                        var message = $"{renderer.name}: material slot[{i}] is null";
                        report?.Warnings.Add(new ConversionWarning(message));
                        if (verbose)
                        {
                            LogUtility.Warning(ToolName, "AAO-precheck", message, renderer);
                        }
                        continue;
                    }

                    var mainTexture = material.mainTexture;
                    var mainTex = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                    var resolvedMainTexture = mainTexture != null ? mainTexture : mainTex;

                    if (resolvedMainTexture == null)
                    {
                        var message = $"{renderer.name}: material[{i}] {material.name} has null mainTexture/_MainTex";
                        report?.Warnings.Add(new ConversionWarning(message));
                        if (verbose)
                        {
                            LogUtility.Warning(ToolName, "AAO-precheck", message, renderer);
                        }
                        continue;
                    }

                    if (verbose)
                    {
                        var textureName = resolvedMainTexture != null ? resolvedMainTexture.name : "(null)";
                        LogUtility.Info(ToolName, "AAO-precheck", $"{renderer.name}: material[{i}] {material.name} -> texture {textureName}", renderer);
                    }
                }
            }
        }
    }
}
