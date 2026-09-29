using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;

[assembly: ExportsPlugin(typeof(YoridoriModifiers.Core.Editor.YMTextureFinalizerNdmfPlugin))]

namespace YoridoriModifiers.Core.Editor
{
    public sealed class YMTextureFinalizerNdmfPlugin : Plugin<YMTextureFinalizerNdmfPlugin>
    {
        public const string PluginQualifiedName = "jp.yoridrill.ym-texture-finalizer";
        public override string QualifiedName => PluginQualifiedName;
        public override string DisplayName => "YM Texture Finalizer";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("net.rs64.tex-trans-tool")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .BeforePlugin("com.github.kurotu.vrc-quest-tools")
                .Run("Compress Referenced YM Textures", context => { FinalizeTextures(context); });
        }

        public static int FinalizeTextures(BuildContext context) => FinalizeTextures(
            context?.AvatarRootObject,
            YMBuildTargetUtility.Resolve(context?.AvatarRootObject),
            context?.AssetSaver);

        public static int FinalizeTextures(GameObject avatarRoot, BuildTarget target)
            => FinalizeTextures(avatarRoot, target, null);

        private static int FinalizeTextures(GameObject avatarRoot, BuildTarget target, IAssetSaver assetSaver)
        {
            if (avatarRoot == null) return 0;
            var referenced = CollectReferencedTextures(avatarRoot);
            var count = 0;
            foreach (var texture in referenced)
            {
                if (!YMTextureRegistry.TryGet(texture, out var info) || info.IsFinalized) continue;
                var format = SelectFormat(info, target);
                if (!CanCompress(texture, format))
                {
                    LogUtility.Warning("YM Texture Finalizer", $"{texture.name}: {format} requires compatible block dimensions; keeping {texture.format}.");
                    assetSaver?.SaveAsset(texture);
                    continue;
                }
                var finalized = UnityEngine.Object.Instantiate(texture);
                finalized.name = texture.name;
                finalized.hideFlags &= ~(HideFlags.DontSave | HideFlags.DontSaveInBuild
                    | HideFlags.DontSaveInEditor | HideFlags.HideAndDontSave);
                if (info.Usage == YMTextureUsage.NormalMap && target != BuildTarget.Android)
                    GeneratedTextureUtility.PackRgbNormalForDesktop(finalized);
                EditorUtility.CompressTexture(finalized, format, ResolveQuality(info, target));
                GeneratedTextureUtility.ConfigureRuntimeGeneratedTexture(finalized);
                if (finalized.format != format)
                {
                    LogUtility.Warning("YM Texture Finalizer", $"{texture.name}: compression to {format} failed; keeping {texture.format}.");
                    UnityEngine.Object.DestroyImmediate(finalized);
                    assetSaver?.SaveAsset(texture);
                    continue;
                }
                NdmfObjectRegistry.RegisterReplacement(texture, finalized);
                // Keep the uncompressed working texture as the direct parent. This
                // preserves the complete build-time lineage and also makes it clear
                // that compression happens exactly once, at this final boundary.
                var finalizedInfo = YMTextureRegistry.Register(finalized, texture, info.Usage,
                    info.SourceModifier, info.CompressionQuality);
                finalizedInfo.IsFinalized = true;
                finalizedInfo.CompressionCount = info.CompressionCount + 1;
                ReplaceTextureReferences(avatarRoot, texture, finalized);
                assetSaver?.SaveAsset(finalized);
                count++;
            }
            if (count > 0)
                LogUtility.Info("YM Texture Finalizer", $"Compressed {count} referenced YM texture(s) for {target}.");
            return count;
        }

        private static void ReplaceTextureReferences(GameObject avatarRoot, Texture2D source, Texture2D replacement)
        {
            var materials = avatarRoot.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials ?? Array.Empty<Material>())
                .Where(material => material != null && material.shader != null)
                .Distinct();
            foreach (var material in materials)
            {
                var changed = false;
                var propertyCount = ShaderUtil.GetPropertyCount(material.shader);
                for (var i = 0; i < propertyCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(material.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                    var property = ShaderUtil.GetPropertyName(material.shader, i);
                    if (material.GetTexture(property) != source) continue;
                    material.SetTexture(property, replacement);
                    changed = true;
                }
                if (changed) EditorUtility.SetDirty(material);
            }
        }

        internal static Texture2D[] CollectReferencedTextures(GameObject avatarRoot)
        {
            var referenced = new HashSet<Texture2D>(
                EditorUtility.CollectDependencies(new UnityEngine.Object[] { avatarRoot }).OfType<Texture2D>());
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                var propertyCount = ShaderUtil.GetPropertyCount(material.shader);
                for (var i = 0; i < propertyCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(material.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                    if (material.GetTexture(ShaderUtil.GetPropertyName(material.shader, i)) is Texture2D texture)
                        referenced.Add(texture);
                }
            }
            return referenced.ToArray();
        }

        public static TextureFormat SelectFormat(YMGeneratedTextureInfo info, BuildTarget target)
        {
            var android = target == BuildTarget.Android;
            if (info.Usage == YMTextureUsage.SignedDistanceField)
                return android ? TextureFormat.ASTC_4x4 : TextureFormat.BC7;

            var inherited = android ? info.AndroidSettings : info.StandaloneSettings;
            if (inherited != null && inherited.overridden
                && TryMapImporterFormat(inherited.format, android, info.Usage, out var inheritedFormat))
                return inheritedFormat;

            if (android) return TextureFormat.ASTC_6x6;
            return info.Usage == YMTextureUsage.ColorOpaque ? TextureFormat.DXT1 : TextureFormat.BC7;
        }

        private static bool TryMapImporterFormat(TextureImporterFormat importerFormat, bool android,
            YMTextureUsage usage, out TextureFormat format)
        {
            format = default;
            if (!Enum.TryParse(importerFormat.ToString(), out TextureFormat parsed)) return false;
            var name = parsed.ToString();
            if (android)
            {
                if (!name.StartsWith("ASTC_", StringComparison.Ordinal)
                    && name != "ETC2_RGBA8" && name != "ETC2_RGB4") return false;
                if (usage != YMTextureUsage.ColorOpaque && name == "ETC2_RGB4") return false;
            }
            else
            {
                if (name != "BC7" && name != "DXT5" && name != "DXT1") return false;
                if (usage != YMTextureUsage.ColorOpaque && name == "DXT1") return false;
            }
            format = parsed;
            return true;
        }

        private static bool CanCompress(Texture2D texture, TextureFormat format)
        {
            var block = format == TextureFormat.ASTC_6x6 ? 6 : 4;
            return texture.width >= block && texture.height >= block;
        }

        private static TextureCompressionQuality ResolveQuality(YMGeneratedTextureInfo info, BuildTarget target)
        {
            if (info.CompressionQuality != YMTextureCompressionQuality.Normal)
                return TextureCompressionQuality.Best;
            var inherited = target == BuildTarget.Android ? info.AndroidSettings : info.StandaloneSettings;
            if (inherited == null || !inherited.overridden) return TextureCompressionQuality.Normal;
            if (inherited.compressionQuality >= 75) return TextureCompressionQuality.Best;
            if (inherited.compressionQuality <= 25) return TextureCompressionQuality.Fast;
            return TextureCompressionQuality.Normal;
        }
    }

    public static class YMBuildTargetUtility
    {
        public static BuildTarget Resolve(GameObject avatarRoot)
        {
            if (avatarRoot != null)
            {
                foreach (var component in avatarRoot.GetComponents<Component>())
                {
                    if (component == null || component.GetType().FullName != "KRT.VRCQuestTools.Components.PlatformTargetSettings") continue;
                    var type = component.GetType();
                    var value = type.GetField("buildTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(component)
                        ?? type.GetProperty("buildTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(component);
                    if (value?.ToString() == "Android") return BuildTarget.Android;
                    if (value?.ToString() == "PC") return BuildTarget.StandaloneWindows64;
                }
            }
            return EditorUserBuildSettings.activeBuildTarget;
        }
    }
}
