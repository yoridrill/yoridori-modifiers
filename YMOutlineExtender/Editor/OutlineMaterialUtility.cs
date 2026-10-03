using System;
using System.Collections.Generic;
using System.Linq;
using lilToon;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.OutlineExtender
{
    internal readonly struct OutlineSettings
    {
        internal readonly Color Color;
        internal readonly float ObjectSpaceWidth;
        internal readonly bool HasOutline;

        internal OutlineSettings(Color color, float objectSpaceWidth, bool hasOutline)
        {
            Color = color;
            ObjectSpaceWidth = objectSpaceWidth;
            HasOutline = hasOutline;
        }
    }

    internal readonly struct OutlineBakeMaterialInfo
    {
        internal readonly string MainTextureProperty;
        internal readonly Texture2D MainTexture;
        internal readonly float TextureCutoff;
        internal readonly OutlineSettings Outline;
        internal readonly Vector2 TextureScale;
        internal readonly Vector2 TextureOffset;

        internal OutlineBakeMaterialInfo(string property, Texture2D texture, float cutoff, OutlineSettings outline,
            Vector2 textureScale, Vector2 textureOffset)
        {
            MainTextureProperty = property;
            MainTexture = texture;
            TextureCutoff = cutoff;
            Outline = outline;
            TextureScale = textureScale;
            TextureOffset = textureOffset;
        }
    }

    internal readonly struct OutlineBoundaryMaterialInfo
    {
        internal readonly Texture2D MainTexture;
        internal readonly float TextureCutoff;
        internal readonly Vector2 TextureScale;
        internal readonly Vector2 TextureOffset;
        internal readonly OutlineSettings Outline;
        internal readonly bool TestAlpha;

        internal OutlineBoundaryMaterialInfo(Texture2D texture, float cutoff, Vector2 scale,
            Vector2 offset, OutlineSettings outline, bool testAlpha)
        {
            MainTexture = texture; TextureCutoff = cutoff; TextureScale = scale;
            TextureOffset = offset; Outline = outline; TestAlpha = testAlpha;
        }
    }

    internal static class OutlineMaterialUtility
    {
        public const string ShaderName = "Hidden/yoridrill/lilToonOutlineExtender/CutoutOutline";

        public static bool IsOutlineExtenderShader(Shader shader) => shader != null
            && shader.name == ShaderName;

        public static List<Material> CollectMaterials(GameObject root) => root == null
            ? new List<Material>()
            : root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null).Distinct().ToList();

        public static bool IsLilToonCutout(Material material)
        {
            if (!lilMaterialUtils.CheckShaderIslilToon(material)) return false;
            var shaderName = material.shader.name;
            if (lilShaderUtils.IsMultiShaderName(shaderName))
                return material.HasProperty("_TransparentMode") && material.GetFloat("_TransparentMode") == 1;
            return lilShaderUtils.IsCutoutShaderName(shaderName);
        }

        public static bool AutoSelect(Material material)
        {
            if (material == null) return false;
            if (MToonDetector.IsMToonLike(material))
                return RenderTypeResolver.ResolveFromMaterial(material) == RenderType.Cutout
                    && MToonToLilToonMapper.HasOutline(material);
            return IsLilToonCutout(material) && Float(material, "_OutlineWidth") > 0;
        }

        // Add only previously unseen materials. An explicit OFF is as persistent as ON.
        public static void Synchronize(List<OutlineMaterialSelection> selections, IEnumerable<Material> materials,
            bool autoSelectMToon) => AddMissingSelections(selections, materials,
                material => AutoSelect(material) && (!MToonDetector.IsMToonLike(material) || autoSelectMToon));

        // Draw and Fold are opt-in; automatic selection belongs to the Shader mode.
        public static void SynchronizeManualSelections(List<OutlineMaterialSelection> selections,
            IEnumerable<Material> materials) => AddMissingSelections(selections, materials, _ => false);

        private static void AddMissingSelections(List<OutlineMaterialSelection> selections,
            IEnumerable<Material> materials, Func<Material, bool> initiallySelected)
        {
            var known = selections.Where(s => s != null && s.material != null).Select(s => s.material).ToHashSet();
            foreach (var material in materials)
                if (material != null && known.Add(material)) selections.Add(new OutlineMaterialSelection
                {
                    material = material,
                    selected = initiallySelected(material)
                });
        }

        public static float Float(Material m, string name) => m.HasProperty(name) ? m.GetFloat(name) : 0;

        public static bool TryValidate(Material m, out string reason, bool requireEnabledOutline = true)
        {
            reason = null;
            if (!IsLilToonCutout(m)) reason = "Final material must be a lilToon Cutout material.";
            else if (requireEnabledOutline && !HasEnabledOutline(m)) reason = "Final material must have an enabled lilToon Outline with positive width.";
            else
            {
                var name = m.shader.name;
                // Replacing another custom shader would silently discard its custom behavior.
                var path = AssetDatabase.GetAssetPath(m.shader);
                if (!path.StartsWith("Packages/jp.lilxyzw.liltoon/", StringComparison.Ordinal)
                    && !path.StartsWith("Assets/lilToon/", StringComparison.Ordinal))
                    reason = "Third-party custom lilToon shaders are not supported.";
                else if (lilShaderUtils.IsLiteShaderName(name) || lilShaderUtils.IsFurShaderName(name)
                    || lilShaderUtils.IsTessellationShaderName(name) || lilShaderUtils.IsOptionalShaderName(name))
                    reason = "Lite, Fur, Tessellation and Optional variants are not supported.";
                else if (!(m.GetTexture("_MainTex") is Texture2D))
                    reason = "Main Texture must be a Texture2D.";
                else if (Float(m, "_AlphaMaskMode") != 0
                    || (Float(m, "_UseMain2ndTex") != 0 && Float(m, "_Main2ndTexAlphaMode") != 0)
                    || (Float(m, "_UseMain3rdTex") != 0 && Float(m, "_Main3rdTexAlphaMode") != 0)
                    || m.GetVector("_DissolveParams").x != 0 || Float(m, "_UseDither") != 0)
                    reason = "Alpha Mask, layer alpha, Dissolve and Dither are not supported.";
                else if (Float(m, "_UseParallax") != 0 || Float(m, "_UDIMDiscardCompile") != 0
                    || Float(m, "_AudioLink2Main2nd") != 0 || Float(m, "_AudioLink2Main3rd") != 0)
                    reason = "Parallax, UDIM discard and AudioLink layer alpha are not supported.";
                else if (m.GetColor("_Color").a <= 0 || Float(m, "_Cutoff") <= 0
                    || Float(m, "_Cutoff") >= m.GetColor("_Color").a)
                    reason = "Cutoff must be strictly between zero and Main Color alpha.";
                else
                {
                    var texture = m.GetTexture("_MainTex");
                    if (!SupportedWrap(texture.wrapModeU) || !SupportedWrap(texture.wrapModeV))
                        reason = "Only Repeat and Clamp texture wrapping are supported.";
                    else if (texture.filterMode == FilterMode.Point)
                        reason = "Point-filtered textures are not supported (SDF requires interpolation).";
                }
            }
            return reason == null;
        }

        public static bool TryGetBakeMaterialInfo(Material material, out OutlineBakeMaterialInfo info,
            out string reason, bool requireEnabledOutline = true)
        {
            info = default;
            reason = null;
            if (material == null || material.shader == null)
            {
                reason = "Material or Shader is missing.";
                return false;
            }

            var isMToon = MToonDetector.IsMToonLike(material);
            var isLilToon = lilMaterialUtils.CheckShaderIslilToon(material);
            if (isMToon)
            {
                if (RenderTypeResolver.ResolveFromMaterial(material) != RenderType.Cutout)
                    reason = "Material must use Cutout / MASK rendering.";
            }
            else if (isLilToon)
            {
                if (!IsLilToonCutout(material)) reason = "Material must use Cutout / MASK rendering.";
                else if (!ValidateLilToonAlpha(material, out reason)) { }
            }
            else if (!IsGenericCutout(material))
            {
                reason = "Unsupported shaders must expose a normal Cutout Main Texture and cutoff property.";
            }
            if (reason != null) return false;

            var mainProperty = ResolveMainTextureProperty(material);
            if (mainProperty == null || !(material.GetTexture(mainProperty) is Texture2D texture))
            {
                reason = "Main Texture must be a Texture2D.";
                return false;
            }
            if (!SupportedWrap(texture.wrapModeU) || !SupportedWrap(texture.wrapModeV))
            {
                reason = "Only Repeat and Clamp texture wrapping are supported.";
                return false;
            }

            var cutoff = ResolveTextureCutoff(material);
            if (!(cutoff > 0f && cutoff < 1f))
            {
                reason = "The effective texture cutoff must be strictly between zero and one.";
                return false;
            }

            var outline = ResolveOutlineSettings(material, isMToon);
            if (requireEnabledOutline && !outline.HasOutline)
            {
                reason = "Material must have an enabled Outline with positive width.";
                return false;
            }
            info = new OutlineBakeMaterialInfo(mainProperty, texture, cutoff, outline,
                material.GetTextureScale(mainProperty), material.GetTextureOffset(mainProperty));
            return true;
        }

        public static bool TryGetBoundaryMaterialInfo(Material material,
            out OutlineBoundaryMaterialInfo info, out string reason)
        {
            info = default;
            reason = null;
            if (material == null || material.shader == null)
            {
                reason = "Material or Shader is missing.";
                return false;
            }
            var isMToon = MToonDetector.IsMToonLike(material);
            var outline = ResolveOutlineSettings(material, isMToon);
            if (!outline.HasOutline)
            {
                reason = "Material must have an enabled Outline with positive width.";
                return false;
            }

            var isCutout = isMToon
                ? RenderTypeResolver.ResolveFromMaterial(material) == RenderType.Cutout
                : lilMaterialUtils.CheckShaderIslilToon(material)
                    ? IsLilToonCutout(material)
                    : IsGenericCutout(material);
            var property = ResolveMainTextureProperty(material);
            var texture = property != null ? material.GetTexture(property) as Texture2D : null;
            var canTestAlpha = isCutout && texture != null
                && SupportedWrap(texture.wrapModeU) && SupportedWrap(texture.wrapModeV);
            info = new OutlineBoundaryMaterialInfo(texture,
                canTestAlpha ? ResolveTextureCutoff(material) : 0f,
                property != null ? material.GetTextureScale(property) : Vector2.one,
                property != null ? material.GetTextureOffset(property) : Vector2.zero,
                outline, canTestAlpha);
            return true;
        }

        private static OutlineSettings ResolveOutlineSettings(Material material, bool isMToon)
        {
            if (isMToon)
            {
                var width = MToonToLilToonMapper.ResolveOutlineWidthInMeters(material);
                return new OutlineSettings(MToonToLilToonMapper.ResolveOutlineColor(material), width, width > 0f);
            }
            var widthValue = Float(material, "_OutlineWidth");
            var enabled = widthValue > 0f;
            if (lilMaterialUtils.CheckShaderIslilToon(material)) enabled &= HasEnabledOutline(material);
            else if (material.HasProperty("_UseOutline")) enabled &= Float(material, "_UseOutline") > 0f;
            var color = material.HasProperty("_OutlineColor") ? material.GetColor("_OutlineColor") : Color.black;
            return new OutlineSettings(color, Mathf.Max(0f, widthValue) * 0.01f, enabled);
        }

        internal static string ResolveMainTextureProperty(Material material)
        {
            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null) return "_BaseMap";
            if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null) return "_MainTex";
            return null;
        }

        private static float ResolveTextureCutoff(Material material)
        {
            var cutoff = material.HasProperty("_AlphaCutoff")
                ? material.GetFloat("_AlphaCutoff")
                : material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;
            var alpha = 1f;
            if (material.HasProperty("_BaseColorFactor")) alpha = material.GetColor("_BaseColorFactor").a;
            else if (material.HasProperty("_Color")) alpha = material.GetColor("_Color").a;
            return cutoff / Mathf.Max(alpha, 1e-6f);
        }

        private static bool IsGenericCutout(Material material)
        {
            if (!material.HasProperty("_Cutoff") && !material.HasProperty("_AlphaCutoff")) return false;
            var tag = material.GetTag("RenderType", false, string.Empty);
            return tag == "TransparentCutout" || material.IsKeywordEnabled("_ALPHATEST_ON")
                || (material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.AlphaTest
                    && material.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent);
        }

        private static bool ValidateLilToonAlpha(Material material, out string reason)
        {
            reason = null;
            if (Float(material, "_AlphaMaskMode") != 0
                || (Float(material, "_UseMain2ndTex") != 0 && Float(material, "_Main2ndTexAlphaMode") != 0)
                || (Float(material, "_UseMain3rdTex") != 0 && Float(material, "_Main3rdTexAlphaMode") != 0)
                || material.GetVector("_DissolveParams").x != 0 || Float(material, "_UseDither") != 0)
                reason = "Alpha Mask, layer alpha, Dissolve and Dither are not supported.";
            else if (Float(material, "_UseParallax") != 0 || Float(material, "_UDIMDiscardCompile") != 0
                || Float(material, "_AudioLink2Main2nd") != 0 || Float(material, "_AudioLink2Main3rd") != 0)
                reason = "Parallax, UDIM discard and AudioLink layer alpha are not supported.";
            return reason == null;
        }

        public static string LocalizeReason(string reason, bool english)
        {
            if (english) return reason;
            return reason switch
            {
                "Final material must be a lilToon Cutout material." => "lilToonのCutoutマテリアルを指定してください。",
                "Final material must have an enabled lilToon Outline with positive width." => "最終的なlilToonマテリアルでOutlineを有効にし、Outline Widthを0より大きくしてください。",
                "Third-party custom lilToon shaders are not supported." => "他のカスタムlilToonシェーダーには対応していません。",
                "Lite, Fur, Tessellation and Optional variants are not supported." => "Lite・Fur・Tessellation・Optionalには対応していません。",
                "Main Texture must be a Texture2D." => "Main TextureにTexture2Dを指定してください。",
                "Alpha Mask, layer alpha, Dissolve and Dither are not supported." => "Alpha Mask・レイヤーのAlpha変更・Dissolve・Ditherには対応していません。",
                "Parallax, UDIM discard and AudioLink layer alpha are not supported." => "Parallax・UDIM Discard・AudioLinkのレイヤー変更には対応していません。",
                "Cutoff must be strictly between zero and Main Color alpha." => "Cutoffを0より大きく、Main ColorのAlphaより小さく設定してください。",
                "Only Repeat and Clamp texture wrapping are supported." => "TextureのWrap ModeをRepeatまたはClampに設定してください。",
                "Point-filtered textures are not supported (SDF requires interpolation)." => "TextureのFilter ModeをBilinearまたはTrilinearに設定してください。",
                "Material or Shader is missing." => "MaterialまたはShaderが見つかりません。",
                "Material must use Cutout / MASK rendering." => "Cutout / MASKのMaterialを指定してください。",
                "Unsupported shaders must expose a normal Cutout Main Texture and cutoff property." => "未対応Shaderでは通常のCutout用Main TextureとCutoffプロパティが必要です。",
                "The effective texture cutoff must be strictly between zero and one." => "実効Cutoffを0より大きく1より小さく設定してください。",
                "Material must have an enabled Outline with positive width." => "Outlineを有効にし、幅を0より大きく設定してください。",
                _ => reason
            };
        }

        private static bool HasEnabledOutline(Material material)
        {
            if (Float(material, "_OutlineWidth") <= 0f) return false;
            var shaderName = material.shader.name;
            return lilShaderUtils.IsMultiShaderName(shaderName)
                ? Float(material, "_UseOutline") > 0f
                : lilShaderUtils.IsOutlineShaderName(shaderName);
        }

        private static bool SupportedWrap(TextureWrapMode mode) => mode == TextureWrapMode.Repeat || mode == TextureWrapMode.Clamp;
    }
}
