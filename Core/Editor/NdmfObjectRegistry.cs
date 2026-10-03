using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using nadena.dev.ndmf;
using UnityEngine;
using Object = UnityEngine.Object;

namespace YoridoriModifiers.Core.Editor
{
    /// <summary>
    /// Creates build-time replacements while keeping NDMF's object provenance intact.
    /// Use these methods instead of creating a clone and registering it as a separate step.
    /// </summary>
    public static class NdmfObjectRegistry
    {
        // ObjectRegistry tracks a single original. Preserve all inputs of material merges
        // for downstream modifiers, without keeping assets alive between builds/previews.
        private static readonly ConditionalWeakTable<Object, HashSet<ObjectReference>> Sources = new();
        private static readonly ConditionalWeakTable<Object, HashSet<ObjectReference>> ControlSources = new();

        // Some intentional merges expose only their representative's controls.
        // Keep this separate from provenance, which still contains every input.
        public static void RegisterControlSource(Object replacement, Object representative)
        {
            ControlSources.Remove(replacement);
            ControlSources.Add(replacement, GetControlSourceReferences(representative));
        }

        public static HashSet<ObjectReference> GetControlSourceReferences(Object obj) => obj != null
            && ControlSources.TryGetValue(obj, out var sources)
                ? new HashSet<ObjectReference>(sources) : GetSourceReferences(obj);

        public static HashSet<ObjectReference> GetSourceReferences(Object obj)
        {
            if (obj == null) return new HashSet<ObjectReference>();
            return Sources.TryGetValue(obj, out var sources)
                ? new HashSet<ObjectReference>(sources)
                : new HashSet<ObjectReference> { ObjectRegistry.GetReference(obj) };
        }

        public static void RegisterMergedSources(Object replacement, IEnumerable<Material> originals)
        {
            var sources = GetSourceReferences(replacement);
            foreach (var original in originals) sources.UnionWith(GetSourceReferences(original));
            Sources.Remove(replacement);
            Sources.Add(replacement, sources);
        }

        public static T Clone<T>(T original) where T : Object
        {
            if (original == null) throw new ArgumentNullException(nameof(original));

            return CreateReplacement(original, () => Object.Instantiate(original));
        }

        public static T CreateReplacement<T>(Object original, Func<T> createReplacement) where T : Object
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (createReplacement == null) throw new ArgumentNullException(nameof(createReplacement));

            var replacement = createReplacement();
            return RegisterReplacement(original, replacement);
        }

        public static T RegisterReplacement<T>(Object original, T replacement) where T : Object
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (replacement == null) throw new InvalidOperationException("A replacement factory returned null.");

            ObjectRegistry.RegisterReplacedObject(original, replacement);
            Sources.Remove(replacement);
            Sources.Add(replacement, GetSourceReferences(original));
            ControlSources.Remove(replacement);
            if (ControlSources.TryGetValue(original, out var controls))
                ControlSources.Add(replacement, new HashSet<ObjectReference>(controls));
            return replacement;
        }
    }
}
