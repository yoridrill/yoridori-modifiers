using System;
using UnityEditor;
using UnityEngine;

namespace YoridoriModifiers.Core.Editor
{
    public static class GeneratedTextureUtility
    {
        public static void CompressGeneratedTexture(Texture2D texture, string context, bool isNormalMap = false)
        {
            if (texture == null) throw new InvalidOperationException($"CompressGeneratedTexture: texture is null ({context}).");

            var targetFormat = isNormalMap ? TextureFormat.DXT5 : TextureFormat.DXT5;
            EditorUtility.CompressTexture(texture, targetFormat, TextureCompressionQuality.Normal);
            if (texture.format == TextureFormat.RGBA32)
            {
                throw new InvalidOperationException($"Generated texture compression failed for {context}; format is still RGBA32.");
            }
        }

        public static void ConfigureGeneratedTextureImporter(
            TextureImporter importer,
            bool isNormalMap,
            bool isMask,
            bool streamingMipmaps = true)
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

            var settings = importer.GetPlatformTextureSettings("Standalone");
            settings.overridden = true;
            settings.format = TextureImporterFormat.Automatic;
            settings.textureCompression = TextureImporterCompression.Compressed;
            settings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(settings);
        }
    }
}
