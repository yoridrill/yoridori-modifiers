using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.OutlineExtender
{
    // Resolve provenance once per processing pass instead of rebuilding both sets
    // for every material slot. Explicit OFF and unknown merge inputs stay distinct.
    internal sealed class OutlineMaterialSelectionSet
    {
        private readonly HashSet<ObjectReference> enabled = new();
        private readonly HashSet<ObjectReference> disabled = new();
        private readonly Dictionary<Material, (bool selected, bool mixed)> results = new();

        internal OutlineMaterialSelectionSet(IEnumerable<OutlineMaterialSelection> selections)
        {
            foreach (var selection in selections)
                if (selection != null && selection.material != null)
                    (selection.selected ? enabled : disabled)
                        .UnionWith(NdmfObjectRegistry.GetSourceReferences(selection.material));
        }

        internal bool IsSelected(Material material, out bool mixed)
        {
            if (material == null) { mixed = false; return false; }
            if (!results.TryGetValue(material, out var result))
            {
                var sources = NdmfObjectRegistry.GetControlSourceReferences(material);
                var any = sources.Overlaps(enabled);
                var conflict = any && (sources.Overlaps(disabled) || !sources.IsSubsetOf(enabled));
                results[material] = result = (any && !conflict, conflict);
            }
            mixed = result.mixed;
            return result.selected;
        }
    }
}
