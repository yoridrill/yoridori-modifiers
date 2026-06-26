using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YoridoriModifiers.HairLookKit
{
    internal static class HairOutlineCorrection
    {
        internal sealed class UvRange
        {
            public float minV = float.PositiveInfinity;
            public float maxV = float.NegativeInfinity;
        }

        internal static Dictionary<int, UvRange> BuildOriginalUvRangeBySubMesh(Mesh mesh, IReadOnlyList<int> subMeshes, IReadOnlyList<Vector2> uv)
        {
            var result = new Dictionary<int, UvRange>();
            foreach (var subMesh in subMeshes)
            {
                if (subMesh < 0 || subMesh >= mesh.subMeshCount) continue;
                var range = new UvRange();
                foreach (var index in mesh.GetTriangles(subMesh))
                {
                    if (index < 0 || index >= uv.Count) continue;
                    range.minV = Mathf.Min(range.minV, uv[index].y);
                    range.maxV = Mathf.Max(range.maxV, uv[index].y);
                }
                if (float.IsInfinity(range.minV) || float.IsInfinity(range.maxV))
                {
                    range.minV = 0f;
                    range.maxV = 1f;
                }
                result[subMesh] = range;
            }
            return result;
        }

        internal static float ResolveTipAlpha(int subMesh, int vertexIndex, IReadOnlyList<Vector2> uv, IReadOnlyDictionary<int, UvRange> ranges, float tipWidth, float tipRange)
        {
            if (vertexIndex < 0 || vertexIndex >= uv.Count || ranges == null || !ranges.TryGetValue(subMesh, out var range)) return 1f;
            var span = Mathf.Max(0.0001f, range.maxV - range.minV);
            var normalized = Mathf.Clamp01((uv[vertexIndex].y - range.minV) / span);
            var t = Mathf.Clamp01(normalized / Mathf.Max(0.0001f, tipRange));
            return Mathf.Lerp(Mathf.Clamp01(tipWidth), 1f, t);
        }

        internal static void ApplyAverageNormals(Mesh mesh, IReadOnlyList<int> bakeIndices, IReadOnlyList<float> outlineAlphaByVertex)
        {
            if (mesh == null || bakeIndices == null || bakeIndices.Count == 0) return;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            if (vertices == null || normals == null || vertices.Length != normals.Length) return;
            var colors = mesh.colors != null && mesh.colors.Length == vertices.Length
                ? mesh.colors
                : Enumerable.Repeat(Color.white, vertices.Length).ToArray();
            var grouped = bakeIndices
                .Where(i => i >= 0 && i < vertices.Length)
                .GroupBy(i => vertices[i]);
            foreach (var group in grouped)
            {
                var average = Vector3.zero;
                var indices = group.ToArray();
                foreach (var index in indices) average += normals[index];
                average = average.normalized;
                var encoded = new Color(average.x * 0.5f + 0.5f, average.y * 0.5f + 0.5f, average.z * 0.5f + 0.5f, 1f);
                foreach (var index in indices)
                {
                    encoded.a = index < outlineAlphaByVertex.Count ? outlineAlphaByVertex[index] : 1f;
                    colors[index] = encoded;
                }
            }
            mesh.colors = colors;
        }
    }
}
