using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

[assembly: ExportsPlugin(typeof(YoridoriModifiers.OutlineExtender.YMOutlineExtenderNdmfPlugin))]

namespace YoridoriModifiers.OutlineExtender
{
    public sealed class YMOutlineExtenderNdmfPlugin : Plugin<YMOutlineExtenderNdmfPlugin>
    {
        public override string QualifiedName => "jp.yoridrill.ym-outline-extender";
        public override string DisplayName => "YM Outline Extender";

        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving).Run("Initialize Outline Extender Selections", context =>
            {
                var materials = OutlineMaterialUtility.CollectMaterials(context.AvatarRootObject);
                var hasMToonConverter = context.AvatarRootObject
                    .GetComponentsInChildren<YoridoriModifiers.MToonToLilToon.MToonToLilToonComponent>(true)
                    .Any(item => item != null);
                foreach (var component in context.AvatarRootObject.GetComponentsInChildren<YMOutlineExtenderComponent>(true))
                {
                    OutlineMaterialUtility.Synchronize(component.materialSelections, materials, hasMToonConverter);
                    OutlineMaterialUtility.SynchronizeManualSelections(component.bakeMaterialSelections, materials);
                    OutlineMaterialUtility.SynchronizeManualSelections(component.boundaryMaterialSelections, materials);
                }
            });

            InPhase(BuildPhase.Transforming)
                .AfterPlugin("jp.yoridrill.ym-mesh-trimmer")
                .AfterPlugin("net.rs64.tex-trans-tool")
                .AfterPlugin("jp.yoridrill.ym-mtoon-to-liltoon")
                .AfterPlugin("jp.yoridrill.ym-hair-look-kit")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .BeforePlugin("com.github.kurotu.vrc-quest-tools")
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                    sequence.Run("Extend Final Cutout Outlines", Execute));
        }

        private static void Execute(BuildContext context)
        {
            var root = context.AvatarRootObject;
            var components = root.GetComponentsInChildren<YMOutlineExtenderComponent>(true);
            try
            {
                var selected = components.OrderBy(c => PreviewCoordinator.GetDepthFromRoot(c.transform, root.transform)).FirstOrDefault();
                if (selected == null || !selected.enabled || !selected.enableOutlineExtender) return;
                ErrorReport.WithContextObject(selected, () => OutlineExtenderProcessor.Apply(selected, context));
            }
            finally
            {
                foreach (var component in components) UnityEngine.Object.DestroyImmediate(component);
            }
        }
    }

    internal static class OutlineExtenderProcessor
    {
        private enum ProcessingMode { None, Shader, Bake, Conflict }

        internal static void Apply(YMOutlineExtenderComponent component, BuildContext context)
            => Apply(component, context, FindAnimatedMaterials(context.AvatarRootObject,
                context.Extension<AnimatorServicesContext>()));

        internal static void ApplyPreview(YMOutlineExtenderComponent component, OutlineSdfCache sdfCache,
            OutlineBakeCache bakeCache, OutlineBoundaryPaddingCache paddingCache = null)
        {
            if (component == null) return;
            Apply(component, null, new HashSet<Material>(), sdfCache, bakeCache, paddingCache ?? new OutlineBoundaryPaddingCache());
        }

        private static void Apply(YMOutlineExtenderComponent component, BuildContext context, HashSet<Material> blocked)
            => Apply(component, context, blocked, new OutlineSdfCache(), new OutlineBakeCache(), new OutlineBoundaryPaddingCache());

        private static void Apply(YMOutlineExtenderComponent component, BuildContext context, HashSet<Material> blocked,
            OutlineSdfCache sdfCache, OutlineBakeCache bakeCache, OutlineBoundaryPaddingCache paddingCache)
        {
            var root = context != null
                ? context.AvatarRootObject
                : PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            var shader = Shader.Find(OutlineMaterialUtility.ShaderName);
            var shaderAvailable = shader != null && !ShaderUtil.ShaderHasError(shader);
            var padding = OutlineBoundaryFoldProcessor.PrepareBoundaryPadding(root, component, paddingCache, blocked);
            OutlineBoundaryFoldProcessor.InstallPadding(root, padding, context);
            var foldStats = OutlineBoundaryFoldProcessor.Apply(root, component, context);
            var controls = OutlineHairMaterialControls.Create(root);
            var shaderSelections = controls.CreateSelectionSet(component.materialSelections);
            var bakeSelections = controls.CreateSelectionSet(component.bakeMaterialSelections);
            var replacements = new Dictionary<Material, Material>();
            foreach (var material in OutlineMaterialUtility.CollectMaterials(root))
            {
                var mode = ResolveMode(material, shaderSelections, bakeSelections, out var mixed);
                if (mode == ProcessingMode.None)
                {
                    if (mixed) Warn($"{material.name}: merged ON/OFF selections; skipped.", component);
                    continue;
                }
                if (mode == ProcessingMode.Conflict)
                {
                    Warn($"{material.name}: selected for both Shader and Bake, or merged from conflicting selections; skipped.", component);
                    continue;
                }
                if (blocked.Contains(material))
                {
                    Warn($"{material.name}: animated material/alpha/texture configuration is not supported; skipped.", component);
                    continue;
                }
                if (mode == ProcessingMode.Shader && !OutlineMaterialUtility.TryValidate(material, out var reason))
                {
                    Warn($"{material.name}: {reason} Skipped.", component);
                    continue;
                }
                if (mode == ProcessingMode.Bake
                    && !OutlineMaterialUtility.TryGetBakeMaterialInfo(material, out _, out reason))
                {
                    Warn($"{material.name}: {reason} Skipped.", component);
                    continue;
                }
                if (mode == ProcessingMode.Shader && !shaderAvailable)
                {
                    Warn($"{material.name}: Custom shader is missing or has compiler errors; skipped.", component);
                    continue;
                }
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Material generated;
                    Texture2D generatedTexture;
                    if (mode == ProcessingMode.Shader)
                    {
                        var mainTexture = (Texture2D)material.GetTexture("_MainTex");
                        var uvCoverage = OutlineUvCoverage.Build(root, material, mainTexture);
                        generated = CreateShaderMaterial(material, shader, sdfCache,
                            component.outlineWidthMultiplier,
                            uvCoverage, out generatedTexture);
                    }
                    else
                    {
                        OutlineMaterialUtility.TryGetBakeMaterialInfo(material, out var bakeInfo, out _);
                        var uvCoverage = OutlineUvCoverage.Build(root, material, bakeInfo.MainTexture,
                            bakeInfo.MainTextureProperty);
                        var metric = OutlineUvMetric.Build(root, material, bakeInfo.MainTexture,
                            bakeInfo.MainTextureProperty);
                        generated = CreateBakeMaterial(material, bakeInfo, bakeCache,
                            component.outlineWidthMultiplier, uvCoverage, metric,
                            component.verboseLog ? samples => LogBakeDiagnostics(material, bakeInfo,
                                component.outlineWidthMultiplier, samples, component) : null,
                            out generatedTexture);
                    }
                    if (context != null)
                    {
                        context.AssetSaver.SaveAsset(generatedTexture);
                        context.AssetSaver.SaveAsset(generated);
                    }
                    replacements.Add(material, generated);
                    LogUtility.Verbose("YM Outline Extender", component.verboseLog,
                        $"{material.name}: {mode} generated in {watch.Elapsed.TotalSeconds:F2}s; "
                        + $"SDF cache={sdfCache.Count}, Bake cache={bakeCache.Count}", component);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Warn($"{material.name}: Outline texture generation failed; skipped. {ex.Message}", component);
                }
            }
            OutlineRendererUtility.ReplaceMaterials(root, replacements);
            if (context == null)
            {
                var used = new HashSet<Material>(OutlineMaterialUtility.CollectMaterials(root));
                foreach (var material in padding.Values)
                    if (!used.Contains(material)) UnityEngine.Object.DestroyImmediate(material);
            }
            LogUtility.Verbose("YM Outline Extender", component.verboseLog,
                $"Boundary Fill: materials={padding.Count}, distance={OutlineBoundaryFoldProcessor.NormalizeFillDistance(component.boundaryFillDistanceMultiplier)}W; "
                + $"Boundary Fold: renderers={foldStats.Renderers}, open edges={foldStats.Edges}, "
                + $"added vertices={foldStats.Vertices}, added triangles={foldStats.Triangles}, "
                + $"return flap angle={OutlineBoundaryFoldProcessor.NormalizeFoldAngle(component.boundaryFoldAngleDegrees)}deg, "
                + $"length={OutlineBoundaryFoldProcessor.NormalizeFoldLength(component.boundaryFoldLengthMultiplier)}W, "
                + $"shading band={OutlineBoundaryFoldProcessor.NormalizeSupportBandWidth(component.boundarySupportBandWidthMultiplier)}W",
                component);
        }

        private static ProcessingMode ResolveMode(Material material, OutlineMaterialSelectionSet shaderSelections,
            OutlineMaterialSelectionSet bakeSelections, out bool mixed)
        {
            var shader = shaderSelections.IsSelected(material, out var shaderMixed);
            var bake = bakeSelections.IsSelected(material, out var bakeMixed);
            mixed = shaderMixed || bakeMixed;
            if (shader && bake) return ProcessingMode.Conflict;
            if (mixed && (shader || bake)) return ProcessingMode.Conflict;
            return shader ? ProcessingMode.Shader : bake ? ProcessingMode.Bake : ProcessingMode.None;
        }

        private static Material CreateShaderMaterial(Material source, Shader shader, OutlineSdfCache cache,
            float widthMultiplier, OutlineUvCoverage uvCoverage, out Texture2D texture)
        {
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Outline Extender shader is unavailable.");
            var originalCutoff = source.GetFloat("_Cutoff");
            texture = cache.Get((Texture2D)source.GetTexture("_MainTex"),
                originalCutoff / source.GetColor("_Color").a, uvCoverage);
            var generated = NdmfObjectRegistry.Clone(source);
            generated.name = source.name + "_OutlineExtender";
            var queue = source.renderQueue;
            generated.shader = shader;
            // Changing shader can replace values and local keywords with the target
            // shader defaults. Re-apply every lilToon setting after the swap; only the
            // Main Texture alpha contract and cutoff are changed below.
            generated.CopyPropertiesFromMaterial(source);
            generated.renderQueue = queue;
            generated.SetTexture("_MainTex", texture);
            generated.SetFloat("_Cutoff", 0.5f);
            generated.SetFloat("_YMSdfRange", OutlineSdfGenerator.DistanceRange);
            generated.SetFloat("_YMWidthMultiplier", Mathf.Clamp(widthMultiplier, 0f, 2f));
            var color = generated.GetColor("_Color");
            color.a = 1;
            generated.SetColor("_Color", color);
            return generated;
        }

        private static Material CreateBakeMaterial(Material source, OutlineBakeMaterialInfo info,
            OutlineBakeCache cache, float widthMultiplier, OutlineUvCoverage uvCoverage,
            OutlineUvMetric metric, Action<IReadOnlyList<OutlineUvMetricSample>> diagnostics,
            out Texture2D texture)
        {
            var targetWidth = info.Outline.ObjectSpaceWidth * Mathf.Clamp(widthMultiplier, 0f, 2f)
                * OutlineBakeCache.ObjectSpaceWidthCompensation;
            texture = cache.Get(info.MainTexture, info.TextureCutoff, info.Outline.Color,
                targetWidth, uvCoverage, metric, diagnostics);
            var generated = NdmfObjectRegistry.Clone(source);
            generated.name = source.name + "_OutlineExtenderBake";
            // Keep the source shader and every material setting. Only the appropriate
            // main texture slot points to the derived normal color-alpha texture.
            generated.SetTexture(info.MainTextureProperty, texture);
            return generated;
        }

        private static void LogBakeDiagnostics(Material material, OutlineBakeMaterialInfo info,
            float widthMultiplier, IReadOnlyList<OutlineUvMetricSample> samples, UnityEngine.Object context)
        {
            var rawWidth = material.HasProperty("_OutlineWidth") ? material.GetFloat("_OutlineWidth") : 0f;
            var normalizedWidth = info.Outline.ObjectSpaceWidth;
            var targetWidth = normalizedWidth * Mathf.Clamp(widthMultiplier, 0f, 2f)
                * OutlineBakeCache.ObjectSpaceWidthCompensation;
            LogUtility.Verbose("YM Outline Extender",
                true,
                $"{material.name}: Bake width diagnostics: _OutlineWidth={rawWidth:G9}, " +
                $"normalized/object-space width={normalizedWidth:G9}m, " +
                $"width ratio={widthMultiplier:G9}, Bake compensation={OutlineBakeCache.ObjectSpaceWidthCompensation:G9}x, " +
                $"target object-space width={targetWidth:G9}m, minimum texel width={OutlineBakeCache.MinimumTexelWidth:G9}, " +
                $"texture cutoff={info.TextureCutoff:G9}, " +
                $"coverage transition={OutlineBakeCache.CoverageHalfWidth * 2f:G9} texel", context);
            foreach (var sample in samples)
                LogUtility.Verbose("YM Outline Extender",
                    true,
                    $"{material.name}: boundary ({sample.X}, {sample.Y}): " +
                    $"object length per texel={sample.ObjectLengthPerTexel:G9}m, " +
                    $"required texel width={sample.RequiredTexelWidth:G9}, " +
                    $"final texel width={sample.FinalTexelWidth:G9} (contributors={sample.Contributors}), " +
                    $"sample signed distance={sample.SignedDistance:G9}, " +
                    $"SDF threshold={-sample.FinalTexelWidth:G9} texels", context);
        }

        private static HashSet<Material> FindAnimatedMaterials(GameObject root, AnimatorServicesContext services)
        {
            var result = new HashSet<Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                var clips = services.AnimationIndex.GetClipsForObjectPath(path);
                foreach (var clip in clips)
                {
                    var bindings = clip.GetFloatCurveBindings().Concat(clip.GetObjectCurveBindings());
                    if (bindings.Any(b => b.path == path && typeof(Renderer).IsAssignableFrom(b.type)
                        && (b.propertyName.StartsWith("m_Materials", StringComparison.Ordinal)
                            || b.propertyName.StartsWith("material.", StringComparison.Ordinal))))
                    {
                        // Conservative first release: includes material swaps and animated
                        // alpha features enabled only by an animation, not by the material.
                        result.UnionWith(renderer.sharedMaterials.Where(m => m != null));
                        break;
                    }
                }
            }
            return result;
        }

        private static void Warn(string message, UnityEngine.Object context) => LogUtility.Warning("YM Outline Extender", message, context);
    }
}
