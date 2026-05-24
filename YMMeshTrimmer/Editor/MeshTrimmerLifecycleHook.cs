using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YoridoriModifiers.MeshTrimmer
{
[InitializeOnLoad]
public static class MeshTrimmerLifecycleHook
{
    private static readonly Dictionary<int, int> QueuedUndoGroups = new Dictionary<int, int>();

    static MeshTrimmerLifecycleHook()
    {
        ObjectFactory.componentWasAdded += OnComponentWasAdded;
        Undo.postprocessModifications += OnPostprocessModifications;
    }

    private static void OnComponentWasAdded(Component component)
    {
        var trimmer = component as MeshTrimmerComponent;
        if (trimmer == null) return;
        QueueDetect(trimmer);
    }

    private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
    {
        for (int i = 0; i < modifications.Length; i++)
        {
            var target = modifications[i].currentValue.target as MeshTrimmerComponent;
            if (target == null) continue;
            if (target.targets == null || target.targets.Count == 0)
            {
                QueueDetect(target);
            }
        }
        return modifications;
    }

    private static void QueueDetect(MeshTrimmerComponent trimmer)
    {
        if (trimmer == null) return;
        int id = trimmer.GetInstanceID();
        int undoGroup = Undo.GetCurrentGroup();
        if (QueuedUndoGroups.ContainsKey(id))
        {
            QueuedUndoGroups[id] = Mathf.Min(QueuedUndoGroups[id], undoGroup);
            return;
        }
        QueuedUndoGroups[id] = undoGroup;

        EditorApplication.delayCall += () =>
        {
            if (!QueuedUndoGroups.TryGetValue(id, out var queuedUndoGroup)) return;
            QueuedUndoGroups.Remove(id);
            if (trimmer == null) return;
            MeshTrimmerComponentEditor.EnsureAutoDetectedTargets(trimmer, true);
            EditorUtility.SetDirty(trimmer);
            Undo.CollapseUndoOperations(queuedUndoGroup);
        };
    }
}

}
