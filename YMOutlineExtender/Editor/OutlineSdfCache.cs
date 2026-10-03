using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.OutlineExtender
{
    /// <summary>One cache per build; color and width deliberately do not participate.</summary>
    internal sealed class OutlineSdfCache
    {
        private readonly Dictionary<(Texture2D, float, Hash128, Hash128), Texture2D> textures = new();
        public int Count => textures.Count;
        internal IEnumerable<Texture2D> Textures => textures.Values;

        public Texture2D Get(Texture2D source, float cutoff, OutlineUvCoverage uvCoverage = null)
        {
            var key = (source, cutoff, source.imageContentsHash, uvCoverage?.Signature ?? default);
            if (textures.TryGetValue(key, out var existing) && existing != null) return existing;
            // Preserve source color space. Only alpha is replaced; the finalizer compresses later.
            var output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, source.mipmapCount, !source.isDataSRGB)
            {
                name = source.name + "_OutlineExtenderSDF",
                wrapModeU = source.wrapModeU, wrapModeV = source.wrapModeV,
                filterMode = source.filterMode, anisoLevel = source.anisoLevel,
                mipMapBias = source.mipMapBias
            };
            try
            {
                for (var mip = 0; mip < source.mipmapCount; mip++)
                {
                    var width = Mathf.Max(1, source.width >> mip);
                    var height = Mathf.Max(1, source.height >> mip);
                    var pixels = ReadPixels(source, mip, width, height);
                    var coverage = uvCoverage?.Rasterize(width, height);
                    var alpha = OutlineSdfGenerator.Generate(pixels, width, height, cutoff,
                        source.wrapModeU == TextureWrapMode.Repeat, source.wrapModeV == TextureWrapMode.Repeat, progress =>
                            Report(source, mip, (mip + progress * 0.85f) / source.mipmapCount, "SDF"),
                        OutlineSdfGenerator.DistanceRange / (1 << mip), coverage);
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        if (source.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear)
                            pixels[i] = pixels[i].gamma;
                        pixels[i].a = 0.5f + (alpha[i] - 0.5f) * (1 << mip) / (2 * OutlineSdfGenerator.DistanceRange);
                    }
                    output.SetPixels(pixels, mip);
                }
                output.Apply(false, false);
                GeneratedTextureUtility.ConfigureRuntimeGeneratedTexture(output);
                YMTextureRegistry.Register(output, source, YMTextureUsage.SignedDistanceField,
                    "YM Outline Extender", YMTextureCompressionQuality.SdfHigh);
                textures[key] = output;
                return output;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(output);
                throw;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        internal void DestroyTextures()
        {
            foreach (var texture in textures.Values)
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            textures.Clear();
        }

        private static void Report(Texture source, int mip, float progress, string stage)
        {
            if (EditorUtility.DisplayCancelableProgressBar("YM Outline Extender",
                $"{source.name} ({source.width}×{source.height}) — {stage}, mip {mip}", progress))
                throw new OperationCanceledException("Outline Extender baking cancelled.");
        }

        internal static Color[] ReadPixels(Texture2D source, int mip, int width, int height)
        {
            if (source.isReadable)
            {
                var pixels = source.GetPixels(mip);
                if (source.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear)
                    for (var i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].linear;
                return pixels;
            }
            // Explicit LOD avoids applying the source mip bias twice. This editor-only
            // shader is never assigned to an avatar material or included in its build.
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Texture2D readable = null;
            Material readMaterial = null;
            try
            {
                var shader = Shader.Find("Hidden/Yoridori Modifiers/Outline Extender Read Main Mip");
                if (shader == null) throw new InvalidOperationException("SDF readback shader was not found.");
                readMaterial = new Material(shader);
                readMaterial.SetFloat("_Mip", mip);
                Graphics.Blit(source, rt, readMaterial);
                RenderTexture.active = rt;
                readable = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply(false, false);
                return readable.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
                if (readMaterial != null) UnityEngine.Object.DestroyImmediate(readMaterial);
            }
        }
    }
}
