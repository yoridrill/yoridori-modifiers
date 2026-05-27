using System;
using System.Linq;
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
        private const string DefaultFaceShadowMaskTextureGuid = "7fcb903d503482383c04f62fc730ef62";

        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/Long Sleeves", false, -900)]
        private static void CreateVRoidLongSleevesComponentsGameObject() => CreatePresetGameObject(SleevePreset.LongSleeves, false);

        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/Short Sleeves", false, -899)]
        private static void CreateVRoidShortSleevesComponentsGameObject() => CreatePresetGameObject(SleevePreset.ShortSleeves, false);

        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/Kimono", false, -898)]
        private static void CreateVRoidKimonoComponentsGameObject() => CreatePresetGameObject(SleevePreset.Kimono, false);

        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/(via VRM 0.0) Long Sleeves", false, -897)]
        private static void CreateVRoid00LongSleevesComponentsGameObject() => CreatePresetGameObject(SleevePreset.LongSleeves, true);

        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/(via VRM 0.0) Short Sleeves", false, -896)]
        private static void CreateVRoid00ShortSleevesComponentsGameObject() => CreatePresetGameObject(SleevePreset.ShortSleeves, true);

        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/(via VRM 0.0) Kimono", false, -895)]
        private static void CreateVRoid00KimonoComponentsGameObject() => CreatePresetGameObject(SleevePreset.Kimono, true);

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/Long Sleeves", false, -894)]
        private static void AddVRoidLongSleevesArmPatch() => AddArmPatchToRoot(SleevePreset.LongSleeves, false);

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/Short Sleeves", false, -893)]
        private static void AddVRoidShortSleevesArmPatch() => AddArmPatchToRoot(SleevePreset.ShortSleeves, false);

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/Kimono", false, -892)]
        private static void AddVRoidKimonoArmPatch() => AddArmPatchToRoot(SleevePreset.Kimono, false);

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Mesh Trimmer", false, -891)]
        private static void AddVRoidMeshTrimmer() => AddMeshTrimmersToRoot("Add YM Mesh Trimmer with VRoid Defaults");

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM MToon to lilToon", false, -890)]
        private static void AddVRoidMToonToLilToon() => AddMToonToLilToonToRoot("Add YM MToon to lilToon with VRoid Defaults");

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Eye Freeze", false, -889)]
        private static void AddVRoidEyeFreeze() => AddSingleComponentToRoot<YMEyeFreeze>("Add YM Eye Freeze with VRoid Defaults");

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Long Sleeves", false, -888)]
        private static void AddVRoid00LongSleevesArmPatch() => AddArmPatchToRoot(SleevePreset.LongSleeves, true);

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Short Sleeves", false, -887)]
        private static void AddVRoid00ShortSleevesArmPatch() => AddArmPatchToRoot(SleevePreset.ShortSleeves, true);

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Kimono", false, -886)]
        private static void AddVRoid00KimonoArmPatch() => AddArmPatchToRoot(SleevePreset.Kimono, true);

        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/Long Sleeves", true)]
        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/Short Sleeves", true)]
        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/Kimono", true)]
        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/(via VRM 0.0) Long Sleeves", true)]
        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/(via VRM 0.0) Short Sleeves", true)]
        [MenuItem(BaseMenu + "Create YM Components Object for VRoid/(via VRM 0.0) Kimono", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/Long Sleeves", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/Short Sleeves", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/Kimono", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Mesh Trimmer", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM MToon to lilToon", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Eye Freeze", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Long Sleeves", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Short Sleeves", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Kimono", true)]
        private static bool ValidatePresetMenu() => Selection.activeGameObject != null;

        private static void CreatePresetGameObject(SleevePreset preset, bool fromVrm00)
        {
            var root = Selection.activeGameObject;
            if (root == null) return;

            var undoName = BuildUndoName(preset, fromVrm00);
            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            var componentObject = new GameObject(BuildPresetGameObjectName(preset, fromVrm00));
            componentObject.name = GameObjectUtility.GetUniqueNameForSibling(root.transform, componentObject.name);
            Undo.RegisterCreatedObjectUndo(componentObject, undoName);
            Undo.SetTransformParent(componentObject.transform, root.transform, undoName);
            componentObject.transform.localPosition = Vector3.zero;
            componentObject.transform.localRotation = Quaternion.identity;
            componentObject.transform.localScale = Vector3.one;

            AddPresetComponents(componentObject, root, preset, fromVrm00);

            Selection.activeGameObject = componentObject;
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(root);
        }

        private static void AddPresetComponents(GameObject target, GameObject avatarRoot, SleevePreset preset, bool fromVrm00)
        {
            var armPatch = Undo.AddComponent<ArmPatchComponent>(target);
            ConfigureArmPatch(armPatch, avatarRoot, preset, fromVrm00);

            AddMeshTrimmerComponents(target);

            var mtoon = Undo.AddComponent<MToonToLilToonComponent>(target);
            ConfigureMToonToLilToon(mtoon, avatarRoot);
            Undo.AddComponent<YMEyeFreeze>(target);

            EditorUtility.SetDirty(target);
        }

        private static void AddArmPatchToRoot(SleevePreset preset, bool fromVrm00)
        {
            var root = Selection.activeGameObject;
            if (root == null) return;

            var undoName = BuildArmPatchUndoName(preset, fromVrm00);
            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            var armPatch = GetOrAddComponent<ArmPatchComponent>(root, undoName);
            ConfigureArmPatch(armPatch, root, preset, fromVrm00);

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(root);
        }

        private static void AddMeshTrimmersToRoot(string undoName)
        {
            var root = Selection.activeGameObject;
            if (root == null) return;

            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            AddMeshTrimmerComponents(root);

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(root);
        }

        private static void AddMToonToLilToonToRoot(string undoName)
        {
            var root = Selection.activeGameObject;
            if (root == null) return;

            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            var component = GetOrAddComponent<MToonToLilToonComponent>(root, undoName);
            ConfigureMToonToLilToon(component, root);

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(root);
        }

        private static void AddSingleComponentToRoot<T>(string undoName) where T : Component
        {
            var root = Selection.activeGameObject;
            if (root == null) return;

            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            GetOrAddComponent<T>(root, undoName);

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(root);
        }

        private static void AddMeshTrimmerComponents(GameObject target)
        {
            var mobileTrimmer = Undo.AddComponent<MeshTrimmerComponent>(target);
            ConfigureMobileMeshTrimmer(mobileTrimmer);

            var windowsTrimmer = Undo.AddComponent<MeshTrimmerComponent>(target);
            ConfigureWindowsMeshTrimmer(windowsTrimmer);
        }

        private static void ConfigureMToonToLilToon(MToonToLilToonComponent component, GameObject avatarRoot)
        {
            if (component == null) return;
            Undo.RecordObject(component, "Configure YM MToon to lilToon");
            component.enableFaceShadowTuning = true;
            var faceMaterial = DetectDefaultFaceMaterial(avatarRoot);
            component.faceShadowFaceMaterial = faceMaterial;
            component.fakeShadowFaceMaterial = faceMaterial;
            component.faceShadowSdfTexture = LoadDefaultFaceShadowMaskTexture();
            EditorUtility.SetDirty(component);
        }

        private static Material DetectDefaultFaceMaterial(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;

            var materials = avatarRoot.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer != null ? renderer.sharedMaterials : Array.Empty<Material>())
                .Where(material => material != null)
                .Distinct()
                .ToList();
            if (materials.Count == 0) return null;

            var face = materials.FirstOrDefault(m => m != null
                && m.name.IndexOf("FACE", StringComparison.OrdinalIgnoreCase) >= 0
                && m.name.IndexOf("SKIN", StringComparison.OrdinalIgnoreCase) >= 0);
            if (face != null) return face;

            face = materials.FirstOrDefault(m => m != null
                && (m.name.IndexOf("FACE", StringComparison.OrdinalIgnoreCase) >= 0
                    || m.name.IndexOf("顔", StringComparison.OrdinalIgnoreCase) >= 0));
            if (face != null) return face;

            return materials.FirstOrDefault();
        }

        private static Texture2D LoadDefaultFaceShadowMaskTexture()
        {
            var texturePath = AssetDatabase.GUIDToAssetPath(DefaultFaceShadowMaskTextureGuid);
            var texture = !string.IsNullOrEmpty(texturePath)
                ? AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath)
                : null;
            if (texture != null) return texture;

            var guids = AssetDatabase.FindAssets("VRoidFaceShadowFlat t:Texture2D");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null) return texture;
            }

            return null;
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

        private static string BuildArmPatchUndoName(SleevePreset preset, bool fromVrm00)
        {
            string suffix = preset switch
            {
                SleevePreset.LongSleeves => "Long Sleeves",
                SleevePreset.ShortSleeves => "Short Sleeves",
                SleevePreset.Kimono => "Kimono",
                _ => "Preset"
            };

            return fromVrm00
                ? $"Add YM Arm Patch with VRM 0.0 VRoid {suffix} Defaults"
                : $"Add YM Arm Patch with VRoid {suffix} Defaults";
        }

        private static string BuildPresetGameObjectName(SleevePreset preset, bool fromVrm00)
        {
            string suffix = preset switch
            {
                SleevePreset.LongSleeves => "Long Sleeves",
                SleevePreset.ShortSleeves => "Short Sleeves",
                SleevePreset.Kimono => "Kimono",
                _ => "Preset"
            };

            return fromVrm00
                ? $"Yoridori Modifiers VRoid 0.0 {suffix}"
                : $"Yoridori Modifiers VRoid {suffix}";
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
                SleevePreset.ShortSleeves => new Vector3(0f, 0f, -12f),
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
                component.forearmTwistBoneCount = ForearmTwistBoneCount.Count4;
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
