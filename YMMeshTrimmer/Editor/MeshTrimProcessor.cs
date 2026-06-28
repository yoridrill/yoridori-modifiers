using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.Rendering;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.MeshTrimmer
{
public static class MeshTrimProcessor
{
    private const string ToolName = "YM Mesh Trimmer";
    private const float DefaultMinPolygonAreaRatio = 0.01f;
    private const float DefaultMinChordLengthRatio = 0.03f;
    private const float LoopDuplicateUvEpsilonSqr = 1e-12f;

    private static bool IsVerbose(MeshTrimmerComponent trimmer)
        => trimmer != null && trimmer.debugEdgeCrossingRoutes;
    public struct VertexSource
    {
        public int index0; public float weight0;
        public int index1; public float weight1;
        public int index2; public float weight2;
        public int index3; public float weight3;

        public static VertexSource Original(int originalIndex)
        {
            return new VertexSource { index0 = originalIndex, weight0 = 1f, index1 = -1, index2 = -1, index3 = -1 };
        }
    }

    private class SubMeshTask
    {
        public AlphaMaskProcessor.AlphaMaskData maskData;
        public Texture2D texture;
        public bool enablePreSubdivide;
        public int preSubdivideLevel;
    }


    private enum TriangleTrimState
    {
        StrongKeep,
        StrongTrim,
        Clipped,
        Ambiguous
    }

    private struct TriangleTrimResult
    {
        public int triangleIndex;
        public TriangleTrimState state;
        public float originalAreaUv;
        public float keptAreaUv;
        public float removedAreaUv;
        public float keptAreaRatio;
        public float removedAreaRatio;
        public int cutEdges;
        public int[] cutPoints;
        public Vector2 keptRegionCentroidUv;
        public Vector2 removedRegionCentroidUv;
        public int keptRegionContactEdges;
        public int removedRegionContactEdges;
        public int generatedTriangles;
    }

    private struct EdgeCutInfo
    {
        public long edgeKey;
        public int triangleIndex;
        public int cutPointIndex;
        public Vector2 cutPointUv;
        public int edgeA;
        public int edgeB;
        public Vector2 keptSideCentroidUv;
        public Vector2 removedSideCentroidUv;
        public bool keptSideUsesEdgeA;
    }

    private struct TrimStats
    {
        public int originalTriangles;
        public int outputTriangles;
        public int removedTriangles;
        public int addedVertices;
        public int intersections;
        public int allInsideButInteriorOutside;
        public int allOutsideButInteriorInside;
        public int centroidOnlyInsidePreserved;
        public int singleEdgeMidpointInsideDiscarded;
        public int singleEdgeMidpointAndCentroidInsidePreserved;
        public int twoEdgeMidpointsInsideClipped;
        public int allEdgeMidpointsInsidePreserved;
        public int insidePointFallbackPreserved;
        public int routeWholeKeep;
        public int routeWholeTrim;
        public int routeOneLine;
        public int routeTwoLineOddOddEven;
        public int routeTwoLineEvenEven;
        public int routeMajorityFallback;
    }

    public static void ApplyTrim(MeshTrimmerComponent trimmer)
    {
        ApplyTrim(trimmer, true);
    }

    public static void ApplyTrim(MeshTrimmerComponent trimmer, bool preserveBlendShapes, BuildContext context = null)
    {
        if (trimmer == null) return;

        Dictionary<Texture2D, AlphaMaskProcessor.AlphaMaskData> maskCache = new Dictionary<Texture2D, AlphaMaskProcessor.AlphaMaskData>();
        Dictionary<SkinnedMeshRenderer, Dictionary<int, SubMeshTask>> tasksByRenderer = new Dictionary<SkinnedMeshRenderer, Dictionary<int, SubMeshTask>>();
        int preSubdivideEnabledTargetCount = 0;

        foreach (var target in trimmer.targets)
        {
            if (target == null || !target.enabled || target.mainTexture == null)
            {
                continue;
            }

            if (!maskCache.TryGetValue(target.mainTexture, out var maskData))
            {
                if (!AlphaMaskProcessor.TryBuildMask(target.mainTexture, trimmer, out maskData))
                {
                    continue;
                }
                maskCache[target.mainTexture] = maskData;
            }

            if (target.enablePreSubdivide) preSubdivideEnabledTargetCount++;

            foreach (var usage in target.usages)
            {
                if (usage == null || usage.renderer == null || !usage.renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!tasksByRenderer.TryGetValue(usage.renderer, out var subTasks))
                {
                    subTasks = new Dictionary<int, SubMeshTask>();
                    tasksByRenderer[usage.renderer] = subTasks;
                }

                if (!subTasks.TryGetValue(usage.subMeshIndex, out var existingTask))
                {
                    existingTask = new SubMeshTask();
                    subTasks[usage.subMeshIndex] = existingTask;
                }

                existingTask.maskData = maskData;
                existingTask.texture = target.mainTexture;
                existingTask.enablePreSubdivide = existingTask.enablePreSubdivide || target.enablePreSubdivide;
                existingTask.preSubdivideLevel = Mathf.Max(existingTask.preSubdivideLevel, target.preSubdivideLevel);
            }
        }

        LogUtility.Verbose(
            ToolName,
            IsVerbose(trimmer),
            "Trim",
            $"TaskRenderers={tasksByRenderer.Count}, PreSubdivideEnabledTargetCount={preSubdivideEnabledTargetCount}, TrimAlgorithm={trimmer.trimAlgorithm}");
        foreach (var kv in tasksByRenderer)
        {
            ProcessRenderer(kv.Key, kv.Value, trimmer, preserveBlendShapes, context);
        }
    }

    private static void ProcessRenderer(
        SkinnedMeshRenderer renderer,
        Dictionary<int, SubMeshTask> tasks,
        MeshTrimmerComponent trimmer,
        bool preserveBlendShapes,
        BuildContext context)
    {
        ApplyDerivedMeshSettings(trimmer);
        Mesh src = renderer.sharedMesh;
        if (src == null)
        {
            return;
        }

        var vertices = new List<Vector3>(src.vertices);
        var normals = new List<Vector3>(src.normals);
        var tangents = new List<Vector4>(src.tangents);
        var uv = new List<Vector2>(src.uv);
        var uv2 = new List<Vector2>(src.uv2);
        var uv3 = new List<Vector2>(src.uv3);
        var uv4 = new List<Vector2>(src.uv4);
        var colors = new List<Color>(src.colors);
        var boneWeights = new List<BoneWeight>(src.boneWeights);
        var vertexSources = new List<VertexSource>(vertices.Count);
        for (int v = 0; v < vertices.Count; v++)
        {
            vertexSources.Add(VertexSource.Original(v));
        }

        bool hasNormals = normals.Count == vertices.Count;
        bool hasTangents = tangents.Count == vertices.Count;
        bool hasUv = uv.Count == vertices.Count;
        bool hasUv2 = uv2.Count == vertices.Count;
        bool hasUv3 = uv3.Count == vertices.Count;
        bool hasUv4 = uv4.Count == vertices.Count;
        bool hasColors = colors.Count == vertices.Count;
        bool hasBoneWeights = boneWeights.Count == vertices.Count;

        if (!hasUv)
        {
            LogUtility.Warning(ToolName, "Trim", $"UV missing. Renderer skipped: {renderer.name}");
            return;
        }

        int baseVertexCount = vertices.Count;
        List<int>[] newSubMeshIndices = new List<int>[src.subMeshCount];

        for (int sub = 0; sub < src.subMeshCount; sub++)
        {
            int[] srcIndices = src.GetTriangles(sub);
            newSubMeshIndices[sub] = new List<int>(srcIndices.Length);

            if (!tasks.TryGetValue(sub, out var task))
            {
                newSubMeshIndices[sub].AddRange(srcIndices);
                continue;
            }

            int[] workingIndices = srcIndices;
            int triBeforeSub = srcIndices.Length / 3;
            int preAddedVertices = 0;
            var swPre = System.Diagnostics.Stopwatch.StartNew();
            if (task.enablePreSubdivide && task.preSubdivideLevel > 0)
            {
                workingIndices = PreSubdivideIndices(srcIndices, task.preSubdivideLevel, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights, hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, ref preAddedVertices);
            }
            swPre.Stop();

            TrimStats stats = ProcessSubMesh(
                workingIndices,
                task.maskData,
                trimmer,
                renderer.name,
                sub,
                vertices,
                normals,
                tangents,
                uv,
                uv2,
                uv3,
                uv4,
                colors,
                boneWeights,
                hasNormals,
                hasTangents,
                hasUv2,
                hasUv3,
                hasUv4,
                hasColors,
                hasBoneWeights,
                vertexSources,
                newSubMeshIndices[sub],
                renderer.sharedMaterials != null && sub < renderer.sharedMaterials.Length && renderer.sharedMaterials[sub] != null ? renderer.sharedMaterials[sub].name : string.Empty);

            stats.addedVertices = vertices.Count - baseVertexCount;
            baseVertexCount = vertices.Count;

            string routeInfo = IsVerbose(trimmer)
                ? $", RouteWholeKeep={stats.routeWholeKeep}, RouteWholeTrim={stats.routeWholeTrim}, RouteOneLine={stats.routeOneLine}, RouteTwoLineOddOddEven={stats.routeTwoLineOddOddEven}, RouteTwoLineEvenEven={stats.routeTwoLineEvenEven}, RouteMajorityFallback={stats.routeMajorityFallback}, MinFragPermille={trimmer.minimumFragmentSizePermille:F3}, MinPolyRatio={trimmer.edgeCrossingMinPolygonAreaRatio:F6}, MinChordRatio={trimmer.edgeCrossingMinChordLengthRatio:F6}"
                : string.Empty;
            LogUtility.Verbose(
                ToolName,
                IsVerbose(trimmer),
                "Trim",
                $"Renderer={renderer.name}, SubMesh={sub}, Texture={task.texture.name}, PreSubdivideEnabled={task.enablePreSubdivide}, PreSubdivideLevel={task.preSubdivideLevel}, TrianglesBeforePreSubdivide={triBeforeSub}, TrianglesAfterPreSubdivide={workingIndices.Length / 3}, PreSubdivideAddedVertices={preAddedVertices}, PreSubdivideMs={swPre.ElapsedMilliseconds}, " +
                $"OriginalTriangles={stats.originalTriangles}, OutputTriangles={stats.outputTriangles}, RemovedTriangles={stats.removedTriangles}, " +
                $"AddedVertices={stats.addedVertices}, Intersections={stats.intersections}, " +
                $"AllInsideButInteriorOutside={stats.allInsideButInteriorOutside}, AllOutsideButInteriorInside={stats.allOutsideButInteriorInside}, " +
                $"CentroidOnlyInsidePreserved={stats.centroidOnlyInsidePreserved}, SingleEdgeMidpointInsideDiscarded={stats.singleEdgeMidpointInsideDiscarded}, " +
                $"SingleEdgeMidpointAndCentroidInsidePreserved={stats.singleEdgeMidpointAndCentroidInsidePreserved}, TwoEdgeMidpointsInsideClipped={stats.twoEdgeMidpointsInsideClipped}, " +
                $"AllEdgeMidpointsInsidePreserved={stats.allEdgeMidpointsInsidePreserved}, InsidePointFallbackPreserved={stats.insidePointFallbackPreserved}{routeInfo}, " +
                $"TrianglesAfterTrim={stats.outputTriangles}");
        }

        Mesh dst = new Mesh
        {
            name = src.name + "_YMMeshTrimmerTrimmed",
            indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            subMeshCount = src.subMeshCount,
            bindposes = src.bindposes
        };

        dst.SetVertices(vertices);
        if (hasNormals) dst.SetNormals(normals);
        if (hasTangents) dst.SetTangents(tangents);
        if (hasUv) dst.SetUVs(0, uv);
        if (hasUv2) dst.SetUVs(1, uv2);
        if (hasUv3) dst.SetUVs(2, uv3);
        if (hasUv4) dst.SetUVs(3, uv4);
        if (hasColors) dst.SetColors(colors);
        if (hasBoneWeights) dst.boneWeights = boneWeights.ToArray();

        for (int sub = 0; sub < src.subMeshCount; sub++)
        {
            dst.SetTriangles(newSubMeshIndices[sub], sub, true);
        }

        float[] savedBlendShapeWeights = SaveBlendShapeWeights(renderer, src);
        string[] savedBlendShapeNames = SaveBlendShapeNames(src);
        CopyBlendShapes(src, dst, vertexSources, renderer.name, preserveBlendShapes, trimmer != null && trimmer.debugEdgeCrossingRoutes);

        dst.RecalculateBounds();
        NdmfObjectRegistry.RegisterReplacement(src, dst);
        context?.AssetSaver.SaveAsset(dst);
        renderer.sharedMesh = dst;
        RestoreBlendShapeWeights(renderer, dst, savedBlendShapeNames, savedBlendShapeWeights);
    }

    private static void ApplyDerivedMeshSettings(MeshTrimmerComponent trimmer)
    {
        if (trimmer == null) return;
        float minFragmentRatio = Mathf.Clamp(trimmer.minimumFragmentSizePermille, 0.01f, 100.0f) * 0.001f;
        trimmer.edgeCrossingMinPolygonAreaRatio = minFragmentRatio;
        trimmer.edgeCrossingMinChordLengthRatio = Mathf.Sqrt(minFragmentRatio);
        trimmer.minTriangleUvArea = 1e-10f;
        trimmer.minTriangleWorldArea = 1e-12f;
        trimmer.edgeCrossingMergeEpsilon = 0.001f;
        trimmer.edgeCrossingEndpointSnapEpsilon = 0.001f;
        trimmer.edgeCrossingCacheQuantizeStep = 0.001f;
    }

    private static int[] PreSubdivideIndices(
        int[] srcIndices,
        int level,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Vector2> uv,
        List<Vector2> uv2,
        List<Vector2> uv3,
        List<Vector2> uv4,
        List<Color> colors,
        List<BoneWeight> boneWeights,
        bool hasNormals,
        bool hasTangents,
        bool hasUv2,
        bool hasUv3,
        bool hasUv4,
        bool hasColors,
        bool hasBoneWeights,
        List<VertexSource> vertexSources,
        ref int addedVertices)
    {
        var indices = srcIndices;
        for (int lv = 0; lv < level; lv++)
        {
            var cache = new Dictionary<long, int>();
            var next = new List<int>(indices.Length * 4);
            for (int i = 0; i < indices.Length; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                int m01 = GetOrCreateMid(i0, i1, cache, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights, hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, ref addedVertices);
                int m12 = GetOrCreateMid(i1, i2, cache, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights, hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, ref addedVertices);
                int m20 = GetOrCreateMid(i2, i0, cache, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights, hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, ref addedVertices);
                next.Add(i0); next.Add(m01); next.Add(m20);
                next.Add(m01); next.Add(i1); next.Add(m12);
                next.Add(m20); next.Add(m12); next.Add(i2);
                next.Add(m01); next.Add(m12); next.Add(m20);
            }
            indices = next.ToArray();
        }
        return indices;
    }

    private static int GetOrCreateMid(int a, int b, Dictionary<long, int> cache,
        List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector2> uv, List<Vector2> uv2, List<Vector2> uv3, List<Vector2> uv4,
        List<Color> colors, List<BoneWeight> boneWeights, bool hasNormals, bool hasTangents, bool hasUv2, bool hasUv3, bool hasUv4, bool hasColors, bool hasBoneWeights,
        List<VertexSource> vertexSources, ref int addedVertices)
    {
        int lo = Math.Min(a,b), hi=Math.Max(a,b); long key=((long)lo<<32)|(uint)hi;
        if (cache.TryGetValue(key, out int idx)) return idx;
        TrimStats dummy = default;
        idx = AddInterpolatedVertex(a,b,0.5f,vertices,normals,tangents,uv,uv2,uv3,uv4,colors,boneWeights,hasNormals,hasTangents,hasUv2,hasUv3,hasUv4,hasColors,hasBoneWeights,vertexSources, ref dummy);
        cache[key]=idx; addedVertices++; return idx;
    }

    private static float SignedArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        float o1 = SignedArea(p1, p2, q1);
        float o2 = SignedArea(p1, p2, q2);
        float o3 = SignedArea(q1, q2, p1);
        float o4 = SignedArea(q1, q2, p2);
        return (o1 * o2 < 0f) && (o3 * o4 < 0f);
    }

    private static TrimStats ProcessSubMesh(
        int[] srcIndices,
        AlphaMaskProcessor.AlphaMaskData maskData,
        MeshTrimmerComponent trimmer,
        string debugRendererName,
        int debugSubMeshIndex,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Vector2> uv,
        List<Vector2> uv2,
        List<Vector2> uv3,
        List<Vector2> uv4,
        List<Color> colors,
        List<BoneWeight> boneWeights,
        bool hasNormals,
        bool hasTangents,
        bool hasUv2,
        bool hasUv3,
        bool hasUv4,
        bool hasColors,
        bool hasBoneWeights,
        List<VertexSource> vertexSources,
        List<int> dstIndices,
        string debugMaterialName)
    {
        // LegacyInsidePoint = existing advanced inside-point route (not 7-point majority fallback).
        if (trimmer != null && trimmer.trimAlgorithm == MeshTrimmerComponent.TrimAlgorithm.LegacyInsidePoint)
        {
            return ProcessSubMeshInsidePoint(srcIndices, maskData, trimmer, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, dstIndices);
        }

        return ProcessSubMeshEdgeCrossing(srcIndices, maskData, trimmer, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
            hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, dstIndices, debugMaterialName, debugRendererName, debugSubMeshIndex);
    }

    // NOTE: Edge-crossing route uses dedicated routing, and falls back to 7-point majority voting when route payload emission is inconsistent.
    // The routing/polygon payload generated by EdgeCrossingTrimRouter is prepared and validated separately.
    // This keeps Preview/Build stable while integration is completed in subsequent steps.
    private static TrimStats ProcessSubMeshEdgeCrossing(
        int[] srcIndices,
        AlphaMaskProcessor.AlphaMaskData maskData,
        MeshTrimmerComponent trimmer,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Vector2> uv,
        List<Vector2> uv2,
        List<Vector2> uv3,
        List<Vector2> uv4,
        List<Color> colors,
        List<BoneWeight> boneWeights,
        bool hasNormals,
        bool hasTangents,
        bool hasUv2,
        bool hasUv3,
        bool hasUv4,
        bool hasColors,
        bool hasBoneWeights,
        List<VertexSource> vertexSources,
        List<int> dstIndices,
        string debugMaterialName,
        string debugRendererName,
        int debugSubMeshIndex)
    {
        var shared = BuildSharedCrossings(srcIndices, maskData, uv, trimmer, out var rawCrossingCounts);
        TrimStats stats = new TrimStats();
        var debugRouteCounts = new Dictionary<string, int>();
        var debugFallbackCounts = new Dictionary<string, int>();
        var suspicious = new List<string>();
        const int suspiciousMax = 5;
        var crossingVertexCache = new Dictionary<(int, int, float), int>();
        for (int i = 0; i < srcIndices.Length; i += 3)
        {
            int i0 = srcIndices[i];
            int i1 = srcIndices[i + 1];
            int i2 = srcIndices[i + 2];
            var ctx = new EdgeCrossingTrimRouter.TriangleContext
            {
                v0 = i0, v1 = i1, v2 = i2,
                uv0 = uv[i0], uv1 = uv[i1], uv2 = uv[i2],
                sharedCrossings = shared,
                SampleInside = u => AlphaMaskProcessor.SampleMask(maskData, u)
            };
            var result = EdgeCrossingTrimRouter.ProcessTriangle(ctx);
            string majorityFallbackReason = "none";
            string finalAction = "none";
            int majorityInsideCount = -1;
            stats.originalTriangles++;
            if (result.route == EdgeCrossingTrimRouter.TriangleRoute.WholeKeep)
            {
                stats.routeWholeKeep++;
                AddTriangle(dstIndices, i0, i1, i2, vertices, uv, trimmer, ref stats, skipAreaThresholds: true);
                finalAction = "WholeKeep";
            }
            else if (result.route == EdgeCrossingTrimRouter.TriangleRoute.WholeTrim)
            {
                stats.routeWholeTrim++;
                stats.removedTriangles++;
                finalAction = "WholeTrim";
            }
            else
            {
                if (result.insidePolygons != null && result.insidePolygons.Length > 0
                    && (result.route == EdgeCrossingTrimRouter.TriangleRoute.TwoOddEdgesAsOneLine
                        || result.route == EdgeCrossingTrimRouter.TriangleRoute.TwoOddEdgesAndOneEvenEdge
                        || result.route == EdgeCrossingTrimRouter.TriangleRoute.TwoEvenEdges))
                {
                    if (result.route == EdgeCrossingTrimRouter.TriangleRoute.TwoOddEdgesAsOneLine) stats.routeOneLine++;
                    else if (result.route == EdgeCrossingTrimRouter.TriangleRoute.TwoOddEdgesAndOneEvenEdge) stats.routeTwoLineOddOddEven++;
                    else stats.routeTwoLineEvenEven++;
                    bool emitOk = TryEmitInsidePolygons(result, i0, i1, i2, trimmer, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                        hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, crossingVertexCache, dstIndices, ref stats, out string polyFailReason, out string polyFailDetail);
                    if (!emitOk)
                    {
                        stats.routeMajorityFallback++;
                        majorityFallbackReason = $"emit_inside_polygons_failed:{polyFailReason}";
                        EmitMajority7PointTriangle(maskData, trimmer, i0, i1, i2, vertices, uv, dstIndices, ref stats, out var insideCount7);
                        majorityInsideCount = insideCount7;
                        finalAction = "FallbackMajority7";
                    }
                    else finalAction = "EmitInsidePolygonsSuccess";
                }
                else if (result.route == EdgeCrossingTrimRouter.TriangleRoute.TwoOddEdgesAsOneLine && result.hasOneLineSplit && IsValidLegacyOneLinePayload(ctx, result))
                {
                    stats.routeOneLine++;
                    if (!TryEmitOneLineSplit(result, i0, i1, i2, trimmer, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                        hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, crossingVertexCache, dstIndices, ref stats, out string oneLineFailReason))
                    {
                        stats.routeMajorityFallback++;
                        majorityFallbackReason = $"emit_one_line_failed:{oneLineFailReason}";
                        EmitMajority7PointTriangle(maskData, trimmer, i0, i1, i2, vertices, uv, dstIndices, ref stats, out _);
                        majorityInsideCount = -2;
                        finalAction = "FallbackMajority7";
                    }
                    else finalAction = "EmitOneLineLegacySuccess";
                }
                else
                {
                    stats.routeMajorityFallback++;
                    majorityFallbackReason = "route_or_payload_unexpected";
                    EmitMajority7PointTriangle(maskData, trimmer, i0, i1, i2, vertices, uv, dstIndices, ref stats, out _);
                    majorityInsideCount = -2;
                    finalAction = "FallbackMajority7";
                }
            }
            if (IsVerbose(trimmer))
            {
                string rk = result.route.ToString();
                debugRouteCounts[rk] = debugRouteCounts.TryGetValue(rk, out var rc) ? rc + 1 : 1;
                if (majorityFallbackReason != "none") debugFallbackCounts[majorityFallbackReason] = debugFallbackCounts.TryGetValue(majorityFallbackReason, out var fc) ? fc + 1 : 1;
                if (suspicious.Count < suspiciousMax)
                {
                    int[] s = { ctx.v0, ctx.v1, ctx.v2 };
                    int[] e = { ctx.v1, ctx.v2, ctx.v0 };
                    var infos = EdgeCrossingTrimRouter.BuildEdgeInfos(ctx);
                    int before0 = rawCrossingCounts != null && rawCrossingCounts.TryGetValue(new EdgeCrossingTrimRouter.EdgeKey(s[0], e[0]), out var b0) ? b0 : 0;
                    int before1 = rawCrossingCounts != null && rawCrossingCounts.TryGetValue(new EdgeCrossingTrimRouter.EdgeKey(s[1], e[1]), out var b1) ? b1 : 0;
                    int before2 = rawCrossingCounts != null && rawCrossingCounts.TryGetValue(new EdgeCrossingTrimRouter.EdgeKey(s[2], e[2]), out var b2) ? b2 : 0;
                    int after0 = infos[0].crossings.Count;
                    int after1 = infos[1].crossings.Count;
                    int after2 = infos[2].crossings.Count;
                    int removedNearEndpoint = Mathf.Max(0, before0 - after0) + Mathf.Max(0, before1 - after1) + Mathf.Max(0, before2 - after2);
                    bool splitRoute = result.route == EdgeCrossingTrimRouter.TriangleRoute.TwoOddEdgesAsOneLine
                        || result.route == EdgeCrossingTrimRouter.TriangleRoute.TwoOddEdgesAndOneEvenEdge
                        || result.route == EdgeCrossingTrimRouter.TriangleRoute.TwoEvenEdges;
                    bool crossingButTrimmed = finalAction == "WholeTrim" && (before0 + before1 + before2) > 0;
                    bool majorityBorderline = majorityFallbackReason.Contains("FallbackMajority7") || majorityFallbackReason.Contains("majority");
                    bool majorityInside3 = majorityInsideCount == 3;
                    bool normalizationAllRemoved = (before0 + before1 + before2) > 0 && (after0 + after1 + after2) == 0;
                    bool splitEmitFailed = splitRoute && majorityFallbackReason.StartsWith("emit_");
                    if (crossingButTrimmed || majorityFallbackReason != "none" || normalizationAllRemoved || splitEmitFailed || majorityBorderline || majorityInside3)
                    {
                        suspicious.Add($"renderer={debugRendererName} subMesh={debugSubMeshIndex} tri={i / 3} route={result.route} final={finalAction} reason={majorityFallbackReason} before=[{before0},{before1},{before2}] after=[{after0},{after1},{after2}] removedEndpointNear={removedNearEndpoint} majorityInsideCount={majorityInsideCount}");
                    }
                }
            }
        }

        if (IsVerbose(trimmer))
        {
            LogUtility.Info(ToolName, "EdgeRouteSummary", $"renderer={debugRendererName} subMesh={debugSubMeshIndex} material={debugMaterialName} triangles={stats.originalTriangles} routes={FormatCountMap(debugRouteCounts)} fallbacks={FormatCountMap(debugFallbackCounts)} suspiciousCount={suspicious.Count}");
            for (int i = 0; i < suspicious.Count; i++) LogUtility.Info(ToolName, "EdgeRouteSuspicious", suspicious[i]);
        }

        return stats;
    }

    private static string FormatCountMap(Dictionary<string, int> map)
    {
        if (map == null || map.Count == 0) return "{}";
        var items = new List<string>(map.Count);
        foreach (var kv in map) items.Add($"{kv.Key}:{kv.Value}");
        return "{" + string.Join(",", items) + "}";
    }

    private static bool IsValidLegacyOneLinePayload(EdgeCrossingTrimRouter.TriangleContext ctx, EdgeCrossingTrimRouter.TriangleProcessResult result)
    {
        if (!IsLocalCrossingNonDefault(result.splitCrossingA) || !IsLocalCrossingNonDefault(result.splitCrossingB)) return false;
        Vector2 a = EdgeCrossingTrimRouter.GetLocalCrossingUv(ctx, result.splitCrossingA);
        Vector2 b = EdgeCrossingTrimRouter.GetLocalCrossingUv(ctx, result.splitCrossingB);
        return (a - b).sqrMagnitude > 1e-12f;
    }

    private static bool IsLocalCrossingNonDefault(EdgeCrossingTrimRouter.LocalCrossing c)
        => !(c.edgeIndex == 0 && c.edgeStart == 0 && c.edgeEnd == 0 && Mathf.Abs(c.t) <= 1e-8f);

    private static void EmitMajority7PointTriangle(
        AlphaMaskProcessor.AlphaMaskData maskData,
        MeshTrimmerComponent trimmer,
        int i0, int i1, int i2,
        List<Vector3> vertices,
        List<Vector2> uv,
        List<int> dstIndices,
        ref TrimStats stats,
        out int insideCount)
    {
        Vector2 uv0 = uv[i0];
        Vector2 uv1 = uv[i1];
        Vector2 uv2 = uv[i2];
        insideCount = 0;
        if (AlphaMaskProcessor.SampleMask(maskData, uv0)) insideCount++;
        if (AlphaMaskProcessor.SampleMask(maskData, uv1)) insideCount++;
        if (AlphaMaskProcessor.SampleMask(maskData, uv2)) insideCount++;
        if (AlphaMaskProcessor.SampleMask(maskData, (uv0 + uv1) * 0.5f)) insideCount++;
        if (AlphaMaskProcessor.SampleMask(maskData, (uv1 + uv2) * 0.5f)) insideCount++;
        if (AlphaMaskProcessor.SampleMask(maskData, (uv2 + uv0) * 0.5f)) insideCount++;
        if (AlphaMaskProcessor.SampleMask(maskData, (uv0 + uv1 + uv2) / 3f)) insideCount++;
        if (insideCount >= 4) AddTriangle(dstIndices, i0, i1, i2, vertices, uv, trimmer, ref stats);
        else stats.removedTriangles++;
    }

    private static bool TryEmitOneLineSplit(
        EdgeCrossingTrimRouter.TriangleProcessResult result,
        int i0, int i1, int i2,
        MeshTrimmerComponent trimmer,
        List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector2> uv, List<Vector2> uv2, List<Vector2> uv3, List<Vector2> uv4,
        List<Color> colors, List<BoneWeight> boneWeights,
        bool hasNormals, bool hasTangents, bool hasUv2, bool hasUv3, bool hasUv4, bool hasColors, bool hasBoneWeights,
        List<VertexSource> vertexSources,
        Dictionary<(int, int, float), int> crossingVertexCache,
        List<int> dstIndices,
        ref TrimStats stats,
        out string failReason)
    {
        failReason = "none";
        int cutA = GetOrCreateCrossingVertex(result.splitCrossingA, crossingVertexCache, trimmer, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
            hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, ref stats);
        int cutB = GetOrCreateCrossingVertex(result.splitCrossingB, crossingVertexCache, trimmer, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
            hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, ref stats);
        if (!IsChordLengthValid(i0, i1, i2, cutA, cutB, uv, trimmer)) { failReason = "area_too_small"; return false; }

        if (result.keptInsideVertices == null || result.keptInsideVertices.Length == 0) { failReason = "no_kept_inside_vertices"; return false; }
        var staged = new List<(int a, int b, int c)>();
        if (result.keptInsideVertices.Length == 1)
        {
            int a = result.keptInsideVertices[0];
            int b = cutA;
            int c = cutB;
            GetTrianglePreserveWinding(i0, i1, i2, ref a, ref b, ref c, vertices);
            if (!IsTriangleValidForEmit(a, b, c, vertices, uv, trimmer)) { failReason = "world_area_too_small"; return false; }
            staged.Add((a, b, c));
            CommitStagedTriangles(staged, dstIndices, ref stats);
            return true;
        }

        if (result.keptInsideVertices.Length == 2)
        {
            int a = result.keptInsideVertices[0];
            int b = result.keptInsideVertices[1];
            int t0a = a, t0b = b, t0c = cutA;
            GetTrianglePreserveWinding(i0, i1, i2, ref t0a, ref t0b, ref t0c, vertices);
            if (!IsTriangleValidForEmit(t0a, t0b, t0c, vertices, uv, trimmer)) { failReason = "winding_failed"; return false; }
            staged.Add((t0a, t0b, t0c));
            int t1a = b, t1b = cutB, t1c = cutA;
            GetTrianglePreserveWinding(i0, i1, i2, ref t1a, ref t1b, ref t1c, vertices);
            if (!IsTriangleValidForEmit(t1a, t1b, t1c, vertices, uv, trimmer)) { failReason = "duplicate_vertex"; return false; }
            staged.Add((t1a, t1b, t1c));
            CommitStagedTriangles(staged, dstIndices, ref stats);
            return staged.Count > 0;
        }

        failReason = "emitted_zero_triangles";
        return false;
    }

    private static void CommitStagedTriangles(List<(int a, int b, int c)> staged, List<int> dstIndices, ref TrimStats stats)
    {
        for (int i = 0; i < staged.Count; i++)
        {
            var t = staged[i];
            dstIndices.Add(t.a);
            dstIndices.Add(t.b);
            dstIndices.Add(t.c);
            stats.outputTriangles++;
        }
    }

    private static int GetOrCreateCrossingVertex(
        EdgeCrossingTrimRouter.LocalCrossing crossing,
        Dictionary<(int, int, float), int> cache,
        MeshTrimmerComponent trimmer,
        List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector2> uv, List<Vector2> uv2, List<Vector2> uv3, List<Vector2> uv4,
        List<Color> colors, List<BoneWeight> boneWeights,
        bool hasNormals, bool hasTangents, bool hasUv2, bool hasUv3, bool hasUv4, bool hasColors, bool hasBoneWeights,
        List<VertexSource> vertexSources,
        ref TrimStats stats)
    {
        int a = Mathf.Min(crossing.edgeStart, crossing.edgeEnd);
        int b = Mathf.Max(crossing.edgeStart, crossing.edgeEnd);
        float t = crossing.edgeStart <= crossing.edgeEnd ? crossing.t : 1f - crossing.t;
        float quantizeStep = trimmer != null ? Mathf.Max(0.00001f, trimmer.edgeCrossingCacheQuantizeStep) : 0.001f;
        var key = (a, b, Mathf.Round(t / quantizeStep) * quantizeStep);
        if (cache.TryGetValue(key, out int idx)) return idx;
        int newIndex = AddInterpolatedVertex(crossing.edgeStart, crossing.edgeEnd, crossing.t, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
            hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, ref stats);
        cache[key] = newIndex;
        return newIndex;
    }

    private static bool TryEmitInsidePolygons(
        EdgeCrossingTrimRouter.TriangleProcessResult result,
        int i0, int i1, int i2,
        MeshTrimmerComponent trimmer,
        List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector2> uv, List<Vector2> uv2, List<Vector2> uv3, List<Vector2> uv4,
        List<Color> colors, List<BoneWeight> boneWeights,
        bool hasNormals, bool hasTangents, bool hasUv2, bool hasUv3, bool hasUv4, bool hasColors, bool hasBoneWeights,
        List<VertexSource> vertexSources,
        Dictionary<(int, int, float), int> crossingVertexCache,
        List<int> dstIndices,
        ref TrimStats stats,
        out string failReason,
        out string failDetail)
    {
        failReason = "none";
        failDetail = "none";
        if (result.insidePolygons == null || result.insidePolygons.Length == 0) { failReason = "empty_inside_polygons"; return false; }
        var staged = new List<(int a, int b, int c)>();
        float srcAreaForFan = Mathf.Abs((uv[i1].x - uv[i0].x) * (uv[i2].y - uv[i0].y) - (uv[i2].x - uv[i0].x) * (uv[i1].y - uv[i0].y)) * 0.5f;
        float minFragmentRatio = trimmer != null ? Mathf.Clamp(trimmer.minimumFragmentSizePermille, 0.01f, 100.0f) * 0.001f : 0.0002f;
        float fanTriangleMinAreaRatio = Mathf.Clamp(minFragmentRatio * 0.25f, 0.0001f, 0.02f);
        for (int p = 0; p < result.insidePolygons.Length; p++)
        {
            var poly = result.insidePolygons[p];
            if (poly == null || poly.Length < 3) { failReason = "polygon_too_small"; return false; }
            var indices = new List<int>(poly.Length);
            for (int k = 0; k < poly.Length; k++)
            {
                var v = poly[k];
                if (v.isOriginalVertex) indices.Add(v.originalVertexId);
                else indices.Add(GetOrCreateCrossingVertex(v.crossing, crossingVertexCache, trimmer, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                    hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, ref stats));
            }
            var before = new List<Vector2>(indices.Count);
            for (int i = 0; i < indices.Count; i++) before.Add(uv[indices[i]]);
            SimplifyPolygonIndices(indices, uv, out int removedAdjacent, out int removedCollinear);
            var after = new List<Vector2>(indices.Count);
            for (int i = 0; i < indices.Count; i++) after.Add(uv[indices[i]]);
            if (indices.Count < 3) { failReason = "polygon_too_small_after_simplify"; failDetail = $"poly={p} removedAdjacentDuplicateCount={removedAdjacent} removedCollinearCount={removedCollinear}"; return false; }
            if (!ValidateInsideLoop(indices, i0, i1, i2, uv, trimmer, out failDetail))
            {
                failDetail = $"{failDetail} poly={p} vertexCount={indices.Count} removedAdjacentDuplicateCount={removedAdjacent} removedCollinearCount={removedCollinear}";
                failReason = "loop_validation_failed";
                return false;
            }
            float signedArea = ComputePolygonSignedArea(indices, uv);
            if (signedArea < 0f)
            {
                indices.Reverse();
            }

            for (int k = 1; k + 1 < indices.Count; k++)
            {
                int a = indices[0];
                int b = indices[k];
                int c = indices[k + 1];
                float fanUvArea = Mathf.Abs((uv[b].x - uv[a].x) * (uv[c].y - uv[a].y) - (uv[c].x - uv[a].x) * (uv[b].y - uv[a].y)) * 0.5f;
                GetTrianglePreserveWinding(i0, i1, i2, ref a, ref b, ref c, vertices);
                float fanAreaRatio = srcAreaForFan > 0f ? fanUvArea / srcAreaForFan : 0f;
                float fanMinArea = srcAreaForFan * fanTriangleMinAreaRatio;
                if (!IsFanTriangleValidForEmit(a, b, c, vertices, uv, fanUvArea, fanMinArea)) { failDetail = $"{failDetail} polygonAreaRatio={(srcAreaForFan>0f?Mathf.Abs(ComputePolygonSignedArea(indices, uv))/srcAreaForFan:0f):F8} fanTriangleIndex={k} fanUvArea={fanUvArea:F8} fanTriangleAreaRatio={fanAreaRatio:F8} fanTriangleMinAreaRatio={fanTriangleMinAreaRatio:F8} fanTriangleMinAreaThreshold={fanMinArea:F8}"; failReason = "fan_triangle_invalid"; return false; }
                staged.Add((a, b, c));
            }
        }
        if (staged.Count == 0) { failReason = "no_staged_triangles"; return false; }
        CommitStagedTriangles(staged, dstIndices, ref stats);
        return true;
    }

    private static void SimplifyPolygonIndices(List<int> indices, List<Vector2> uv, out int removedAdjacent, out int removedCollinear)
    {
        removedAdjacent = 0;
        removedCollinear = 0;
        float eps = Mathf.Sqrt(LoopDuplicateUvEpsilonSqr);
        if (indices == null) return;

        bool changed = true;
        while (changed && indices.Count >= 3)
        {
            changed = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int j = (i + 1) % indices.Count;
                if ((uv[indices[i]] - uv[indices[j]]).sqrMagnitude <= eps * eps)
                {
                    indices.RemoveAt(j);
                    removedAdjacent++;
                    changed = true;
                    break;
                }
            }
        }

        changed = true;
        while (changed && indices.Count >= 3)
        {
            changed = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int prev = (i - 1 + indices.Count) % indices.Count;
                int next = (i + 1) % indices.Count;
                Vector2 a = uv[indices[prev]];
                Vector2 b = uv[indices[i]];
                Vector2 c = uv[indices[next]];
                float area2 = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
                if (area2 <= eps)
                {
                    indices.RemoveAt(i);
                    removedCollinear++;
                    changed = true;
                    break;
                }
            }
        }
    }

    private static bool ValidateInsideLoop(List<int> indices, int i0, int i1, int i2, List<Vector2> uv, MeshTrimmerComponent trimmer, out string failDetail)
    {
        failDetail = "none";
        if (indices == null || indices.Count < 3) { failDetail = "polygon_too_small"; return false; }
        var seen = new HashSet<int>();
        for (int i = 0; i < indices.Count; i++)
        {
            if (!seen.Add(indices[i])) { failDetail = $"duplicate_vertex:index={indices[i]}"; return false; }
            Vector2 p = uv[indices[i]];
            Vector2 q = uv[indices[(i + 1) % indices.Count]];
            if ((p - q).sqrMagnitude < LoopDuplicateUvEpsilonSqr) { failDetail = $"adjacent_duplicate_uv:i={i}"; return false; }
            for (int j = i + 1; j < indices.Count; j++)
            {
                if ((p - uv[indices[j]]).sqrMagnitude < LoopDuplicateUvEpsilonSqr) { failDetail = $"duplicate_uv:i={i},j={j}"; return false; }
            }
        }
        float srcArea = Mathf.Abs((uv[i1].x - uv[i0].x) * (uv[i2].y - uv[i0].y) - (uv[i2].x - uv[i0].x) * (uv[i1].y - uv[i0].y)) * 0.5f;
        float polyArea = Mathf.Abs(ComputePolygonSignedArea(indices, uv));
        if (polyArea <= 0f) { failDetail = "poly_area_non_positive"; return false; }
        float minAreaRatio = trimmer != null ? Mathf.Clamp(trimmer.edgeCrossingMinPolygonAreaRatio, 0.0001f, 1f) : DefaultMinPolygonAreaRatio;
        if (srcArea > 0f && polyArea < srcArea * minAreaRatio) { failDetail = $"poly_area_too_small:poly={polyArea},src={srcArea},minRatio={minAreaRatio}"; return false; }
        if (HasSelfIntersection(indices, uv)) { failDetail = "self_intersection"; return false; }
        return true;
    }

    private static bool IsChordLengthValid(int i0, int i1, int i2, int cutA, int cutB, List<Vector2> uv, MeshTrimmerComponent trimmer)
    {
        float srcArea = Mathf.Abs((uv[i1].x - uv[i0].x) * (uv[i2].y - uv[i0].y) - (uv[i2].x - uv[i0].x) * (uv[i1].y - uv[i0].y)) * 0.5f;
        if (srcArea <= 0f) return false;
        float refLen = Mathf.Sqrt(srcArea);
        float chordLen = Vector2.Distance(uv[cutA], uv[cutB]);
        float minChordRatio = trimmer != null ? Mathf.Clamp(trimmer.edgeCrossingMinChordLengthRatio, 0.0001f, 1f) : DefaultMinChordLengthRatio;
        return chordLen >= refLen * minChordRatio;
    }

    private static float ComputePolygonSignedArea(List<int> indices, List<Vector2> uv)
    {
        float a = 0f;
        for (int i = 0; i < indices.Count; i++)
        {
            Vector2 p = uv[indices[i]];
            Vector2 q = uv[indices[(i + 1) % indices.Count]];
            a += p.x * q.y - q.x * p.y;
        }
        return a * 0.5f;
    }

    private static bool HasSelfIntersection(List<int> indices, List<Vector2> uv)
    {
        int n = indices.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a1 = uv[indices[i]];
            Vector2 a2 = uv[indices[(i + 1) % n]];
            for (int j = i + 1; j < n; j++)
            {
                if (Mathf.Abs(i - j) <= 1) continue;
                if (i == 0 && j == n - 1) continue;
                Vector2 b1 = uv[indices[j]];
                Vector2 b2 = uv[indices[(j + 1) % n]];
                if (SegmentsIntersect(a1, a2, b1, b2)) return true;
            }
        }
        return false;
    }

    private static void GetTrianglePreserveWinding(
        int srcA, int srcB, int srcC,
        ref int a, ref int b, ref int c,
        List<Vector3> vertices)
    {
        Vector3 srcN = Vector3.Cross(vertices[srcB] - vertices[srcA], vertices[srcC] - vertices[srcA]);
        Vector3 newN = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
        if (Vector3.Dot(srcN, newN) < 0f)
        {
            int tmp = b;
            b = c;
            c = tmp;
        }
    }

    private static bool IsTriangleValidForEmit(
        int a, int b, int c,
        List<Vector3> vertices,
        List<Vector2> uv,
        MeshTrimmerComponent trimmer)
    {
        if (a == b || b == c || c == a) return false;
        Vector2 uva = uv[a];
        Vector2 uvb = uv[b];
        Vector2 uvc = uv[c];
        float uvArea = Mathf.Abs((uvb.x - uva.x) * (uvc.y - uva.y) - (uvc.x - uva.x) * (uvb.y - uva.y)) * 0.5f;
        if (uvArea < trimmer.minTriangleUvArea) return false;
        float worldArea = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5f;
        if (worldArea < trimmer.minTriangleWorldArea) return false;
        return true;
    }

    private static bool IsFanTriangleValidForEmit(int a, int b, int c, List<Vector3> vertices, List<Vector2> uv, float fanUvArea, float fanMinArea)
    {
        if (a == b || b == c || c == a) return false;
        if (fanUvArea <= fanMinArea) return false;
        float worldArea = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5f;
        if (worldArea <= 1e-12f) return false;
        return true;
    }

    private static Dictionary<EdgeCrossingTrimRouter.EdgeKey, List<EdgeCrossingTrimRouter.EdgeCrossing>> BuildSharedCrossings(int[] srcIndices, AlphaMaskProcessor.AlphaMaskData maskData, List<Vector2> uv, MeshTrimmerComponent trimmer, out Dictionary<EdgeCrossingTrimRouter.EdgeKey, int> rawCrossingCounts)
    {
        var shared = new Dictionary<EdgeCrossingTrimRouter.EdgeKey, List<EdgeCrossingTrimRouter.EdgeCrossing>>();
        rawCrossingCounts = new Dictionary<EdgeCrossingTrimRouter.EdgeKey, int>();
        var visited = new HashSet<EdgeCrossingTrimRouter.EdgeKey>();
        for (int i = 0; i < srcIndices.Length; i += 3)
        {
            int i0 = srcIndices[i];
            int i1 = srcIndices[i + 1];
            int i2 = srcIndices[i + 2];
            RegisterEdgeCrossing(maskData, uv, i0, i1, shared, visited, trimmer);
            RegisterEdgeCrossing(maskData, uv, i1, i2, shared, visited, trimmer);
            RegisterEdgeCrossing(maskData, uv, i2, i0, shared, visited, trimmer);
        }
        float vertexEpsilon = trimmer != null ? Mathf.Max(0.00001f, trimmer.edgeCrossingEndpointSnapEpsilon) : 0.001f;
        float crossingPairEpsilon = trimmer != null ? Mathf.Max(0.00001f, trimmer.edgeCrossingMergeEpsilon) : 0.001f;
        int rawCrossingCount = 0;
        int normalizedCrossingCount = 0;
        int removedVertexNearCrossingCount = 0;
        int removedNearPairCrossingCount = 0;
        foreach (var kv in shared)
        {
            kv.Value.Sort((x, y) => x.t.CompareTo(y.t));
            rawCrossingCounts[kv.Key] = kv.Value.Count;
            rawCrossingCount += kv.Value.Count;
            for (int i = kv.Value.Count - 1; i >= 0; i--)
            {
                float t = kv.Value[i].t;
                if (t <= vertexEpsilon || t >= 1f - vertexEpsilon)
                {
                    kv.Value.RemoveAt(i);
                    removedVertexNearCrossingCount++;
                }
            }
            int k = 0;
            while (k + 1 < kv.Value.Count)
            {
                if (Mathf.Abs(kv.Value[k + 1].t - kv.Value[k].t) < crossingPairEpsilon)
                {
                    kv.Value.RemoveAt(k + 1);
                    kv.Value.RemoveAt(k);
                    removedNearPairCrossingCount += 2;
                    continue;
                }
                k++;
            }
            normalizedCrossingCount += kv.Value.Count;
        }
        if (trimmer != null && trimmer.debugEdgeCrossingRoutes)
        {
            LogUtility.Info(ToolName, "EdgeRouteSummary", $"CrossingNormalize raw={rawCrossingCount} normalized={normalizedCrossingCount} removedVertexNear={removedVertexNearCrossingCount} removedNearPair={removedNearPairCrossingCount}");
        }
        return shared;
    }

    private static void RegisterEdgeCrossing(
        AlphaMaskProcessor.AlphaMaskData maskData,
        List<Vector2> uv,
        int a,
        int b,
        Dictionary<EdgeCrossingTrimRouter.EdgeKey, List<EdgeCrossingTrimRouter.EdgeCrossing>> shared,
        HashSet<EdgeCrossingTrimRouter.EdgeKey> visited,
        MeshTrimmerComponent trimmer)
    {
        var key = new EdgeCrossingTrimRouter.EdgeKey(a, b);
        if (!visited.Add(key)) return;
        if (!shared.TryGetValue(key, out var list))
        {
            list = new List<EdgeCrossingTrimRouter.EdgeCrossing>(2);
            shared[key] = list;
        }

        Vector2 ua = uv[a];
        Vector2 ub = uv[b];
        const int samples = 32;
        float endpointSnapEpsilon = trimmer != null ? Mathf.Max(0.00001f, trimmer.edgeCrossingEndpointSnapEpsilon) : 0.001f;
        bool prevInside = AlphaMaskProcessor.SampleMask(maskData, ua);
        float prevT = 0f;

        for (int s = 1; s <= samples; s++)
        {
            float curT = (float)s / samples;
            Vector2 curUv = Vector2.LerpUnclamped(ua, ub, curT);
            bool curInside = AlphaMaskProcessor.SampleMask(maskData, curUv);
            if (curInside == prevInside)
            {
                prevT = curT;
                continue;
            }

            float lo = prevT;
            float hi = curT;
            bool loInside = prevInside;
            for (int it = 0; it < 12; it++)
            {
                float mid = (lo + hi) * 0.5f;
                Vector2 um = Vector2.LerpUnclamped(ua, ub, mid);
                bool midInside = AlphaMaskProcessor.SampleMask(maskData, um);
                if (midInside == loInside) lo = mid;
                else hi = mid;
            }

            bool canonical = key.a == a && key.b == b;
            float tCanonical = canonical ? hi : 1f - hi;
            if (tCanonical < endpointSnapEpsilon) tCanonical = 0f;
            else if (tCanonical > 1f - endpointSnapEpsilon) tCanonical = 1f;
            bool beforeInsideCanonical = canonical ? loInside : !loInside;
            list.Add(new EdgeCrossingTrimRouter.EdgeCrossing { edge = key, t = tCanonical, isBeforeInside = beforeInsideCanonical });

            prevInside = curInside;
            prevT = curT;
        }
    }

    private static TrimStats ProcessSubMeshInsidePoint(
        int[] srcIndices,
        AlphaMaskProcessor.AlphaMaskData maskData,
        MeshTrimmerComponent trimmer,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Vector2> uv,
        List<Vector2> uv2,
        List<Vector2> uv3,
        List<Vector2> uv4,
        List<Color> colors,
        List<BoneWeight> boneWeights,
        bool hasNormals,
        bool hasTangents,
        bool hasUv2,
        bool hasUv3,
        bool hasUv4,
        bool hasColors,
        bool hasBoneWeights,
        List<VertexSource> vertexSources,
        List<int> dstIndices)
    {
        TrimStats stats = new TrimStats();
        var triangleResults = new List<TriangleTrimResult>(srcIndices.Length / 3);
        var edgeCuts = new List<EdgeCutInfo>();
        EdgeIntersectionCache cache = new EdgeIntersectionCache();

        // Prepass: generate and record all direct edge intersections first.
        for (int i = 0; i < srcIndices.Length; i += 3)
        {
            int triIndex = i / 3;
            int i0 = srcIndices[i];
            int i1 = srcIndices[i + 1];
            int i2 = srcIndices[i + 2];
            bool in0 = AlphaMaskProcessor.SampleMask(maskData, uv[i0]);
            bool in1 = AlphaMaskProcessor.SampleMask(maskData, uv[i1]);
            bool in2 = AlphaMaskProcessor.SampleMask(maskData, uv[i2]);
            int insideCount = (in0 ? 1 : 0) + (in1 ? 1 : 0) + (in2 ? 1 : 0);

            int[] idx = { i0, i1, i2 };
            bool[] inside = { in0, in1, in2 };

            if (insideCount == 1)
            {
                int insideV = -1, outA = -1, outB = -1;
                for (int k = 0; k < 3; k++)
                {
                    if (inside[k]) insideV = idx[k];
                    else if (outA < 0) outA = idx[k];
                    else outB = idx[k];
                }
                int cutA = GetOrCreateIntersection(insideV, outA, maskData, trimmer,
                    vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                    hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights,
                    vertexSources, cache, ref stats);
                int cutB = GetOrCreateIntersection(insideV, outB, maskData, trimmer,
                    vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                    hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights,
                    vertexSources, cache, ref stats);
                AddEdgeCut(edgeCuts, triIndex, insideV, outA, cutA, uv);
                AddEdgeCut(edgeCuts, triIndex, insideV, outB, cutB, uv);
            }
            else if (insideCount == 2)
            {
                int outsideV = -1, inA = -1, inB = -1;
                for (int k = 0; k < 3; k++)
                {
                    if (!inside[k]) outsideV = idx[k];
                    else if (inA < 0) inA = idx[k];
                    else inB = idx[k];
                }
                int cutA = GetOrCreateIntersection(inA, outsideV, maskData, trimmer,
                    vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                    hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights,
                    vertexSources, cache, ref stats);
                int cutB = GetOrCreateIntersection(inB, outsideV, maskData, trimmer,
                    vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                    hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights,
                    vertexSources, cache, ref stats);
                AddEdgeCut(edgeCuts, triIndex, inA, outsideV, cutA, uv);
                AddEdgeCut(edgeCuts, triIndex, inB, outsideV, cutB, uv);
            }
        }

        for (int i = 0; i < srcIndices.Length; i += 3)
        {
            int triIndex = i / 3;
            int i0 = srcIndices[i];
            int i1 = srcIndices[i + 1];
            int i2 = srcIndices[i + 2];
            stats.originalTriangles++;

            bool in0 = AlphaMaskProcessor.SampleMask(maskData, uv[i0]);
            bool in1 = AlphaMaskProcessor.SampleMask(maskData, uv[i1]);
            bool in2 = AlphaMaskProcessor.SampleMask(maskData, uv[i2]);
            Vector2 m01 = (uv[i0] + uv[i1]) * 0.5f;
            Vector2 m12 = (uv[i1] + uv[i2]) * 0.5f;
            Vector2 m20 = (uv[i2] + uv[i0]) * 0.5f;
            Vector2 centroid = (uv[i0] + uv[i1] + uv[i2]) / 3f;
            bool m01In = AlphaMaskProcessor.SampleMask(maskData, m01);
            bool m12In = AlphaMaskProcessor.SampleMask(maskData, m12);
            bool m20In = AlphaMaskProcessor.SampleMask(maskData, m20);
            bool centroidIn = AlphaMaskProcessor.SampleMask(maskData, centroid);

            int insideCount = (in0 ? 1 : 0) + (in1 ? 1 : 0) + (in2 ? 1 : 0);

            if (insideCount == 3)
            {
                if (!m01In || !m12In || !m20In || !centroidIn)
                {
                    stats.allInsideButInteriorOutside++;
                }

                AddTriangle(dstIndices, i0, i1, i2, vertices, uv, trimmer, ref stats);
                triangleResults.Add(BuildResult(triIndex, TriangleTrimState.StrongKeep, i0, i1, i2, uv, 1f, 0));
                continue;
            }

            if (insideCount == 0)
            {
                int edgeInsideCount = (m01In ? 1 : 0) + (m12In ? 1 : 0) + (m20In ? 1 : 0);
                if (m01In || m12In || m20In || centroidIn)
                {
                    stats.allOutsideButInteriorInside++;
                }

                if (edgeInsideCount == 0)
                {
                    if (centroidIn)
                    {
                        AddTriangle(dstIndices, i0, i1, i2, vertices, uv, trimmer, ref stats);
                        stats.centroidOnlyInsidePreserved++;
                        triangleResults.Add(BuildResult(triIndex, TriangleTrimState.Ambiguous, i0, i1, i2, uv, 1f, 0));
                    }
                    else
                    {
                        stats.removedTriangles++;
                        triangleResults.Add(BuildResult(triIndex, TriangleTrimState.StrongTrim, i0, i1, i2, uv, 0f, 0));
                    }
                    continue;
                }

                if (edgeInsideCount == 1)
                {
                    if (centroidIn)
                    {
                        AddTriangle(dstIndices, i0, i1, i2, vertices, uv, trimmer, ref stats);
                        stats.singleEdgeMidpointAndCentroidInsidePreserved++;
                        triangleResults.Add(BuildResult(triIndex, TriangleTrimState.Ambiguous, i0, i1, i2, uv, 1f, 1));
                    }
                    else
                    {
                        stats.removedTriangles++;
                        stats.singleEdgeMidpointInsideDiscarded++;
                        triangleResults.Add(BuildResult(triIndex, TriangleTrimState.Ambiguous, i0, i1, i2, uv, 0f, 1));
                    }
                    continue;
                }

                if (edgeInsideCount == 3)
                {
                    AddTriangle(dstIndices, i0, i1, i2, vertices, uv, trimmer, ref stats);
                    stats.allEdgeMidpointsInsidePreserved++;
                    triangleResults.Add(BuildResult(triIndex, TriangleTrimState.Ambiguous, i0, i1, i2, uv, 1f, 3));
                    continue;
                }

                int virtualInside = -1;
                int edgeA1 = -1, edgeB1 = -1;
                int edgeA2 = -1, edgeB2 = -1;
                if (m01In && m20In)
                {
                    virtualInside = i0;
                    edgeA1 = i0; edgeB1 = i1;
                    edgeA2 = i0; edgeB2 = i2;
                }
                else if (m01In && m12In)
                {
                    virtualInside = i1;
                    edgeA1 = i1; edgeB1 = i0;
                    edgeA2 = i1; edgeB2 = i2;
                }
                else if (m12In && m20In)
                {
                    virtualInside = i2;
                    edgeA1 = i2; edgeB1 = i1;
                    edgeA2 = i2; edgeB2 = i0;
                }
                else
                {
                    AddTriangle(dstIndices, i0, i1, i2, vertices, uv, trimmer, ref stats);
                    stats.insidePointFallbackPreserved++;
                    triangleResults.Add(BuildResult(triIndex, TriangleTrimState.Ambiguous, i0, i1, i2, uv, 1f, 2));
                    continue;
                }

                if (!TryCreateIntersectionFromInsideSegment(maskData, trimmer, edgeA1, edgeB1, 0.5f, 1f,
                        vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                        hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights,
                        vertexSources,
                        ref stats, out int cutA)
                    || !TryCreateIntersectionFromInsideSegment(maskData, trimmer, edgeA2, edgeB2, 0.5f, 1f,
                        vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                        hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights,
                        vertexSources,
                        ref stats, out int cutB))
                {
                    AddTriangle(dstIndices, i0, i1, i2, vertices, uv, trimmer, ref stats);
                    stats.insidePointFallbackPreserved++;
                    triangleResults.Add(BuildResult(triIndex, TriangleTrimState.Ambiguous, i0, i1, i2, uv, 1f, 2));
                    continue;
                }

                AddEdgeCut(edgeCuts, triIndex, edgeA1, edgeB1, cutA, uv);
                AddEdgeCut(edgeCuts, triIndex, edgeA2, edgeB2, cutB, uv);
                AddTrianglePreserveWinding(dstIndices, i0, i1, i2, virtualInside, cutA, cutB, vertices, uv, trimmer, ref stats);
                stats.twoEdgeMidpointsInsideClipped++;
                triangleResults.Add(BuildResult(triIndex, TriangleTrimState.Clipped, i0, i1, i2, uv, 0.5f, 2));
                continue;
            }

            int[] idx = { i0, i1, i2 };
            bool[] inside = { in0, in1, in2 };

            if (insideCount == 1)
            {
                int insideV = -1;
                int outA = -1;
                int outB = -1;
                for (int k = 0; k < 3; k++)
                {
                    if (inside[k])
                    {
                        insideV = idx[k];
                    }
                    else if (outA < 0)
                    {
                        outA = idx[k];
                    }
                    else
                    {
                        outB = idx[k];
                    }
                }

                int cutA = GetOrCreateIntersection(insideV, outA, maskData, trimmer,
                    vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                    hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights,
                    vertexSources,
                    cache, ref stats);

                int cutB = GetOrCreateIntersection(insideV, outB, maskData, trimmer,
                    vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                    hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights,
                    vertexSources,
                    cache, ref stats);

                AddEdgeCut(edgeCuts, triIndex, insideV, outA, cutA, uv);
                AddEdgeCut(edgeCuts, triIndex, insideV, outB, cutB, uv);
                AddTrianglePreserveWinding(dstIndices, i0, i1, i2, insideV, cutA, cutB, vertices, uv, trimmer, ref stats);
                triangleResults.Add(BuildResult(triIndex, TriangleTrimState.Clipped, i0, i1, i2, uv, 0.33f, 2));
            }
            else if (insideCount == 2)
            {
                int outsideV = -1;
                int inA = -1;
                int inB = -1;
                for (int k = 0; k < 3; k++)
                {
                    if (!inside[k])
                    {
                        outsideV = idx[k];
                    }
                    else if (inA < 0)
                    {
                        inA = idx[k];
                    }
                    else
                    {
                        inB = idx[k];
                    }
                }

                int cutA = GetOrCreateIntersection(inA, outsideV, maskData, trimmer,
                    vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                    hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights,
                    vertexSources,
                    cache, ref stats);

                int cutB = GetOrCreateIntersection(inB, outsideV, maskData, trimmer,
                    vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
                    hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights,
                    vertexSources,
                    cache, ref stats);

                AddEdgeCut(edgeCuts, triIndex, inA, outsideV, cutA, uv);
                AddEdgeCut(edgeCuts, triIndex, inB, outsideV, cutB, uv);
                AddTrianglePreserveWinding(dstIndices, i0, i1, i2, inA, inB, cutA, vertices, uv, trimmer, ref stats);
                AddTrianglePreserveWinding(dstIndices, i0, i1, i2, inB, cutB, cutA, vertices, uv, trimmer, ref stats);
                triangleResults.Add(BuildResult(triIndex, TriangleTrimState.Clipped, i0, i1, i2, uv, 0.66f, 2));
            }
        }

        // Fill extended per-triangle metadata from collected cut records.
        var cutPointsByTri = new Dictionary<int, List<int>>();
        for (int i = 0; i < edgeCuts.Count; i++)
        {
            var e = edgeCuts[i];
            if (!cutPointsByTri.TryGetValue(e.triangleIndex, out var list))
            {
                list = new List<int>(2);
                cutPointsByTri[e.triangleIndex] = list;
            }
            if (!list.Contains(e.cutPointIndex)) list.Add(e.cutPointIndex);
        }
        for (int i = 0; i < triangleResults.Count; i++)
        {
            var r = triangleResults[i];
            int triBase = r.triangleIndex * 3;
            if (triBase >= 0 && triBase + 2 < srcIndices.Length)
            {
                int ti0 = srcIndices[triBase];
                int ti1 = srcIndices[triBase + 1];
                int ti2 = srcIndices[triBase + 2];
                Vector2 triCentroid = (uv[ti0] + uv[ti1] + uv[ti2]) / 3f;
                r.keptRegionCentroidUv = triCentroid;
                r.removedRegionCentroidUv = triCentroid;
            }
            if (cutPointsByTri.TryGetValue(r.triangleIndex, out var cps))
            {
                r.cutPoints = cps.ToArray();
                r.keptRegionContactEdges = cps.Count;
                r.removedRegionContactEdges = cps.Count;
                if (cps.Count >= 2 && triBase >= 0 && triBase + 2 < srcIndices.Length)
                {
                    int ti0 = srcIndices[triBase];
                    int ti1 = srcIndices[triBase + 1];
                    int ti2 = srcIndices[triBase + 2];
                    Vector2 cp0 = uv[cps[0]];
                    Vector2 cp1 = uv[cps[1]];
                    // Approximate split centroids for clipped-triangle metadata.
                    r.keptRegionCentroidUv = (uv[ti0] + cp0 + cp1) / 3f;
                    r.removedRegionCentroidUv = (uv[ti1] + uv[ti2] + cp0 + cp1) * 0.25f;
                }
            }
            else
            {
                r.keptRegionContactEdges = r.cutEdges;
                r.removedRegionContactEdges = r.cutEdges;
            }
            if (r.state == TriangleTrimState.StrongTrim) r.generatedTriangles = 0;
            else if (r.state == TriangleTrimState.StrongKeep) r.generatedTriangles = 1;
            else if (r.state == TriangleTrimState.Clipped) r.generatedTriangles = r.keptAreaRatio > 0.5f ? 2 : 1;
            else if (r.state == TriangleTrimState.Ambiguous) r.generatedTriangles = r.keptAreaRatio > 0f ? 1 : 0;
            triangleResults[i] = r;
        }

        // Backfill edge kept/removed side centroids from triangle metadata for continuity scoring/diagnostics.
        var triByIndex = new Dictionary<int, TriangleTrimResult>(triangleResults.Count);
        for (int i = 0; i < triangleResults.Count; i++)
        {
            triByIndex[triangleResults[i].triangleIndex] = triangleResults[i];
        }
        for (int i = 0; i < edgeCuts.Count; i++)
        {
            var e = edgeCuts[i];
            if (!triByIndex.TryGetValue(e.triangleIndex, out var tr)) continue;
            // Keep edge-side ownership from cut-generation phase (edgeA=kept side convention).
            // Only refresh centroid hints for diagnostics/scoring.
            e.keptSideCentroidUv = tr.keptRegionCentroidUv;
            e.removedSideCentroidUv = tr.removedRegionCentroidUv;
            edgeCuts[i] = e;
        }
        stats.outputTriangles = dstIndices.Count / 3;
        return stats;
    }


    private static long MakeEdgeKey(int a, int b)
    {
        int lo = Math.Min(a, b);
        int hi = Math.Max(a, b);
        return ((long)lo << 32) | (uint)hi;
    }

    private static void AddEdgeCut(List<EdgeCutInfo> edgeCuts, int triangleIndex, int edgeA, int edgeB, int cutPointIndex, List<Vector2> uv)
    {
        if (cutPointIndex < 0 || cutPointIndex >= uv.Count) return;
        long key = MakeEdgeKey(edgeA, edgeB);
        for (int i = 0; i < edgeCuts.Count; i++)
        {
            if (edgeCuts[i].triangleIndex == triangleIndex && edgeCuts[i].edgeKey == key)
            {
                return;
            }
        }

        edgeCuts.Add(new EdgeCutInfo
        {
            edgeKey = key,
            triangleIndex = triangleIndex,
            cutPointIndex = cutPointIndex,
            cutPointUv = uv[cutPointIndex],
            edgeA = edgeA,
            edgeB = edgeB,
            keptSideCentroidUv = uv[edgeA],
            removedSideCentroidUv = uv[edgeB],
            keptSideUsesEdgeA = true
        });
    }

    private static TriangleTrimResult BuildResult(int triangleIndex, TriangleTrimState state, int i0, int i1, int i2, List<Vector2> uv, float keptAreaRatio, int cutEdges)
    {
        float area = Mathf.Abs(SignedArea(uv[i0], uv[i1], uv[i2])) * 0.5f;
        float kept = area * Mathf.Clamp01(keptAreaRatio);
        float removed = Mathf.Max(0f, area - kept);
        return new TriangleTrimResult
        {
            triangleIndex = triangleIndex,
            state = state,
            originalAreaUv = area,
            keptAreaUv = kept,
            removedAreaUv = removed,
            keptAreaRatio = area > 0f ? kept / area : 0f,
            removedAreaRatio = area > 0f ? removed / area : 0f,
            cutEdges = cutEdges,
            cutPoints = Array.Empty<int>(),
            keptRegionCentroidUv = (uv[i0] + uv[i1] + uv[i2]) / 3f,
            removedRegionCentroidUv = (uv[i0] + uv[i1] + uv[i2]) / 3f,
            keptRegionContactEdges = cutEdges,
            removedRegionContactEdges = cutEdges,
            generatedTriangles = state == TriangleTrimState.Clipped ? 1 : (state == TriangleTrimState.StrongKeep ? 1 : 0)
        };
    }

    private static int GetOrCreateIntersection(
        int insideIndex,
        int outsideIndex,
        AlphaMaskProcessor.AlphaMaskData maskData,
        MeshTrimmerComponent trimmer,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Vector2> uv,
        List<Vector2> uv2,
        List<Vector2> uv3,
        List<Vector2> uv4,
        List<Color> colors,
        List<BoneWeight> boneWeights,
        bool hasNormals,
        bool hasTangents,
        bool hasUv2,
        bool hasUv3,
        bool hasUv4,
        bool hasColors,
        bool hasBoneWeights,
        List<VertexSource> vertexSources,
        EdgeIntersectionCache cache,
        ref TrimStats stats)
    {
        if (cache.TryGet(insideIndex, outsideIndex, out int cached))
        {
            return cached;
        }

        Vector2 uvIn = uv[insideIndex];
        Vector2 uvOut = uv[outsideIndex];

        float lo = 0f;
        float hi = 1f;
        for (int i = 0; i < 10; i++)
        {
            float mid = (lo + hi) * 0.5f;
            Vector2 uvMid = Vector2.LerpUnclamped(uvIn, uvOut, mid);
            bool inside = AlphaMaskProcessor.SampleMask(maskData, uvMid);
            if (inside)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        float t = lo;

        if (t <= trimmer.minIntersectionT)
        {
            cache.Set(insideIndex, outsideIndex, insideIndex);
            return insideIndex;
        }

        if (t >= trimmer.maxIntersectionT)
        {
            cache.Set(insideIndex, outsideIndex, outsideIndex);
            return outsideIndex;
        }

        int newIndex = AddInterpolatedVertex(insideIndex, outsideIndex, t, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
            hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, ref stats);
        cache.Set(insideIndex, outsideIndex, newIndex);
        return newIndex;
    }

    private static bool TryCreateIntersectionFromInsideSegment(
        AlphaMaskProcessor.AlphaMaskData maskData,
        MeshTrimmerComponent trimmer,
        int edgeA,
        int edgeB,
        float insideT,
        float outsideT,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Vector2> uv,
        List<Vector2> uv2,
        List<Vector2> uv3,
        List<Vector2> uv4,
        List<Color> colors,
        List<BoneWeight> boneWeights,
        bool hasNormals,
        bool hasTangents,
        bool hasUv2,
        bool hasUv3,
        bool hasUv4,
        bool hasColors,
        bool hasBoneWeights,
        List<VertexSource> vertexSources,
        ref TrimStats stats,
        out int index)
    {
        index = -1;
        Vector2 uvA = uv[edgeA];
        Vector2 uvB = uv[edgeB];
        Vector2 uvIn = Vector2.LerpUnclamped(uvA, uvB, insideT);
        Vector2 uvOut = Vector2.LerpUnclamped(uvA, uvB, outsideT);

        bool startInside = AlphaMaskProcessor.SampleMask(maskData, uvIn);
        bool endOutside = !AlphaMaskProcessor.SampleMask(maskData, uvOut);
        if (!startInside || !endOutside)
        {
            return false;
        }

        float lo = 0f;
        float hi = 1f;
        for (int i = 0; i < 10; i++)
        {
            float mid = (lo + hi) * 0.5f;
            Vector2 uvMid = Vector2.LerpUnclamped(uvIn, uvOut, mid);
            if (AlphaMaskProcessor.SampleMask(maskData, uvMid))
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        float localT = lo;
        if (localT >= trimmer.maxIntersectionT)
        {
            index = edgeB;
            return true;
        }

        float clampedLocal = localT <= trimmer.minIntersectionT ? 0f : localT;
        float globalT = Mathf.LerpUnclamped(insideT, outsideT, clampedLocal);
        index = AddInterpolatedVertex(edgeA, edgeB, globalT, vertices, normals, tangents, uv, uv2, uv3, uv4, colors, boneWeights,
            hasNormals, hasTangents, hasUv2, hasUv3, hasUv4, hasColors, hasBoneWeights, vertexSources, ref stats);
        return true;
    }

    private static int AddInterpolatedVertex(
        int a,
        int b,
        float t,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Vector2> uv,
        List<Vector2> uv2,
        List<Vector2> uv3,
        List<Vector2> uv4,
        List<Color> colors,
        List<BoneWeight> boneWeights,
        bool hasNormals,
        bool hasTangents,
        bool hasUv2,
        bool hasUv3,
        bool hasUv4,
        bool hasColors,
        bool hasBoneWeights,
        List<VertexSource> vertexSources,
        ref TrimStats stats)
    {
        int newIndex = vertices.Count;
        vertices.Add(MeshAttributeInterpolator.Lerp(vertices[a], vertices[b], t));
        if (hasNormals) normals.Add(MeshAttributeInterpolator.Lerp(normals[a], normals[b], t).normalized);
        if (hasTangents) tangents.Add(MeshAttributeInterpolator.Lerp(tangents[a], tangents[b], t));
        uv.Add(MeshAttributeInterpolator.Lerp(uv[a], uv[b], t));
        if (hasUv2) uv2.Add(MeshAttributeInterpolator.Lerp(uv2[a], uv2[b], t));
        if (hasUv3) uv3.Add(MeshAttributeInterpolator.Lerp(uv3[a], uv3[b], t));
        if (hasUv4) uv4.Add(MeshAttributeInterpolator.Lerp(uv4[a], uv4[b], t));
        if (hasColors) colors.Add(MeshAttributeInterpolator.Lerp(colors[a], colors[b], t));
        if (hasBoneWeights) boneWeights.Add(MeshAttributeInterpolator.Lerp(boneWeights[a], boneWeights[b], t));
        vertexSources.Add(LerpVertexSource(vertexSources[a], vertexSources[b], t));
        stats.intersections++;
        return newIndex;
    }

    private static void CopyBlendShapes(
        Mesh sourceMesh,
        Mesh newMesh,
        List<VertexSource> vertexSources,
        string rendererName,
        bool preserveBlendShapes,
        bool verboseLog)
    {
        if (!preserveBlendShapes || sourceMesh.blendShapeCount == 0)
        {
            LogUtility.Verbose(ToolName, verboseLog, "BlendShape", $"Renderer={rendererName}, PreserveBlendShapes={preserveBlendShapes}, BlendShapeCount=0, TotalFrameCount=0, ProcessedDeltaVertexCount=0, ElapsedMs=0");
            return;
        }

        if (vertexSources.Count != newMesh.vertexCount)
        {
            LogUtility.Warning(ToolName, "BlendShape", $"Vertex source count mismatch. vertexSources={vertexSources.Count}, newVertexCount={newMesh.vertexCount}");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int sourceVertexCount = sourceMesh.vertexCount;
        int newVertexCount = newMesh.vertexCount;
        int totalFrames = 0;

        Vector3[] oldDeltaVertices = new Vector3[sourceVertexCount];
        Vector3[] oldDeltaNormals = new Vector3[sourceVertexCount];
        Vector3[] oldDeltaTangents = new Vector3[sourceVertexCount];
        Vector3[] newDeltaVertices = new Vector3[newVertexCount];
        Vector3[] newDeltaNormals = new Vector3[newVertexCount];
        Vector3[] newDeltaTangents = new Vector3[newVertexCount];

        for (int shapeIndex = 0; shapeIndex < sourceMesh.blendShapeCount; shapeIndex++)
        {
            string shapeName = sourceMesh.GetBlendShapeName(shapeIndex);
            int frameCount = sourceMesh.GetBlendShapeFrameCount(shapeIndex);

            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                totalFrames++;
                float weight = sourceMesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);
                sourceMesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, oldDeltaVertices, oldDeltaNormals, oldDeltaTangents);

                for (int i = 0; i < newVertexCount; i++)
                {
                    VertexSource src = vertexSources[i];
                    newDeltaVertices[i] = WeightedDelta(oldDeltaVertices, src);
                    newDeltaNormals[i] = WeightedDelta(oldDeltaNormals, src);
                    newDeltaTangents[i] = WeightedDelta(oldDeltaTangents, src);
                }

                newMesh.AddBlendShapeFrame(shapeName, weight, newDeltaVertices, newDeltaNormals, newDeltaTangents);
            }
        }

        sw.Stop();
        LogUtility.Verbose(ToolName, verboseLog, "BlendShape", $"Renderer={rendererName}, PreserveBlendShapes={preserveBlendShapes}, BlendShapeCount={sourceMesh.blendShapeCount}, TotalFrameCount={totalFrames}, ProcessedDeltaVertexCount={newVertexCount}, ElapsedMs={sw.ElapsedMilliseconds}");
    }


    private static Vector3 WeightedDelta(Vector3[] deltas, VertexSource src)
    {
        Vector3 v = Vector3.zero;
        if (src.index0 >= 0 && src.weight0 > 0f) v += deltas[src.index0] * src.weight0;
        if (src.index1 >= 0 && src.weight1 > 0f) v += deltas[src.index1] * src.weight1;
        if (src.index2 >= 0 && src.weight2 > 0f) v += deltas[src.index2] * src.weight2;
        if (src.index3 >= 0 && src.weight3 > 0f) v += deltas[src.index3] * src.weight3;
        return v;
    }

    private static VertexSource LerpVertexSource(VertexSource a, VertexSource b, float t)
    {
        float wa = 1f - t;
        float wb = t;
        int[] idx = { -1, -1, -1, -1 };
        float[] w = { 0f, 0f, 0f, 0f };

        AddWeighted(ref idx, ref w, a.index0, a.weight0 * wa);
        AddWeighted(ref idx, ref w, a.index1, a.weight1 * wa);
        AddWeighted(ref idx, ref w, a.index2, a.weight2 * wa);
        AddWeighted(ref idx, ref w, a.index3, a.weight3 * wa);

        AddWeighted(ref idx, ref w, b.index0, b.weight0 * wb);
        AddWeighted(ref idx, ref w, b.index1, b.weight1 * wb);
        AddWeighted(ref idx, ref w, b.index2, b.weight2 * wb);
        AddWeighted(ref idx, ref w, b.index3, b.weight3 * wb);

        KeepTop4(ref idx, ref w);
        Normalize(ref w);

        return new VertexSource { index0 = idx[0], weight0 = w[0], index1 = idx[1], weight1 = w[1], index2 = idx[2], weight2 = w[2], index3 = idx[3], weight3 = w[3] };
    }

    private static void AddWeighted(ref int[] idx, ref float[] w, int index, float weight)
    {
        if (index < 0 || weight <= 0f) return;
        for (int i = 0; i < 4; i++)
        {
            if (idx[i] == index) { w[i] += weight; return; }
        }
        for (int i = 0; i < 4; i++)
        {
            if (idx[i] < 0) { idx[i] = index; w[i] = weight; return; }
        }
        int min = 0;
        for (int i = 1; i < 4; i++) if (w[i] < w[min]) min = i;
        if (weight > w[min]) { idx[min] = index; w[min] = weight; }
    }

    private static void KeepTop4(ref int[] idx, ref float[] w)
    {
        for (int i = 0; i < 3; i++)
        for (int j = i + 1; j < 4; j++)
        {
            if (w[j] > w[i])
            {
                float tw = w[i]; w[i] = w[j]; w[j] = tw;
                int ti = idx[i]; idx[i] = idx[j]; idx[j] = ti;
            }
        }
    }

    private static void Normalize(ref float[] w)
    {
        float sum = w[0] + w[1] + w[2] + w[3];
        if (sum <= 0f) return;
        w[0] /= sum; w[1] /= sum; w[2] /= sum; w[3] /= sum;
    }

    private static float[] SaveBlendShapeWeights(SkinnedMeshRenderer renderer, Mesh sourceMesh)
    {
        int count = sourceMesh != null ? sourceMesh.blendShapeCount : 0;
        float[] weights = new float[count];
        for (int i = 0; i < count; i++)
        {
            weights[i] = renderer.GetBlendShapeWeight(i);
        }

        return weights;
    }

    private static string[] SaveBlendShapeNames(Mesh sourceMesh)
    {
        int count = sourceMesh != null ? sourceMesh.blendShapeCount : 0;
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = sourceMesh.GetBlendShapeName(i);
        }

        return names;
    }

    private static void RestoreBlendShapeWeights(SkinnedMeshRenderer renderer, Mesh newMesh, string[] names, float[] weights)
    {
        if (renderer == null || newMesh == null || names == null || weights == null)
        {
            return;
        }

        int count = Mathf.Min(names.Length, weights.Length);
        for (int i = 0; i < count; i++)
        {
            int newIndex = newMesh.GetBlendShapeIndex(names[i]);
            if (newIndex < 0 && i < newMesh.blendShapeCount)
            {
                newIndex = i;
            }

            if (newIndex >= 0 && newIndex < newMesh.blendShapeCount)
            {
                renderer.SetBlendShapeWeight(newIndex, weights[i]);
            }
        }
    }

    private static bool AddTrianglePreserveWinding(
        List<int> indices,
        int srcA,
        int srcB,
        int srcC,
        int a,
        int b,
        int c,
        List<Vector3> vertices,
        List<Vector2> uv,
        MeshTrimmerComponent trimmer,
        ref TrimStats stats)
    {
        Vector3 srcN = Vector3.Cross(vertices[srcB] - vertices[srcA], vertices[srcC] - vertices[srcA]);
        Vector3 newN = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
        if (Vector3.Dot(srcN, newN) < 0f)
        {
            int tmp = b;
            b = c;
            c = tmp;
        }

        return AddTriangle(indices, a, b, c, vertices, uv, trimmer, ref stats);
    }

    private static bool AddTriangle(
        List<int> indices,
        int a,
        int b,
        int c,
        List<Vector3> vertices,
        List<Vector2> uv,
        MeshTrimmerComponent trimmer,
        ref TrimStats stats,
        bool skipAreaThresholds = false)
    {
        if (a == b || b == c || c == a)
        {
            stats.removedTriangles++;
            return false;
        }

        Vector2 uva = uv[a];
        Vector2 uvb = uv[b];
        Vector2 uvc = uv[c];

        if (!skipAreaThresholds)
        {
            float uvArea = Mathf.Abs((uvb.x - uva.x) * (uvc.y - uva.y) - (uvc.x - uva.x) * (uvb.y - uva.y)) * 0.5f;
            if (uvArea < trimmer.minTriangleUvArea)
            {
                stats.removedTriangles++;
                return false;
            }

            float worldArea = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5f;
            if (worldArea < trimmer.minTriangleWorldArea)
            {
                stats.removedTriangles++;
                return false;
            }
        }

        indices.Add(a);
        indices.Add(b);
        indices.Add(c);
        stats.outputTriangles++;
        return true;
    }
}

}
