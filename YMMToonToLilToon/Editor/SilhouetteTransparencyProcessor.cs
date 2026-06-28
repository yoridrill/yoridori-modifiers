using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace YoridoriModifiers.MToonToLilToon
{
    internal static class SilhouetteTransparencyProcessor
    {
        // Use one bit so the feature can coexist with avatars that use the other stencil bits.
        private const int StencilBit = 64;
        private const int ClothingStencilRenderQueue = 2498;
        private const int SilhouetteRenderQueue = 2499;
        private const int RefractionBlurRenderQueue = 2900;

        internal static void Apply(
            GameObject processingRoot,
            Material clothingMaterial,
            Material bodyMaterial,
            Color clothingColor,
            Color shadowColor,
            float opacity,
            float blur,
            ConversionReport report,
            BuildContext buildContext,
            AnimatorServicesContext animatorServices)
        {
            if (processingRoot == null) return;
            if (clothingMaterial == null || bodyMaterial == null)
            {
                Warn(report, "Silhouette transparency: both clothing and body materials must be selected.");
                return;
            }

            var renderers = processingRoot.GetComponentsInChildren<Renderer>(true);
            var clothingRenderers = FindRenderersUsing(renderers, clothingMaterial);
            var bodyRenderers = FindRenderersUsing(renderers, bodyMaterial);
            if (clothingRenderers.Count == 0 || bodyRenderers.Count == 0)
            {
                Warn(report, "Silhouette transparency: the selected clothing or body material was not found on a Renderer.");
                return;
            }

            if (clothingMaterial == bodyMaterial)
            {
                Warn(report, "Silhouette transparency: clothing and body must use different materials.");
                return;
            }

            var refractionBlurShader = Shader.Find("Hidden/lilToonRefractionBlur");
            if (refractionBlurShader == null)
            {
                Warn(report, "Silhouette transparency: Hidden/lilToonRefractionBlur was not found; the feature was skipped.");
                return;
            }

            var sharedRenderers = clothingRenderers.Intersect(bodyRenderers).ToList();
            foreach (var sharedRenderer in sharedRenderers)
            {
                clothingRenderers.Remove(sharedRenderer);
                if (sharedRenderer is not SkinnedMeshRenderer skinnedRenderer)
                {
                    bodyRenderers.Remove(sharedRenderer);
                    Warn(report, $"Silhouette transparency: {sharedRenderer.name} contains both clothing and body, but only SkinnedMeshRenderer separation is supported.");
                    continue;
                }

                if (TrySeparateClothingSubMesh(
                        processingRoot,
                        skinnedRenderer,
                        clothingMaterial,
                        bodyMaterial,
                        report,
                        buildContext,
                        animatorServices,
                        out var separatedClothingRenderer))
                {
                    clothingRenderers.Add(separatedClothingRenderer);
                }
                else
                {
                    bodyRenderers.Remove(sharedRenderer);
                }
            }

            var compatibleClothingRenderers = clothingRenderers
                .Where(renderer => CanPrepareTargetSubMesh(renderer, clothingMaterial, report))
                .ToList();
            var compatibleBodyRenderers = bodyRenderers
                .Where(renderer => CanPrepareTargetSubMesh(renderer, bodyMaterial, report))
                .ToList();
            if (compatibleClothingRenderers.Count == 0 || compatibleBodyRenderers.Count == 0)
            {
                Warn(report, "Silhouette transparency: no compatible clothing or body submesh was found; the feature was skipped.");
                return;
            }

            var clothingStencil = CreateClothingStencilMaterial(clothingMaterial);
            var silhouette = CreateSilhouetteMaterial(bodyMaterial, clothingColor, shadowColor);
            var refractionBlur = CreateRefractionBlurMaterial(clothingMaterial, refractionBlurShader, opacity, blur);

            var appliedClothingCount = 0;
            foreach (var renderer in compatibleClothingRenderers)
            {
                if (!TryPrepareTargetSubMesh(renderer, clothingMaterial, report, buildContext)) continue;
                AppendMaterial(renderer, clothingStencil);
                AppendMaterial(renderer, refractionBlur);
                appliedClothingCount++;
            }

            var appliedBodyCount = 0;
            foreach (var renderer in compatibleBodyRenderers)
            {
                if (!TryPrepareTargetSubMesh(renderer, bodyMaterial, report, buildContext)) continue;
                AppendMaterial(renderer, silhouette);
                appliedBodyCount++;
            }

            if (appliedClothingCount == 0 || appliedBodyCount == 0) return;

            SaveAsset(buildContext, clothingStencil);
            SaveAsset(buildContext, silhouette);
            SaveAsset(buildContext, refractionBlur);
        }

        private static List<Renderer> FindRenderersUsing(IEnumerable<Renderer> renderers, Material material)
        {
            return renderers
                .Where(renderer => renderer != null && renderer.sharedMaterials.Contains(material))
                .ToList();
        }

        private static bool TrySeparateClothingSubMesh(
            GameObject processingRoot,
            SkinnedMeshRenderer sourceRenderer,
            Material clothingMaterial,
            Material bodyMaterial,
            ConversionReport report,
            BuildContext buildContext,
            AnimatorServicesContext animatorServices,
            out SkinnedMeshRenderer clothingRenderer)
        {
            clothingRenderer = null;
            var sourceMesh = sourceRenderer != null ? sourceRenderer.sharedMesh : null;
            if (sourceMesh == null)
            {
                Warn(report, $"Silhouette transparency: {sourceRenderer?.name} has no Mesh to separate.");
                return false;
            }

            if (sourceRenderer.GetComponent<Cloth>() != null)
            {
                Warn(report, $"Silhouette transparency: {sourceRenderer.name} uses a Cloth component. Clothing separation was skipped to preserve cloth simulation.");
                return false;
            }

            var sourceMaterials = sourceRenderer.sharedMaterials ?? Array.Empty<Material>();
            if (sourceMaterials.Length != sourceMesh.subMeshCount)
            {
                Warn(report, $"Silhouette transparency: {sourceRenderer.name} already has {sourceMaterials.Length} materials for {sourceMesh.subMeshCount} submeshes. Clothing separation was skipped.");
                return false;
            }

            var clothingIndices = Enumerable.Range(0, sourceMaterials.Length)
                .Where(index => sourceMaterials[index] == clothingMaterial)
                .ToArray();
            var bodyIndices = Enumerable.Range(0, sourceMaterials.Length)
                .Where(index => sourceMaterials[index] == bodyMaterial)
                .ToArray();
            if (clothingIndices.Length != 1 || bodyIndices.Length != 1 || sourceMesh.subMeshCount <= 1)
            {
                Warn(report, $"Silhouette transparency: {sourceRenderer.name} must contain exactly one clothing submesh and one body submesh.");
                return false;
            }

            var clothingIndex = clothingIndices[0];
            var bodyIndex = bodyIndices[0];
            var remainingIndices = Enumerable.Range(0, sourceMesh.subMeshCount)
                .Where(index => index != clothingIndex && index != bodyIndex)
                .Concat(new[] { bodyIndex })
                .ToArray();
            var remainingMesh = CreateSubMeshSelection(
                sourceMesh,
                remainingIndices,
                $"{sourceMesh.name}_WithoutSilhouetteClothing");
            var clothingMesh = CreateSubMeshSelection(
                sourceMesh,
                new[] { clothingIndex },
                $"{sourceMesh.name}_SilhouetteClothing");

            var clothingObject = new GameObject($"{sourceRenderer.name}_SilhouetteClothing")
            {
                layer = sourceRenderer.gameObject.layer
            };
            clothingObject.transform.SetParent(sourceRenderer.transform, false);
            clothingRenderer = clothingObject.AddComponent<SkinnedMeshRenderer>();
            EditorUtility.CopySerialized(sourceRenderer, clothingRenderer);
            clothingRenderer.sharedMesh = clothingMesh;
            clothingRenderer.sharedMaterials = new[] { clothingMaterial };

            if (sourceRenderer.HasPropertyBlock())
            {
                var propertyBlock = new MaterialPropertyBlock();
                sourceRenderer.GetPropertyBlock(propertyBlock);
                clothingRenderer.SetPropertyBlock(propertyBlock);
            }

            sourceRenderer.sharedMesh = remainingMesh;
            sourceRenderer.sharedMaterials = remainingIndices.Select(index => sourceMaterials[index]).ToArray();

            AddSeparatedRendererToLodGroups(processingRoot, sourceRenderer, clothingRenderer);
            var hasAnimatedClothingMaterial = RemapSeparatedRendererAnimationCurves(
                sourceRenderer,
                clothingRenderer,
                clothingIndex,
                remainingIndices,
                animatorServices);
            if (hasAnimatedClothingMaterial)
            {
                Warn(report, $"Silhouette transparency: {sourceRenderer.name} animates the separated clothing material. The base-material animation was retargeted, but the generated stencil and refraction materials remain based on the configured clothing material.");
            }
            SaveAsset(buildContext, remainingMesh);
            SaveAsset(buildContext, clothingMesh);
            return true;
        }

        private static Mesh CreateSubMeshSelection(Mesh source, IReadOnlyList<int> sourceIndices, string name)
        {
            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = name;
            var descriptors = sourceIndices.Select(source.GetSubMesh).ToArray();

            for (var index = 0; index < mesh.subMeshCount; index++)
            {
                mesh.SetSubMesh(
                    index,
                    new SubMeshDescriptor(0, 0),
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }

            mesh.subMeshCount = descriptors.Length;
            for (var index = 0; index < descriptors.Length; index++)
            {
                mesh.SetSubMesh(
                    index,
                    descriptors[index],
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }

            mesh.bounds = source.bounds;
            return mesh;
        }

        private static void AddSeparatedRendererToLodGroups(
            GameObject processingRoot,
            Renderer source,
            Renderer destination)
        {
            if (processingRoot == null || source == null || destination == null) return;

            foreach (var lodGroup in processingRoot.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lodGroup.GetLODs();
                var changed = false;
                for (var lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                {
                    var renderers = lods[lodIndex].renderers?.ToList() ?? new List<Renderer>();
                    var sourceIndex = renderers.IndexOf(source);
                    if (sourceIndex < 0 || renderers.Contains(destination)) continue;
                    renderers.Insert(sourceIndex + 1, destination);
                    lods[lodIndex].renderers = renderers.ToArray();
                    changed = true;
                }

                if (changed) lodGroup.SetLODs(lods);
            }
        }

        private static bool RemapSeparatedRendererAnimationCurves(
            SkinnedMeshRenderer source,
            SkinnedMeshRenderer destination,
            int clothingMaterialIndex,
            IReadOnlyList<int> remainingMaterialIndices,
            AnimatorServicesContext animatorServices)
        {
            if (source == null || destination == null || animatorServices == null) return false;

            var paths = animatorServices.ObjectPathRemapper;
            var sourcePath = paths.GetVirtualPathForObject(source.gameObject);
            var destinationPath = paths.GetVirtualPathForObject(destination.gameObject);
            var clips = animatorServices.AnimationIndex.GetClipsForObjectPath(sourcePath).ToArray();
            var hasAnimatedClothingMaterial = false;
            foreach (var clip in clips)
            {
                var floatBindings = clip.GetFloatCurveBindings()
                    .Where(binding => binding.path == sourcePath
                        && binding.type == typeof(SkinnedMeshRenderer)
                        && (binding.propertyName == "m_Enabled"
                            || binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)))
                    .ToArray();
                foreach (var binding in floatBindings)
                {
                    var destinationBinding = binding;
                    destinationBinding.path = destinationPath;
                    clip.SetFloatCurve(destinationBinding, clip.GetFloatCurve(binding));
                }

                var materialBindings = clip.GetObjectCurveBindings()
                    .Select(binding => (binding, index: ParseMaterialArrayIndex(binding.propertyName)))
                    .Where(item => item.binding.path == sourcePath
                        && item.binding.type == typeof(SkinnedMeshRenderer)
                        && item.index >= 0)
                    .Select(item => (item.binding, item.index, curve: clip.GetObjectCurve(item.binding)))
                    .ToArray();
                foreach (var item in materialBindings)
                {
                    clip.SetObjectCurve(item.binding, null);
                }

                foreach (var item in materialBindings)
                {
                    var destinationBinding = item.binding;
                    if (item.index == clothingMaterialIndex)
                    {
                        destinationBinding.path = destinationPath;
                        destinationBinding.propertyName = BuildMaterialArrayProperty(0);
                        clip.SetObjectCurve(destinationBinding, item.curve);
                        hasAnimatedClothingMaterial = true;
                        continue;
                    }

                    var remappedIndex = IndexOf(remainingMaterialIndices, item.index);
                    if (remappedIndex < 0) continue;
                    destinationBinding.propertyName = BuildMaterialArrayProperty(remappedIndex);
                    clip.SetObjectCurve(destinationBinding, item.curve);
                }
            }

            return hasAnimatedClothingMaterial;
        }

        private static int ParseMaterialArrayIndex(string propertyName)
        {
            const string prefix = "m_Materials.Array.data[";
            if (string.IsNullOrEmpty(propertyName)
                || !propertyName.StartsWith(prefix, StringComparison.Ordinal)
                || !propertyName.EndsWith("]", StringComparison.Ordinal))
            {
                return -1;
            }

            var value = propertyName.Substring(prefix.Length, propertyName.Length - prefix.Length - 1);
            return int.TryParse(value, out var index) ? index : -1;
        }

        private static string BuildMaterialArrayProperty(int index)
        {
            return $"m_Materials.Array.data[{index}]";
        }

        private static int IndexOf(IReadOnlyList<int> values, int value)
        {
            if (values == null) return -1;
            for (var index = 0; index < values.Count; index++)
            {
                if (values[index] == value) return index;
            }

            return -1;
        }

        private static bool TryPrepareTargetSubMesh(
            Renderer renderer,
            Material targetMaterial,
            ConversionReport report,
            BuildContext buildContext)
        {
            if (!CanPrepareTargetSubMesh(renderer, targetMaterial, null)) return false;
            var mesh = GetSharedMesh(renderer);
            var materials = renderer.sharedMaterials ?? Array.Empty<Material>();
            var targetIndices = Enumerable.Range(0, materials.Length)
                .Where(index => materials[index] == targetMaterial)
                .ToList();
            var targetIndex = targetIndices[0];
            if (targetIndex == mesh.subMeshCount - 1) return true;

            var order = Enumerable.Range(0, mesh.subMeshCount)
                .Where(index => index != targetIndex)
                .Concat(new[] { targetIndex })
                .ToArray();
            var reorderedMesh = UnityEngine.Object.Instantiate(mesh);
            reorderedMesh.name = $"{mesh.name}_SilhouetteSubMeshOrder";
            var reorderedSubMeshes = order
                .Select(mesh.GetSubMesh)
                .ToArray();

            // Setting reordered descriptors directly can temporarily overlap the descriptor
            // that still occupies the destination range. Unity treats that as undefined
            // behavior, so clear every descriptor before assigning the final order.
            for (var destination = 0; destination < order.Length; destination++)
            {
                reorderedMesh.SetSubMesh(
                    destination,
                    new SubMeshDescriptor(0, 0),
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }

            for (var destination = 0; destination < order.Length; destination++)
            {
                reorderedMesh.SetSubMesh(
                    destination,
                    reorderedSubMeshes[destination],
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
            reorderedMesh.bounds = mesh.bounds;

            var reorderedMaterials = order.Select(index => materials[index]).ToArray();
            SetSharedMesh(renderer, reorderedMesh);
            renderer.sharedMaterials = reorderedMaterials;
            SaveAsset(buildContext, reorderedMesh);
            return true;
        }

        private static bool CanPrepareTargetSubMesh(
            Renderer renderer,
            Material targetMaterial,
            ConversionReport report)
        {
            var mesh = GetSharedMesh(renderer);
            if (mesh == null)
            {
                Warn(report, $"Silhouette transparency: {renderer.name} has no supported Mesh.");
                return false;
            }

            var materials = renderer.sharedMaterials ?? Array.Empty<Material>();
            if (materials.Length != mesh.subMeshCount)
            {
                Warn(report, $"Silhouette transparency: {renderer.name} already has {materials.Length} materials for {mesh.subMeshCount} submeshes. The Renderer was skipped to avoid changing existing overflow-material behavior.");
                return false;
            }

            var targetCount = materials.Count(material => material == targetMaterial);
            if (targetCount == 1) return true;

            Warn(report, $"Silhouette transparency: {renderer.name} uses the selected material on {targetCount} submeshes. Exactly one is required per Renderer.");
            return false;
        }

        private static Mesh GetSharedMesh(Renderer renderer)
        {
            return renderer switch
            {
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                MeshRenderer meshRenderer => meshRenderer.GetComponent<MeshFilter>()?.sharedMesh,
                _ => null
            };
        }

        private static void SetSharedMesh(Renderer renderer, Mesh mesh)
        {
            switch (renderer)
            {
                case SkinnedMeshRenderer skinned:
                    skinned.sharedMesh = mesh;
                    break;
                case MeshRenderer meshRenderer:
                    var filter = meshRenderer.GetComponent<MeshFilter>();
                    if (filter != null) filter.sharedMesh = mesh;
                    break;
            }
        }

        private static Material CreateClothingStencilMaterial(Material source)
        {
            var material = new Material(source) { name = $"{source.name}_SilhouetteStencil" };
            SetFloatIfExists(material, "_Cull", (float)CullMode.Back);
            SetFloatIfExists(material, "_UseOutline", 0f);
            SetFloatIfExists(material, "_ColorMask", 0f);
            SetFloatIfExists(material, "_OutlineColorMask", 0f);
            SetFloatIfExists(material, "_ZWrite", 0f);
            SetFloatIfExists(material, "_ZTest", (float)CompareFunction.LessEqual);
            ConfigureStencil(material, CompareFunction.Always, StencilOp.Replace);
            ConfigureHiddenSafetyFallback(material);
            material.renderQueue = ClothingStencilRenderQueue;
            return material;
        }

        private static Material CreateSilhouetteMaterial(Material source, Color clothingColor, Color shadowColor)
        {
            var material = new Material(source) { name = $"{source.name}_Silhouette" };
            foreach (var property in new[]
                     {
                         "_UseMain2ndTex", "_UseMain3rdTex", "_UseBacklight", "_UseRimShade",
                         "_UseReflection", "_UseMatCap", "_UseMatCap2nd", "_UseRim", "_UseGlitter",
                         "_UseEmission", "_UseEmission2nd", "_UseBumpMap", "_UseBump2ndMap",
                         "_UseAnisotropy", "_UseParallax", "_UsePOM", "_UseAudioLink", "_UseOutline"
                     })
            {
                SetFloatIfExists(material, property, 0f);
            }

            SetFloatIfExists(material, "_UseShadow", 1f);
            SetFloatIfExists(material, "_ShadowReceive", 0f);
            SetFloatIfExists(material, "_ShadowColorType", 0f);
            SetFloatIfExists(material, "_ShadowBorderRange", 0f);
            SetFloatIfExists(material, "_ShadowBorder", 0.5f);
            SetFloatIfExists(material, "_ShadowBlur", 0.5f);
            SetColorIfExists(material, "_Color", clothingColor);
            SetColorIfExists(material, "_BaseColor", clothingColor);
            SetColorIfExists(material, "_ShadowColor", shadowColor);
            // Leave shader-default white textures unresolved. Texture2D.whiteTexture has
            // DontSaveInEditor and crashes NDMF Manual Bake when AssetSaver persists it.
            SetTextureIfExists(material, "_ShadowColorTex", null);
            SetFloatIfExists(material, "_OutlineEnable", 0f);
            SetFloatIfExists(material, "_OutlineWidth", 0f);
            material.SetShaderPassEnabled("Outline", false);
            SetFloatIfExists(material, "_ZWrite", 0f);
            SetFloatIfExists(material, "_ZTest", (float)CompareFunction.Always);
            ConfigureStencil(material, CompareFunction.Equal, StencilOp.Keep);
            ConfigureHiddenSafetyFallback(material);
            material.renderQueue = SilhouetteRenderQueue;
            return material;
        }

        private static Material CreateRefractionBlurMaterial(
            Material source,
            Shader shader,
            float opacity,
            float blur)
        {
            var material = new Material(source)
            {
                name = $"{source.name}_SilhouetteRefractionBlur",
                shader = shader,
                renderQueue = RefractionBlurRenderQueue
            };
            // lilToon refraction premultiplies the lit clothing color by alpha and then
            // interpolates it with the blurred background using alpha again. Without
            // compensation the clothing contribution becomes alpha squared and the
            // middle of the slider loses energy. For UI opacity p, using a=sqrt(p) and
            // scaling refraction by 1+a yields:
            //   background: (1-a)(1+a) = 1-p, clothing: a*a = p.
            var refractionAlpha = Mathf.Sqrt(Mathf.Clamp01(opacity));
            var refractionColorCompensation = Mathf.LinearToGammaSpace(1f + refractionAlpha);
            // Multiply preserves the source clothing alpha, including parts hidden by
            // the original Cutout texture, while applying the requested opacity.
            SetFloatIfExists(material, "_AlphaMaskMode", 2f); // Multiply
            SetTextureIfExists(material, "_AlphaMask", null);
            SetFloatIfExists(material, "_AlphaMaskScale", 1f);
            SetFloatIfExists(material, "_AlphaMaskValue", refractionAlpha - 1f);
            SetFloatIfExists(material, "_Smoothness", Mathf.Clamp01(blur));
            SetFloatIfExists(material, "_RefractionStrength", 0f);
            SetFloatIfExists(material, "_RefractionColorFromMain", 0f);
            SetColorIfExists(
                material,
                "_RefractionColor",
                new Color(
                    refractionColorCompensation,
                    refractionColorCompensation,
                    refractionColorCompensation,
                    1f));
            SetFloatIfExists(material, "_Cull", (float)CullMode.Back);
            SetFloatIfExists(material, "_SrcBlend", (float)BlendMode.One);
            SetFloatIfExists(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloatIfExists(material, "_ZWrite", 1f);
            // Refraction shaders still draw blurred background at alpha zero. Requiring
            // equal depth prevents cutout-hidden geometry in front of the visible dress
            // (for example VRoid waist ribbons) from drawing over the dress stencil.
            SetFloatIfExists(material, "_ZTest", (float)CompareFunction.Equal);
            ConfigureStencil(material, CompareFunction.Equal, StencilOp.Keep);
            ConfigureHiddenSafetyFallback(material);
            return material;
        }

        private static void ConfigureHiddenSafetyFallback(Material material)
        {
            material?.SetOverrideTag("VRCFallback", "Hidden");
        }

        private static void ConfigureStencil(Material material, CompareFunction comparison, StencilOp pass)
        {
            SetFloatIfExists(material, "_StencilRef", StencilBit);
            SetFloatIfExists(material, "_StencilReadMask", StencilBit);
            SetFloatIfExists(material, "_StencilWriteMask", StencilBit);
            SetFloatIfExists(material, "_StencilComp", (float)comparison);
            SetFloatIfExists(material, "_StencilPass", (float)pass);
            SetFloatIfExists(material, "_StencilFail", (float)StencilOp.Keep);
            SetFloatIfExists(material, "_StencilZFail", (float)StencilOp.Keep);
        }

        private static void AppendMaterial(Renderer renderer, Material material)
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Concat(new[] { material }).ToArray();
        }

        private static void SetFloatIfExists(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static void SetColorIfExists(Material material, string property, Color value)
        {
            if (material != null && material.HasProperty(property)) material.SetColor(property, value);
        }

        private static void SetTextureIfExists(Material material, string property, Texture value)
        {
            if (material != null && material.HasProperty(property)) material.SetTexture(property, value);
        }

        private static void SaveAsset(BuildContext buildContext, UnityEngine.Object asset)
        {
            if (buildContext != null && asset != null) buildContext.AssetSaver.SaveAsset(asset);
        }

        private static void Warn(ConversionReport report, string message)
        {
            report?.Warnings.Add(new ConversionWarning(message));
        }
    }
}
