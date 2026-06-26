using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.HairLookKit
{
    internal static class YMHairLookKitPresetMenu
    {
        private const string MenuPath = "GameObject/Yoridori Modifiers/Add Component with VRoid Defaults/YM Hair Look Kit";

        [MenuItem(MenuPath, false, -888)]
        private static void AddVRoidHairLookKit()
        {
            var target = Selection.activeGameObject;
            if (target == null) return;
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(target) ?? target;

            Undo.SetCurrentGroupName("Add YM Hair Look Kit with VRoid Defaults");
            var undoGroup = Undo.GetCurrentGroup();

            var component = target.GetComponent<YMHairLookKitComponent>();
            if (component == null)
            {
                component = Undo.AddComponent<YMHairLookKitComponent>(target);
            }

            YMHairLookKitDefaults.ApplyVRoidDefaults(component, avatarRoot);
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(target);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateAddVRoidHairLookKit()
        {
            return Selection.activeGameObject != null;
        }
    }
}
