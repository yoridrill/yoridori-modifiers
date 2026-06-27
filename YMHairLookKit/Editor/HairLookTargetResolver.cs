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

        internal static Dictionary<Material, Material> ResolveMaterialCanonicalMap(
            IEnumerable<Material> configuredMaterials,
            IReadOnlyList<Material> currentMaterials)
        {
            var result = new Dictionary<Material, Material>();
            foreach (var material in configuredMaterials ?? Enumerable.Empty<Material>())
            {
                var resolvedMaterials = ResolveCurrentMaterialReferences(material, currentMaterials)
                    .Where(resolved => resolved != null)
                    .ToList();
                var canonical = resolvedMaterials.FirstOrDefault();
                if (canonical == null) continue;
                foreach (var resolved in resolvedMaterials)
                {
                    if (!result.ContainsKey(resolved)) result[resolved] = canonical;
                }
            }
            return result;
        }

        internal static Material ResolveCurrentMaterialReference(Material configuredMaterial, IReadOnlyList<Material> currentMaterials)
        {
            return ResolveCurrentMaterialReferences(configuredMaterial, currentMaterials).FirstOrDefault();
        }

        internal static IReadOnlyList<Material> ResolveCurrentMaterialReferences(Material configuredMaterial, IReadOnlyList<Material> currentMaterials)
        {
            if (configuredMaterial == null) return Array.Empty<Material>();
            if (currentMaterials == null || currentMaterials.Count == 0) return new[] { configuredMaterial };

            var configuredName = configuredMaterial.name;
            if (string.IsNullOrEmpty(configuredName))
            {
                return currentMaterials.Contains(configuredMaterial)
                    ? new[] { configuredMaterial }
                    : Array.Empty<Material>();
            }

            var exactMatches = currentMaterials
                .Where(material => material != null && material.name == configuredName)
                .Distinct()
                .ToList();
            if (exactMatches.Count > 0) return exactMatches;

            foreach (var generatedName in new[]
                     {
                         $"{configuredName}_lilToon",
                         $"{configuredName}_HairLookKit",
                         $"{configuredName}_Merged",
                     })
            {
                var generatedMatches = currentMaterials
                    .Where(material => material != null && material.name == generatedName)
                    .Distinct()
                    .ToList();
                if (generatedMatches.Count > 0) return generatedMatches;
            }

            var fallback = currentMaterials.FirstOrDefault(material =>
                material != null
                && (material.name.StartsWith($"{configuredName}_", StringComparison.Ordinal)
                    || material.name.StartsWith($"{configuredName} ", StringComparison.Ordinal)
                    || material.name.StartsWith($"{configuredName}(", StringComparison.Ordinal)));
            if (fallback != null) return new[] { fallback };
            return new[] { configuredMaterial };
        }

        internal static bool IsResolvedSupported(Material material, IReadOnlyList<Material> currentMaterials, bool allowMToonWithConverter)
        {
            var resolved = ResolveCurrentMaterialReferences(material, currentMaterials);
            return resolved.Count > 0 && resolved.All(candidate =>
                IsLilToonLike(candidate)
                || (allowMToonWithConverter && MToonDetector.IsMToonLike(candidate)));
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
