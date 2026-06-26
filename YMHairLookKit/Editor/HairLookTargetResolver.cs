using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YoridoriModifiers.Core.Editor;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.HairLookKit
{
    internal static class HairLookTargetResolver
    {
        internal static IReadOnlyList<Material> CollectCurrentMaterials(GameObject root)
        {
            if (root == null) return Array.Empty<Material>();
            return root.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer != null ? renderer.sharedMaterials : Array.Empty<Material>())
                .Where(material => material != null)
                .Distinct()
                .ToList();
        }

        internal static HashSet<Material> ResolveMaterialSet(IEnumerable<Material> configuredMaterials, IReadOnlyList<Material> currentMaterials)
        {
            var result = new HashSet<Material>();
            foreach (var material in configuredMaterials ?? Enumerable.Empty<Material>())
            {
                var resolved = ResolveCurrentMaterialReference(material, currentMaterials);
                if (resolved != null) result.Add(resolved);
            }
            return result;
        }

        internal static Material ResolveCurrentMaterialReference(Material configuredMaterial, IReadOnlyList<Material> currentMaterials)
        {
            if (configuredMaterial == null || currentMaterials == null) return configuredMaterial;
            if (currentMaterials.Count == 0 || currentMaterials.Contains(configuredMaterial)) return configuredMaterial;

            var configuredName = configuredMaterial.name;
            if (string.IsNullOrEmpty(configuredName)) return configuredMaterial;

            return currentMaterials.FirstOrDefault(material =>
                    material != null
                    && (material.name == configuredName
                        || material.name.StartsWith($"{configuredName}_", StringComparison.Ordinal)
                        || material.name.StartsWith($"{configuredName} ", StringComparison.Ordinal)
                        || material.name.StartsWith($"{configuredName}(", StringComparison.Ordinal)))
                ?? configuredMaterial;
        }

        internal static bool IsResolvedSupported(Material material, IReadOnlyList<Material> currentMaterials, bool allowMToonWithConverter)
        {
            var resolved = ResolveCurrentMaterialReference(material, currentMaterials);
            return IsLilToonLike(resolved)
                || (allowMToonWithConverter && MToonDetector.IsMToonLike(resolved));
        }

        internal static bool AreMergeTargetsSupported(YMHairLookKitComponent component, IReadOnlyList<Material> currentMaterials, bool allowMToonWithConverter)
        {
            if (component == null || !component.enableHairMerge || component.hairSelections == null) return false;
            var selected = component.hairSelections
                .Where(s => s != null && s.selected && s.material != null)
                .Select(s => s.material)
                .ToList();
            return selected.Count > 0 && selected.All(m => IsResolvedSupported(m, currentMaterials, allowMToonWithConverter));
        }

        internal static bool HasMToonToLilToonComponent(YMHairLookKitComponent component)
        {
            if (component == null) return false;
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            return root != null && root.GetComponentsInChildren<MToonToLilToonComponent>(true).Any(c => c != null);
        }

        internal static bool IsSelectedForMerge(YMHairLookKitComponent component, Material material)
        {
            if (component == null || material == null || !component.enableHairMerge || component.hairSelections == null) return false;
            return component.hairSelections.Any(s => s != null && s.selected && s.material == material);
        }

        internal static bool IsLilToonLike(Material material)
        {
            return material != null
                && material.shader != null
                && material.shader.name.IndexOf("liltoon", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
