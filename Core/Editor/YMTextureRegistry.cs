using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace YoridoriModifiers.Core.Editor
{
    public enum YMTextureUsage
    {
        ColorWithAlpha,
        ColorOpaque,
        NormalMap,
        Mask,
        SignedDistanceField,
    }

    public enum YMTextureCompressionQuality
    {
        Normal,
        High,
        SdfHigh,
    }

    public sealed class YMGeneratedTextureInfo
    {
        public Texture2D Texture { get; internal set; }
        public IReadOnlyList<Texture2D> Parents { get; internal set; }
        public IReadOnlyList<Texture2D> OriginalTextures { get; internal set; }
        public bool IsSrgb { get; internal set; }
        public bool HasMipmaps { get; internal set; }
        public TextureWrapMode WrapU { get; internal set; }
        public TextureWrapMode WrapV { get; internal set; }
        public FilterMode FilterMode { get; internal set; }
        public YMTextureUsage Usage { get; internal set; }
        public YMTextureCompressionQuality CompressionQuality { get; internal set; }
        public string SourceModifier { get; internal set; }
        public string OriginalAssetPath { get; internal set; }
        public TextureImporterPlatformSettings StandaloneSettings { get; internal set; }
        public TextureImporterPlatformSettings AndroidSettings { get; internal set; }
        public bool IsFinalized { get; internal set; }
        public int CompressionCount { get; internal set; }
    }

    public static class YMTextureRegistry
    {
        private static readonly ConditionalWeakTable<Texture2D, YMGeneratedTextureInfo> Entries = new();

        public static YMGeneratedTextureInfo Register(
            Texture2D texture,
            IEnumerable<Texture2D> parents,
            YMTextureUsage usage,
            string sourceModifier,
            YMTextureCompressionQuality quality = YMTextureCompressionQuality.Normal)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            var parentList = (parents ?? Enumerable.Empty<Texture2D>()).Where(t => t != null).Distinct().ToArray();
            var originals = new HashSet<Texture2D>();
            foreach (var parent in parentList)
            {
                if (TryGet(parent, out var parentInfo)) originals.UnionWith(parentInfo.OriginalTextures);
                else originals.Add(parent);
            }
            var original = originals.FirstOrDefault() ?? parentList.FirstOrDefault();
            var importer = original != null ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(original)) as TextureImporter : null;
            var info = new YMGeneratedTextureInfo
            {
                Texture = texture,
                Parents = parentList,
                OriginalTextures = originals.ToArray(),
                IsSrgb = texture.isDataSRGB,
                HasMipmaps = texture.mipmapCount > 1,
                WrapU = texture.wrapModeU,
                WrapV = texture.wrapModeV,
                FilterMode = texture.filterMode,
                Usage = usage,
                CompressionQuality = quality,
                SourceModifier = sourceModifier ?? "Yoridori Modifiers",
                OriginalAssetPath = original != null ? AssetDatabase.GetAssetPath(original) : string.Empty,
                StandaloneSettings = importer?.GetPlatformTextureSettings("Standalone"),
                AndroidSettings = importer?.GetPlatformTextureSettings("Android"),
            };
            Entries.Remove(texture);
            Entries.Add(texture, info);
            GeneratedTextureUtility.ConfigureRuntimeGeneratedTexture(texture);
            return info;
        }

        public static YMGeneratedTextureInfo Register(Texture2D texture, Texture2D parent,
            YMTextureUsage usage, string sourceModifier,
            YMTextureCompressionQuality quality = YMTextureCompressionQuality.Normal) =>
            Register(texture, parent != null ? new[] { parent } : Array.Empty<Texture2D>(), usage, sourceModifier, quality);

        public static bool TryGet(Texture2D texture, out YMGeneratedTextureInfo info)
        {
            if (texture != null && Entries.TryGetValue(texture, out info)) return true;
            info = null;
            return false;
        }
    }
}
