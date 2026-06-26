using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;
using Object = UnityEngine.Object;

namespace YoridoriModifiers.HairLookKit
{
    internal static class HairAtlasBuilder
    {
        private const string ToolName = "YM Hair Look Kit";

        private enum TextureBakeKind
        {
            Color,
            LinearMask,
            InvertedLinearMask,
            NormalMap,
        }

        private sealed class AtlasPlacement
        {
            public int sourceIndex;
            public int x;
            public int y;
            public int width;
            public int height;

            public Rect ToUvRect(int atlasSize)
            {
                return new Rect(
                    x / (float)atlasSize,
                    y / (float)atlasSize,
                    width / (float)atlasSize,
                    height / (float)atlasSize);
            }
        }

        internal static Dictionary<int, Rect> BuildMainAtlas(
            IReadOnlyList<Material> materials,
            IReadOnlyList<int> mergeIndices,
            Material mergedMaterial,
            int atlasMaxSize,
            Action<string> onProgress,
            BuildContext buildContext)
        {
            if (mergeIndices.Count == 1)
            {
                return new Dictionary<int, Rect> { [mergeIndices[0]] = new Rect(0f, 0f, 1f, 1f) };
            }

            onProgress?.Invoke("Baking atlas...");
            var textures = new List<Texture2D>();
            for (var i = 0; i < mergeIndices.Count; i++)
            {
                var material = materials[mergeIndices[i]];
                TryGetMainTextureWithTransform(material, out var texture, out var textureScale, out var offset);
                var color = ResolveMaterialBaseColor(material);
                var readable = TextureReadUtility.ToReadableTextureWithTransform(texture, textureScale, offset);
                textures.Add(readable != null ? MultiplyTextureColor(readable, color) : NewSolidTexture(color));
            }

            if (textures.All(t => t == null)) throw new InvalidOperationException("Hair merge failed: no main textures resolved for selected materials.");

            var fallback = FirstNonNullTexture(textures) ?? NewSolidTexture(Color.white);
            var packTextures = PrepareBaseAtlasTextures(textures, fallback);
            var atlasSize = ResolvePackedAtlasSize(packTextures, atlasMaxSize, out var placements, out var scale);
            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, true);
            GeneratedTextureUtility.ConfigureRuntimeGeneratedTexture(atlas);
            atlas.SetPixels(Enumerable.Repeat(new Color(0f, 0f, 0f, 0f), atlasSize * atlasSize).ToArray());
            foreach (var placement in placements)
            {
                var source = packTextures[placement.sourceIndex];
                var resized = Mathf.Approximately(scale, 1f) && source.width == placement.width && source.height == placement.height
                    ? source
                    : ResizeTexture(source, placement.width, placement.height);
                atlas.SetPixels(placement.x, placement.y, placement.width, placement.height, resized.GetPixels());
            }
            atlas.Apply(true, false);
            BleedTransparentPixels(atlas, 2);
            atlas = CompressGeneratedAtlas(atlas, "_MainTex");
            buildContext?.AssetSaver.SaveAsset(atlas);
            SetTextureIfAnyExists(mergedMaterial, new[] { "_MainTex", "_BaseMap" }, atlas);
            SetTextureScaleOffsetIfAnyExists(mergedMaterial, new[] { "_MainTex", "_BaseMap" }, Vector2.one, Vector2.zero);
            SetColorIfAnyExists(mergedMaterial, new[] { "_Color", "_BaseColor" }, Color.white);

            var result = new Dictionary<int, Rect>();
            foreach (var placement in placements)
            {
                result[mergeIndices[placement.sourceIndex]] = placement.ToUvRect(atlasSize);
            }
            return result;
        }

        internal static void BakeOptionalAtlases(
            IReadOnlyList<Material> materials,
            IReadOnlyList<int> mergeIndices,
            Material mergedMaterial,
            IReadOnlyDictionary<int, Rect> rectsBySubMesh,
            List<string> warnings,
            bool verboseLog,
            BuildContext buildContext)
        {
            var mainTexture = GetTextureFromAny(mergedMaterial, new[] { "_MainTex", "_BaseMap" }) as Texture2D;
            if (mainTexture == null) return;
            var rects = mergeIndices
                .Select(index => rectsBySubMesh != null && rectsBySubMesh.TryGetValue(index, out var rect) ? rect : new Rect(0f, 0f, 1f, 1f))
                .ToList();

            BakeOptionalAtlas(new[] { "_ShadowColorTex", "_Shadow1stColorTex" }, materials, mergeIndices, mergedMaterial, new[] { "_ShadeTex", "_ShadeTexture", "_ShadeMap", "_ShadeMultiplyTexture", "_ShadeColorTexture" }, mainTexture.width, mainTexture.height, rects, warnings, TextureBakeKind.Color, verboseLog, buildContext);
            BakeOptionalAtlas(new[] { "_EmissionMap" }, materials, mergeIndices, mergedMaterial, new[] { "_EmissiveMap", "_EmissionMap" }, mainTexture.width, mainTexture.height, rects, warnings, TextureBakeKind.Color, verboseLog, buildContext);
            BakeOptionalAtlas(new[] { "_BumpMap" }, materials, mergeIndices, mergedMaterial, new[] { "_NormalMap", "_BumpMap" }, mainTexture.width, mainTexture.height, rects, warnings, TextureBakeKind.NormalMap, verboseLog, buildContext);
            BakeOptionalAtlas(new[] { "_ShadowStrengthMask" }, materials, mergeIndices, mergedMaterial, new[] { "_ShadingShiftTex" }, mainTexture.width, mainTexture.height, rects, warnings, TextureBakeKind.InvertedLinearMask, verboseLog, buildContext);
            BakeOptionalAtlas(new[] { "_ShadowBorderMask" }, materials, mergeIndices, mergedMaterial, new[] { "_ShadingGradeTexture", "_ShadowBorderMask" }, mainTexture.width, mainTexture.height, rects, warnings, TextureBakeKind.LinearMask, verboseLog, buildContext);
            BakeOptionalAtlas(new[] { "_OutlineTex", "_OutlineMask" }, materials, mergeIndices, mergedMaterial, new[] { "_OutlineWidthTex", "_OutlineWidthTexture", "_OutlineWidthMultiplyTexture", "_OutlineMask" }, mainTexture.width, mainTexture.height, rects, warnings, TextureBakeKind.LinearMask, verboseLog, buildContext);
            NormalizeMergedEmissionAndMatCapState(materials, mergeIndices, mergedMaterial);
            ValidateMergedMaterialTextureReferences(mergedMaterial, warnings, verboseLog);
        }

        private static void BakeOptionalAtlas(
            IReadOnlyList<string> destinationProperties,
            IReadOnlyList<Material> materials,
            IReadOnlyList<int> mergeIndices,
            Material mergedMaterial,
            IReadOnlyList<string> sourceProperties,
            int atlasWidth,
            int atlasHeight,
            IReadOnlyList<Rect> rects,
            List<string> warnings,
            TextureBakeKind bakeKind,
            bool verboseLog,
            BuildContext buildContext)
        {
            var destinationProperty = destinationProperties.FirstOrDefault(mergedMaterial.HasProperty);
            if (string.IsNullOrEmpty(destinationProperty)) return;

            var textures = new List<Texture2D>();
            for (var i = 0; i < mergeIndices.Count; i++)
            {
                var source = materials[mergeIndices[i]];
                Texture texture = null;
                var textureScale = Vector2.one;
                var offset = Vector2.zero;
                if (source != null)
                {
                    var sourceProperty = sourceProperties.FirstOrDefault(source.HasProperty);
                    if (!string.IsNullOrEmpty(sourceProperty))
                    {
                        texture = source.GetTexture(sourceProperty);
                        textureScale = source.GetTextureScale(sourceProperty);
                        offset = source.GetTextureOffset(sourceProperty);
                    }
                }

                var readable = TextureReadUtility.ToReadableTextureWithTransform(texture, textureScale, offset, bakeKind == TextureBakeKind.NormalMap);
                if (bakeKind == TextureBakeKind.InvertedLinearMask && readable != null)
                {
                    InvertRgb(readable);
                }
                if (bakeKind == TextureBakeKind.NormalMap && readable != null)
                {
                    ConvertPackedNormalTextureToRgbNormal(readable);
                    ApplyNormalScaleToRgbNormal(readable, ResolveNormalScale(source));
                }
                textures.Add(readable);
            }

            if (textures.All(t => t == null)) return;
            var fallbackColor = ResolveAtlasFallbackColor(destinationProperty);
            var fallback = bakeKind == TextureBakeKind.NormalMap
                ? NewSolidTexture(NeutralNormalColor(), true)
                : FirstNonNullTexture(textures) ?? NewSolidTexture(fallbackColor);
            var atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, true, bakeKind == TextureBakeKind.NormalMap);
            GeneratedTextureUtility.ConfigureRuntimeGeneratedTexture(atlas);
            atlas.SetPixels(Enumerable.Repeat(new Color(0f, 0f, 0f, 0f), atlasWidth * atlasHeight).ToArray());
            for (var i = 0; i < textures.Count && i < rects.Count; i++)
            {
                var src = textures[i] ?? fallback;
                var rect = rects[i];
                var pixelWidth = Mathf.Max(1, Mathf.RoundToInt(rect.width * atlasWidth));
                var pixelHeight = Mathf.Max(1, Mathf.RoundToInt(rect.height * atlasHeight));
                var pixelX = Mathf.Clamp(Mathf.RoundToInt(rect.x * atlasWidth), 0, atlasWidth - pixelWidth);
                var pixelY = Mathf.Clamp(Mathf.RoundToInt(rect.y * atlasHeight), 0, atlasHeight - pixelHeight);
                var resized = ResizeTexture(src, pixelWidth, pixelHeight, bakeKind == TextureBakeKind.NormalMap);
                atlas.SetPixels(pixelX, pixelY, pixelWidth, pixelHeight, resized.GetPixels());
            }

            atlas.Apply(true, false);
            if (bakeKind == TextureBakeKind.NormalMap)
            {
                ReplaceTransparentPixels(atlas, NeutralNormalColor());
                NormalizeRgbNormalTexture(atlas);
            }
            else
            {
                BleedTransparentPixels(atlas, 2);
            }

            atlas = CompressGeneratedAtlas(atlas, destinationProperty);
            buildContext?.AssetSaver.SaveAsset(atlas);
            mergedMaterial.SetTexture(destinationProperty, atlas);
            mergedMaterial.SetTextureScale(destinationProperty, Vector2.one);
            mergedMaterial.SetTextureOffset(destinationProperty, Vector2.zero);
            if (bakeKind == TextureBakeKind.NormalMap)
            {
                SetFloatIfAnyExists(mergedMaterial, new[] { "_BumpScale", "_NormalScale" }, 1f);
                SetFloatIfAnyExists(mergedMaterial, new[] { "_UseBumpMap", "_UseNormalMap" }, 1f);
            }
            else if (bakeKind == TextureBakeKind.InvertedLinearMask)
            {
                SetFloatIfAnyExists(mergedMaterial, new[] { "_UseShadowMask", "_UseShadowStrengthMask" }, 1f);
                SetFloatIfAnyExists(mergedMaterial, new[] { "_ShadowMaskType" }, 0f);
            }
        }

        private static Texture2D[] PrepareBaseAtlasTextures(IReadOnlyList<Texture2D> sourceTextures, Texture2D fallback)
        {
            var prepared = sourceTextures.Select(t => t != null ? t : fallback).ToArray();
            const float fixedScale = 0.99f;
            var resized = new Texture2D[prepared.Length];
            for (var i = 0; i < prepared.Length; i++)
            {
                var width = Mathf.Max(1, Mathf.RoundToInt(prepared[i].width * fixedScale));
                var height = Mathf.Max(1, Mathf.RoundToInt(prepared[i].height * fixedScale));
                resized[i] = ResizeTexture(prepared[i], width, height);
            }
            return resized;
        }

        private static int ResolvePackedAtlasSize(IReadOnlyList<Texture2D> textures, int requestedMaxSize, out List<AtlasPlacement> placements, out float scale)
        {
            var sizes = new[] { 256, 512, 1024, 2048, 4096, 8192 };
            var maxSize = Mathf.Clamp(requestedMaxSize, sizes[0], sizes[^1]);
            foreach (var candidate in sizes)
            {
                if (candidate > maxSize) break;
                if (TryPackAtlas(textures, candidate, 1f, out placements))
                {
                    scale = 1f;
                    return candidate;
                }
            }

            var low = 0.01f;
            var high = 1f;
            placements = null;
            for (var i = 0; i < 16; i++)
            {
                var mid = (low + high) * 0.5f;
                if (TryPackAtlas(textures, maxSize, mid, out var midPlacements))
                {
                    low = mid;
                    placements = midPlacements;
                }
                else
                {
                    high = mid;
                }
            }

            scale = low;
            if (placements == null && !TryPackAtlas(textures, maxSize, scale, out placements))
            {
                throw new InvalidOperationException($"Hair merge failed: atlas packing failed for {maxSize}x{maxSize}.");
            }
            return maxSize;
        }

        private static bool TryPackAtlas(IReadOnlyList<Texture2D> textures, int atlasSize, float scale, out List<AtlasPlacement> placements)
        {
            const int padding = 2;
            placements = new List<AtlasPlacement>();
            if (textures == null || textures.Count == 0) return true;

            var freeRects = new List<RectInt> { new(0, 0, atlasSize, atlasSize) };
            var items = textures
                .Select((texture, index) => new
                {
                    index,
                    width = Mathf.Max(1, Mathf.RoundToInt((texture != null ? texture.width : 1) * scale)),
                    height = Mathf.Max(1, Mathf.RoundToInt((texture != null ? texture.height : 1) * scale)),
                })
                .OrderByDescending(item => item.height)
                .ThenByDescending(item => item.width)
                .ToList();

            foreach (var item in items)
            {
                var outerWidth = item.width + padding * 2;
                var outerHeight = item.height + padding * 2;
                if (outerWidth > atlasSize || outerHeight > atlasSize) return false;

                var bestIndex = -1;
                var bestArea = int.MaxValue;
                var bestShortSide = int.MaxValue;
                for (var i = 0; i < freeRects.Count; i++)
                {
                    var free = freeRects[i];
                    if (outerWidth > free.width || outerHeight > free.height) continue;
                    var leftoverX = free.width - outerWidth;
                    var leftoverY = free.height - outerHeight;
                    var area = free.width * free.height - outerWidth * outerHeight;
                    var shortSide = Mathf.Min(leftoverX, leftoverY);
                    if (area > bestArea || (area == bestArea && shortSide >= bestShortSide)) continue;
                    bestIndex = i;
                    bestArea = area;
                    bestShortSide = shortSide;
                }

                if (bestIndex < 0) return false;
                var target = freeRects[bestIndex];
                var used = new RectInt(target.x, target.y, outerWidth, outerHeight);
                SplitFreeRects(freeRects, used);
                PruneContainedFreeRects(freeRects);

                placements.Add(new AtlasPlacement
                {
                    sourceIndex = item.index,
                    x = used.x + padding,
                    y = used.y + padding,
                    width = item.width,
                    height = item.height,
                });
            }

            placements = placements.OrderBy(p => p.sourceIndex).ToList();
            return true;
        }

        private static void SplitFreeRects(List<RectInt> freeRects, RectInt used)
        {
            for (var i = freeRects.Count - 1; i >= 0; i--)
            {
                var free = freeRects[i];
                if (!free.Overlaps(used)) continue;
                freeRects.RemoveAt(i);

                if (used.xMin > free.xMin)
                {
                    freeRects.Add(new RectInt(free.xMin, free.yMin, used.xMin - free.xMin, free.height));
                }
                if (used.xMax < free.xMax)
                {
                    freeRects.Add(new RectInt(used.xMax, free.yMin, free.xMax - used.xMax, free.height));
                }
                if (used.yMin > free.yMin)
                {
                    freeRects.Add(new RectInt(free.xMin, free.yMin, free.width, used.yMin - free.yMin));
                }
                if (used.yMax < free.yMax)
                {
                    freeRects.Add(new RectInt(free.xMin, used.yMax, free.width, free.yMax - used.yMax));
                }
            }
        }

        private static void PruneContainedFreeRects(List<RectInt> freeRects)
        {
            for (var i = freeRects.Count - 1; i >= 0; i--)
            {
                if (freeRects[i].width <= 0 || freeRects[i].height <= 0)
                {
                    freeRects.RemoveAt(i);
                    continue;
                }
                for (var j = freeRects.Count - 1; j >= 0; j--)
                {
                    if (i == j) continue;
                    if (!Contains(freeRects[j], freeRects[i])) continue;
                    freeRects.RemoveAt(i);
                    break;
                }
            }
        }

        private static bool Contains(RectInt outer, RectInt inner)
        {
            return inner.xMin >= outer.xMin
                && inner.yMin >= outer.yMin
                && inner.xMax <= outer.xMax
                && inner.yMax <= outer.yMax;
        }

        private static Texture2D FirstNonNullTexture(IReadOnlyList<Texture2D> textures)
        {
            if (textures == null) return null;
            for (var i = 0; i < textures.Count; i++)
            {
                if (textures[i] != null) return textures[i];
            }
            return null;
        }

        private static Texture GetTextureFromAny(Material material, IReadOnlyList<string> propertyNames)
        {
            if (material == null || propertyNames == null) return null;
            for (var i = 0; i < propertyNames.Count; i++)
            {
                var propertyName = propertyNames[i];
                if (!material.HasProperty(propertyName)) continue;
                var texture = material.GetTexture(propertyName);
                if (texture != null) return texture;
            }
            return null;
        }

        private static void NormalizeMergedEmissionAndMatCapState(IReadOnlyList<Material> materials, IReadOnlyList<int> mergeIndices, Material mergedMaterial)
        {
            if (materials == null || mergeIndices == null || mergedMaterial == null) return;
            var hasEmissionTexture = false;
            var hasEmissionColor = false;
            var emissionColor = Color.black;
            var hasMatCapTexture = false;
            var hasMatCapColor = false;
            var matCapColor = Color.black;

            foreach (var sourceIndex in mergeIndices)
            {
                if (sourceIndex < 0 || sourceIndex >= materials.Count) continue;
                var source = materials[sourceIndex];
                if (source == null) continue;
                var emissionTex = GetTextureFromAny(source, new[] { "_EmissiveMap", "_EmissionMap" });
                if (emissionTex != null && !IsLikelyDummyTexture(emissionTex)) hasEmissionTexture = true;
                if (TryGetColorFromAny(source, new[] { "_EmissiveFactor", "_EmissionColor" }, out var sourceEmissionColor) && !IsApproximatelyBlack(sourceEmissionColor))
                {
                    if (!hasEmissionColor) emissionColor = sourceEmissionColor;
                    hasEmissionColor = true;
                }

                var matCapTex = GetTextureFromAny(source, new[] { "_MatcapTex", "_SphereAdd" });
                if (matCapTex != null && !IsLikelyDummyTexture(matCapTex)) hasMatCapTexture = true;
                if (TryGetColorFromAny(source, new[] { "_MatcapColor" }, out var sourceMatCapColor) && !IsApproximatelyBlack(sourceMatCapColor))
                {
                    if (!hasMatCapColor) matCapColor = sourceMatCapColor;
                    hasMatCapColor = true;
                }
            }

            SetFloatIfAnyExists(mergedMaterial, new[] { "_UseEmission" }, hasEmissionTexture || hasEmissionColor ? 1f : 0f);
            SetColorIfAnyExists(mergedMaterial, new[] { "_EmissionColor" }, hasEmissionColor ? emissionColor : Color.black);
            SetFloatIfAnyExists(mergedMaterial, new[] { "_UseMatCap" }, hasMatCapTexture ? 1f : 0f);
            SetColorIfAnyExists(mergedMaterial, new[] { "_MatCapColor" }, hasMatCapTexture ? (hasMatCapColor ? matCapColor : Color.white) : Color.black);
        }

        private static void ValidateMergedMaterialTextureReferences(Material mergedMaterial, List<string> warnings, bool verboseLog)
        {
            if (mergedMaterial == null) return;
            foreach (var propertyName in new[] { "_MainTex", "_BaseMap", "_BumpMap", "_EmissionMap", "_ShadowColorTex", "_Shadow1stColorTex", "_ShadowBorderMask", "_ShadowStrengthMask", "_OutlineTex", "_OutlineMask" })
            {
                if (!mergedMaterial.HasProperty(propertyName)) continue;
                var texture = mergedMaterial.GetTexture(propertyName);
                if (texture == null) continue;
                if (verboseLog)
                {
                    var format = texture is Texture2D texture2D ? texture2D.format.ToString() : "(non-Texture2D)";
                    LogUtility.Info(ToolName, $"{mergedMaterial.name} {propertyName} format={format}");
                }
                if (texture is Texture2D t && t.format == TextureFormat.RGBA32)
                {
                    warnings?.Add($"{mergedMaterial.name}: {propertyName} remains RGBA32 after compression.");
                }
            }
        }

        private static bool IsLikelyDummyTexture(Texture texture)
        {
            return texture != null && texture.width <= 8 && texture.height <= 8;
        }

        private static bool IsApproximatelyBlack(Color color)
        {
            return color.r <= 0.001f && color.g <= 0.001f && color.b <= 0.001f;
        }

        private static Texture2D CompressGeneratedAtlas(Texture2D atlas, string propertyName, BuildTarget? buildTarget = null)
        {
            var isNormal = string.Equals(propertyName, "_BumpMap", StringComparison.OrdinalIgnoreCase);
            return GeneratedTextureUtility.CompressGeneratedTexture(atlas, propertyName, isNormal, buildTarget);
        }

        private static Color NeutralNormalColor()
        {
            return new Color(0.5f, 0.5f, 1f, 1f);
        }

        private static Color ResolveAtlasFallbackColor(string destinationProperty)
        {
            if (string.IsNullOrEmpty(destinationProperty)) return Color.white;
            if (destinationProperty.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0) return NeutralNormalColor();
            if (destinationProperty.IndexOf("Emission", StringComparison.OrdinalIgnoreCase) >= 0) return Color.black;
            return Color.white;
        }

        private static float ResolveNormalScale(Material material)
        {
            if (material == null) return 1f;
            if (material.HasProperty("_BumpScale")) return material.GetFloat("_BumpScale");
            if (material.HasProperty("_NormalScale")) return material.GetFloat("_NormalScale");
            return 1f;
        }

        private static void InvertRgb(Texture2D texture)
        {
            if (texture == null) return;
            var pixels = texture.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                pixel.r = 1f - pixel.r;
                pixel.g = 1f - pixel.g;
                pixel.b = 1f - pixel.b;
                pixels[i] = pixel;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
        }

        private static void ConvertPackedNormalTextureToRgbNormal(Texture2D texture)
        {
            if (texture == null) return;
            var pixels = texture.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = EncodeRgbNormal(DecodePackedNormalAg(pixels[i]));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
        }

        private static void NormalizeRgbNormalTexture(Texture2D texture)
        {
            if (texture == null) return;
            var pixels = texture.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = EncodeRgbNormal(DecodeRgbNormal(pixels[i]));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
        }

        private static void ApplyNormalScaleToRgbNormal(Texture2D texture, float normalScale)
        {
            if (texture == null || Mathf.Approximately(normalScale, 1f)) return;
            var pixels = texture.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                var normal = DecodeRgbNormal(pixels[i]);
                normal.x *= normalScale;
                normal.y *= normalScale;
                if (normal.sqrMagnitude <= 1e-8f) normal = Vector3.forward;
                normal.Normalize();
                pixels[i] = EncodeRgbNormal(normal);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
        }

        private static Vector3 DecodeRgbNormal(Color color)
        {
            var normal = new Vector3(color.r * 2f - 1f, color.g * 2f - 1f, color.b * 2f - 1f);
            if (normal.sqrMagnitude <= 1e-8f) return Vector3.forward;
            normal.Normalize();
            return normal;
        }

        private static Vector3 DecodePackedNormalAg(Color color)
        {
            var x = color.a * 2f - 1f;
            var y = color.g * 2f - 1f;
            var z = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Clamp01(x * x + y * y)));
            return new Vector3(x, y, z);
        }

        private static Color EncodeRgbNormal(Vector3 normal)
        {
            if (normal.sqrMagnitude <= 1e-8f) normal = Vector3.forward;
            normal.Normalize();
            return new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
        }

        private static void ReplaceTransparentPixels(Texture2D texture, Color replacement)
        {
            if (texture == null) return;
            var pixels = texture.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a > 0.0001f) continue;
                pixels[i] = replacement;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
        }

        private static void BleedTransparentPixels(Texture2D texture, int iterations)
        {
            if (texture == null || iterations <= 0) return;
            var width = texture.width;
            var height = texture.height;
            var pixels = texture.GetPixels();
            var work = new Color[pixels.Length];

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                Array.Copy(pixels, work, pixels.Length);
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var index = y * width + x;
                        if (pixels[index].a > 0.0001f) continue;

                        var sum = Color.clear;
                        var count = 0;
                        for (var offsetY = -1; offsetY <= 1; offsetY++)
                        {
                            var neighborY = y + offsetY;
                            if (neighborY < 0 || neighborY >= height) continue;
                            for (var offsetX = -1; offsetX <= 1; offsetX++)
                            {
                                var neighborX = x + offsetX;
                                if (neighborX < 0 || neighborX >= width) continue;
                                var neighbor = pixels[neighborY * width + neighborX];
                                if (neighbor.a <= 0.0001f) continue;
                                sum += neighbor;
                                count++;
                            }
                        }

                        if (count <= 0) continue;
                        var averaged = sum / count;
                        var maxNeighborAlpha = 0f;
                        for (var offsetY = -1; offsetY <= 1; offsetY++)
                        {
                            var neighborY = y + offsetY;
                            if (neighborY < 0 || neighborY >= height) continue;
                            for (var offsetX = -1; offsetX <= 1; offsetX++)
                            {
                                var neighborX = x + offsetX;
                                if (neighborX < 0 || neighborX >= width) continue;
                                maxNeighborAlpha = Mathf.Max(maxNeighborAlpha, pixels[neighborY * width + neighborX].a);
                            }
                        }
                        averaged.a = maxNeighborAlpha;
                        work[index] = averaged;
                    }
                }

                var previous = pixels;
                pixels = work;
                work = previous;
            }

            texture.SetPixels(pixels);
            texture.Apply();
        }

        private static Texture2D ResizeTexture(Texture2D source, int width, int height, bool linear = false)
        {
            if (linear && source != null && source.isReadable)
            {
                var resizedCpu = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                var pixels = new Color[width * height];
                for (var y = 0; y < height; y++)
                {
                    var v = height > 1 ? y / (float)(height - 1) : 0.5f;
                    for (var x = 0; x < width; x++)
                    {
                        var u = width > 1 ? x / (float)(width - 1) : 0.5f;
                        pixels[y * width + x] = source.GetPixelBilinear(u, v);
                    }
                }
                resizedCpu.SetPixels(pixels);
                resizedCpu.Apply(false, false);
                return resizedCpu;
            }

            var renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var current = RenderTexture.active;
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;
            var resized = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resized.Apply();
            RenderTexture.active = current;
            RenderTexture.ReleaseTemporary(renderTexture);
            return resized;
        }

        private static bool TryGetMainTextureWithTransform(Material material, out Texture texture, out Vector2 scale, out Vector2 offset)
        {
            texture = null;
            scale = Vector2.one;
            offset = Vector2.zero;
            if (material == null) return false;
            foreach (var propertyName in new[] { "_MainTex", "_BaseMap" })
            {
                if (!material.HasProperty(propertyName)) continue;
                texture = material.GetTexture(propertyName);
                scale = material.GetTextureScale(propertyName);
                offset = material.GetTextureOffset(propertyName);
                return texture != null;
            }
            return false;
        }

        private static Color ResolveMaterialBaseColor(Material material)
        {
            if (material == null) return Color.white;
            return TryGetColorFromAny(material, new[] { "_BaseColor", "_Color", "_MainColor" }, out var color)
                ? color
                : Color.white;
        }

        private static Texture2D MultiplyTextureColor(Texture2D texture, Color color)
        {
            if (texture == null) return null;
            var copy = Object.Instantiate(texture);
            for (var y = 0; y < copy.height; y++)
            {
                for (var x = 0; x < copy.width; x++)
                {
                    copy.SetPixel(x, y, copy.GetPixel(x, y) * color);
                }
            }
            copy.Apply(true, false);
            return copy;
        }

        private static Texture2D NewSolidTexture(Color color, bool linear = false)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear);
            texture.SetPixels(Enumerable.Repeat(color, 16).ToArray());
            texture.Apply();
            return texture;
        }

        private static bool TryGetColorFromAny(Material material, IReadOnlyList<string> propertyNames, out Color color)
        {
            color = Color.white;
            if (material == null || propertyNames == null) return false;
            foreach (var propertyName in propertyNames)
            {
                if (!material.HasProperty(propertyName)) continue;
                color = material.GetColor(propertyName);
                return true;
            }
            return false;
        }

        private static void SetFloatIfAnyExists(Material material, IReadOnlyList<string> propertyNames, float value)
        {
            if (material == null || propertyNames == null) return;
            foreach (var propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName)) material.SetFloat(propertyName, value);
            }
        }

        private static void SetColorIfAnyExists(Material material, IReadOnlyList<string> propertyNames, Color value)
        {
            if (material == null || propertyNames == null) return;
            foreach (var propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName)) material.SetColor(propertyName, value);
            }
        }

        private static void SetTextureIfAnyExists(Material material, IReadOnlyList<string> propertyNames, Texture value)
        {
            if (material == null || propertyNames == null) return;
            foreach (var propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName)) material.SetTexture(propertyName, value);
            }
        }

        private static void SetTextureScaleOffsetIfAnyExists(Material material, IReadOnlyList<string> propertyNames, Vector2 scale, Vector2 offset)
        {
            if (material == null || propertyNames == null) return;
            foreach (var propertyName in propertyNames)
            {
                if (!material.HasProperty(propertyName)) continue;
                material.SetTextureScale(propertyName, scale);
                material.SetTextureOffset(propertyName, offset);
            }
        }
    }
}
