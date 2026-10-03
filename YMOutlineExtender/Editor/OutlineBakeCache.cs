using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.OutlineExtender
{
    /// <summary>Build/preview cache for normal color-alpha bake results and reusable distance data.</summary>
    internal sealed class OutlineBakeCache
    {
        internal const float ObjectSpaceWidthCompensation = 2f;
        internal const float MinimumTexelWidth = 1f;

        // The final Cutout shader also performs edge AA. A full one-texel bake ramp
        // consumes the entire line when the physical width resolves to about one
        // texel, leaving no fully covered sample. Keep a sub-texel ramp while the
        // 50% coverage contour remains exactly at the requested SDF threshold.
        internal const float CoverageHalfWidth = 0.25f;

        private sealed class DistanceData
        {
            internal readonly Color[][] Pixels;
            internal readonly float[][] Distances;
            internal DistanceData(int mipCount) { Pixels = new Color[mipCount][]; Distances = new float[mipCount][]; }
        }

        private readonly Dictionary<(Texture2D, float, Hash128, Hash128), DistanceData> distanceData = new();
        private readonly Dictionary<(Texture2D, float, Hash128, Hash128, Hash128, Color, float), Texture2D> textures = new();
        internal IEnumerable<Texture2D> Textures => textures.Values;
        internal int Count => textures.Count;
        internal int DistanceFieldCount => distanceData.Count;

        internal Texture2D Get(Texture2D source, float cutoff, Color outlineColor, float objectWidth,
            OutlineUvCoverage coverage, OutlineUvMetric metric,
            Action<IReadOnlyList<OutlineUvMetricSample>> diagnostics = null)
        {
            if (metric == null) throw new InvalidOperationException("No usable UV-to-object-space metric was found.");
            var sourceKey = (source, cutoff, source.imageContentsHash,
                coverage?.Signature ?? default(Hash128));
            var outputKey = (source, cutoff, source.imageContentsHash,
                coverage?.Signature ?? default(Hash128), metric.Signature, outlineColor, objectWidth);
            if (textures.TryGetValue(outputKey, out var existing) && existing != null) return existing;
            if (!distanceData.TryGetValue(sourceKey, out var data))
            {
                data = GenerateDistanceData(source, cutoff, coverage);
                distanceData[sourceKey] = data;
            }

            var output = new Texture2D(source.width, source.height, TextureFormat.RGBA32,
                source.mipmapCount, !source.isDataSRGB)
            {
                name = source.name + "_OutlineExtenderBake",
                wrapModeU = source.wrapModeU, wrapModeV = source.wrapModeV,
                filterMode = source.filterMode, anisoLevel = source.anisoLevel,
                mipMapBias = source.mipMapBias
            };
            try
            {
                var storedOutlineColor = outlineColor;
                if (source.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear)
                    storedOutlineColor = storedOutlineColor.gamma;
                for (var mip = 0; mip < source.mipmapCount; mip++)
                {
                    var width = Mathf.Max(1, source.width >> mip);
                    var height = Mathf.Max(1, source.height >> mip);
                    var pixels = (Color[])data.Pixels[mip].Clone();
                    var distances = data.Distances[mip];
                    if (source.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear)
                        for (var i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
                    var range = OutlineSdfGenerator.DistanceRange / (1 << mip);
                    var widths = metric.RasterizeRequiredWidths(width, height, distances,
                        objectWidth, Mathf.Max(MinimumTexelWidth, range - 0.75f),
                        MinimumTexelWidth, mip == 0 ? diagnostics : null);
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        // Never touch original opaque/cutout pixels: the bake only grows
                        // from the original contour into its transparent side.
                        if (pixels[i].a >= cutoff || float.IsPositiveInfinity(widths[i])) continue;
                        var signedDistance = distances[i] - 0.5f;
                        var lineCoverage = Mathf.Clamp01(
                            (signedDistance + widths[i]) / (2f * CoverageHalfWidth) + 0.5f);
                        if (lineCoverage <= 0f) continue;
                        pixels[i].r = storedOutlineColor.r;
                        pixels[i].g = storedOutlineColor.g;
                        pixels[i].b = storedOutlineColor.b;
                        // Convert analytic coverage to texture alpha while keeping the
                        // unchanged material cutoff at 50% coverage. Full line coverage
                        // must reach alpha 1; compressing it into a narrow band around the
                        // cutoff makes one-to-two-texel lines collapse under mip filtering.
                        var outlineAlpha = lineCoverage < 0.5f
                            ? Mathf.Lerp(pixels[i].a, cutoff, lineCoverage * 2f)
                            : Mathf.Lerp(cutoff, 1f, lineCoverage * 2f - 1f);
                        pixels[i].a = Mathf.Max(pixels[i].a, outlineAlpha);
                    }
                    output.SetPixels(pixels, mip);
                }
                output.Apply(false, false);
                GeneratedTextureUtility.ConfigureRuntimeGeneratedTexture(output);
                YMTextureRegistry.Register(output, source, YMTextureUsage.ColorWithAlpha,
                    "YM Outline Extender Bake", YMTextureCompressionQuality.High);
                textures[outputKey] = output;
                return output;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(output);
                throw;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private static DistanceData GenerateDistanceData(Texture2D source, float cutoff, OutlineUvCoverage coverage)
        {
            var data = new DistanceData(source.mipmapCount);
            for (var mip = 0; mip < source.mipmapCount; mip++)
            {
                var width = Mathf.Max(1, source.width >> mip);
                var height = Mathf.Max(1, source.height >> mip);
                var pixels = OutlineSdfCache.ReadPixels(source, mip, width, height);
                var mask = coverage?.Rasterize(width, height);
                data.Pixels[mip] = pixels;
                data.Distances[mip] = OutlineSdfGenerator.Generate(pixels, width, height, cutoff,
                    source.wrapModeU == TextureWrapMode.Repeat,
                    source.wrapModeV == TextureWrapMode.Repeat,
                    progress => Report(source, mip, (mip + progress) / source.mipmapCount),
                    OutlineSdfGenerator.DistanceRange / (1 << mip), mask);
            }
            return data;
        }

        internal void DestroyTextures()
        {
            foreach (var texture in textures.Values)
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            textures.Clear();
            distanceData.Clear();
        }

        private static void Report(Texture source, int mip, float progress)
        {
            if (EditorUtility.DisplayCancelableProgressBar("YM Outline Extender",
                $"{source.name} ({source.width}×{source.height}) — Bake, mip {mip}", progress))
                throw new OperationCanceledException("Outline Extender baking cancelled.");
        }
    }
}
