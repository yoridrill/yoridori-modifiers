using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace YoridoriModifiers.OutlineExtender
{
    internal readonly struct OutlineUvMetricSample
    {
        internal readonly int X, Y;
        internal readonly float SignedDistance;
        internal readonly float ObjectLengthPerTexel;
        internal readonly float RequiredTexelWidth;
        internal readonly float FinalTexelWidth;
        internal readonly int Contributors;

        internal OutlineUvMetricSample(int x, int y, float signedDistance, float objectLengthPerTexel,
            float requiredTexelWidth, float finalTexelWidth, int contributors)
        {
            X = x; Y = y; SignedDistance = signedDistance;
            ObjectLengthPerTexel = objectLengthPerTexel;
            RequiredTexelWidth = requiredTexelWidth;
            FinalTexelWidth = finalTexelWidth;
            Contributors = contributors;
        }
    }

    /// <summary>
    /// Per-triangle UV-to-avatar-space Jacobians used by the texture bake path.
    /// Overlapping UV surfaces deliberately resolve with Min: a texel shared by a
    /// narrow and a wide requirement must use the thinner line to avoid overdraw.
    /// </summary>
    internal sealed class OutlineUvMetric
    {
        private readonly struct Triangle
        {
            internal readonly Vector2 A, B, C;
            internal readonly Vector3 DPdu, DPdv;
            internal Triangle(Vector2 a, Vector2 b, Vector2 c, Vector3 dPdu, Vector3 dPdv)
            { A = a; B = b; C = c; DPdu = dPdu; DPdv = dPdv; }
        }

        private readonly List<Triangle> triangles;
        private readonly bool repeatU, repeatV;
        internal Hash128 Signature { get; }

        private OutlineUvMetric(List<Triangle> triangles, bool repeatU, bool repeatV, Hash128 signature)
        {
            this.triangles = triangles;
            this.repeatU = repeatU;
            this.repeatV = repeatV;
            Signature = signature;
        }

        internal static OutlineUvMetric Build(GameObject root, Material material, Texture texture,
            string textureProperty)
        {
            if (root == null || material == null || texture == null) return null;
            var scale = material.GetTextureScale(textureProperty);
            var offset = material.GetTextureOffset(textureProperty);
            var result = new List<Triangle>();
            ulong hashA = 1469598103934665603UL, hashB = 1099511628211UL;
            MixInt((int)texture.wrapModeU); MixInt((int)texture.wrapModeV);
            MixFloat(scale.x); MixFloat(scale.y); MixFloat(offset.x); MixFloat(offset.y);

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = OutlineRendererUtility.GetMesh(renderer);
                if (mesh == null) continue;
                var materials = renderer.sharedMaterials;
                var uv = new List<Vector2>();
                mesh.GetUVs(0, uv);
                if (uv.Count != mesh.vertexCount) continue;
                var vertices = mesh.vertices;
                var toRoot = root.transform.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (var subMesh = 0; subMesh < Math.Min(mesh.subMeshCount, materials.Length); subMesh++)
                {
                    if (materials[subMesh] != material) continue;
                    var indices = mesh.GetTriangles(subMesh);
                    for (var i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var ia = indices[i]; var ib = indices[i + 1]; var ic = indices[i + 2];
                        var a = Vector2.Scale(uv[ia], scale) + offset;
                        var b = Vector2.Scale(uv[ib], scale) + offset;
                        var c = Vector2.Scale(uv[ic], scale) + offset;
                        var p0 = toRoot.MultiplyPoint3x4(vertices[ia]);
                        var p1 = toRoot.MultiplyPoint3x4(vertices[ib]);
                        var p2 = toRoot.MultiplyPoint3x4(vertices[ic]);
                        var du1 = b.x - a.x; var dv1 = b.y - a.y;
                        var du2 = c.x - a.x; var dv2 = c.y - a.y;
                        var determinant = du1 * dv2 - du2 * dv1;
                        if (Mathf.Abs(determinant) < 1e-12f) continue;
                        var inverse = 1f / determinant;
                        var e1 = p1 - p0; var e2 = p2 - p0;
                        var dPdu = (e1 * dv2 - e2 * dv1) * inverse;
                        var dPdv = (-e1 * du2 + e2 * du1) * inverse;
                        if (!Finite(dPdu) || !Finite(dPdv)) continue;
                        result.Add(new Triangle(a, b, c, dPdu, dPdv));
                        MixVector2(a); MixVector2(b); MixVector2(c);
                        MixVector3(dPdu); MixVector3(dPdv);
                    }
                }
            }
            return result.Count == 0 ? null : new OutlineUvMetric(result,
                texture.wrapModeU == TextureWrapMode.Repeat,
                texture.wrapModeV == TextureWrapMode.Repeat,
                new Hash128(hashA, hashB));

            void MixInt(int value)
            {
                hashA = (hashA ^ (uint)value) * 1099511628211UL;
                hashB = (hashB ^ ((uint)value + 0x9e3779b9U)) * 14029467366897019727UL;
            }
            void MixFloat(float value) => MixInt(value.GetHashCode());
            void MixVector2(Vector2 value) { MixFloat(value.x); MixFloat(value.y); }
            void MixVector3(Vector3 value) { MixFloat(value.x); MixFloat(value.y); MixFloat(value.z); }
        }

        internal float[] RasterizeRequiredWidths(int width, int height, float[] signedDistances,
            float desiredObjectWidth, float maximumWidth,
            float minimumWidth = 0f,
            Action<IReadOnlyList<OutlineUvMetricSample>> diagnostics = null)
        {
            var result = new float[width * height];
            for (var i = 0; i < result.Length; i++) result[i] = float.PositiveInfinity;
            var winningObjectLength = diagnostics != null ? new float[result.Length] : null;
            var winningRequiredWidth = diagnostics != null ? new float[result.Length] : null;
            var contributorCounts = diagnostics != null ? new int[result.Length] : null;
            var rows = new List<RasterTriangle>[height];
            for (var i = 0; i < height; i++) rows[i] = new List<RasterTriangle>();
            foreach (var triangle in triangles)
            {
                var minU = Mathf.Min(triangle.A.x, triangle.B.x, triangle.C.x);
                var maxU = Mathf.Max(triangle.A.x, triangle.B.x, triangle.C.x);
                var minV = Mathf.Min(triangle.A.y, triangle.B.y, triangle.C.y);
                var maxV = Mathf.Max(triangle.A.y, triangle.B.y, triangle.C.y);
                var firstX = repeatU ? Mathf.CeilToInt(-maxU) : 0;
                var lastX = repeatU ? Mathf.FloorToInt(1f - minU) : 0;
                var firstY = repeatV ? Mathf.CeilToInt(-maxV) : 0;
                var lastY = repeatV ? Mathf.FloorToInt(1f - minV) : 0;
                for (var sy = firstY; sy <= lastY; sy++)
                for (var sx = firstX; sx <= lastX; sx++)
                    AddRows(rows, width, height, triangle, new Vector2(sx, sy));
            }

            Parallel.For(0, height, y =>
            {
                foreach (var triangle in rows[y])
                {
                    for (var x = triangle.MinX; x <= triangle.MaxX; x++)
                    {
                        var p = new Vector2(x, y);
                        if (!triangle.Contains(p)) continue;
                        var index = y * width + x;
                        var signed = signedDistances[index] - 0.5f;
                        if (signed > 0.75f || signed < -maximumWidth - 1f) continue;
                        var gx = SampleSigned(signedDistances, width, height, x + 1, y)
                            - SampleSigned(signedDistances, width, height, x - 1, y);
                        var gy = SampleSigned(signedDistances, width, height, x, y + 1)
                            - SampleSigned(signedDistances, width, height, x, y - 1);
                        var gradientLength = Mathf.Sqrt(gx * gx + gy * gy);
                        if (gradientLength < 1e-6f) continue;
                        var nx = gx / gradientLength; var ny = gy / gradientLength;
                        var objectStep = triangle.DPdu * (nx / width) + triangle.DPdv * (ny / height);
                        var objectLengthPerTexel = objectStep.magnitude;
                        if (!(objectLengthPerTexel > 1e-9f)) continue;
                        var requiredUnclamped = desiredObjectWidth / objectLengthPerTexel;
                        var required = Mathf.Clamp(requiredUnclamped, 0f, maximumWidth);
                        if (contributorCounts != null) contributorCounts[index]++;
                        // Min is intentional for overlapping UVs; thin always wins.
                        if (required < result[index])
                        {
                            result[index] = required;
                            if (winningObjectLength != null)
                            {
                                winningObjectLength[index] = objectLengthPerTexel;
                                winningRequiredWidth[index] = requiredUnclamped;
                            }
                        }
                    }
                }
            });
            // Resolve overlaps with Min first, then apply the rasterization floor.
            // This preserves the "thin surface wins" rule without allowing a final
            // line narrower than one texel in the Bake path.
            if (minimumWidth > 0f)
                Parallel.For(0, result.Length, i =>
                {
                    if (!float.IsPositiveInfinity(result[i]))
                        result[i] = Mathf.Max(minimumWidth, result[i]);
                });
            if (diagnostics != null)
            {
                var candidates = new List<int>();
                for (var i = 0; i < result.Length; i++)
                {
                    var signed = signedDistances[i] - 0.5f;
                    if (!float.IsPositiveInfinity(result[i]) && signed >= -1f && signed <= 0.25f)
                        candidates.Add(i);
                }
                var samples = new List<OutlineUvMetricSample>();
                var used = new HashSet<int>();
                const int sampleCount = 5;
                for (var s = 0; s < sampleCount && candidates.Count > 0; s++)
                {
                    var selected = candidates[Mathf.RoundToInt((candidates.Count - 1) * (s / (sampleCount - 1f)))];
                    if (!used.Add(selected)) continue;
                    samples.Add(new OutlineUvMetricSample(selected % width, selected / width,
                        signedDistances[selected] - 0.5f, winningObjectLength[selected],
                        winningRequiredWidth[selected], result[selected], contributorCounts[selected]));
                }
                diagnostics(samples);
            }
            return result;
        }

        private readonly struct RasterTriangle
        {
            internal readonly Vector2 A, B, C;
            internal readonly Vector3 DPdu, DPdv;
            internal readonly int MinX, MaxX;
            private readonly float toleranceAB, toleranceBC, toleranceCA;
            internal RasterTriangle(Vector2 a, Vector2 b, Vector2 c, Vector3 dPdu, Vector3 dPdv,
                int minX, int maxX)
            {
                A = a; B = b; C = c; DPdu = dPdu; DPdv = dPdv; MinX = minX; MaxX = maxX;
                toleranceAB = 0.5f * (Mathf.Abs(b.x - a.x) + Mathf.Abs(b.y - a.y));
                toleranceBC = 0.5f * (Mathf.Abs(c.x - b.x) + Mathf.Abs(c.y - b.y));
                toleranceCA = 0.5f * (Mathf.Abs(a.x - c.x) + Mathf.Abs(a.y - c.y));
            }
            internal bool Contains(Vector2 p) => Edge(A, B, p) >= -toleranceAB
                && Edge(B, C, p) >= -toleranceBC && Edge(C, A, p) >= -toleranceCA;
        }

        private static void AddRows(List<RasterTriangle>[] rows, int width, int height, Triangle source, Vector2 shift)
        {
            var a = ToPixel(source.A + shift, width, height);
            var b = ToPixel(source.B + shift, width, height);
            var c = ToPixel(source.C + shift, width, height);
            if (Edge(a, b, c) < 0) (b, c) = (c, b);
            var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x) - 1));
            var maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x) + 1));
            var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y) - 1));
            var maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y) + 1));
            if (minX > maxX || minY > maxY) return;
            var triangle = new RasterTriangle(a, b, c, source.DPdu, source.DPdv, minX, maxX);
            for (var y = minY; y <= maxY; y++) rows[y].Add(triangle);
        }

        private float SampleSigned(float[] values, int width, int height, int x, int y)
        {
            x = Address(x, width, repeatU); y = Address(y, height, repeatV);
            return values[y * width + x] - 0.5f;
        }

        private static Vector2 ToPixel(Vector2 uv, int width, int height) =>
            new(uv.x * width - 0.5f, uv.y * height - 0.5f);
        private static float Edge(Vector2 a, Vector2 b, Vector2 p) =>
            (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        private static int Address(int value, int size, bool repeat) => repeat
            ? (value % size + size) % size : Math.Max(0, Math.Min(value, size - 1));
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
