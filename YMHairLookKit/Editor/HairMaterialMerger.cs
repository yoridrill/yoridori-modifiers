using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.Rendering;
using YoridoriModifiers.Core.Editor;
using YoridoriModifiers.MToonToLilToon;
using Object = UnityEngine.Object;

namespace YoridoriModifiers.HairLookKit
{
    internal static class HairMaterialMerger
    {
        internal sealed class Result
        {
            public Material mergedMaterial;
        }

        private sealed class RendererMergeTarget
        {
            internal Renderer renderer;
            internal Material[] originalMaterials;
            internal List<int> mergeIndices;
        }

        internal static Result MergeAvatar(
            GameObject root,
            HashSet<Material> selectedForMerge,
            IReadOnlyDictionary<Material, Material> canonicalByMaterial,
            Material representativeMaterial,
            bool enableOutlineCorrection,
            float hairTipOutlineWidth,
            float hairTipRange,
            int atlasMaxSize,
            List<string> warnings,
            bool verboseLog,
            Action<string> onProgress,
            BuildContext buildContext)
        {
            if (root == null || selectedForMerge == null || selectedForMerge.Count == 0) return null;
            var targets = new List<RendererMergeTarget>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                var originalMaterials = renderer.sharedMaterials;
                if (originalMaterials == null || originalMaterials.Length == 0) continue;
                var mergeIndices = Enumerable.Range(0, originalMaterials.Length)
                    .Where(index => originalMaterials[index] != null && selectedForMerge.Contains(originalMaterials[index]))
                    .ToList();
                if (mergeIndices.Count == 0) continue;
                targets.Add(new RendererMergeTarget
                {
                    renderer = renderer,
                    originalMaterials = originalMaterials,
                    mergeIndices = mergeIndices,
                });
            }
            if (targets.Count == 0) return null;

            var globalMaterials = new List<Material>();
            var globalIndexByMaterial = new Dictionary<Material, int>();
            foreach (var target in targets)
            {
                foreach (var materialIndex in target.mergeIndices)
                {
                    var material = target.originalMaterials[materialIndex];
                    var canonical = ResolveCanonical(material, canonicalByMaterial);
                    if (canonical == null || globalIndexByMaterial.ContainsKey(canonical)) continue;
                    globalIndexByMaterial[canonical] = globalMaterials.Count;
                    globalMaterials.Add(canonical);
                }
            }
            if (globalMaterials.Count == 0) return null;

            var globalIndices = Enumerable.Range(0, globalMaterials.Count).ToList();
            var mergedOutputRenderType = ResolveMergedOutputRenderType(globalMaterials, globalIndices);
            var representativeIndex = ResolveMergedRepresentativeIndex(globalMaterials, globalIndices, mergedOutputRenderType);
            var representativeCanonical = ResolveCanonical(representativeMaterial, canonicalByMaterial);
            var rep = representativeCanonical != null && globalIndexByMaterial.ContainsKey(representativeCanonical)
                ? representativeCanonical
                : globalMaterials[representativeIndex];
            if (rep == null) return null;

            var mergedMaterial = NdmfObjectRegistry.CreateReplacement(
                rep,
                () => new Material(rep) { name = $"{rep.name}_Merged" });
            EnsureReferenceTrackableObjectFlags(mergedMaterial);
            ForceMergedRenderType(mergedMaterial, rep, mergedOutputRenderType);
            buildContext?.AssetSaver.SaveAsset(mergedMaterial);

            var globalRects = HairAtlasBuilder.BuildMainAtlas(globalMaterials, globalIndices, mergedMaterial, atlasMaxSize, onProgress, buildContext);
            if (globalMaterials.Count > 1)
            {
                HairAtlasBuilder.BakeOptionalAtlases(globalMaterials, globalIndices, mergedMaterial, globalRects, warnings, verboseLog, buildContext);
            }
            foreach (var target in targets)
            {
                var rectsBySubMesh = new Dictionary<int, Rect>();
                foreach (var materialIndex in target.mergeIndices)
                {
                    var canonical = ResolveCanonical(target.originalMaterials[materialIndex], canonicalByMaterial);
                    if (canonical == null || !globalIndexByMaterial.TryGetValue(canonical, out var globalIndex)) continue;
                    if (globalRects.TryGetValue(globalIndex, out var rect)) rectsBySubMesh[materialIndex] = rect;
                }
                ApplyMergedMaterialAndMesh(
                    target.renderer,
                    target.originalMaterials,
                    target.mergeIndices,
                    mergedMaterial,
                    rectsBySubMesh,
                    enableOutlineCorrection,
                    hairTipOutlineWidth,
                    hairTipRange,
                    buildContext);
            }

            return new Result { mergedMaterial = mergedMaterial };
        }

        private static Material ResolveCanonical(Material material, IReadOnlyDictionary<Material, Material> canonicalByMaterial)
        {
            if (material == null) return null;
            return canonicalByMaterial != null && canonicalByMaterial.TryGetValue(material, out var canonical)
                ? canonical
                : material;
        }

        private static RenderType ResolveMergedOutputRenderType(IReadOnlyList<Material> materials, IReadOnlyList<int> mergeIndices)
        {
            var transparentCount = mergeIndices.Count(index => RenderTypeResolver.ResolveFromMaterial(materials[index]) == RenderType.Transparent);
            return transparentCount > mergeIndices.Count - transparentCount ? RenderType.Transparent : RenderType.Cutout;
        }

        private static int ResolveMergedRepresentativeIndex(IReadOnlyList<Material> materials, IReadOnlyList<int> mergeIndices, RenderType mergedOutputRenderType)
        {
            if (mergedOutputRenderType == RenderType.Transparent)
            {
                return mergeIndices
                    .OrderBy(index => materials[index] != null ? materials[index].renderQueue : int.MaxValue)
                    .ThenBy(index => index)
                    .First();
            }

            return mergeIndices
                .OrderBy(index => RenderTypeResolver.ResolveFromMaterial(materials[index]) == RenderType.Transparent ? 1 : 0)
                .ThenBy(index => index)
                .First();
        }

        private static void ForceMergedRenderType(Material destination, Material source, RenderType outputRenderType)
        {
            if (destination == null) return;
            var cutoff = source != null && source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f;
            if (source != null && source.HasProperty("_AlphaCutoff")) cutoff = source.GetFloat("_AlphaCutoff");

            switch (outputRenderType)
            {
                case RenderType.Transparent:
                    destination.DisableKeyword("_ALPHATEST_ON");
                    destination.EnableKeyword("_ALPHABLEND_ON");
                    destination.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    destination.SetOverrideTag("RenderType", "Transparent");
                    destination.renderQueue = source != null ? source.renderQueue : (int)RenderQueue.Transparent;
                    SetFloatIfAnyExists(destination, new[] { "_Cutoff" }, 0.001f);
                    SetFloatIfAnyExists(destination, new[] { "_UseClipping" }, 0f);
                    SetFloatIfAnyExists(destination, new[] { "_AlphaMode", "_TransparentMode", "_RenderingMode", "_RenderMode" }, 2f);
                    SetFloatIfAnyExists(destination, new[] { "_SrcBlend" }, (float)BlendMode.One);
                    SetFloatIfAnyExists(destination, new[] { "_DstBlend" }, (float)BlendMode.OneMinusSrcAlpha);
                    SetFloatIfAnyExists(destination, new[] { "_ZWrite" }, 0f);
                    break;
                default:
                    destination.EnableKeyword("_ALPHATEST_ON");
                    destination.DisableKeyword("_ALPHABLEND_ON");
                    destination.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    destination.SetOverrideTag("RenderType", "TransparentCutout");
                    destination.renderQueue = (int)RenderQueue.AlphaTest;
                    SetFloatIfAnyExists(destination, new[] { "_Cutoff" }, cutoff);
                    SetFloatIfAnyExists(destination, new[] { "_UseClipping" }, 1f);
                    SetFloatIfAnyExists(destination, new[] { "_AlphaMode", "_TransparentMode", "_RenderingMode", "_RenderMode" }, 1f);
                    SetFloatIfAnyExists(destination, new[] { "_SrcBlend" }, (float)BlendMode.One);
                    SetFloatIfAnyExists(destination, new[] { "_DstBlend" }, (float)BlendMode.Zero);
                    SetFloatIfAnyExists(destination, new[] { "_ZWrite" }, 1f);
                    break;
            }
        }

        private static void ApplyMergedMaterialAndMesh(
            Renderer renderer,
            Material[] originalMaterials,
            IReadOnlyList<int> mergeIndices,
            Material mergedMaterial,
            IReadOnlyDictionary<int, Rect> rectsBySubMesh,
            bool enableOutlineCorrection,
            float hairTipOutlineWidth,
            float hairTipRange,
            BuildContext buildContext)
        {
            var mergeSet = mergeIndices.ToHashSet();
            var newMaterials = new List<Material>();
            var mesh = ResolveMesh(renderer);
            if (mesh == null)
            {
                for (var i = 0; i < originalMaterials.Length; i++)
                {
                    if (mergeSet.Contains(i)) continue;
                    newMaterials.Add(originalMaterials[i]);
                }
                newMaterials.Add(mergedMaterial);
                renderer.sharedMaterials = newMaterials.ToArray();
                return;
            }

            var meshCopy = NdmfObjectRegistry.Clone(mesh);
            EnsureReferenceTrackableObjectFlags(meshCopy);
            buildContext?.AssetSaver.SaveAsset(meshCopy);
            var vertices = meshCopy.vertices.ToList();
            var uv = meshCopy.uv;
            if (uv == null || uv.Length == 0) uv = Enumerable.Repeat(Vector2.zero, vertices.Count).ToArray();
            if (uv.Length < vertices.Count)
            {
                var expanded = new Vector2[vertices.Count];
                for (var i = 0; i < uv.Length; i++) expanded[i] = uv[i];
                uv = expanded;
            }
            var uvList = uv.ToList();
            var normals = meshCopy.normals;
            var tangents = meshCopy.tangents;
            var colors = meshCopy.colors;
            var boneWeights = meshCopy.boneWeights;
            var normalList = normals != null && normals.Length == vertices.Count ? normals.ToList() : null;
            var tangentList = tangents != null && tangents.Length == vertices.Count ? tangents.ToList() : null;
            var colorList = colors != null && colors.Length == vertices.Count ? colors.ToList() : null;
            var boneWeightList = boneWeights != null && boneWeights.Length == vertices.Count ? boneWeights.ToList() : null;
            var outlineAlphaByVertex = Enumerable.Repeat(1f, vertices.Count).ToList();
            var uvRanges = HairOutlineCorrection.BuildOriginalUvRangeBySubMesh(meshCopy, mergeIndices, uvList);

            var newSubMeshes = new List<int[]>();
            var mergedTriangles = new List<int>();
            for (var subMesh = 0; subMesh < meshCopy.subMeshCount; subMesh++)
            {
                var triangles = meshCopy.GetTriangles(subMesh);
                if (!mergeSet.Contains(subMesh))
                {
                    newSubMeshes.Add(triangles);
                    if (subMesh < originalMaterials.Length)
                    {
                        newMaterials.Add(originalMaterials[subMesh]);
                    }
                    continue;
                }

                var rect = rectsBySubMesh != null && rectsBySubMesh.TryGetValue(subMesh, out var found)
                    ? found
                    : new Rect(0f, 0f, 1f, 1f);
                var remap = rect.width < 0.999f || rect.height < 0.999f || rect.xMin > 0f || rect.yMin > 0f;
                foreach (var originalIndex in triangles)
                {
                    if (originalIndex < 0 || originalIndex >= vertices.Count) continue;
                    var nextIndex = originalIndex;
                    if (remap)
                    {
                        var srcUv = originalIndex < uvList.Count ? uvList[originalIndex] : Vector2.zero;
                        nextIndex = vertices.Count;
                        vertices.Add(vertices[originalIndex]);
                        uvList.Add(new Vector2(
                            Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Repeat(srcUv.x, 1f)),
                            Mathf.Lerp(rect.yMin, rect.yMax, Mathf.Repeat(srcUv.y, 1f))));
                        if (normalList != null) normalList.Add(normalList[originalIndex]);
                        if (tangentList != null) tangentList.Add(tangentList[originalIndex]);
                        if (colorList != null) colorList.Add(colorList[originalIndex]);
                        if (boneWeightList != null) boneWeightList.Add(boneWeightList[originalIndex]);
                        outlineAlphaByVertex.Add(HairOutlineCorrection.ResolveTipAlpha(subMesh, originalIndex, uvList, uvRanges, hairTipOutlineWidth, hairTipRange));
                    }
                    else
                    {
                        outlineAlphaByVertex[originalIndex] = Mathf.Min(
                            outlineAlphaByVertex[originalIndex],
                            HairOutlineCorrection.ResolveTipAlpha(subMesh, originalIndex, uvList, uvRanges, hairTipOutlineWidth, hairTipRange));
                    }
                    mergedTriangles.Add(nextIndex);
                }
            }

            meshCopy.SetVertices(vertices);
            meshCopy.SetUVs(0, uvList);
            if (normalList != null) meshCopy.SetNormals(normalList);
            if (tangentList != null) meshCopy.SetTangents(tangentList);
            if (colorList != null) meshCopy.SetColors(colorList);
            if (boneWeightList != null) meshCopy.boneWeights = boneWeightList.ToArray();
            if (vertices.Count > 65535) meshCopy.indexFormat = IndexFormat.UInt32;
            meshCopy.subMeshCount = newSubMeshes.Count + 1;
            for (var i = 0; i < newSubMeshes.Count; i++) meshCopy.SetTriangles(newSubMeshes[i], i, false);
            meshCopy.SetTriangles(mergedTriangles.ToArray(), newSubMeshes.Count, false);

            if (enableOutlineCorrection)
            {
                HairOutlineCorrection.ApplyAverageNormals(meshCopy, mergedTriangles.Distinct().ToArray(), outlineAlphaByVertex);
                SetFloatIfAnyExists(mergedMaterial, new[] { "_OutlineVertexR2Width" }, 2f);
            }

            newMaterials.Add(mergedMaterial);
            ApplyMesh(renderer, meshCopy);
            renderer.sharedMaterials = newMaterials.ToArray();
        }

        internal static Mesh ResolveMesh(Renderer renderer)
        {
            return renderer switch
            {
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                MeshRenderer _ => renderer.GetComponent<MeshFilter>()?.sharedMesh,
                _ => null
            };
        }

        internal static void ApplyMesh(Renderer renderer, Mesh mesh)
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

        private static void EnsureReferenceTrackableObjectFlags(Object generatedObject)
        {
            if (generatedObject == null) return;
            var dontSaveFlags = HideFlags.DontSave | HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor | HideFlags.HideAndDontSave;
            generatedObject.hideFlags &= ~dontSaveFlags;
        }

        private static void SetFloatIfAnyExists(Material material, IReadOnlyList<string> propertyNames, float value)
        {
            if (material == null || propertyNames == null) return;
            foreach (var propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName)) material.SetFloat(propertyName, value);
            }
        }
    }
}
