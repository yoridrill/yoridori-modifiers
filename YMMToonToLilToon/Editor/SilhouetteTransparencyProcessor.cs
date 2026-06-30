using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.MToonToLilToon
{
    internal static class SilhouetteTransparencyProcessor
    {
        // Reserve bit 128 for Hair Look Kit's eyebrow/front-hair stencil state.
        private const int SilhouetteStencilMask = 127;
        private const int MinimumBodyStencilRenderQueue = 2501;
        private const int MaximumBodyStencilRenderQueue = 2987;
        private const int RefractionBlurRenderQueue = 2989;
        private const string Dither4x4TexturePath = "Packages/jp.lilxyzw.liltoon/Texture/lil_bayer_4x4.png";
        private static readonly int[] AvailableStencilReferences = Enumerable.Range(1, 127)
            // Hair Look Kit uses 51 for the face/FakeShadow comparison, produces 115
            // after inverting bit 64, and uses the high bit for eyebrows/front hair.
            // Keep the high bit clear and also avoid the former fixed YM value 64.
            .Where(value => value != 51 && value != 64 && value != 115)
            .ToArray();

        internal static void Apply(
            GameObject processingRoot,
            Material clothingMaterial,
            Material bodyMaterial,
            Color shadowColor,
            float opacity,
            bool useRefractionBlur,
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

            Shader refractionBlurShader = null;
            Shader ditherShader = null;
            Texture2D ditherTexture = null;
            if (useRefractionBlur)
            {
                refractionBlurShader = Shader.Find("Hidden/lilToonRefractionBlur");
                if (refractionBlurShader == null)
                {
                    Warn(report, "Silhouette transparency: Hidden/lilToonRefractionBlur was not found; the feature was skipped.");
                    return;
                }
            }
            else
            {
                ditherShader = Shader.Find("Hidden/lilToonCutout");
                ditherTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Dither4x4TexturePath);
                if (ditherShader == null || ditherTexture == null)
                {
                    Warn(report, "Silhouette transparency: the lilToon Cutout shader or x4 dither texture was not found; the feature was skipped.");
                    return;
                }
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
                        useRefractionBlur,
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

            var stencilReference = CreateStencilReference();
            var bodyStencilRenderQueue = CreateBodyStencilRenderQueue();
            var clothingOverlayRenderQueue = bodyStencilRenderQueue + 1;

            var clothingAuxiliary = useRefractionBlur
                ? CreateRefractionClothingOverlayMaterial(
                    clothingMaterial,
                    shadowColor,
                    stencilReference,
                    clothingOverlayRenderQueue)
                : CreateDitheredClothingOverlayMaterial(
                    clothingMaterial,
                    shadowColor,
                    opacity,
                    ditherShader,
                    ditherTexture,
                    stencilReference,
                    clothingOverlayRenderQueue);
            var bodyAuxiliary = CreateBodyStencilMaterial(
                bodyMaterial,
                stencilReference,
                bodyStencilRenderQueue);
            var refractionBlur = useRefractionBlur
                ? CreateRefractionBlurMaterial(clothingMaterial, refractionBlurShader, opacity, blur)
                : null;

            var appliedClothingCount = 0;
            foreach (var renderer in compatibleClothingRenderers)
            {
                if (!TryPrepareTargetSubMesh(renderer, clothingMaterial, report, buildContext)) continue;
                AppendMaterial(renderer, clothingAuxiliary);
                if (refractionBlur != null) AppendMaterial(renderer, refractionBlur);
                appliedClothingCount++;
            }

            var appliedBodyCount = 0;
            foreach (var renderer in compatibleBodyRenderers)
            {
                if (!TryPrepareTargetSubMesh(renderer, bodyMaterial, report, buildContext)) continue;
                AppendMaterial(renderer, bodyAuxiliary);
                appliedBodyCount++;
            }

            if (appliedClothingCount == 0 || appliedBodyCount == 0) return;

            SaveAsset(buildContext, clothingAuxiliary);
            SaveAsset(buildContext, bodyAuxiliary);
            if (refractionBlur != null) SaveAsset(buildContext, refractionBlur);
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
            bool useRefractionBlur,
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
                var generatedMaterialDescription = useRefractionBlur
                    ? "stencil and refraction materials"
                    : "stencil and dithered silhouette materials";
                Warn(report, $"Silhouette transparency: {sourceRenderer.name} animates the separated clothing material. The base-material animation was retargeted, but the generated {generatedMaterialDescription} remain based on the configured clothing material.");
            }
            SaveAsset(buildContext, remainingMesh);
            SaveAsset(buildContext, clothingMesh);
            return true;
        }

        private static Mesh CreateSubMeshSelection(Mesh source, IReadOnlyList<int> sourceIndices, string name)
        {
            var mesh = NdmfObjectRegistry.Clone(source);
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
            var reorderedMesh = NdmfObjectRegistry.Clone(mesh);
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

        private static Material CreateBodyStencilMaterial(
            Material source,
            int stencilReference,
            int renderQueue)
        {
            var material = NdmfObjectRegistry.CreateReplacement(
                source,
                () => new Material(source) { name = $"{source.name}_SilhouetteBodyStencil" });
            SetFloatIfExists(material, "_Cull", (float)CullMode.Back);
            SetFloatIfExists(material, "_UseOutline", 0f);
            SetFloatIfExists(material, "_OutlineEnable", 0f);
            SetFloatIfExists(material, "_OutlineWidth", 0f);
            SetFloatIfExists(material, "_ColorMask", 0f);
            SetFloatIfExists(material, "_OutlineColorMask", 0f);
            SetFloatIfExists(material, "_ZWrite", 0f);
            SetFloatIfExists(material, "_ZTest", (float)CompareFunction.Always);
            SetFloatIfExists(material, "_AsOverlay", 1f);
            material.SetShaderPassEnabled("Outline", false);
            material.SetShaderPassEnabled("ForwardAdd", false);
            material.SetShaderPassEnabled("ShadowCaster", false);
            ConfigureStencil(
                material,
                stencilReference,
                CompareFunction.Always,
                StencilOp.Replace,
                SilhouetteStencilMask);
            ConfigureHiddenSafetyFallback(material);
            material.renderQueue = renderQueue;
            return material;
        }

        private static Material CreateRefractionClothingOverlayMaterial(
            Material source,
            Color shadowColor,
            int stencilReference,
            int renderQueue)
        {
            var material = NdmfObjectRegistry.CreateReplacement(
                source,
                () => new Material(source) { name = $"{source.name}_SilhouetteClothingOverlay" });
            ConfigureSilhouetteAppearance(material, Color.white, shadowColor);
            // The clothing has already been drawn. Multiply only the generated
            // silhouette shading so both rendering modes preserve the clothing color.
            SetTextureIfExists(material, "_MainTex", null);
            SetTextureIfExists(material, "_BaseMap", null);
            SetFloatIfExists(material, "_Cull", (float)CullMode.Back);
            ConfigureMultiplyBlend(material);
            SetFloatIfExists(material, "_AsOverlay", 1f);
            SetFloatIfExists(material, "_ZWrite", 0f);
            SetFloatIfExists(material, "_ZTest", (float)CompareFunction.Equal);
            material.SetShaderPassEnabled("ForwardAdd", false);
            material.SetShaderPassEnabled("ShadowCaster", false);
            ConfigureStencil(
                material,
                stencilReference,
                CompareFunction.Equal,
                StencilOp.Keep,
                0);
            ConfigureHiddenSafetyFallback(material);
            material.renderQueue = renderQueue;
            return material;
        }

        private static Material CreateDitheredClothingOverlayMaterial(
            Material source,
            Color shadowColor,
            float opacity,
            Shader ditherShader,
            Texture2D ditherTexture,
            int stencilReference,
            int renderQueue)
        {
            var material = NdmfObjectRegistry.CreateReplacement(
                source,
                () => new Material(source) { name = $"{source.name}_SilhouetteDither" });
            material.shader = ditherShader;
            var silhouetteColor = Color.white;
            // Match refraction semantics: 0 shows the silhouette most strongly,
            // while 1 restores the original clothing appearance.
            silhouetteColor.a *= 1f - Mathf.Clamp01(opacity);
            ConfigureSilhouetteAppearance(material, silhouetteColor, shadowColor);
            // The clothing was already drawn once. Sampling its main texture again
            // would square its RGB during multiply blending. Use the shader's default
            // white texture; exact-depth testing still excludes cutout-hidden geometry.
            SetTextureIfExists(material, "_MainTex", null);
            SetTextureIfExists(material, "_BaseMap", null);
            ConfigureDitheredMultiply(material, ditherTexture);
            SetFloatIfExists(material, "_Cull", (float)CullMode.Back);
            SetFloatIfExists(material, "_ZWrite", 0f);
            // Draw only on the clothing surface that already populated the depth buffer.
            SetFloatIfExists(material, "_ZTest", (float)CompareFunction.Equal);
            ConfigureStencil(
                material,
                stencilReference,
                CompareFunction.Equal,
                StencilOp.Keep,
                0);
            ConfigureHiddenSafetyFallback(material);
            material.renderQueue = renderQueue;
            return material;
        }

        private static void ConfigureSilhouetteAppearance(
            Material material,
            Color mainColor,
            Color shadowColor)
        {
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
            SetColorIfExists(material, "_Color", mainColor);
            SetColorIfExists(material, "_BaseColor", mainColor);
            SetColorIfExists(material, "_ShadowColor", shadowColor);
            // Leave shader-default white textures unresolved. Texture2D.whiteTexture has
            // DontSaveInEditor and crashes NDMF Manual Bake when AssetSaver persists it.
            SetTextureIfExists(material, "_ShadowColorTex", null);
            SetFloatIfExists(material, "_OutlineEnable", 0f);
            SetFloatIfExists(material, "_OutlineWidth", 0f);
            material.SetShaderPassEnabled("Outline", false);
        }

        private static void ConfigureDitheredMultiply(Material material, Texture2D ditherTexture)
        {
            SetFloatIfExists(material, "_TransparentMode", 1f); // Cutout
            SetFloatIfExists(material, "_UseDither", 1f);
            SetTextureIfExists(material, "_DitherTex", ditherTexture);
            SetFloatIfExists(material, "_DitherMaxValue", 15f); // x4 Bayer
            SetFloatIfExists(material, "_Cutoff", 0.5f);
            SetFloatIfExists(material, "_AlphaToMask", 1f);
            SetFloatIfExists(material, "_AsOverlay", 1f);
            SetFloatIfExists(material, "_ColorMask", (float)(ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue));
            ConfigureMultiplyBlend(material);
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.EnableKeyword("UNITY_UI_ALPHACLIP");
            material.EnableKeyword("ETC1_EXTERNAL_ALPHA");
            material.DisableKeyword("UNITY_UI_CLIP_RECT");
            material.SetShaderPassEnabled("ForwardAdd", false);
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("DepthNormals", false);
            material.SetShaderPassEnabled("DepthForwardOnly", false);
            material.SetShaderPassEnabled("MotionVectors", false);
        }

        private static void ConfigureMultiplyBlend(Material material)
        {
            SetFloatIfExists(material, "_SrcBlend", (float)BlendMode.DstColor);
            SetFloatIfExists(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloatIfExists(material, "_SrcBlendAlpha", (float)BlendMode.One);
            SetFloatIfExists(material, "_DstBlendAlpha", (float)BlendMode.Zero);
            SetFloatIfExists(material, "_BlendOp", (float)BlendOp.Add);
        }

        private static Material CreateRefractionBlurMaterial(
            Material source,
            Shader shader,
            float opacity,
            float blur)
        {
            var material = NdmfObjectRegistry.CreateReplacement(
                source,
                () => new Material(source)
                {
                    name = $"{source.name}_SilhouetteRefractionBlur",
                    shader = shader,
                    renderQueue = RefractionBlurRenderQueue
                });
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
            // The Inspector exposes perceptual blur strength: 0 is sharp and 1 is
            // strongly blurred. lilToon's _Smoothness uses the opposite direction.
            SetFloatIfExists(material, "_Smoothness", 1f - Mathf.Clamp01(blur));
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
            // (for example VRoid waist ribbons) from drawing over visible clothing.
            SetFloatIfExists(material, "_ZTest", (float)CompareFunction.Equal);
            // Do not clip refraction to the hard body mask. The silhouette overlay was
            // already masked; drawing refraction across the visible clothing surface
            // lets its blur kernel extend beyond the silhouette edge.
            DisableStencil(material);
            ConfigureHiddenSafetyFallback(material);
            return material;
        }

        private static void ConfigureHiddenSafetyFallback(Material material)
        {
            material?.SetOverrideTag("VRCFallback", "Hidden");
        }

        private static int CreateStencilReference()
        {
            var randomValue = CreateRandomUInt32();
            return AvailableStencilReferences[(int)(randomValue % (uint)AvailableStencilReferences.Length)];
        }

        private static int CreateBodyStencilRenderQueue()
        {
            var slotCount = (MaximumBodyStencilRenderQueue - MinimumBodyStencilRenderQueue) / 2 + 1;
            var randomValue = CreateRandomUInt32();
            var slot = (int)(randomValue % (uint)slotCount);
            return MinimumBodyStencilRenderQueue + slot * 2;
        }

        private static uint CreateRandomUInt32()
        {
            return BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
        }

        private static void ConfigureStencil(
            Material material,
            int reference,
            CompareFunction comparison,
            StencilOp pass,
            int writeMask)
        {
            SetFloatIfExists(material, "_StencilRef", reference);
            SetFloatIfExists(material, "_StencilReadMask", SilhouetteStencilMask);
            SetFloatIfExists(material, "_StencilWriteMask", writeMask);
            SetFloatIfExists(material, "_StencilComp", (float)comparison);
            SetFloatIfExists(material, "_StencilPass", (float)pass);
            SetFloatIfExists(material, "_StencilFail", (float)StencilOp.Keep);
            SetFloatIfExists(material, "_StencilZFail", (float)StencilOp.Keep);
        }

        private static void DisableStencil(Material material)
        {
            SetFloatIfExists(material, "_StencilRef", 0f);
            SetFloatIfExists(material, "_StencilReadMask", 0f);
            SetFloatIfExists(material, "_StencilWriteMask", 0f);
            SetFloatIfExists(material, "_StencilComp", (float)CompareFunction.Always);
            SetFloatIfExists(material, "_StencilPass", (float)StencilOp.Keep);
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
