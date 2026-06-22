using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.ArmPatch;
using YoridoriModifiers.EyeFreeze;
using YoridoriModifiers.FacialMapper;
using YoridoriModifiers.MeshTrimmer;
using YoridoriModifiers.MToonToLilToon;
using YoridoriModifiers.VRoidSkirtRefine;

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

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM VRoid Skirt Refine", false, -891)]
        private static void AddVRoidSkirtRefine() => AddSingleComponentToRoot<YMVRoidSkirtRefine>("Add YM VRoid Skirt Refine with VRoid Defaults");

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Mesh Trimmer", false, -890)]
        private static void AddVRoidMeshTrimmer() => AddMeshTrimmersToRoot("Add YM Mesh Trimmer with VRoid Defaults");

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM MToon to lilToon", false, -889)]
        private static void AddVRoidMToonToLilToon() => AddMToonToLilToonToRoot("Add YM MToon to lilToon with VRoid Defaults");

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Eye Freeze", false, -888)]
        private static void AddVRoidEyeFreeze() => AddSingleComponentToRoot<YMEyeFreeze>("Add YM Eye Freeze with VRoid Defaults");

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Facial Mapper", false, -887)]
        private static void AddVRoidFacialMapper() => AddFacialMapperToRoot("Add YM Facial Mapper with VRoid Defaults");

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Long Sleeves", false, -887)]
        private static void AddVRoid00LongSleevesArmPatch() => AddArmPatchToRoot(SleevePreset.LongSleeves, true);

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Short Sleeves", false, -886)]
        private static void AddVRoid00ShortSleevesArmPatch() => AddArmPatchToRoot(SleevePreset.ShortSleeves, true);

        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Kimono", false, -885)]
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
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM VRoid Skirt Refine", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Mesh Trimmer", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM MToon to lilToon", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Eye Freeze", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Facial Mapper", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Long Sleeves", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Short Sleeves", true)]
        [MenuItem(BaseMenu + "Add Component with VRoid Defaults/YM Arm Patch/(via VRM 0.0) Kimono", true)]
        private static bool ValidatePresetMenu() => Selection.activeGameObject != null;

        private static void CreatePresetGameObject(SleevePreset preset, bool fromVrm00)
        {
            var selected = Selection.activeGameObject;
            if (selected == null) return;
            var avatarRoot = ResolveAvatarRoot(selected);

            var undoName = BuildUndoName(preset, fromVrm00);
            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            var componentObject = new GameObject(BuildPresetGameObjectName(preset, fromVrm00));
            componentObject.name = GameObjectUtility.GetUniqueNameForSibling(avatarRoot.transform, componentObject.name);
            Undo.RegisterCreatedObjectUndo(componentObject, undoName);
            Undo.SetTransformParent(componentObject.transform, avatarRoot.transform, undoName);
            componentObject.transform.localPosition = Vector3.zero;
            componentObject.transform.localRotation = Quaternion.identity;
            componentObject.transform.localScale = Vector3.one;

            AddPresetComponents(componentObject, avatarRoot, preset, fromVrm00);

            Selection.activeGameObject = componentObject;
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(avatarRoot);
        }

        private static void AddPresetComponents(GameObject target, GameObject avatarRoot, SleevePreset preset, bool fromVrm00)
        {
            var armPatch = Undo.AddComponent<ArmPatchComponent>(target);
            ConfigureArmPatch(armPatch, avatarRoot, preset, fromVrm00);

            TryAddSinglePerAvatarComponent<YMVRoidSkirtRefine>(target, avatarRoot, out _);

            AddMeshTrimmerComponents(target);

            var mtoon = Undo.AddComponent<MToonToLilToonComponent>(target);
            ConfigureMToonToLilToon(mtoon, avatarRoot);
            Undo.AddComponent<YMEyeFreeze>(target);

            EditorUtility.SetDirty(target);
        }

        private static void AddArmPatchToRoot(SleevePreset preset, bool fromVrm00)
        {
            var target = Selection.activeGameObject;
            if (target == null) return;
            var avatarRoot = ResolveAvatarRoot(target);

            var undoName = BuildArmPatchUndoName(preset, fromVrm00);
            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            if (TryAddComponent(target, out ArmPatchComponent armPatch))
            {
                ConfigureArmPatch(armPatch, avatarRoot, preset, fromVrm00);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(target);
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
            var target = Selection.activeGameObject;
            if (target == null) return;
            var avatarRoot = ResolveAvatarRoot(target);

            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            if (TryAddComponent(target, out MToonToLilToonComponent component))
            {
                ConfigureMToonToLilToon(component, avatarRoot);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(target);
        }

        private static void AddSingleComponentToRoot<T>(string undoName) where T : Component
        {
            var target = Selection.activeGameObject;
            if (target == null) return;
            var avatarRoot = ResolveAvatarRoot(target);

            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            TryAddSinglePerAvatarComponent<T>(target, avatarRoot, out _);

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(target);
        }

        private static void AddFacialMapperToRoot(string undoName)
        {
            var target = Selection.activeGameObject;
            if (target == null) return;
            var avatarRoot = ResolveAvatarRoot(target);

            Undo.SetCurrentGroupName(undoName);
            int undoGroup = Undo.GetCurrentGroup();

            if (TryAddSinglePerAvatarComponent<YMFacialMapper>(target, avatarRoot, out var component))
            {
                ConfigureFacialMapper(component);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(target);
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

        private static void ConfigureFacialMapper(YMFacialMapper component)
        {
            if (component == null) return;
            Undo.RecordObject(component, "Configure YM Facial Mapper");
            YMFacialMapperDefaults.ApplyEyeMouthSplitForVRoid(component);
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

        private static GameObject ResolveAvatarRoot(GameObject target)
        {
            if (target == null) return null;
            return PreviewCoordinator.FindAvatarRoot(target) ?? target;
        }

        private static bool TryAddComponent<T>(GameObject target, out T component) where T : Component
        {
            component = null;
            if (target == null) return false;

            var existing = target.GetComponent<T>();
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return false;
            }

            component = Undo.AddComponent<T>(target);
            return component != null;
        }

        private static bool TryAddSinglePerAvatarComponent<T>(GameObject target, GameObject avatarRoot, out T component) where T : Component
        {
            component = null;
            if (target == null) return false;

            return TryAddComponent(target, out component);
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
            component.MigrateSerializedValuesIfNeeded();

            component.enableShoulderFix = true;
            component.enableForearmFix = true;
            component.enableThumbFix = true;

            component.upperArmRollAxis = TwistAxis.X;
            component.shoulderPositionOffset = Vector3.zero;
            component.shoulderEulerOffset = preset switch
            {
                SleevePreset.LongSleeves => new Vector3(0f, 0f, -10f),
                SleevePreset.ShortSleeves => new Vector3(0f, 0f, -10f),
                SleevePreset.Kimono => new Vector3(0f, 0f, -5f),
                _ => Vector3.zero
            };

            component.forearmRollAxis = TwistAxis.X;
            component.forearmPitchAxis = TwistAxis.Z;
            component.forearmElbowScale = Vector3.one;
            component.forearmWristScale = Vector3.one;
            component.forearmElbowRollOffset = 0f;
            component.forearmTwistBoneCount = ForearmTwistBoneCount.Count0;
            component.forearmTwistBoneType = ForearmTwistBoneType.None;
            component.forearmSkinMaterialName = "Auto";
            component.forearmPreferElbowShape = false;

            if (preset == SleevePreset.ShortSleeves)
            {
                component.forearmTwistBoneCount = ForearmTwistBoneCount.Count8;
                component.forearmTwistBoneType = ForearmTwistBoneType.AllTwist;
                component.forearmElbowScale = new Vector3(1f, 0.85f, 0.95f);
                component.forearmWristScale = new Vector3(1f, 1.1f, 0.95f);
            }
            else if (preset == SleevePreset.Kimono)
            {
                var twistTarget = FindBodySkinMaterialName(root);
                component.forearmTwistBoneCount = ForearmTwistBoneCount.Count4;
                component.forearmSkinMaterialName = string.IsNullOrEmpty(twistTarget) ? "Auto" : twistTarget;
                component.forearmTwistBoneType = string.IsNullOrEmpty(twistTarget)
                    ? ForearmTwistBoneType.AllTwist
                    : ForearmTwistBoneType.SkinOnly;
                component.forearmElbowScale = new Vector3(0.9f, 1f, 1f);
                component.forearmWristScale = new Vector3(1f, 1.1f, 0.95f);
                component.forearmElbowRollOffset = -70f;
                component.forearmPreferElbowShape = true;
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
            component.maskDilatePixels = 20;
            component.alphaThreshold = 0.01f;
            EditorUtility.SetDirty(component);
        }
    }
}
