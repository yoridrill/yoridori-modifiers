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
                foreach (var component in context.AvatarRootObject.GetComponentsInChildren<YMOutlineExtenderComponent>(true))
                    OutlineMaterialUtility.Synchronize(component.materialSelections, materials);
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
        private static bool IsSelected(Material material, IEnumerable<OutlineMaterialSelection> selections, out bool mixed)
        {
            var sources = NdmfObjectRegistry.GetSourceReferences(material);
            var enabled = new HashSet<ObjectReference>();
            var disabled = new HashSet<ObjectReference>();
            foreach (var selection in selections.Where(s => s != null && s.material != null))
                (selection.selected ? enabled : disabled).UnionWith(NdmfObjectRegistry.GetSourceReferences(selection.material));
            var any = sources.Overlaps(enabled);
            mixed = any && (sources.Overlaps(disabled) || sources.Any(s => !enabled.Contains(s)));
            return any && !mixed;
        }

        internal static void Apply(YMOutlineExtenderComponent component, BuildContext context)
            => Apply(component, context, FindAnimatedMaterials(context.AvatarRootObject,
                context.Extension<AnimatorServicesContext>()));

        internal static void ApplyPreview(YMOutlineExtenderComponent component, OutlineSdfCache cache)
        {
            if (component == null) return;
            Apply(component, null, new HashSet<Material>(), cache);
        }

        private static void Apply(YMOutlineExtenderComponent component, BuildContext context, HashSet<Material> blocked)
            => Apply(component, context, blocked, new OutlineSdfCache());

        private static void Apply(YMOutlineExtenderComponent component, BuildContext context, HashSet<Material> blocked,
            OutlineSdfCache cache)
        {
            var root = context != null
                ? context.AvatarRootObject
                : PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            var shader = Shader.Find(OutlineMaterialUtility.ShaderName);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
            {
                Warn("Custom shader is missing or has compiler errors. Materials were not changed.", component);
                return;
            }
            var replacements = new Dictionary<Material, Material>();
            foreach (var material in OutlineMaterialUtility.CollectMaterials(root))
            {
                if (!IsSelected(material, component.materialSelections, out var mixed))
                {
                    if (mixed) Warn($"{material.name}: merged ON/OFF selections; skipped.", component);
                    continue;
                }
                if (blocked.Contains(material))
                {
                    Warn($"{material.name}: animated material/alpha/texture configuration is not supported; skipped.", component);
                    continue;
                }
                if (!OutlineMaterialUtility.TryValidate(material, out var reason))
                {
                    Warn($"{material.name}: {reason} Skipped.", component);
                    continue;
                }
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var mainTexture = (Texture2D)material.GetTexture("_MainTex");
                    var uvCoverage = OutlineUvCoverage.Build(root, material, mainTexture);
                    var generated = CreateMaterial(material, shader, cache,
                        component.outlineWidthMultiplier, uvCoverage);
                    if (context != null)
                    {
                        context.AssetSaver.SaveAsset(generated);
                    }
                    replacements.Add(material, generated);
                    LogUtility.Verbose("YM Outline Extender", component.verboseLog,
                        $"{material.name}: SDF generated in {watch.Elapsed.TotalSeconds:F2}s; cache={cache.Count}", component);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Warn($"{material.name}: SDF generation failed; skipped. {ex.Message}", component);
                }
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                    if (materials[i] != null && replacements.TryGetValue(materials[i], out var replacement))
                    {
                        materials[i] = replacement;
                        changed = true;
                    }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private static Material CreateMaterial(Material source, Shader shader, OutlineSdfCache cache,
            float widthMultiplier = 1f, OutlineUvCoverage uvCoverage = null)
        {
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Outline Extender shader is unavailable.");
            var originalCutoff = source.GetFloat("_Cutoff");
            var texture = cache.Get((Texture2D)source.GetTexture("_MainTex"),
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
