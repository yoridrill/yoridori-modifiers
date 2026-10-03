using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using static YoridoriModifiers.OutlineExtender.OutlineRendererUtility;
using UnityEngine.Rendering;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.OutlineExtender
{
    internal sealed class OutlineBoundaryPaddingCache
    {
        private readonly Dictionary<Hash128, Texture2D> textures = new();
        internal IEnumerable<Texture2D> Textures => textures.Values.Where(t => t != null);
        internal Texture2D Get(Hash128 key, Func<Texture2D> generate)
        {
            if (textures.TryGetValue(key, out var texture) && (texture != null || ReferenceEquals(texture, null))) return texture;
            return textures[key] = generate();
        }
        internal void DestroyTextures()
        {
            foreach (var texture in Textures) UnityEngine.Object.DestroyImmediate(texture);
            textures.Clear();
        }
        internal void RetainTextures(IEnumerable<Texture2D> retained)
        {
            var keep = new HashSet<Texture2D>(retained);
            foreach (var entry in textures.ToArray())
                if (entry.Value != null && !keep.Contains(entry.Value))
                { UnityEngine.Object.DestroyImmediate(entry.Value); textures.Remove(entry.Key); }
        }
    }

    internal static partial class OutlineBoundaryFoldProcessor
    {
        private readonly struct PaddingFace
        {
            internal readonly Vector2 A, B, C;
            internal PaddingFace(Vector2 a, Vector2 b, Vector2 c) { A = a; B = b; C = c; }
        }

        private readonly struct PaddingEdge
        {
            internal readonly int Face;
            internal readonly Vector2 A, B, Inward;
            internal readonly float CoefficientE, CoefficientF;
            internal PaddingEdge(int face, Vector2 a, Vector2 b, Vector2 inward, float e, float f)
            { Face = face; A = a; B = b; Inward = inward; CoefficientE = e; CoefficientF = f; }
            internal float AvailableDistance(float t)
            {
                var result = float.PositiveInfinity;
                if (CoefficientE + CoefficientF > 0f) result = Mathf.Min(result, (1f - t) / (CoefficientE + CoefficientF));
                if (CoefficientE < 0f) result = Mathf.Min(result, t / -CoefficientE);
                return result;
            }
        }

        internal static float NormalizeFillDistance(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? 1f : Mathf.Clamp(value, 0f, 2f);

        // Does not mutate the renderers: polygon estimation uses the same prepared
        // material infos, while the build/preview installs the returned replacements.
        internal static Dictionary<Material, Material> PrepareBoundaryPadding(GameObject root,
            YMOutlineExtenderComponent component, OutlineBoundaryPaddingCache cache,
            HashSet<Material> blocked = null)
        {
            var replacements = new Dictionary<Material, Material>();
            if (root == null || component == null || !component.enabled || !component.enableOutlineExtender
                || !component.enableBoundaryFold
                || NormalizeFillDistance(component.boundaryFillDistanceMultiplier) <= 0f
                || !(component.outlineWidthMultiplier > 0f)) return replacements;
            var selections = OutlineHairMaterialControls.Create(root).CreateSelectionSet(component.boundaryMaterialSelections);
            foreach (var material in OutlineMaterialUtility.CollectMaterials(root))
            {
                if (blocked?.Contains(material) == true
                    || !selections.IsSelected(material, out var mixed) || mixed
                    || !OutlineMaterialUtility.TryGetBoundaryMaterialInfo(material, out var info, out _)
                    || !CanPadMaterial(material, info)) continue;
                var faces = new List<PaddingFace>(); var edges = new List<PaddingEdge>();
                CollectPaddingGeometry(root, material, info.MainTexture, faces, edges);
                if (edges.Count == 0) continue;
                var distance = info.Outline.ObjectSpaceWidth * Mathf.Clamp(component.outlineWidthMultiplier, 0f, 2f)
                    * NormalizeFillDistance(component.boundaryFillDistanceMultiplier);
                var signature = PaddingSignature(info, distance, faces, edges);
                var texture = cache.Get(signature, () => GenerateBoundaryPadding(info, distance, faces, edges));
                if (texture == null) continue;
                var generated = NdmfObjectRegistry.Clone(material);
                generated.name = material.name + "_BoundaryFilled";
                var main = OutlineMaterialUtility.ResolveMainTextureProperty(material);
                generated.SetTexture(main, texture);
                // Native shade maps use the transformed main UV directly. lilToon's
                // outline texture has an independent transform, which must match.
                foreach (var property in new[] { "_ShadeTex", "_ShadeTexture", "_ShadowColorTex", "_Shadow2ndColorTex", "_Shadow3rdColorTex", "_OutlineTex" })
                    if (material.HasProperty(property) && material.GetTexture(property) == info.MainTexture
                        && (AuxiliaryUsesMainUv(material, property)
                            || (material.GetTextureScale(property) == info.TextureScale
                                && material.GetTextureOffset(property) == info.TextureOffset))) generated.SetTexture(property, texture);
                replacements.Add(material, generated);
            }
            return replacements;
        }

        private static bool CanPadMaterial(Material material, OutlineBoundaryMaterialInfo info) =>
            info.TestAlpha && info.TextureCutoff > 0f && info.TextureCutoff < 1f
            && (!lilToon.lilMaterialUtils.CheckShaderIslilToon(material)
                || OutlineMaterialUtility.TryGetBakeMaterialInfo(material, out _, out _));

        private static bool AuxiliaryUsesMainUv(Material material, string property) =>
            YoridoriModifiers.MToonToLilToon.MToonDetector.IsMToonLike(material)
                && (property == "_ShadeTex" || property == "_ShadeTexture")
            || lilToon.lilMaterialUtils.CheckShaderIslilToon(material)
                && (property == "_ShadowColorTex" || property == "_Shadow2ndColorTex" || property == "_Shadow3rdColorTex");

        internal static void InstallPadding(GameObject root, IReadOnlyDictionary<Material, Material> replacements, BuildContext context)
        {
            var savedTextures = new HashSet<Texture>();
            foreach (var material in replacements.Values)
            {
                context?.AssetSaver.SaveAsset(material);
                var property = OutlineMaterialUtility.ResolveMainTextureProperty(material);
                var texture = material.GetTexture(property);
                if (savedTextures.Add(texture)) context?.AssetSaver.SaveAsset(texture);
            }
            OutlineRendererUtility.ReplaceMaterials(root, replacements);
        }

        private static void CollectPaddingGeometry(GameObject root, Material target, Texture2D texture,
            List<PaddingFace> faces, List<PaddingEdge> edges)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = GetMesh(renderer);
                if (mesh == null || mesh.vertexCount == 0) continue;
                var positions = mesh.vertices; var uv = mesh.uv;
                if (uv.Length != positions.Length) continue;
                var counts = new Dictionary<PositionEdgeKey, int>();
                var triangles = new int[mesh.subMeshCount][];
                for (var sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    triangles[sub] = mesh.GetTriangles(sub);
                    for (var i = 0; i + 2 < triangles[sub].Length; i += 3)
                        for (var j = 0; j < 3; j++)
                        {
                            var key = new PositionEdgeKey(positions[triangles[sub][i + j]], positions[triangles[sub][i + (j + 1) % 3]]);
                            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
                        }
                }
                var materials = renderer.sharedMaterials;
                for (var sub = 0; sub < Math.Min(materials.Length, triangles.Length); sub++)
                {
                    var material = materials[sub]; if (material == null || triangles[sub] == null) continue;
                    var property = OutlineMaterialUtility.ResolveMainTextureProperty(material);
                    if (property == null || material.GetTexture(property) != texture) continue;
                    var scale = material.GetTextureScale(property); var offset = material.GetTextureOffset(property);
                    for (var i = 0; i + 2 < triangles[sub].Length; i += 3)
                    {
                        var indices = new[] { triangles[sub][i], triangles[sub][i + 1], triangles[sub][i + 2] };
                        var coords = indices.Select(v => Vector2.Scale(uv[v], scale) + offset).ToArray();
                        var face = faces.Count; faces.Add(new PaddingFace(coords[0], coords[1], coords[2]));
                        if (material != target || !mesh.HasVertexAttribute(VertexAttribute.Normal)) continue;
                        for (var j = 0; j < 3; j++)
                        {
                            var a = indices[j]; var b = indices[(j + 1) % 3]; var c = indices[(j + 2) % 3];
                            if (counts[new PositionEdgeKey(positions[a], positions[b])] != 1) continue;
                            var e = positions[b] - positions[a]; var f = positions[c] - positions[a];
                            var ee = (double)Vector3.Dot(e, e); var ef = (double)Vector3.Dot(e, f); var ff = (double)Vector3.Dot(f, f);
                            var determinant = ee * ff - ef * ef;
                            if (ee <= 1e-24 || determinant <= ee * ff * 1e-12) continue;
                            var inward = (f - e * (float)(ef / ee)).normalized;
                            var ei = (double)Vector3.Dot(e, inward); var fi = (double)Vector3.Dot(f, inward);
                            var coefficientE = (float)((ff * ei - ef * fi) / determinant);
                            var coefficientF = (float)((ee * fi - ef * ei) / determinant);
                            var uvA = coords[j]; var uvB = coords[(j + 1) % 3]; var uvC = coords[(j + 2) % 3];
                            var uvInward = (uvB - uvA) * coefficientE + (uvC - uvA) * coefficientF;
                            if (Mathf.Abs(Cross(uvB - uvA, uvC - uvA)) < 1e-14f) continue;
                            edges.Add(new PaddingEdge(face, uvA, uvB, uvInward, coefficientE, coefficientF));
                        }
                    }
                }
            }
        }

        private static Hash128 PaddingSignature(OutlineBoundaryMaterialInfo info, float distance,
            List<PaddingFace> faces, List<PaddingEdge> edges)
        {
            ulong a = 1469598103934665603UL, b = 1099511628211UL;
            void Mix(int value) { a = (a ^ (uint)value) * 1099511628211UL; b = (b ^ (uint)value) * 14029467366897019727UL; }
            void Vector(Vector2 value) { Mix(value.x.GetHashCode()); Mix(value.y.GetHashCode()); }
            Mix(info.MainTexture.GetInstanceID()); Mix(info.MainTexture.imageContentsHash.GetHashCode());
            Mix(info.MainTexture.width); Mix(info.MainTexture.height); Mix((int)info.MainTexture.wrapModeU); Mix((int)info.MainTexture.wrapModeV);
            Mix((int)info.MainTexture.filterMode); Mix(info.MainTexture.mipmapCount); Mix(info.MainTexture.isDataSRGB ? 1 : 0);
            Mix(info.TextureCutoff.GetHashCode()); Mix(distance.GetHashCode());
            foreach (var face in faces) { Vector(face.A); Vector(face.B); Vector(face.C); }
            foreach (var edge in edges)
            { Mix(edge.Face); Vector(edge.A); Vector(edge.B); Vector(edge.Inward); Mix(edge.CoefficientE.GetHashCode()); Mix(edge.CoefficientF.GetHashCode()); }
            return new Hash128(a, b);
        }

        private static Texture2D GenerateBoundaryPadding(OutlineBoundaryMaterialInfo info, float distance,
            List<PaddingFace> faces, List<PaddingEdge> edges)
        {
            var source = info.MainTexture; var width = source.width; var height = source.height;
            var pixels = OutlineSdfCache.ReadPixels(source, 0, width, height);
            var sampler = new TextureSampler(source, pixels);
            var islands = PaddingIslands(faces);
            var ownership = new int[pixels.Length]; Array.Fill(ownership, -1);
            var repeatU = source.wrapModeU == TextureWrapMode.Repeat; var repeatV = source.wrapModeV == TextureWrapMode.Repeat;
            for (var i = 0; i < faces.Count; i++)
                RasterPaddingFace(faces[i], width, height, repeatU, repeatV, (x, y) =>
                {
                    var index = y * width + x;
                    ownership[index] = ownership[index] == -1 || ownership[index] == islands[i] ? islands[i] : -2;
                });
            var outputPixels = (Color[])pixels.Clone(); var changed = false;
            foreach (var edge in edges)
            {
                var texelInward = Vector2.Scale(edge.Inward, new Vector2(width, height));
                var speed = texelInward.magnitude;
                if (!(speed > 1e-8f)) continue;
                var steps = Mathf.Clamp(Mathf.CeilToInt(Vector2.Scale(edge.B - edge.A, new Vector2(width, height)).magnitude * 2f), 1, 32768);
                for (var sample = 0; sample <= steps; sample++)
                {
                    var t = (float)sample / steps; var uv = Vector2.LerpUnclamped(edge.A, edge.B, t);
                    var limit = Mathf.Min(distance, edge.AvailableDistance(t));
                    if (limit < 0f) continue;
                    var found = sampler.Alpha(uv) >= info.TextureCutoff; var hit = 0f;
                    var searchSteps = Mathf.Clamp(Mathf.CeilToInt(limit * speed * 4f), 1, 4096);
                    var previous = 0f;
                    for (var probe = 1; !found && probe <= searchSteps; probe++)
                    {
                        var d = limit * probe / searchSteps;
                        if (sampler.Alpha(uv + edge.Inward * d) >= info.TextureCutoff)
                        {
                            var low = previous; var high = d;
                            for (var iteration = 0; iteration < 20; iteration++)
                            {
                                var middle = (low + high) * 0.5f;
                                if (sampler.Alpha(uv + edge.Inward * middle) >= info.TextureCutoff) high = middle; else low = middle;
                            }
                            found = true; hit = high;
                        }
                        previous = d;
                    }
                    if (!found) continue;
                    // Read farther into the same source triangle to avoid copying
                    // transparent antialiasing colors. Always sample the original image.
                    var colorUv = uv + edge.Inward * Mathf.Min(edge.AvailableDistance(t), hit + 0.75f / speed);
                    var color = sampler.Sample(colorUv); color.a = 1f;
                    var start = Vector2.Scale(uv, new Vector2(width, height)) - Vector2.one * 0.5f;
                    var end = start + texelInward * hit;
                    var outside = start - texelInward / speed * 2f;
                    PaintPaddingStroke(outside, end, width, height, repeatU, repeatV, (x, y) =>
                    {
                        var index = y * width + x;
                        // Texels covered by another UV island must never be painted.
                        if (ownership[index] != -1 && ownership[index] != islands[edge.Face]) return;
                        if (pixels[index].a >= info.TextureCutoff || outputPixels[index].Equals(color)) return;
                        outputPixels[index] = color; changed = true;
                    });
                }
            }
            if (!changed) return null;
            if (source.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear)
                for (var i = 0; i < outputPixels.Length; i++) outputPixels[i] = outputPixels[i].gamma;
            var output = new Texture2D(width, height, TextureFormat.RGBA32, source.mipmapCount, !source.isDataSRGB)
            {
                name = source.name + "_BoundaryFilled", wrapModeU = source.wrapModeU, wrapModeV = source.wrapModeV,
                filterMode = source.filterMode, anisoLevel = source.anisoLevel, mipMapBias = source.mipMapBias
            };
            try
            {
                output.SetPixels(outputPixels); output.Apply(true, false);
                GeneratedTextureUtility.ConfigureRuntimeGeneratedTexture(output);
                YMTextureRegistry.Register(output, source, YMTextureUsage.ColorWithAlpha,
                    "YM Outline Extender Boundary Fill", YMTextureCompressionQuality.High);
                return output;
            }
            catch { UnityEngine.Object.DestroyImmediate(output); throw; }
        }

        private static int[] PaddingIslands(List<PaddingFace> faces)
        {
            var parent = Enumerable.Range(0, faces.Count).ToArray();
            int Root(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            var vertexFaces = new Dictionary<Vector2, int>();
            for (var i = 0; i < faces.Count; i++)
                foreach (var uv in new[] { faces[i].A, faces[i].B, faces[i].C })
                    if (vertexFaces.TryGetValue(uv, out var other)) parent[Root(i)] = Root(other); else vertexFaces[uv] = i;
            for (var i = 0; i < parent.Length; i++) parent[i] = Root(i);
            return parent;
        }

        private static void RasterPaddingFace(PaddingFace face, int width, int height, bool repeatU, bool repeatV, Action<int, int> paint)
        {
            var min = Vector2.Min(face.A, Vector2.Min(face.B, face.C)); var max = Vector2.Max(face.A, Vector2.Max(face.B, face.C));
            var firstX = repeatU ? Mathf.CeilToInt(-max.x) : 0; var lastX = repeatU ? Mathf.FloorToInt(1f - min.x) : 0;
            var firstY = repeatV ? Mathf.CeilToInt(-max.y) : 0; var lastY = repeatV ? Mathf.FloorToInt(1f - min.y) : 0;
            for (var sy = firstY; sy <= lastY; sy++) for (var sx = firstX; sx <= lastX; sx++)
            {
                var shift = new Vector2(sx, sy);
                var a = Vector2.Scale(face.A + shift, new Vector2(width, height)) - Vector2.one * 0.5f;
                var b = Vector2.Scale(face.B + shift, new Vector2(width, height)) - Vector2.one * 0.5f;
                var c = Vector2.Scale(face.C + shift, new Vector2(width, height)) - Vector2.one * 0.5f;
                var area = Cross(b - a, c - a); if (Mathf.Abs(area) < 1e-8f) continue;
                if (area < 0f) (b, c) = (c, b);
                var low = Vector2.Min(a, Vector2.Min(b, c)); var high = Vector2.Max(a, Vector2.Max(b, c));
                for (var y = Mathf.Max(0, Mathf.FloorToInt(low.y)); y <= Mathf.Min(height - 1, Mathf.CeilToInt(high.y)); y++)
                    for (var x = Mathf.Max(0, Mathf.FloorToInt(low.x)); x <= Mathf.Min(width - 1, Mathf.CeilToInt(high.x)); x++)
                    {
                        var p = new Vector2(x, y);
                        if (Cross(b - a, p - a) >= -1e-5f && Cross(c - b, p - b) >= -1e-5f && Cross(a - c, p - c) >= -1e-5f) paint(x, y);
                    }
            }
        }

        private static void PaintPaddingStroke(Vector2 a, Vector2 b, int width, int height, bool repeatU, bool repeatV, Action<int, int> paint)
        {
            const float radius = 0.75f;
            var low = Vector2.Min(a, b) - Vector2.one * radius; var high = Vector2.Max(a, b) + Vector2.one * radius;
            var ab = b - a;
            for (var y = Mathf.FloorToInt(low.y); y <= Mathf.CeilToInt(high.y); y++)
                for (var x = Mathf.FloorToInt(low.x); x <= Mathf.CeilToInt(high.x); x++)
                {
                    if ((!repeatU && (x < 0 || x >= width)) || (!repeatV && (y < 0 || y >= height))) continue;
                    var p = new Vector2(x, y); var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-20f));
                    if ((p - (a + ab * t)).sqrMagnitude > radius * radius) continue;
                    paint(repeatU ? (x % width + width) % width : x, repeatV ? (y % height + height) % height : y);
                }
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
