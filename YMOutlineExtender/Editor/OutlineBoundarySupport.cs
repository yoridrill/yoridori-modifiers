using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace YoridoriModifiers.OutlineExtender
{
    internal static partial class OutlineBoundaryFoldProcessor
    {
        // Cuts are shared by physical edges, including UV/material splits. Quantized
        // parameters keep both sides at exactly the same position after interpolation.
        private const int CutResolution = 10000000;
        private const float BarycentricEpsilon = 1e-6f;

        private readonly struct VertexSample
        {
            internal readonly int A, B, C;
            internal readonly Vector3 Weights;
            internal VertexSample(int a, int b, int c, Vector3 weights)
            { A = a; B = b; C = c; Weights = weights; }
            internal Vector3 Read(IReadOnlyList<Vector3> values) =>
                values[A] * Weights.x + values[B] * Weights.y + values[C] * Weights.z;
            internal Vector4 Read(IReadOnlyList<Vector4> values) =>
                values[A] * Weights.x + values[B] * Weights.y + values[C] * Weights.z;
            internal Color Read(IReadOnlyList<Color> values) =>
                values[A] * Weights.x + values[B] * Weights.y + values[C] * Weights.z;
        }

        private sealed class SupportFace
        {
            internal int Sub;
            internal int[] Indices;
            internal List<List<Vector3>> Polygons;
        }

        private static Mesh AddSupportBand(Mesh source,
            IReadOnlyList<(DirectedEdge edge, float depth)> boundaries, float widthMultiplier,
            IReadOnlyDictionary<WeldPositionKey, Vector3> sourceGeometryNormals,
            out Dictionary<WeldPositionKey, Vector3> supportedGeometryNormals,
            IReadOnlyDictionary<PositionEdgeKey, SortedSet<int>> boundaryCuts = null)
        {
            var positions = source.vertices;
            var edgeWidths = new Dictionary<PositionEdgeKey, float>();
            var vertexWidths = new Dictionary<WeldPositionKey, float>();
            foreach (var item in boundaries)
            {
                var width = item.depth * widthMultiplier;
                if (!(width > 0f)) continue;
                var a = positions[item.edge.A]; var b = positions[item.edge.B];
                MinWidth(edgeWidths, new PositionEdgeKey(a, b), width);
                MinWidth(vertexWidths, new WeldPositionKey(a), width);
                MinWidth(vertexWidths, new WeldPositionKey(b), width);
            }

            var faces = new List<SupportFace>();
            var edgeCuts = new Dictionary<PositionEdgeKey, SortedSet<int>>();
            if (boundaryCuts != null)
                foreach (var pair in boundaryCuts) edgeCuts[pair.Key] = new SortedSet<int>(pair.Value);
            for (var sub = 0; sub < source.subMeshCount; sub++)
            {
                if (source.GetTopology(sub) != MeshTopology.Triangles) continue;
                var indices = source.GetTriangles(sub);
                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    var ids = new[] { indices[i], indices[i + 1], indices[i + 2] };
                    var polygons = new List<List<Vector3>>
                        { new() { Vector3.right, Vector3.up, Vector3.forward } };
                    var guarded = new bool[3];
                    for (var edge = 0; edge < 3; edge++)
                    {
                        var next = (edge + 1) % 3; var third = (edge + 2) % 3;
                        var a = positions[ids[edge]]; var b = positions[ids[next]];
                        if (!edgeWidths.TryGetValue(new PositionEdgeKey(a, b), out var width)) continue;
                        var length = (b - a).magnitude;
                        if (!(length > 0f)) continue;
                        var height = Vector3.Cross(b - a, positions[ids[third]] - a).magnitude / length;
                        if (!(height > 0f)) continue;
                        // Parallel inset line on the original face. Shrink on small
                        // triangles so an unchanged interior remains at narrow corners.
                        var fraction = Mathf.Min(width / height, 0.25f);
                        var coefficients = Vector3.zero; coefficients[third] = 1f;
                        SplitPolygons(polygons, coefficients, fraction);
                        guarded[edge] = guarded[next] = true;
                    }
                    for (var corner = 0; corner < 3; corner++)
                    {
                        if (guarded[corner]
                            || !vertexWidths.TryGetValue(new WeldPositionKey(positions[ids[corner]]), out var width)) continue;
                        // A boundary vertex also belongs to triangles without a boundary
                        // edge. Trim its influence there instead of leaving a large fan.
                        var next = (corner + 1) % 3; var last = (corner + 2) % 3;
                        var firstLength = (positions[ids[next]] - positions[ids[corner]]).magnitude;
                        var lastLength = (positions[ids[last]] - positions[ids[corner]]).magnitude;
                        if (!(firstLength > 0f && lastLength > 0f)) continue;
                        var coefficients = Vector3.zero;
                        coefficients[next] = 1f / Mathf.Min(width / firstLength, 0.25f);
                        coefficients[last] = 1f / Mathf.Min(width / lastLength, 0.25f);
                        SplitPolygons(polygons, coefficients, 1f);
                    }
                    foreach (var polygon in polygons)
                    foreach (var point in polygon)
                        if (TryOriginalEdge(point, out var first, out var second))
                        {
                            var key = new PositionEdgeKey(positions[ids[first]], positions[ids[second]]);
                            var cut = EdgeParameter(point, first, second, ids, positions);
                            if (cut <= 0 || cut >= CutResolution) continue;
                            if (!edgeCuts.TryGetValue(key, out var cuts)) edgeCuts[key] = cuts = new SortedSet<int>();
                            cuts.Add(cut);
                        }
                    faces.Add(new SupportFace { Sub = sub, Indices = ids, Polygons = polygons });
                }
            }

            var samples = Enumerable.Range(0, positions.Length)
                .Select(i => new VertexSample(i, i, i, Vector3.right)).ToList();
            var vertices = positions.ToList();
            var triangles = Enumerable.Range(0, source.subMeshCount).Select(_ => new List<int>()).ToArray();
            var splitIndices = new Dictionary<(EdgeKey edge, Vector3 position), int>();
            foreach (var face in faces)
            {
                var interiorIndices = new Dictionary<(int x, int y), int>();
                foreach (var polygon in face.Polygons)
                {
                    var augmented = new List<(Vector3 bary, int? cut)>();
                    for (var i = 0; i < polygon.Count; i++)
                    {
                        var point = polygon[i]; var nextPoint = polygon[(i + 1) % polygon.Count];
                        augmented.Add((point, null));
                        // Propagate cuts to every neighbor, even unselected submeshes
                        // and faces with no local inset line, to avoid T-junctions.
                        for (var missing = 0; missing < 3; missing++)
                        {
                            if (Mathf.Abs(point[missing]) > BarycentricEpsilon
                                || Mathf.Abs(nextPoint[missing]) > BarycentricEpsilon) continue;
                            var first = (missing + 1) % 3; var second = (missing + 2) % 3;
                            var a = positions[face.Indices[first]]; var b = positions[face.Indices[second]];
                            if (!edgeCuts.TryGetValue(new PositionEdgeKey(a, b), out var cuts)) break;
                            var start = EdgeParameter(point, first, second, face.Indices, positions);
                            var end = EdgeParameter(nextPoint, first, second, face.Indices, positions);
                            var between = cuts.Where(cut => cut > Math.Min(start, end) && cut < Math.Max(start, end));
                            if (start > end) between = between.Reverse();
                            foreach (var cut in between)
                                augmented.Add((EdgeBarycentric(cut, first, second, face.Indices, positions), cut));
                            break;
                        }
                    }
                    var polygonVertices = augmented.Select(p => (index: GetIndex(p.bary, p.cut), bary: p.bary)).ToList();
                    TriangulateSupportPolygon(polygonVertices, triangles[face.Sub]);
                }

                int GetIndex(Vector3 bary, int? canonicalCut = null)
                {
                    for (var i = 0; i < 3; i++)
                        if (bary[i] == 1f) return face.Indices[i];
                    if (TryOriginalEdge(bary, out var first, out var second))
                    {
                        var cut = canonicalCut ?? EdgeParameter(bary, first, second, face.Indices, positions);
                        bary = EdgeBarycentric(cut, first, second, face.Indices, positions);
                        var a = face.Indices[first]; var b = face.Indices[second];
                        var position = EdgePosition(cut, positions[a], positions[b]);
                        // Nearby cuts can round to the same object-space position on
                        // very small bands. Share that vertex rather than creating a
                        // zero-length segment with a separate topological identity.
                        if (position.Equals(positions[a])) return a;
                        if (position.Equals(positions[b])) return b;
                        var edge = new EdgeKey(a, b);
                        if (splitIndices.TryGetValue((edge, position), out var found)) return found;
                        var index = samples.Count;
                        splitIndices[(edge, position)] = index;
                        AddSample(bary, position);
                        return index;
                    }
                    var key = (Mathf.RoundToInt(bary.x * CutResolution), Mathf.RoundToInt(bary.y * CutResolution));
                    if (interiorIndices.TryGetValue(key, out var interior)) return interior;
                    interior = samples.Count; interiorIndices[key] = interior;
                    AddSample(bary, null);
                    return interior;
                }

                void AddSample(Vector3 bary, Vector3? edgePosition)
                {
                    var sample = new VertexSample(face.Indices[0], face.Indices[1], face.Indices[2], bary);
                    samples.Add(sample);
                    vertices.Add(edgePosition ?? sample.Read(positions));
                }
            }

            var result = UnityEngine.Object.Instantiate(source);
            try
            {
                // Rebuild the layout too: imported avatars can use packed skinning
                // attributes which SetBoneWeights cannot write back in that format.
                result.Clear(false);
                result.bindposes = source.bindposes;
                result.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : source.indexFormat;
                result.SetVertices(vertices);
                // Interpolate authored normals; do not recalculate or blend them toward
                // the crease. The later fold step modifies only the physical boundary.
                var normals = source.normals;
                result.SetNormals(samples.Select(sample => sample.Read(normals)).ToList());
                // Subdivision changes incident triangle areas. Keep the fold's geometric
                // directions derived from the original faces, separately from authored
                // shading normals, so adding a support band cannot reorient a hard seam.
                var geometry = positions.Select((p, i) => sourceGeometryNormals.TryGetValue(new WeldPositionKey(p), out var n)
                    ? n : normals[i]).ToArray();
                supportedGeometryNormals = new Dictionary<WeldPositionKey, Vector3>();
                for (var i = 0; i < samples.Count; i++)
                    supportedGeometryNormals[new WeldPositionKey(vertices[i])] = samples[i].Read(geometry).normalized;
                CopySupportChannels(source, result, samples);
                result.subMeshCount = source.subMeshCount;
                for (var sub = 0; sub < source.subMeshCount; sub++)
                    if (source.GetTopology(sub) == MeshTopology.Triangles) result.SetTriangles(triangles[sub], sub, false);
                    else result.SetIndices(source.GetIndices(sub), source.GetTopology(sub), sub, false);
                CopySupportBoneWeights(source, result, samples);
                foreach (var frame in CaptureBlendShapes(source))
                    result.AddBlendShapeFrame(frame.Name, frame.Weight,
                        samples.Select(s => s.Read(frame.Vertices)).ToArray(),
                        samples.Select(s => s.Read(frame.Normals)).ToArray(),
                        samples.Select(s => s.Read(frame.Tangents)).ToArray());
                result.RecalculateBounds();
                return result;
            }
            catch { UnityEngine.Object.DestroyImmediate(result); throw; }
        }

        private static void MinWidth<TKey>(Dictionary<TKey, float> widths, TKey key, float value) =>
            widths[key] = widths.TryGetValue(key, out var previous) ? Mathf.Min(previous, value) : value;

        private static void SplitPolygons(List<List<Vector3>> polygons, Vector3 coefficients, float threshold)
        {
            var split = new List<List<Vector3>>();
            foreach (var polygon in polygons)
            {
                var low = new List<Vector3>(); var high = new List<Vector3>();
                for (var i = 0; i < polygon.Count; i++)
                {
                    var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                    var da = Vector3.Dot(coefficients, a) - threshold;
                    var db = Vector3.Dot(coefficients, b) - threshold;
                    if (da <= 0f) low.Add(a);
                    if (da >= 0f) high.Add(a);
                    if ((da < 0f && db > 0f) || (da > 0f && db < 0f))
                    {
                        // Adjacent clipping polygons traverse their shared segment in
                        // opposite directions. Use one order to obtain bit-identical
                        // intersections, including intersections inside a source face.
                        if (ComparePosition(a, b) > 0) { (a, b) = (b, a); (da, db) = (db, da); }
                        var point = Vector3.LerpUnclamped(a, b, da / (da - db));
                        low.Add(point); high.Add(point);
                    }
                }
                if (low.Count >= 3) split.Add(low);
                if (high.Count >= 3) split.Add(high);
            }
            polygons.Clear(); polygons.AddRange(split);
        }

        private static bool TryOriginalEdge(Vector3 bary, out int first, out int second)
        {
            for (var i = 0; i < 3; i++)
                if (Mathf.Abs(bary[i]) <= BarycentricEpsilon)
                { first = (i + 1) % 3; second = (i + 2) % 3; return true; }
            first = second = -1; return false;
        }

        private static int ComparePosition(Vector3 a, Vector3 b)
        {
            var x = a.x.CompareTo(b.x); if (x != 0) return x;
            var y = a.y.CompareTo(b.y); return y != 0 ? y : a.z.CompareTo(b.z);
        }

        private static int EdgeParameter(Vector3 bary, int first, int second, int[] ids, Vector3[] positions)
        {
            var t = bary[second] / (bary[first] + bary[second]);
            if (ComparePosition(positions[ids[first]], positions[ids[second]]) > 0) t = 1f - t;
            return Mathf.Clamp(Mathf.RoundToInt(t * CutResolution), 0, CutResolution);
        }

        private static Vector3 EdgeBarycentric(int cut, int first, int second, int[] ids, Vector3[] positions)
        {
            var t = (float)cut / CutResolution;
            if (ComparePosition(positions[ids[first]], positions[ids[second]]) > 0) t = 1f - t;
            var bary = Vector3.zero; bary[first] = 1f - t; bary[second] = t; return bary;
        }

        private static Vector3 EdgePosition(int cut, Vector3 a, Vector3 b)
        {
            // Preserve the integer cut through position construction. Converting it
            // to reversed float barycentrics and quantizing again can shift it by one
            // step, placing the two sides of a UV seam at different positions.
            if (ComparePosition(a, b) > 0) (a, b) = (b, a);
            return Vector3.LerpUnclamped(a, b, (float)cut / CutResolution);
        }

        private static void TriangulateSupportPolygon(List<(int index, Vector3 bary)> polygon, List<int> triangles)
        {
            // Test ears in the source triangle's barycentric plane, in double
            // precision. Object-space rounding can make a collinear edge cut look
            // convex on an oblique millimeter band and skip its shared segment.
            for (var i = polygon.Count - 1; i >= 0; i--)
                if (polygon[i].index == polygon[(i + 1) % polygon.Count].index) polygon.RemoveAt(i);
            while (polygon.Count > 3)
            {
                var best = -1; var bestArea = 0d;
                for (var i = 0; i < polygon.Count; i++)
                {
                    var previous = (i + polygon.Count - 1) % polygon.Count;
                    var next = (i + 1) % polygon.Count;
                    var a = polygon[previous].bary; var b = polygon[i].bary; var c = polygon[next].bary;
                    var area = Cross(a, b, c);
                    if (area <= bestArea) continue;
                    var blocked = false;
                    for (var j = 0; j < polygon.Count; j++)
                    {
                        if (j == previous || j == i || j == next) continue;
                        var p = polygon[j].bary;
                        if (Cross(a, b, p) >= -1e-14 && Cross(b, c, p) >= -1e-14 && Cross(c, a, p) >= -1e-14)
                        { blocked = true; break; }
                    }
                    if (!blocked) { best = i; bestArea = area; }
                }
                if (best < 0)
                {
                    // Retain the topology even for a numerically degenerate sliver;
                    // dropping the remainder would introduce a false open boundary.
                    for (var i = 1; i + 1 < polygon.Count; i++)
                    { triangles.Add(polygon[0].index); triangles.Add(polygon[i].index); triangles.Add(polygon[i + 1].index); }
                    return;
                }
                triangles.Add(polygon[(best + polygon.Count - 1) % polygon.Count].index);
                triangles.Add(polygon[best].index);
                triangles.Add(polygon[(best + 1) % polygon.Count].index);
                polygon.RemoveAt(best);
            }
            if (polygon.Count == 3)
            { triangles.Add(polygon[0].index); triangles.Add(polygon[1].index); triangles.Add(polygon[2].index); }

            static double Cross(Vector3 a, Vector3 b, Vector3 c) =>
                ((double)b.y - a.y) * ((double)c.z - a.z) - ((double)b.z - a.z) * ((double)c.y - a.y);
        }

        private static void CopySupportChannels(Mesh source, Mesh destination, IReadOnlyList<VertexSample> samples)
        {
            var tangents = source.tangents;
            if (tangents.Length == source.vertexCount)
                destination.SetTangents(samples.Select(s => s.Read(tangents)).ToList());
            var colors = source.colors;
            if (colors.Length == source.vertexCount)
            {
                var interpolated = samples.Select(s => s.Read(colors)).ToArray();
                if (source.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8)
                    destination.colors32 = interpolated.Select(c => (Color32)c).ToArray();
                else destination.colors = interpolated;
            }
            for (var channel = 0; channel < 8; channel++)
            {
                var uv = new List<Vector4>(); source.GetUVs(channel, uv);
                if (uv.Count != source.vertexCount) continue;
                var values = samples.Select(s => s.Read(uv)).ToList();
                switch (source.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel)))
                {
                    case 2: destination.SetUVs(channel, values.Select(v => (Vector2)v).ToList()); break;
                    case 3: destination.SetUVs(channel, values.Select(v => (Vector3)v).ToList()); break;
                    default: destination.SetUVs(channel, values); break;
                }
            }
        }

        private static void CopySupportBoneWeights(Mesh source, Mesh destination, IReadOnlyList<VertexSample> samples)
        {
            using var sourceCounts = source.GetBonesPerVertex();
            using var sourceWeights = source.GetAllBoneWeights();
            if (sourceCounts.Length != source.vertexCount || sourceWeights.Length == 0) return;
            var starts = new int[source.vertexCount + 1];
            for (var i = 0; i < source.vertexCount; i++) starts[i + 1] = starts[i] + sourceCounts[i];
            var countValues = new byte[samples.Count];
            var all = new List<BoneWeight1>();
            for (var index = 0; index < samples.Count; index++)
            {
                var sample = samples[index];
                if (index < source.vertexCount)
                {
                    countValues[index] = sourceCounts[index];
                    // Imported meshes may store influences in another order, but
                    // SetBoneWeights requires descending weights for every vertex.
                    all.AddRange(Enumerable.Range(starts[index], sourceCounts[index])
                        .Select(i => sourceWeights[i]).OrderByDescending(w => w.weight).ThenBy(w => w.boneIndex));
                    continue;
                }
                var weights = new Dictionary<int, float>();
                Add(sample.A, sample.Weights.x); Add(sample.B, sample.Weights.y); Add(sample.C, sample.Weights.z);
                var combined = weights.Where(p => p.Value > 0f).OrderByDescending(p => p.Value)
                    .ThenBy(p => p.Key).Take(byte.MaxValue).ToArray();
                var total = combined.Sum(p => p.Value);
                countValues[index] = (byte)combined.Length;
                foreach (var pair in combined)
                    all.Add(new BoneWeight1 { boneIndex = pair.Key, weight = pair.Value / total });

                void Add(int vertex, float coefficient)
                {
                    if (!(coefficient > 0f)) return;
                    for (var i = starts[vertex]; i < starts[vertex + 1]; i++)
                    {
                        var item = sourceWeights[i];
                        weights[item.boneIndex] = weights.TryGetValue(item.boneIndex, out var previous)
                            ? previous + coefficient * item.weight : coefficient * item.weight;
                    }
                }
            }
            using var counts = new NativeArray<byte>(countValues, Allocator.Temp);
            using var resultWeights = new NativeArray<BoneWeight1>(all.ToArray(), Allocator.Temp);
            destination.SetBoneWeights(counts, resultWeights);
        }

        private static int CountTriangles(Mesh mesh)
        {
            var count = 0;
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
                if (mesh.GetTopology(sub) == MeshTopology.Triangles) count += (int)mesh.GetIndexCount(sub) / 3;
            return count;
        }
    }
}
