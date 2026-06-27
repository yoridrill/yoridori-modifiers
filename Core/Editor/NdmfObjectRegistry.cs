using System;
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
            return replacement;
        }
    }
}
