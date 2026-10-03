using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using YoridoriModifiers.Core.Editor;
using YoridoriModifiers.HairLookKit;

namespace YoridoriModifiers.OutlineExtender
{
    // The merged hair material has provenance from every input. Resolve all those
    // inputs to the representative's controls so a disabled row cannot cause an
    // ON/OFF conflict in downstream processing. Original selections stay intact.
    internal sealed class OutlineHairMaterialControls
    {
        private readonly Dictionary<Material, Material> representatives = new();

        internal static OutlineHairMaterialControls Create(GameObject root)
        {
            var result = new OutlineHairMaterialControls();
            if (root == null) return result;
            var hair = root.GetComponentsInChildren<YMHairLookKitComponent>(true)
                .OrderBy(c => PreviewCoordinator.GetDepthFromRoot(c.transform, root.transform)).FirstOrDefault();
            if (hair == null || !hair.enabled || !hair.enableHairMerge) return result;
            var currentSources = new HashSet<ObjectReference>();
            foreach (var material in OutlineMaterialUtility.CollectMaterials(root))
                currentSources.UnionWith(NdmfObjectRegistry.GetSourceReferences(material));
            var candidates = hair.hairSelections.Where(s => s != null && s.selected && s.material != null
                && currentSources.Overlaps(NdmfObjectRegistry.GetSourceReferences(s.material)))
                .Select(s => s.material).Distinct().ToList();
            if (candidates.Count == 0) return result;
            var representative = candidates.Contains(hair.representativeHairMaterialOverride)
                ? hair.representativeHairMaterialOverride : candidates[0];
            foreach (var material in candidates) result.representatives[material] = representative;
            return result;
        }

        internal Material Representative(Material material) => material != null
            && representatives.TryGetValue(material, out var representative) ? representative : material;
        internal bool IsSecondary(Material material) => Representative(material) != material;

        internal OutlineMaterialSelectionSet CreateSelectionSet(IEnumerable<OutlineMaterialSelection> selections) =>
            new(Resolve(selections));

        internal List<OutlineMaterialSelection> Resolve(IEnumerable<OutlineMaterialSelection> selections)
        {
            var entries = selections.Where(s => s != null && s.material != null).ToList();
            var states = new Dictionary<Material, bool>();
            foreach (var entry in entries) states.TryAdd(entry.material, entry.selected);
            var result = new List<OutlineMaterialSelection>(entries.Count);
            foreach (var entry in entries)
            {
                var representative = Representative(entry.material);
                result.Add(new OutlineMaterialSelection { material = entry.material,
                    selected = states.TryGetValue(representative, out var selected) && selected });
            }
            return result;
        }
    }
}
