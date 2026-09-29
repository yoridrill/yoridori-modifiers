using System;
using System.Collections.Generic;
using UnityEngine;

namespace YoridoriModifiers.OutlineExtender
{
    /// <summary>
    /// UV0 areas actually used by the final renderer/submeshes for one material.
    /// Contours outside these areas must not extend back into a transparent mesh edge.
    /// </summary>
    internal sealed class OutlineUvCoverage
    {
        private readonly struct Triangle
        {
            internal readonly Vector2 A, B, C;
            internal Triangle(Vector2 a, Vector2 b, Vector2 c) { A = a; B = b; C = c; }
        }

        private readonly List<Triangle> triangles;
        private readonly bool repeatU;
        private readonly bool repeatV;
        internal Hash128 Signature { get; }

        private OutlineUvCoverage(List<Triangle> triangles, bool repeatU, bool repeatV, Hash128 signature)
        {
            this.triangles = triangles;
            this.repeatU = repeatU;
            this.repeatV = repeatV;
            Signature = signature;
        }

        internal static OutlineUvCoverage Build(GameObject root, Material material, Texture texture)
        {
            if (root == null || material == null || texture == null) return null;
            var scale = material.GetTextureScale("_MainTex");
            var offset = material.GetTextureOffset("_MainTex");
            var result = new List<Triangle>();
            ulong hashA = 1469598103934665603UL;
            ulong hashB = 1099511628211UL;
            MixInt((int)texture.wrapModeU); MixInt((int)texture.wrapModeV);
            MixFloat(scale.x); MixFloat(scale.y); MixFloat(offset.x); MixFloat(offset.y);

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var materials = renderer.sharedMaterials;
                var uv = new List<Vector2>();
                mesh.GetUVs(0, uv);
                if (uv.Count != mesh.vertexCount) continue;
                for (var subMesh = 0; subMesh < Math.Min(mesh.subMeshCount, materials.Length); subMesh++)
                {
                    if (materials[subMesh] != material) continue;
                    var indices = mesh.GetTriangles(subMesh);
                    // Only result-affecting UV geometry participates. Renderer/material
                    // identity and outline settings must not split an otherwise reusable SDF.
                    MixInt(indices.Length);
                    for (var i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var a = Vector2.Scale(uv[indices[i]], scale) + offset;
                        var b = Vector2.Scale(uv[indices[i + 1]], scale) + offset;
                        var c = Vector2.Scale(uv[indices[i + 2]], scale) + offset;
                        result.Add(new Triangle(a, b, c));
                        MixVector(a); MixVector(b); MixVector(c);
                    }
                }
            }
            return result.Count == 0 ? null : new OutlineUvCoverage(result,
                texture.wrapModeU == TextureWrapMode.Repeat,
                texture.wrapModeV == TextureWrapMode.Repeat,
                new Hash128(hashA, hashB));

            void MixInt(int value)
            {
                hashA = (hashA ^ (uint)value) * 1099511628211UL;
                hashB = (hashB ^ ((uint)value + 0x9e3779b9U)) * 14029467366897019727UL;
            }
            void MixFloat(float value) => MixInt(value.GetHashCode());
            void MixVector(Vector2 value) { MixFloat(value.x); MixFloat(value.y); }
        }

        internal byte[] Rasterize(int width, int height)
        {
            var mask = new byte[width * height];
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
                {
                    var shift = new Vector2(sx, sy);
                    RasterizeTriangle(mask, width, height,
                        triangle.A + shift, triangle.B + shift, triangle.C + shift);
                }
            }
            return mask;
        }

        private static void RasterizeTriangle(byte[] mask, int width, int height, Vector2 aUv, Vector2 bUv, Vector2 cUv)
        {
            // Texture sample centers are integer positions after the half-texel shift.
            var a = new Vector2(aUv.x * width - 0.5f, aUv.y * height - 0.5f);
            var b = new Vector2(bUv.x * width - 0.5f, bUv.y * height - 0.5f);
            var c = new Vector2(cUv.x * width - 0.5f, cUv.y * height - 0.5f);
            var area = Edge(a, b, c);
            if (Mathf.Abs(area) < 1e-8f) return;
            if (area < 0) (b, c) = (c, b);
            var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x) - 1));
            var maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x) + 1));
            var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y) - 1));
            var maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y) + 1));
            var toleranceAB = 0.5f * (Mathf.Abs(b.x - a.x) + Mathf.Abs(b.y - a.y));
            var toleranceBC = 0.5f * (Mathf.Abs(c.x - b.x) + Mathf.Abs(c.y - b.y));
            var toleranceCA = 0.5f * (Mathf.Abs(a.x - c.x) + Mathf.Abs(a.y - c.y));
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x, y);
                if (Edge(a, b, p) >= -toleranceAB && Edge(b, c, p) >= -toleranceBC
                    && Edge(c, a, p) >= -toleranceCA)
                    mask[y * width + x] = 1;
            }
        }

        private static float Edge(Vector2 a, Vector2 b, Vector2 p) =>
            (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

    }
}
