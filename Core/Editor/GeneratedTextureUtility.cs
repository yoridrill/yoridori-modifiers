using System;
using UnityEditor;
using UnityEngine;

namespace YoridoriModifiers.Core.Editor
{
    public static class GeneratedTextureUtility
    {
        public static Texture2D CompressGeneratedTexture(Texture2D texture, string context, bool isNormalMap = false, BuildTarget? buildTarget = null)
        {
            if (texture == null) throw new InvalidOperationException($"CompressGeneratedTexture: texture is null ({context}).");

            var target = buildTarget ?? EditorUserBuildSettings.activeBuildTarget;

            if (isNormalMap)
            {
                return GenerateNormalMapTexture(texture, context, target);
            }

            ConfigureRuntimeGeneratedTexture(texture);
            texture.Apply(true, false);
            EditorUtility.CompressTexture(texture, ResolveRuntimeTextureFormat(target), TextureCompressionQuality.Normal);
            ConfigureRuntimeGeneratedTexture(texture);
            if (texture.format == TextureFormat.RGBA32)
            {
                throw new InvalidOperationException($"Generated texture compression failed for {context}; format is still RGBA32.");
            }

            return texture;
        }

        private static Texture2D GenerateNormalMapTexture(Texture2D texture, string context, BuildTarget buildTarget)
        {
            var output = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, true, true)
            {
                name = texture.name,
                wrapMode = texture.wrapMode,
                filterMode = texture.filterMode,
                anisoLevel = texture.anisoLevel,
            };
            ConfigureRuntimeGeneratedTexture(output);

            output.SetPixels32(PackRgbNormalPixelsForUnity(texture.GetPixels32(0)));
            output.Apply(true, false);
            EditorUtility.CompressTexture(output, ResolveRuntimeTextureFormat(buildTarget), TextureCompressionQuality.Normal);
            ConfigureRuntimeGeneratedTexture(output);
            if (output.format == TextureFormat.RGBA32)
            {
                throw new InvalidOperationException($"Generated normal map compression failed for {context}; format is still RGBA32.");
            }

            return output;
        }

        private static TextureFormat ResolveRuntimeTextureFormat(BuildTarget buildTarget)
        {
            switch (buildTarget)
            {
                case BuildTarget.Android:
                case BuildTarget.iOS:
                    return TextureFormat.ASTC_6x6;
                default:
                    return TextureFormat.DXT5;
            }
        }

        public static void ConfigureRuntimeGeneratedTexture(Texture2D texture, bool streamingMipmaps = true, int streamingMipmapsPriority = 0)
        {
            if (texture == null) return;

            using var serializedTexture = new SerializedObject(texture);
            var streamingMipmapsProperty = serializedTexture.FindProperty("m_StreamingMipmaps");
            if (streamingMipmapsProperty != null)
            {
                streamingMipmapsProperty.boolValue = streamingMipmaps;
            }

            var streamingMipmapsPriorityProperty = serializedTexture.FindProperty("m_StreamingMipmapsPriority");
            if (streamingMipmapsPriorityProperty != null)
            {
                streamingMipmapsPriorityProperty.intValue = streamingMipmapsPriority;
            }

            serializedTexture.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Color32[] PackRgbNormalPixelsForUnity(Color32[] pixels)
        {
            if (pixels == null) return Array.Empty<Color32>();

            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                var normal = DecodeRgbNormal(pixel);
                pixel.g = EncodeNormalChannel(normal.y);
                pixel.b = 255;
                pixel.a = EncodeNormalChannel(normal.x);
                pixel.r = 255;
                pixels[i] = pixel;
            }

            return pixels;
        }

        private static Vector3 DecodeRgbNormal(Color32 pixel)
        {
            var normal = new Vector3(
                pixel.r / 255f * 2f - 1f,
                pixel.g / 255f * 2f - 1f,
                pixel.b / 255f * 2f - 1f);
            if (normal.sqrMagnitude <= 1e-8f) return Vector3.forward;
            normal.Normalize();
            return normal;
        }

        private static byte EncodeNormalChannel(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt((value * 0.5f + 0.5f) * 255f), 0, 255);
        }

        public static void ConfigureGeneratedTextureImporter(
            TextureImporter importer,
            bool isNormalMap,
            bool isMask,
            bool streamingMipmaps = true,
            int maxTextureSize = 0)
        {
            if (importer == null) return;

            importer.textureType = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = streamingMipmaps;
            importer.streamingMipmapsPriority = 0;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.sRGBTexture = !isNormalMap && !isMask;
            importer.crunchedCompression = false;
            if (maxTextureSize > 0)
            {
                importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Clamp(maxTextureSize, 32, 16384));
            }

            var settings = importer.GetPlatformTextureSettings("Standalone");
            settings.overridden = true;
            if (maxTextureSize > 0)
            {
                settings.maxTextureSize = importer.maxTextureSize;
            }
            settings.format = TextureImporterFormat.Automatic;
            settings.textureCompression = TextureImporterCompression.Compressed;
            settings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(settings);
        }
    }
}
