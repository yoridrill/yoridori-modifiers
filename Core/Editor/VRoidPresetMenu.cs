using System;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.ArmPatch;
using YoridoriModifiers.EyeFreeze;
using YoridoriModifiers.MeshTrimmer;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.Core.Editor
{
    internal static class VRoidPresetMenu
    {
        private enum SleevePreset
        {
            LongSleeves,
            ShortSleeves,
            Kimono
        }

        private const string BaseMenu = "GameObject/Yoridori Modifiers/";

        [MenuItem(BaseMenu + "Add VRoid Preset/Long Sleeves", false, -900)]
        private static void AddVRoidLongSleevesPreset() => AddPreset(SleevePreset.LongSleeves, false);

        [MenuItem(BaseMenu + "Add VRoid Preset/Short Sleeves", false, -899)]
        private static void AddVRoidShortSleevesPreset() => AddPreset(SleevePreset.ShortSleeves, false);

        [MenuItem(BaseMenu + "Add VRoid Preset/Kimono", false, -898)]
        private static void AddVRoidKimonoPreset() => AddPreset(SleevePreset.Kimono, false);

        [MenuItem(BaseMenu + "Add VRoid Preset from VRM 0.0/Long Sleeves", false, -897)]
        private static void AddVRoid00LongSleevesPreset() => AddPreset(SleevePreset.LongSleeves, true);

        [MenuItem(BaseMenu + "Add VRoid Preset from VRM 0.0/Short Sleeves", false, -896)]
        private static void AddVRoid00ShortSleevesPreset() => AddPreset(SleevePreset.ShortSleeves, true);

        [MenuItem(BaseMenu + "Add VRoid Preset from VRM 0.0/Kimono", false, -895)]
        private static void AddVRoid00KimonoPreset() => AddPreset(SleevePreset.Kimono, true);

        [MenuItem(BaseMenu + "Add VRoid Preset/Long Sleeves", true)]
        [MenuItem(BaseMenu + "Add VRoid Preset/Short Sleeves", true)]
        [MenuItem(BaseMenu + "Add VRoid Preset/Kimono", true)]
        [MenuItem(BaseMenu + "Add VRoid Preset from VRM 0.0/Long Sleeves", true)]
        [MenuItem(BaseMenu + "Add VRoid Preset from VRM 0.0/Short Sleeves", true)]
        [MenuItem(BaseMenu + "Add VRoid Preset from VRM 0.0/Kimono", true)]
        private static bool ValidatePresetMenu() => Selection.activeGameObject != null;

        private static void AddPreset(SleevePreset preset, bool fromVrm00)
        {
            var root = Selection.activeGameObject;
            if (root == null) return;

            var undoName = BuildUndoName(preset, fromVrm00);
            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            var armPatch = GetOrAddComponent<ArmPatchComponent>(root, undoName);
            ConfigureArmPatch(armPatch, root, preset, fromVrm00);

            var mobileTrimmer = Undo.AddComponent<MeshTrimmerComponent>(root);
            ConfigureMobileMeshTrimmer(mobileTrimmer);

            var windowsTrimmer = Undo.AddComponent<MeshTrimmerComponent>(root);
            ConfigureWindowsMeshTrimmer(windowsTrimmer);

            GetOrAddComponent<MToonToLilToonComponent>(root, undoName);
            GetOrAddComponent<YMEyeFreeze>(root, undoName);

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(root);
        }

        private static T GetOrAddComponent<T>(GameObject root, string undoName) where T : Component
        {
            var component = root.GetComponent<T>();
            if (component != null)
            {
                Undo.RecordObject(component, undoName);
                return component;
            }

            return Undo.AddComponent<T>(root);
        }

        private static string BuildUndoName(SleevePreset preset, bool fromVrm00)
        {
            string suffix = preset switch
            {
                SleevePreset.LongSleeves => "Long Sleeves",
                SleevePreset.ShortSleeves => "Short Sleeves",
                SleevePreset.Kimono => "Kimono",
                _ => "Preset"
            };

            return fromVrm00
                ? $"Add YM VRoid 0.0 {suffix} Preset"
                : $"Add YM VRoid {suffix} Preset";
        }

        private static void ConfigureArmPatch(ArmPatchComponent component, GameObject root, SleevePreset preset, bool fromVrm00)
        {
            Undo.RecordObject(component, BuildUndoName(preset, fromVrm00));

            component.enableShoulderFix = true;
            component.enableForearmFix = true;
            component.enableThumbFix = true;

            component.shoulderPositionOffset = Vector3.zero;
            component.shoulderEulerOffset = preset switch
            {
                SleevePreset.LongSleeves => new Vector3(0f, 0f, -10f),
                SleevePreset.ShortSleeves => new Vector3(0f, 0f, -13f),
                SleevePreset.Kimono => new Vector3(0f, 0f, -5f),
                _ => Vector3.zero
            };

            component.forearmThicknessRootScale = 1f;
            component.forearmThicknessTipScale = 1f;
            component.forearmWidthRootScale = 1f;
            component.forearmWidthTipScale = 1f;
            component.forearmRootRollOffset = 0f;
            component.forearmTwistBoneCount = ForearmTwistBoneCount.Count0;
            component.forearmTwistBoneType = ForearmTwistBoneType.None;
            component.forearmSkinMaterialName = "Auto";

            if (preset == SleevePreset.ShortSleeves)
            {
                component.forearmTwistBoneCount = ForearmTwistBoneCount.Count8;
                component.forearmTwistBoneType = ForearmTwistBoneType.AllTwist;
                component.forearmThicknessRootScale = 0.8f;
                component.forearmThicknessTipScale = 1.1f;
                component.forearmWidthRootScale = 0.95f;
                component.forearmWidthTipScale = 0.9f;
            }
            else if (preset == SleevePreset.Kimono)
            {
                var twistTarget = FindBodySkinMaterialName(root);
                component.forearmTwistBoneCount = ForearmTwistBoneCount.Count8;
                component.forearmSkinMaterialName = string.IsNullOrEmpty(twistTarget) ? "Auto" : twistTarget;
                component.forearmTwistBoneType = string.IsNullOrEmpty(twistTarget)
                    ? ForearmTwistBoneType.AllTwist
                    : ForearmTwistBoneType.SkinOnly;
                component.forearmThicknessTipScale = 1.1f;
                component.forearmWidthTipScale = 0.9f;
                component.forearmRootRollOffset = -70f;
            }

            component.thumbEulerOffset = fromVrm00
                ? new Vector3(0f, -15f, -15f)
                : new Vector3(10f, 0f, 20f);

            EditorUtility.SetDirty(component);
        }

        private static string FindBodySkinMaterialName(GameObject root)
        {
            if (root == null) return null;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || string.IsNullOrEmpty(material.name)) continue;
                    if (material.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (material.name.IndexOf("skin", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    return material.name;
                }
            }

            return null;
        }

        private static void ConfigureMobileMeshTrimmer(MeshTrimmerComponent component)
        {
            component.enableForWindows = false;
            component.enableForAndroid = true;
            component.enableForiOS = true;
            EditorUtility.SetDirty(component);
        }

        private static void ConfigureWindowsMeshTrimmer(MeshTrimmerComponent component)
        {
            component.enableForWindows = true;
            component.enableForAndroid = false;
            component.enableForiOS = false;
            component.maskDilatePixels = 12;
            component.alphaThreshold = 0.01f;
            EditorUtility.SetDirty(component);
        }
    }
}
