using System;
using System.Collections.Generic;
using System.Linq;
using lilToon;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.OutlineExtender
{
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
        public static void Synchronize(List<OutlineMaterialSelection> selections, IEnumerable<Material> materials)
        {
            var known = selections.Where(s => s != null && s.material != null).Select(s => s.material).ToHashSet();
            foreach (var material in materials.Where(m => m != null).Distinct())
                if (known.Add(material)) selections.Add(new OutlineMaterialSelection
                {
                    material = material, selected = AutoSelect(material)
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
