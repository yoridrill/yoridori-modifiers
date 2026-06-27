using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YoridoriModifiers.HairLookKit
{
    internal static class HairOutlineCorrection
    {
        private struct AveragedNormalAccumulator
        {
            internal Vector3 normalSum;
            internal int count;
        }

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
                    var wrappedV = WrapUv01(uv[index].y);
                    range.minV = Mathf.Min(range.minV, wrappedV);
                    range.maxV = Mathf.Max(range.maxV, wrappedV);
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
            var wrappedV = WrapUv01(uv[vertexIndex].y);
            var tipnessRaw = range.maxV > range.minV
                ? 1f - Mathf.InverseLerp(range.minV, range.maxV, wrappedV)
                : 0f;
            var rangeValue = Mathf.Clamp01(tipRange);
            var tipness = rangeValue > 0f
                ? Mathf.Clamp01((tipnessRaw - (1f - rangeValue)) / Mathf.Max(rangeValue, 0.0001f))
                : 0f;
            tipness *= tipness;
            var alpha = 1f - 0.8f * tipness * (1f - Mathf.Clamp01(tipWidth));
            return Mathf.Clamp(alpha, 0.2f, 1f);
        }

        // Based on lilOutlineUtil by lilxyzw (MIT License).
        internal static void ApplyAverageNormals(Mesh mesh, IReadOnlyList<int> bakeIndices, IReadOnlyList<float> outlineAlphaByVertex)
        {
            if (mesh == null) return;
            var vertices = mesh.vertices;
            var vertexCount = vertices?.Length ?? 0;
            if (vertexCount == 0 || bakeIndices == null || bakeIndices.Count == 0) return;
            if (outlineAlphaByVertex == null || outlineAlphaByVertex.Count < vertexCount) return;

            var normals = mesh.normals;
            if (normals == null || normals.Length != vertexCount)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }

            var tangents = mesh.tangents;
            if (tangents == null || tangents.Length != vertexCount)
            {
                mesh.RecalculateTangents();
                tangents = mesh.tangents;
            }

            var colors = mesh.colors != null && mesh.colors.Length == vertexCount
                ? mesh.colors
                : Enumerable.Repeat(Color.white, vertexCount).ToArray();
            var bakeSet = new HashSet<int>(bakeIndices.Where(i => i >= 0 && i < vertexCount));
            if (bakeSet.Count == 0)
            {
                mesh.colors = colors;
                return;
            }

            const float quantizationScale = 10000f;
            var groupedNormals = new Dictionary<Vector3Int, AveragedNormalAccumulator>(bakeSet.Count);
            foreach (var index in bakeSet)
            {
                if (normals == null || normals.Length <= index) continue;
                var key = QuantizePosition(vertices[index], quantizationScale);
                groupedNormals.TryGetValue(key, out var accumulator);
                accumulator.normalSum += normals[index];
                accumulator.count++;
                groupedNormals[key] = accumulator;
            }

            foreach (var index in bakeSet)
            {
                var alpha = Mathf.Clamp(outlineAlphaByVertex[index], 0.2f, 1f);
                if (normals == null || tangents == null || normals.Length <= index || tangents.Length <= index)
                {
                    SetOutlineFallbackColor(colors, index, alpha);
                    continue;
                }

                var key = QuantizePosition(vertices[index], quantizationScale);
                if (!groupedNormals.TryGetValue(key, out var accumulator) || accumulator.count <= 0)
                {
                    SetOutlineFallbackColor(colors, index, alpha);
                    continue;
                }

                var averagedNormal = (accumulator.normalSum / accumulator.count).normalized;
                var normalOs = normals[index];
                var tangentOs = new Vector3(tangents[index].x, tangents[index].y, tangents[index].z);
                if (normalOs.sqrMagnitude <= 1e-10f || tangentOs.sqrMagnitude <= 1e-10f)
                {
                    SetOutlineFallbackColor(colors, index, alpha);
                    continue;
                }

                normalOs.Normalize();
                tangentOs.Normalize();
                var bitangentOs = Vector3.Cross(normalOs, tangentOs) * tangents[index].w;
                if (bitangentOs.sqrMagnitude <= 1e-10f)
                {
                    SetOutlineFallbackColor(colors, index, alpha);
                    continue;
                }

                bitangentOs.Normalize();
                var normalTs = new Vector3(
                    Vector3.Dot(averagedNormal, tangentOs),
                    Vector3.Dot(averagedNormal, bitangentOs),
                    Vector3.Dot(averagedNormal, normalOs));
                if (normalTs.sqrMagnitude > 0f) normalTs.Normalize();
                var encoded = normalTs * 0.5f + Vector3.one * 0.5f;
                colors[index] = new Color(encoded.x, encoded.y, encoded.z, alpha);
            }
            mesh.colors = colors;
        }

        private static void SetOutlineFallbackColor(Color[] colors, int index, float alpha)
        {
            if (colors == null || index < 0 || index >= colors.Length) return;
            colors[index] = new Color(0.5f, 0.5f, 1f, alpha);
        }

        private static Vector3Int QuantizePosition(Vector3 position, float scale)
        {
            return new Vector3Int(
                Mathf.RoundToInt(position.x * scale),
                Mathf.RoundToInt(position.y * scale),
                Mathf.RoundToInt(position.z * scale));
        }

        private static float WrapUv01(float value)
        {
            var wrapped = value - Mathf.Floor(value);
            if (Mathf.Abs(value - 1f) < 0.000001f) return 1f;
            return Mathf.Clamp01(wrapped);
        }
    }
}
