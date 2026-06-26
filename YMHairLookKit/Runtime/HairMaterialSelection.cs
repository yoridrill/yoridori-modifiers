using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.HairLookKit
{
    [Serializable]
    public sealed class HairMaterialSelection
    {
        public Material material;
        public bool selected;
    }

    public static class HairMaterialSelector
    {
        public static List<HairMaterialSelection> BuildDefaultSelections(IEnumerable<Material> materials)
        {
            var distinctMaterials = materials
                .Where(m => m != null)
                .Distinct()
                .ToList();

            var nameMatched = FindNameMatchedHairMaterials(distinctMaterials);

            var transparentCount = nameMatched.Count(m => RenderTypeResolver.ResolveFromMaterial(m) == RenderType.Transparent);
            var nonTransparentCount = nameMatched.Count - transparentCount;
            var transparentDominant = transparentCount > nonTransparentCount;
            var dominantCullMode = CullModeResolver.ResolveMergeCullMode(nameMatched);

            return distinctMaterials
                .Select(m => new HairMaterialSelection
                {
                    material = m,
                    selected = nameMatched.Contains(m)
                        && IsInDominantRenderGroup(m, transparentDominant)
                        && CullModeResolver.ResolveFromMaterial(m) == dominantCullMode,
                })
                .ToList();
        }

        private static List<Material> FindNameMatchedHairMaterials(IEnumerable<Material> materials)
        {
            var materialList = materials
                .Where(m => m != null)
                .ToList();

            var underscoreMatched = materialList
                .Where(m => m.name.IndexOf("HAIR_", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            if (underscoreMatched.Count > 0) return underscoreMatched;

            return materialList
                .Where(m => m.name.IndexOf("HAIR", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        private static bool IsInDominantRenderGroup(Material material, bool transparentDominant)
        {
            if (material == null) return false;
            var renderType = RenderTypeResolver.ResolveFromMaterial(material);
            return transparentDominant
                ? renderType == RenderType.Transparent
                : renderType != RenderType.Transparent;
        }
    }
}
