using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using Unity.Collections;
using UnityEngine;
using static YoridoriModifiers.OutlineExtender.OutlineRendererUtility;
using UnityEngine.Rendering;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.OutlineExtender
{
    internal readonly struct OutlineBoundaryFoldStats
    {
        internal readonly int Renderers;
        internal readonly int Edges;
        internal readonly int Vertices;
        internal readonly int Triangles;

        internal OutlineBoundaryFoldStats(int renderers, int edges, int vertices, int triangles)
        {
            Renderers = renderers; Edges = edges; Vertices = vertices; Triangles = triangles;
        }

        internal OutlineBoundaryFoldStats Add(OutlineBoundaryFoldStats other) =>
            new(Renderers + other.Renderers, Edges + other.Edges,
                Vertices + other.Vertices, Triangles + other.Triangles);
    }

    /// <summary>
    /// Adds a very short inward strip to selected open boundaries. The strip uses the
    /// existing submesh/material so the material's normal outline pass can cover the edge.
    /// Source vertex positions and all source assets remain untouched. The derived
    /// mesh uses softened crease normals for both shading and outline expansion.
    /// </summary>
    internal static partial class OutlineBoundaryFoldProcessor
    {
        internal const float FoldDepthRatio = 1f;
        internal const int DefaultFoldAngleDegrees = 5;
        internal const float DefaultFoldLengthMultiplier = 2f;
        internal const float DefaultSupportBandWidthMultiplier = 0.5f;
        private const float DirectionEpsilon = 1e-8f;
        private const float NormalWeldTolerance = 1e-5f;

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            internal readonly int A, B;
            internal EdgeKey(int a, int b) { A = Math.Min(a, b); B = Math.Max(a, b); }
            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => unchecked(A * 397 ^ B);
        }

        private readonly struct DirectedEdge
        {
            internal readonly int A, B, Third, SubMesh;
            internal DirectedEdge(int a, int b, int third, int subMesh)
            { A = a; B = b; Third = third; SubMesh = subMesh; }
        }

        private readonly struct PositionEdgeKey : IEquatable<PositionEdgeKey>
        {
            private readonly Vector3 a, b;
            internal PositionEdgeKey(Vector3 first, Vector3 second)
            {
                if (Compare(first, second) <= 0) { a = first; b = second; }
                else { a = second; b = first; }
            }
            public bool Equals(PositionEdgeKey other) => a.Equals(other.a) && b.Equals(other.b);
            public override bool Equals(object obj) => obj is PositionEdgeKey other && Equals(other);
            public override int GetHashCode() => unchecked(a.GetHashCode() * 397 ^ b.GetHashCode());
            private static int Compare(Vector3 left, Vector3 right)
            {
                var x = left.x.CompareTo(right.x); if (x != 0) return x;
                var y = left.y.CompareTo(right.y); return y != 0 ? y : left.z.CompareTo(right.z);
            }
        }

        private readonly struct WeldPositionKey : IEquatable<WeldPositionKey>
        {
            private readonly int x, y, z;
            internal WeldPositionKey(Vector3 position)
            {
                x = Mathf.RoundToInt(position.x / NormalWeldTolerance);
                y = Mathf.RoundToInt(position.y / NormalWeldTolerance);
                z = Mathf.RoundToInt(position.z / NormalWeldTolerance);
            }
            public bool Equals(WeldPositionKey other) => x == other.x && y == other.y && z == other.z;
            public override bool Equals(object obj) => obj is WeldPositionKey other && Equals(other);
            public override int GetHashCode() => unchecked((x * 397 ^ y) * 397 ^ z);
        }

        private sealed class BlendFrame
        {
            internal string Name;
            internal float Weight;
            internal Vector3[] Vertices, Normals, Tangents;
        }

        internal sealed class TextureSampler
        {
            private readonly Texture2D texture;
            private readonly Color[] pixels;
            internal TextureSampler(Texture2D texture, Color[] sourcePixels = null)
            {
                this.texture = texture;
                pixels = sourcePixels ?? OutlineSdfCache.ReadPixels(texture, 0, texture.width, texture.height);
            }

            internal Color Sample(Vector2 uv)
            {
                var x = Wrap(uv.x, texture.wrapModeU) * texture.width - 0.5f;
                var y = Wrap(uv.y, texture.wrapModeV) * texture.height - 0.5f;
                var ix = Mathf.FloorToInt(x); var iy = Mathf.FloorToInt(y);
                var x0 = PixelIndex(ix, texture.width, texture.wrapModeU); var x1 = PixelIndex(ix + 1, texture.width, texture.wrapModeU);
                var y0 = PixelIndex(iy, texture.height, texture.wrapModeV); var y1 = PixelIndex(iy + 1, texture.height, texture.wrapModeV);
                return Color.Lerp(Color.Lerp(pixels[y0 * texture.width + x0], pixels[y0 * texture.width + x1], x - ix),
                    Color.Lerp(pixels[y1 * texture.width + x0], pixels[y1 * texture.width + x1], x - ix), y - iy);
            }

            internal float Alpha(Vector2 uv)
            {
                uv.x = Wrap(uv.x, texture.wrapModeU);
                uv.y = Wrap(uv.y, texture.wrapModeV);
                // Match normalized GPU sampling: texel centers are (i + 0.5) / size.
                var x = uv.x * texture.width - 0.5f;
                var y = uv.y * texture.height - 0.5f;
                var floorX = Mathf.FloorToInt(x); var floorY = Mathf.FloorToInt(y);
                var x0 = PixelIndex(floorX, texture.width, texture.wrapModeU);
                var y0 = PixelIndex(floorY, texture.height, texture.wrapModeV);
                var x1 = PixelIndex(floorX + 1, texture.width, texture.wrapModeU);
                var y1 = PixelIndex(floorY + 1, texture.height, texture.wrapModeV);
                var a0 = Mathf.Lerp(pixels[y0 * texture.width + x0].a,
                    pixels[y0 * texture.width + x1].a, x - floorX);
                var a1 = Mathf.Lerp(pixels[y1 * texture.width + x0].a,
                    pixels[y1 * texture.width + x1].a, x - floorX);
                return Mathf.Lerp(a0, a1, y - floorY);
            }

            private static int PixelIndex(int index, int size, TextureWrapMode mode) =>
                mode == TextureWrapMode.Repeat ? (index % size + size) % size : Mathf.Clamp(index, 0, size - 1);

            private static float Wrap(float value, TextureWrapMode mode) =>
                mode == TextureWrapMode.Repeat ? Mathf.Repeat(value, 1f) : Mathf.Clamp01(value);
        }

        internal static OutlineBoundaryFoldStats Apply(GameObject root, YMOutlineExtenderComponent component,
            BuildContext context)
        {
            if (root == null || component == null || !component.enableBoundaryFold) return default;
            var selections = OutlineHairMaterialControls.Create(root).CreateSelectionSet(component.boundaryMaterialSelections);
            var total = default(OutlineBoundaryFoldStats);
            var textureSamplers = new Dictionary<Texture2D, TextureSampler>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var source = GetMesh(renderer);
                if (source == null || source.vertexCount == 0 || source.normals.Length != source.vertexCount) continue;
                var targets = CollectTargets(renderer, source, component, selections, true);
                if (targets.Count == 0) continue;

                var mesh = NdmfObjectRegistry.Clone(source);
                mesh.name = source.name + "_OutlineBoundaryFold";
                var stats = AddFolds(source, mesh, targets, component.outlineWidthMultiplier, textureSamplers,
                    NormalizeFoldAngle(component.boundaryFoldAngleDegrees),
                    NormalizeFoldLength(component.boundaryFoldLengthMultiplier),
                    NormalizeSupportBandWidth(component.boundarySupportBandWidthMultiplier),
                    component.taperBoundaryEndpoints);
                if (stats.Edges == 0)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                    continue;
                }
                context?.AssetSaver.SaveAsset(mesh);
                SetMesh(renderer, mesh);
                total = total.Add(new OutlineBoundaryFoldStats(1, stats.Edges, stats.Vertices, stats.Triangles));
            }
            return total;
        }

        private static Dictionary<int, OutlineBoundaryMaterialInfo> CollectTargets(Renderer renderer,
            Mesh source, YMOutlineExtenderComponent component, OutlineMaterialSelectionSet selections, bool warn,
            IReadOnlyDictionary<Material, Material> padding = null)
        {
            var materials = renderer.sharedMaterials;
            var targets = new Dictionary<int, OutlineBoundaryMaterialInfo>();
            for (var sub = 0; sub < Math.Min(source.subMeshCount, materials.Length); sub++)
            {
                var material = materials[sub];
                if (material == null) continue;
                var selected = selections.IsSelected(material, out var mixed);
                if (mixed)
                {
                    if (warn) LogUtility.Warning("YM Outline Extender",
                        $"{material.name}: merged ON/OFF Mesh Boundary selections; Boundary Fold skipped.", component);
                    continue;
                }
                if (!selected) continue;
                if (padding != null && padding.TryGetValue(material, out var filled)) material = filled;
                if (OutlineMaterialUtility.TryGetBoundaryMaterialInfo(material, out var info, out var reason)) targets[sub] = info;
                else if (warn) LogUtility.Warning("YM Outline Extender", $"{material.name}: {reason} Boundary Fold skipped.", component);
            }
            return targets;
        }

        internal static (int before, int after) EstimatePolygons(GameObject root, YMOutlineExtenderComponent component,
            OutlineBoundaryPaddingCache paddingCache = null)
        {
            if (root == null || component == null || !component.enabled || !component.enableOutlineExtender || !component.enableBoundaryFold) return default;
            var selections = OutlineHairMaterialControls.Create(root).CreateSelectionSet(component.boundaryMaterialSelections);
            var before = 0; var after = 0;
            var ownsPaddingCache = paddingCache == null;
            paddingCache ??= new OutlineBoundaryPaddingCache();
            Dictionary<Material, Material> padding = null;
            try
            {
                padding = PrepareBoundaryPadding(root, component, paddingCache);
                if (!ownsPaddingCache) paddingCache.RetainTextures(padding.Values.Select(material =>
                    material.GetTexture(OutlineMaterialUtility.ResolveMainTextureProperty(material)) as Texture2D));
                var samplers = new Dictionary<Texture2D, TextureSampler>();
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var source = GetMesh(renderer);
                    if (source == null || source.vertexCount == 0 || !source.HasVertexAttribute(VertexAttribute.Normal)) continue;
                    var targets = CollectTargets(renderer, source, component, selections, false, padding);
                    if (targets.Count == 0) continue;
                    var count = CountTriangles(source);
                    var generated = new Mesh();
                    try
                    {
                        var stats = AddFolds(source, generated, targets, component.outlineWidthMultiplier, samplers,
                            NormalizeFoldAngle(component.boundaryFoldAngleDegrees),
                            NormalizeFoldLength(component.boundaryFoldLengthMultiplier),
                            NormalizeSupportBandWidth(component.boundarySupportBandWidthMultiplier), component.taperBoundaryEndpoints);
                        // Include neighboring submeshes whose faces are subdivided to
                        // preserve shared edges, even if their materials are unselected.
                        before += count; after += count + stats.Triangles;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(generated); }
                }
                return (before, after);
            }
            finally
            {
                if (padding != null) foreach (var material in padding.Values) UnityEngine.Object.DestroyImmediate(material);
                if (ownsPaddingCache) paddingCache.DestroyTextures();
            }
        }

        internal static OutlineBoundaryFoldStats AddFolds(Mesh source, Mesh destination,
            IReadOnlyDictionary<int, OutlineBoundaryMaterialInfo> targets, float widthMultiplier,
            Dictionary<Texture2D, TextureSampler> samplers = null,
            float foldAngleDegrees = DefaultFoldAngleDegrees,
            float foldLengthMultiplier = DefaultFoldLengthMultiplier,
            float supportBandWidthMultiplier = DefaultSupportBandWidthMultiplier,
            bool taperEndpoints = true)
            => AddFoldsCore(source, destination, targets, widthMultiplier, samplers, foldAngleDegrees,
                foldLengthMultiplier, supportBandWidthMultiplier, null, taperEndpoints, null);

        private static OutlineBoundaryFoldStats AddFoldsCore(Mesh source, Mesh destination,
            IReadOnlyDictionary<int, OutlineBoundaryMaterialInfo> targets, float widthMultiplier,
            Dictionary<Texture2D, TextureSampler> samplers, float foldAngleDegrees,
            float foldLengthMultiplier, float supportBandWidthMultiplier,
            IReadOnlyDictionary<WeldPositionKey, Vector3> originalGeometryNormals,
            bool taperEndpoints, HashSet<WeldPositionKey> originalTaperEndpoints, bool alphaBoundarySplit = false)
        {
            var oldCount = source.vertexCount;
            var sourceVertices = source.vertices;
            var sourceNormals = source.normals;
            if (oldCount == 0 || sourceNormals.Length != oldCount) return default;
            var sourceUv0 = new List<Vector4>();
            source.GetUVs(0, sourceUv0);

            var triangles = new List<int>[source.subMeshCount];
            var globalCounts = new Dictionary<PositionEdgeKey, int>();
            var directed = new Dictionary<EdgeKey, DirectedEdge>[source.subMeshCount];
            for (var sub = 0; sub < source.subMeshCount; sub++)
            {
                triangles[sub] = new List<int>();
                directed[sub] = new Dictionary<EdgeKey, DirectedEdge>();
                if (source.GetTopology(sub) != MeshTopology.Triangles) continue;
                triangles[sub].AddRange(source.GetTriangles(sub));
                var localCounts = new Dictionary<EdgeKey, int>();
                for (var i = 0; i + 2 < triangles[sub].Count; i += 3)
                {
                    AddEdge(triangles[sub][i], triangles[sub][i + 1], triangles[sub][i + 2], sub,
                        sourceVertices, localCounts, globalCounts, directed[sub]);
                    AddEdge(triangles[sub][i + 1], triangles[sub][i + 2], triangles[sub][i], sub,
                        sourceVertices, localCounts, globalCounts, directed[sub]);
                    AddEdge(triangles[sub][i + 2], triangles[sub][i], triangles[sub][i + 1], sub,
                        sourceVertices, localCounts, globalCounts, directed[sub]);
                }
                foreach (var key in localCounts.Where(pair => pair.Value != 1).Select(pair => pair.Key).ToArray())
                    directed[sub].Remove(key);
            }

            samplers ??= new Dictionary<Texture2D, TextureSampler>();
            var chosen = new List<(DirectedEdge edge, float depth)>();
            var physicalBoundary = new List<(DirectedEdge edge, float depth)>();
            foreach (var target in targets)
            {
                if (target.Key < 0 || target.Key >= directed.Length) continue;
                var info = target.Value;
                var depth = info.Outline.ObjectSpaceWidth * Mathf.Clamp(widthMultiplier, 0f, 2f) * FoldDepthRatio;
                if (!(depth > 1e-7f)) continue;
                TextureSampler sampler = null;
                if (info.TestAlpha && !samplers.TryGetValue(info.MainTexture, out sampler))
                    samplers[info.MainTexture] = sampler = new TextureSampler(info.MainTexture);
                var materialUvValid = sourceUv0.Count == oldCount;
                foreach (var pair in directed[target.Key])
                {
                    var physicalKey = new PositionEdgeKey(sourceVertices[pair.Value.A], sourceVertices[pair.Value.B]);
                    if (!globalCounts.TryGetValue(physicalKey, out var count) || count != 1) continue;
                    var edge = pair.Value;
                    physicalBoundary.Add((edge, depth));
                    if (info.TestAlpha && materialUvValid
                        && !VisibleAtBoundary(edge, sourceUv0, info, sampler, alphaBoundarySplit)) continue;
                    chosen.Add((edge, depth));
                }
            }
            if (chosen.Count == 0) return default;

            // Classify the source boundary before inserting support vertices. A
            // single-edge ribbon must remain excluded after subdivision too.
            HashSet<PositionEdgeKey> eligibleTaperEdges = null;
            var taperPositions = originalTaperEndpoints ?? (taperEndpoints
                ? FindTaperEndpoints(physicalBoundary, sourceVertices, out eligibleTaperEdges) : new HashSet<WeldPositionKey>());
            var alphaCuts = originalTaperEndpoints == null && taperEndpoints && sourceUv0.Count == oldCount
                ? FindAlphaTaperCuts(physicalBoundary, sourceVertices, sourceUv0, targets, samplers, eligibleTaperEdges, taperPositions)
                : null;

            var angleRadians = Mathf.Clamp(foldAngleDegrees, 0f, 89f) * Mathf.Deg2Rad;
            var inwardRatio = Mathf.Cos(angleRadians) * Mathf.Max(0f, foldLengthMultiplier);
            var backwardRatio = Mathf.Sin(angleRadians) * Mathf.Max(0f, foldLengthMultiplier);

            // Vertices shared by more than two boundary edges are non-manifold for a strip.
            // Skip their incident edges instead of risking an invalid build.
            var degrees = new Dictionary<(int sub, int vertex), int>();
            foreach (var item in chosen)
            {
                Increment(degrees, (item.edge.SubMesh, item.edge.A));
                Increment(degrees, (item.edge.SubMesh, item.edge.B));
            }
            chosen.RemoveAll(item => degrees[(item.edge.SubMesh, item.edge.A)] > 2
                                     || degrees[(item.edge.SubMesh, item.edge.B)] > 2);
            if (chosen.Count == 0) return default;

            // Only positions touched by this Boundary Fold are smoothed. Rebuild their
            // normals from area-weighted adjacent triangle normals, then share the result
            // across position-welded copies. Averaging imported normals alone is ineffective
            // when every split vertex was authored with the same front-facing normal.
            var boundaryPositions = new HashSet<WeldPositionKey>();
            foreach (var item in chosen)
            {
                boundaryPositions.Add(new WeldPositionKey(sourceVertices[item.edge.A]));
                boundaryPositions.Add(new WeldPositionKey(sourceVertices[item.edge.B]));
            }
            var geometricNormalSums = new Dictionary<WeldPositionKey, Vector3>();
            for (var sub = 0; sub < source.subMeshCount; sub++)
            {
                if (source.GetTopology(sub) != MeshTopology.Triangles) continue;
                var sourceTriangles = source.GetTriangles(sub);
                for (var i = 0; i + 2 < sourceTriangles.Length; i += 3)
                {
                    var a = sourceTriangles[i];
                    var b = sourceTriangles[i + 1];
                    var c = sourceTriangles[i + 2];
                    // Keep the cross product unnormalized for area-weighted averaging.
                    var faceNormal = Vector3.Cross(sourceVertices[b] - sourceVertices[a],
                        sourceVertices[c] - sourceVertices[a]);
                    if (faceNormal.sqrMagnitude <= 1e-24f) continue;
                    AddGeometricNormal(a, faceNormal);
                    AddGeometricNormal(b, faceNormal);
                    AddGeometricNormal(c, faceNormal);
                }
            }

            var importedNormalSums = new Dictionary<WeldPositionKey, Vector3>();
            var normalFallbacks = new Dictionary<WeldPositionKey, Vector3>();
            for (var i = 0; i < oldCount; i++)
            {
                var position = new WeldPositionKey(sourceVertices[i]);
                if (!boundaryPositions.Contains(position)) continue;
                var normal = sourceNormals[i].sqrMagnitude > DirectionEpsilon
                    ? sourceNormals[i].normalized : Vector3.zero;
                importedNormalSums[position] = importedNormalSums.TryGetValue(position, out var sum)
                    ? sum + normal : normal;
                if (!normalFallbacks.ContainsKey(position)) normalFallbacks[position] = normal;
            }
            var surfaceNormals = boundaryPositions.ToDictionary(key => key, key =>
                originalGeometryNormals != null && originalGeometryNormals.TryGetValue(key, out var original)
                    ? original : geometricNormalSums.TryGetValue(key, out var geometric)
                    ? SafeDirection(geometric.magnitude > 0f ? geometric / geometric.magnitude : Vector3.zero,
                        normalFallbacks[key])
                    : SafeDirection(importedNormalSums[key], normalFallbacks[key]));

            void AddGeometricNormal(int vertexIndex, Vector3 faceNormal)
            {
                var key = new WeldPositionKey(sourceVertices[vertexIndex]);
                if (!boundaryPositions.Contains(key)) return;
                geometricNormalSums[key] = geometricNormalSums.TryGetValue(key, out var sum)
                    ? sum + faceNormal : faceNormal;
            }

            if (supportBandWidthMultiplier > 0f || alphaCuts?.Count > 0)
            {
                var supported = AddSupportBand(source, chosen, supportBandWidthMultiplier,
                    surfaceNormals, out var supportedGeometryNormals, alphaCuts);
                try
                {
                    var stats = AddFoldsCore(supported, destination, targets, widthMultiplier, samplers,
                        foldAngleDegrees, foldLengthMultiplier, 0f, supportedGeometryNormals, taperEndpoints, taperPositions,
                        alphaBoundarySplit || alphaCuts?.Count > 0);
                    if (stats.Edges == 0) return default;
                    return new OutlineBoundaryFoldStats(0, stats.Edges,
                        destination.vertexCount - oldCount, CountTriangles(destination) - CountTriangles(source));
                }
                finally { UnityEngine.Object.DestroyImmediate(supported); }
            }

            // Degree-1/2 adjacency is the boundary chain/loop representation. Averaging
            // its incident directions keeps corners continuous. We deliberately avoid an
            // unbounded miter scale; the selected flap length stays fixed at sharp corners.
            var inwardSum = new Dictionary<(int sub, int vertex), Vector3>();
            var depthByVertex = new Dictionary<(int sub, int vertex), float>();
            var endpointNormalSums = new Dictionary<WeldPositionKey, Vector3>();
            foreach (var item in chosen)
            {
                var e = item.edge;
                var tangent = (sourceVertices[e.B] - sourceVertices[e.A]).normalized;
                var face = FaceDirection(sourceVertices[e.B] - sourceVertices[e.A],
                    sourceVertices[e.Third] - sourceVertices[e.A]);
                if (tangent.sqrMagnitude < DirectionEpsilon || face.sqrMagnitude < DirectionEpsilon) continue;
                var inward = Vector3.Cross(face, tangent).normalized;
                var normalA = surfaceNormals[new WeldPositionKey(sourceVertices[e.A])];
                var normalB = surfaceNormals[new WeldPositionKey(sourceVertices[e.B])];
                AccumulateVertex(e.SubMesh, e.A, inward, normalA, item.depth,
                    inwardSum, depthByVertex);
                AccumulateVertex(e.SubMesh, e.B, inward, normalB, item.depth,
                    inwardSum, depthByVertex);
                // Move a zero-width tip along the boundary toward its chain interior,
                // never out through the surface or across the hem. This direction
                // has no cross-boundary expansion even in oblique views.
                var positionA = new WeldPositionKey(sourceVertices[e.A]);
                var positionB = new WeldPositionKey(sourceVertices[e.B]);
                if (taperPositions.Contains(positionA)) AddEndpointDirection(positionA, tangent);
                if (taperPositions.Contains(positionB)) AddEndpointDirection(positionB, -tangent);
            }

            void AddEndpointDirection(WeldPositionKey position, Vector3 direction) =>
                endpointNormalSums[position] = endpointNormalSums.TryGetValue(position, out var sum) ? sum + direction : direction;

            var keys = inwardSum.Keys.ToList();
            var sourceMap = new List<int>(oldCount + keys.Count * 2);
            for (var i = 0; i < oldCount; i++) sourceMap.Add(i);
            var row0Index = new Dictionary<(int sub, int vertex), int>();
            var foldEndIndex = new Dictionary<(int sub, int vertex), int>();
            var vertices = sourceVertices.ToList();
            var normals = sourceNormals.ToList();
            var outwardSums = new Dictionary<WeldPositionKey, Vector3>();
            foreach (var key in keys)
            {
                var src = key.vertex;
                var surfaceNormal = surfaceNormals[new WeldPositionKey(sourceVertices[src])];
                var backward = -surfaceNormal;
                var inward = SafeDirection(inwardSum[key], Vector3.Cross(surfaceNormal, Vector3.right));
                // Keep surfaceInward tangent to the source surface after corner averaging.
                inward = SafeDirection(Vector3.ProjectOnPlane(inward, surfaceNormal), inward);
                var depth = depthByVertex[key];
                var position = new WeldPositionKey(sourceVertices[src]);
                var pinch = taperPositions.Contains(position);
                outwardSums[position] = outwardSums.TryGetValue(position, out var outward)
                    ? outward - inward : -inward;

                row0Index[key] = vertices.Count;
                vertices.Add(sourceVertices[src]); normals.Add(surfaceNormal); sourceMap.Add(src);

                foldEndIndex[key] = vertices.Count;
                vertices.Add(sourceVertices[src]
                    + inward * (pinch ? 0f : depth * inwardRatio)
                    + backward * (pinch ? 0f : depth * backwardRatio));
                // Use the actual return-flap face normal without blending it back
                // toward the source surface normal.
                normals.Add(surfaceNormal); // Replaced with the measured flap normal below.
                sourceMap.Add(src);
            }

            var acceptedEdges = 0;
            var addedTriangles = 0;
            var accepted = new List<DirectedEdge>();
            foreach (var item in chosen)
            {
                var a = (item.edge.SubMesh, item.edge.A);
                var b = (item.edge.SubMesh, item.edge.B);
                if (!row0Index.ContainsKey(a) || !row0Index.ContainsKey(b)) continue;
                var pinchA = taperPositions.Contains(new WeldPositionKey(sourceVertices[item.edge.A]));
                var pinchB = taperPositions.Contains(new WeldPositionKey(sourceVertices[item.edge.B]));
                if (pinchA && pinchB) continue;
                // The return flap must traverse the shared boundary opposite to the
                // source triangle: it faces behind the surface, not out through it.
                AddStrip(triangles[item.edge.SubMesh], row0Index[a], row0Index[b],
                    foldEndIndex[a], foldEndIndex[b], pinchA, pinchB);
                addedTriangles += (pinchA ? 0 : 1) + (pinchB ? 0 : 1);
                acceptedEdges++;
                accepted.Add(item.edge);
            }
            if (acceptedEdges == 0) return default;

            // Measure the actual generated flap faces first. FoldEnd receives this normal
            // directly. At the crease, average one normalized source-surface normal and
            // one normalized flap normal; area weighting here would let the large source
            // triangles completely dominate the deliberately short flap.
            var flapNormalSums = new Dictionary<WeldPositionKey, Vector3>();
            foreach (var edge in accepted)
            {
                var a = (edge.SubMesh, edge.A);
                var b = (edge.SubMesh, edge.B);
                AccumulateFlapTriangle(row0Index[a], foldEndIndex[b], row0Index[b], edge.A, edge.B);
                AccumulateFlapTriangle(row0Index[a], foldEndIndex[a], foldEndIndex[b], edge.A, edge.B);
            }
            var flapNormals = outwardSums.ToDictionary(pair => pair.Key,
                pair => SafeDirection(flapNormalSums.TryGetValue(pair.Key, out var sum) ? sum : Vector3.zero,
                    -surfaceNormals[pair.Key]));
            var creaseNormals = flapNormals.ToDictionary(pair => pair.Key,
                // At a flat 180-degree return the normals cancel. Still expand out of
                // the open edge (downward at a skirt hem), rather than through the fabric.
                pair => SafeDirection(surfaceNormals[pair.Key] + pair.Value, outwardSums[pair.Key]));
            var endpointNormals = endpointNormalSums.ToDictionary(pair => pair.Key,
                pair => SafeDirection(pair.Value, -outwardSums[pair.Key]));

            for (var i = 0; i < oldCount; i++)
                if (endpointNormals.TryGetValue(new WeldPositionKey(sourceVertices[i]), out var tip))
                    normals[i] = tip;
                else if (creaseNormals.TryGetValue(new WeldPositionKey(sourceVertices[i]), out var crease))
                    normals[i] = crease;
            foreach (var key in keys)
            {
                var position = new WeldPositionKey(sourceVertices[key.vertex]);
                // The return strip closes to one point, including after outline
                // expansion: both tip vertices share a unit chain-inward normal. Merely
                // restoring the authored surface normal leaves a visible end cap
                // in oblique views, and scaling normals is undone by shaders.
                var isTip = endpointNormals.TryGetValue(position, out var tip);
                normals[row0Index[key]] = isTip ? tip : creaseNormals[position];
                normals[foldEndIndex[key]] = isTip ? tip : flapNormals[position];
            }

            void AccumulateFlapTriangle(int ia, int ib, int ic, int sourceA, int sourceB)
            {
                var ab = vertices[ib] - vertices[ia];
                var ac = vertices[ic] - vertices[ia];
                // A millimeter flap has a tiny area in object space. Test relative
                // degeneracy so a valid small flap does not lose its measured normal.
                var face = FaceDirection(ab, ac);
                if (face == Vector3.zero) return;
                AddForPosition(new WeldPositionKey(sourceVertices[sourceA]), face);
                AddForPosition(new WeldPositionKey(sourceVertices[sourceB]), face);
            }

            void AddForPosition(WeldPositionKey position, Vector3 face)
            {
                flapNormalSums[position] = flapNormalSums.TryGetValue(position, out var sum)
                    ? sum + face : face;
            }

            var frames = CaptureBlendShapes(source);
            destination.Clear(false);
            destination.bindposes = source.bindposes;
            destination.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : source.indexFormat;
            destination.SetVertices(vertices);
            destination.SetNormals(normals);
            CopyVertexChannels(source, destination, sourceMap);
            if (endpointNormals.Count > 0) RepairTaperTangents(source, destination, sourceMap, normals);
            destination.subMeshCount = source.subMeshCount;
            for (var sub = 0; sub < triangles.Length; sub++)
            {
                if (source.GetTopology(sub) == MeshTopology.Triangles)
                    destination.SetTriangles(triangles[sub], sub, false);
                else
                    destination.SetIndices(source.GetIndices(sub), source.GetTopology(sub), sub, false);
            }
            CopyBoneWeights(source, destination, sourceMap);
            RestoreBlendShapes(destination, frames, sourceMap);
            destination.RecalculateBounds();
            return new OutlineBoundaryFoldStats(0, acceptedEdges, keys.Count * 2, addedTriangles);
        }

        private static void AddEdge(int a, int b, int third, int sub, IReadOnlyList<Vector3> vertices,
            Dictionary<EdgeKey, int> local, Dictionary<PositionEdgeKey, int> global,
            Dictionary<EdgeKey, DirectedEdge> directed)
        {
            var key = new EdgeKey(a, b);
            local[key] = local.TryGetValue(key, out var lc) ? lc + 1 : 1;
            var physicalKey = new PositionEdgeKey(vertices[a], vertices[b]);
            global[physicalKey] = global.TryGetValue(physicalKey, out var gc) ? gc + 1 : 1;
            if (!directed.ContainsKey(key)) directed[key] = new DirectedEdge(a, b, third, sub);
        }

        private static HashSet<WeldPositionKey> FindTaperEndpoints(
            IReadOnlyList<(DirectedEdge edge, float depth)> chosen, IReadOnlyList<Vector3> vertices,
            out HashSet<PositionEdgeKey> eligibleEdges)
        {
            var neighbors = new Dictionary<Vector3, HashSet<Vector3>>();
            foreach (var item in chosen)
            {
                var a = vertices[item.edge.A]; var b = vertices[item.edge.B];
                if (a.Equals(b)) continue;
                Add(a, b); Add(b, a);
            }
            var visited = new HashSet<Vector3>(); var endpoints = new HashSet<WeldPositionKey>();
            eligibleEdges = new HashSet<PositionEdgeKey>();
            foreach (var start in neighbors.Keys)
            {
                if (!visited.Add(start)) continue;
                var pending = new Stack<Vector3>(); pending.Push(start);
                var chain = new List<Vector3>();
                while (pending.Count > 0)
                {
                    var point = pending.Pop(); chain.Add(point);
                    foreach (var next in neighbors[point]) if (visited.Add(next)) pending.Push(next);
                }
                if (chain.Count <= 2 || chain.Any(p => neighbors[p].Count > 2)) continue;
                foreach (var point in chain)
                    foreach (var next in neighbors[point]) eligibleEdges.Add(new PositionEdgeKey(point, next));
                var ends = chain.Where(p => neighbors[p].Count == 1).ToArray();
                if (ends.Length != 2) continue; // Closed loops and branches do not taper.
                foreach (var end in ends) endpoints.Add(new WeldPositionKey(end));
            }
            return endpoints;

            void Add(Vector3 a, Vector3 b)
            {
                if (!neighbors.TryGetValue(a, out var adjacent)) neighbors[a] = adjacent = new HashSet<Vector3>();
                adjacent.Add(b);
            }
        }

        private static bool VisibleAtBoundary(DirectedEdge edge, IReadOnlyList<Vector4> uv,
            OutlineBoundaryMaterialInfo info, TextureSampler sampler, bool splitAlpha)
        {
            var a = Vector2.Scale((Vector2)uv[edge.A], info.TextureScale) + info.TextureOffset;
            var b = Vector2.Scale((Vector2)uv[edge.B], info.TextureScale) + info.TextureOffset;
            // After splitting at every cutoff crossing, endpoint equality must not
            // retain the transparent segment next to a visible one.
            if (splitAlpha) return sampler.Alpha((a + b) * 0.5f) >= info.TextureCutoff;
            if (sampler.Alpha(a) >= info.TextureCutoff || sampler.Alpha(b) >= info.TextureCutoff
                || sampler.Alpha((a + b) * 0.5f) >= info.TextureCutoff) return true;
            // A coarse edge may cross an opaque island between its endpoints and midpoint.
            var steps = AlphaSampleSteps(a, b, info.MainTexture);
            for (var i = 1; i < steps; i++)
                if (sampler.Alpha(Vector2.LerpUnclamped(a, b, (float)i / steps)) >= info.TextureCutoff) return true;
            return false;
        }

        private static int AlphaSampleSteps(Vector2 a, Vector2 b, Texture2D texture) =>
            Mathf.Clamp(Mathf.CeilToInt(Vector2.Scale(b - a, new Vector2(texture.width, texture.height)).magnitude * 2f), 2, 16384);

        private static Dictionary<PositionEdgeKey, SortedSet<int>> FindAlphaTaperCuts(
            IReadOnlyList<(DirectedEdge edge, float depth)> chosen, Vector3[] positions, IReadOnlyList<Vector4> uv,
            IReadOnlyDictionary<int, OutlineBoundaryMaterialInfo> targets, IReadOnlyDictionary<Texture2D, TextureSampler> samplers,
            HashSet<PositionEdgeKey> eligibleEdges, HashSet<WeldPositionKey> tips)
        {
            var cuts = new Dictionary<PositionEdgeKey, SortedSet<int>>();
            if (eligibleEdges == null) return cuts;
            foreach (var item in chosen)
            {
                var edge = item.edge; var info = targets[edge.SubMesh];
                if (!info.TestAlpha) continue;
                var a = positions[edge.A]; var b = positions[edge.B]; var key = new PositionEdgeKey(a, b);
                if (!eligibleEdges.Contains(key)) continue; // Preserve two-vertex ribbon exclusion.
                var uvA = Vector2.Scale((Vector2)uv[edge.A], info.TextureScale) + info.TextureOffset;
                var uvB = Vector2.Scale((Vector2)uv[edge.B], info.TextureScale) + info.TextureOffset;
                var sampler = samplers[info.MainTexture];
                var steps = AlphaSampleSteps(uvA, uvB, info.MainTexture);
                var previousT = 0f; var previousVisible = sampler.Alpha(uvA) >= info.TextureCutoff;
                for (var sample = 1; sample <= steps; sample++)
                {
                    var t = (float)sample / steps;
                    var visible = sampler.Alpha(Vector2.LerpUnclamped(uvA, uvB, t)) >= info.TextureCutoff;
                    if (visible != previousVisible)
                    {
                        var low = previousT; var high = t;
                        for (var iteration = 0; iteration < 24; iteration++)
                        {
                            var middle = (low + high) * 0.5f;
                            if (middle == low || middle == high) break;
                            if ((sampler.Alpha(Vector2.LerpUnclamped(uvA, uvB, middle)) >= info.TextureCutoff) == previousVisible)
                                low = middle;
                            else high = middle;
                        }
                        var parameter = (low + high) * 0.5f;
                        if (ComparePosition(a, b) > 0) parameter = 1f - parameter;
                        var cut = Mathf.Clamp(Mathf.RoundToInt(parameter * CutResolution), 0, CutResolution);
                        if (!cuts.TryGetValue(key, out var values)) cuts[key] = values = new SortedSet<int>();
                        if (cut > 0 && cut < CutResolution) values.Add(cut);
                        tips.Add(new WeldPositionKey(EdgePosition(cut, a, b)));
                    }
                    previousT = t; previousVisible = visible;
                }
                if (!cuts.TryGetValue(key, out var edgeCuts)) continue;
                // If an opaque span lies within a single source edge, retain a
                // full-width interior vertex between its two zero-width tips.
                var spans = new[] { 0 }.Concat(edgeCuts).Concat(new[] { CutResolution }).Distinct().OrderBy(v => v).ToArray();
                for (var i = 0; i + 1 < spans.Length; i++)
                {
                    var from = spans[i]; var to = spans[i + 1];
                    if (!tips.Contains(new WeldPositionKey(EdgePosition(from, a, b)))
                        || !tips.Contains(new WeldPositionKey(EdgePosition(to, a, b)))) continue;
                    var middle = from + (to - from) / 2;
                    var parameter = (float)middle / CutResolution;
                    if (ComparePosition(a, b) > 0) parameter = 1f - parameter;
                    if (sampler.Alpha(Vector2.LerpUnclamped(uvA, uvB, parameter)) >= info.TextureCutoff
                        && middle > from && middle < to) edgeCuts.Add(middle);
                }
            }
            return cuts;
        }

        private static void AccumulateVertex(int sub, int vertex, Vector3 inward,
            Vector3 normal, float depth, Dictionary<(int, int), Vector3> inwards,
            Dictionary<(int, int), float> depths)
        {
            var key = (sub, vertex);
            // Triangle winding fixes the sign: cross(faceNormal, boundaryTangent)
            // points from the open edge into the adjacent triangle.
            var stableInward = SafeDirection(Vector3.ProjectOnPlane(inward, normal), inward);
            inwards[key] = inwards.TryGetValue(key, out var d) ? d + stableInward : stableInward;
            // If one vertex is shared by selected materials with different widths, keeping
            // the smaller fold avoids exposing an oversized flap at the corner.
            depths[key] = depths.TryGetValue(key, out var old) ? Mathf.Min(old, depth) : depth;
        }

        private static void AddStrip(List<int> triangles, int rowA0, int rowB0, int rowA1, int rowB1,
            bool pinchA = false, bool pinchB = false)
        {
            // A pinched endpoint only needs the remaining nondegenerate triangle.
            if (!pinchB) { triangles.Add(rowA0); triangles.Add(rowB1); triangles.Add(rowB0); }
            if (!pinchA) { triangles.Add(rowA0); triangles.Add(rowA1); triangles.Add(rowB1); }
        }

        private static Vector3 SafeDirection(Vector3 value, Vector3 fallback) =>
            value.sqrMagnitude > DirectionEpsilon ? value.normalized : fallback.normalized;

        private static void RepairTaperTangents(Mesh source, Mesh destination,
            IReadOnlyList<int> sourceMap, IReadOnlyList<Vector3> normals)
        {
            var originalTangents = source.tangents; var originalNormals = source.normals;
            var hasTangents = originalTangents.Length == source.vertexCount;
            var tangents = new List<Vector4>(sourceMap.Count);
            for (var i = 0; i < sourceMap.Count; i++)
            {
                var tangent = hasTangents ? originalTangents[sourceMap[i]] : new Vector4(1f, 0f, 0f, 1f);
                if (!normals[i].Equals(originalNormals[sourceMap[i]]))
                {
                    // A tip normal may align with the old UV tangent. lilToon's
                    // outline-vector path builds normalize(cross(N,T)); a parallel
                    // pair produces NaNs and can discard the entire adjacent face.
                    var normal = normals[i].normalized;
                    var direction = Vector3.ProjectOnPlane((Vector3)tangent, normal);
                    var fallback = Vector3.ProjectOnPlane(originalNormals[sourceMap[i]], normal);
                    if (fallback.sqrMagnitude <= DirectionEpsilon)
                        fallback = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.forward);
                    direction = SafeDirection(direction, fallback);
                    tangent = new Vector4(direction.x, direction.y, direction.z, tangent.w == 0f ? 1f : tangent.w);
                }
                tangents.Add(tangent);
            }
            destination.SetTangents(tangents);
        }

        private static Vector3 FaceDirection(Vector3 ab, Vector3 ac)
        {
            var scale = Mathf.Max(ab.magnitude, ac.magnitude);
            if (!(scale > 0f)) return Vector3.zero;
            var face = Vector3.Cross(ab / scale, ac / scale);
            var magnitude = face.magnitude;
            return magnitude > 1e-8f ? face / magnitude : Vector3.zero;
        }

        internal static int NormalizeFoldAngle(int value) => value <= 5 ? 5 : value <= 10 ? 10
            : value <= 15 ? 15 : value <= 20 ? 20 : 30;

        internal static float NormalizeFoldLength(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? DefaultFoldLengthMultiplier : Mathf.Clamp(value, 0.5f, 8f);

        internal static float NormalizeSupportBandWidth(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? DefaultSupportBandWidthMultiplier : Mathf.Clamp(value, 0f, 2f);

        private static void Increment<TKey>(Dictionary<TKey, int> dictionary, TKey key) =>
            dictionary[key] = dictionary.TryGetValue(key, out var value) ? value + 1 : 1;

        private static void CopyVertexChannels(Mesh source, Mesh destination, IReadOnlyList<int> sourceMap)
        {
            if (source.tangents.Length == source.vertexCount)
                destination.tangents = sourceMap.Select(i => source.tangents[i]).ToArray();
            if (source.HasVertexAttribute(VertexAttribute.Color))
            {
                if (source.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8
                    && source.colors32.Length == source.vertexCount)
                    destination.colors32 = sourceMap.Select(i => source.colors32[i]).ToArray();
                else if (source.colors.Length == source.vertexCount)
                    destination.colors = sourceMap.Select(i => source.colors[i]).ToArray();
            }
            for (var channel = 0; channel < 8; channel++)
            {
                var sourceUvs = new List<Vector4>();
                source.GetUVs(channel, sourceUvs);
                if (sourceUvs.Count != source.vertexCount) continue;
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                switch (source.GetVertexAttributeDimension(attribute))
                {
                    case 2:
                        destination.SetUVs(channel, sourceMap.Select(i => (Vector2)sourceUvs[i]).ToList());
                        break;
                    case 3:
                        destination.SetUVs(channel, sourceMap.Select(i => (Vector3)sourceUvs[i]).ToList());
                        break;
                    default:
                        destination.SetUVs(channel, sourceMap.Select(i => sourceUvs[i]).ToList());
                        break;
                }
            }
        }

        private static void CopyBoneWeights(Mesh source, Mesh destination, IReadOnlyList<int> sourceMap)
        {
            using var sourceCounts = source.GetBonesPerVertex();
            using var sourceWeights = source.GetAllBoneWeights();
            if (sourceCounts.Length != source.vertexCount || sourceWeights.Length == 0) return;
            var starts = new int[source.vertexCount + 1];
            for (var i = 0; i < source.vertexCount; i++) starts[i + 1] = starts[i] + sourceCounts[i];
            var counts = new NativeArray<byte>(sourceMap.Count, Allocator.Temp);
            var all = new List<BoneWeight1>();
            for (var i = 0; i < sourceMap.Count; i++)
            {
                var src = sourceMap[i];
                counts[i] = sourceCounts[src];
                all.AddRange(Enumerable.Range(starts[src], sourceCounts[src])
                    .Select(w => sourceWeights[w]).OrderByDescending(w => w.weight).ThenBy(w => w.boneIndex));
            }
            var weights = new NativeArray<BoneWeight1>(all.ToArray(), Allocator.Temp);
            try { destination.SetBoneWeights(counts, weights); }
            finally { counts.Dispose(); weights.Dispose(); }
        }

        private static List<BlendFrame> CaptureBlendShapes(Mesh source)
        {
            var result = new List<BlendFrame>();
            for (var shape = 0; shape < source.blendShapeCount; shape++)
            for (var frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
            {
                var item = new BlendFrame
                {
                    Name = source.GetBlendShapeName(shape), Weight = source.GetBlendShapeFrameWeight(shape, frame),
                    Vertices = new Vector3[source.vertexCount], Normals = new Vector3[source.vertexCount],
                    Tangents = new Vector3[source.vertexCount]
                };
                source.GetBlendShapeFrameVertices(shape, frame, item.Vertices, item.Normals, item.Tangents);
                result.Add(item);
            }
            return result;
        }

        private static void RestoreBlendShapes(Mesh destination, IEnumerable<BlendFrame> frames,
            IReadOnlyList<int> sourceMap)
        {
            foreach (var frame in frames)
                destination.AddBlendShapeFrame(frame.Name, frame.Weight,
                    sourceMap.Select(i => frame.Vertices[i]).ToArray(),
                    sourceMap.Select(i => frame.Normals[i]).ToArray(),
                    sourceMap.Select(i => frame.Tangents[i]).ToArray());
        }

    }
}
