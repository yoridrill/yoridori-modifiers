using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.VRoidSkirtRefine
{
    [CustomEditor(typeof(YMVRoidSkirtRefine))]
    public sealed class YMVRoidSkirtRefineComponentEditor : UnityEditor.Editor
    {
        private enum Language
        {
            Japanese,
            English
        }

        private enum BoneTargetKind
        {
            FrontLeft,
            FrontRight,
            SideLeft,
            SideRight,
            BackLeft,
            BackRight
        }

        private sealed class DynamicsUsageEstimate
        {
            public int SourcePhysBones;
            public int GeneratedPhysBones;
            public int SourcePhysBoneColliders;
            public int GeneratedPhysBoneColliders;
            public int GeneratedRotationConstraints;
        }

        private static readonly string[] ConstraintModeJa =
        {
            "VRChat Constraints",
            "Unity Constraints"
        };

        private static readonly string[] ConstraintModeEn =
        {
            "VRChat Constraints",
            "Unity Constraints"
        };

        private const string PrefKeyLanguage = "YMVRoidSkirtRefineComponentEditor.Language";
        private const string PrefKeyAdvancedFoldout = "YMVRoidSkirtRefineComponentEditor.AdvancedFoldout";
        private const string PrefKeyOnePieceSettingsFoldout = "YMVRoidSkirtRefineComponentEditor.OnePieceSettingsFoldout";
        private const string PrefKeyLongCoatSettingsFoldout = "YMVRoidSkirtRefineComponentEditor.LongCoatSettingsFoldout";
        private const float SettingsLabelWidth = 180.0f;
        private const int QuestPhysBoneComponentLimit = 8;
        private const int QuestPhysBoneColliderLimit = 16;

        private static readonly GUIContent[] OnePiecePresetLabelsJa =
        {
            new GUIContent("ショートスカート軽め"),
            new GUIContent("ショートスカート重め"),
            new GUIContent("ロングスカート軽め"),
            new GUIContent("ロングスカート重め"),
            new GUIContent("ロングコートに合わせる"),
        };

        private static readonly GUIContent[] LongCoatPresetLabelsJa =
        {
            new GUIContent("ショートスカート軽め"),
            new GUIContent("ショートスカート重め"),
            new GUIContent("ロングスカート軽め"),
            new GUIContent("ロングスカート重め"),
            new GUIContent("前開き"),
            new GUIContent("ワンピースに合わせる"),
        };

        private SerializedProperty enableOnePieceRefineProp;
        private SerializedProperty onePiecePresetProp;
        private SerializedProperty enableOnePieceBoneExtensionProp;
        private SerializedProperty onePieceBoneExtensionModeProp;
        private SerializedProperty onePieceTargetBoneCountProp;
        private SerializedProperty onePieceRootHeightOffsetMultiplierProp;
        private SerializedProperty onePieceBonesProp;
        private SerializedProperty onePieceHipWeightReductionProp;
        private SerializedProperty onePieceMatchLongCoatProp;
        private SerializedProperty onePieceUseUpperLegCollidersProp;
        private SerializedProperty onePieceUseLowerLegCollidersProp;
        private SerializedProperty onePieceUseFloorColliderProp;
        private SerializedProperty onePieceUseFrontRootRotationConstraintsProp;
        private SerializedProperty onePieceMoveFrontRootsTowardUpperLegProp;
        private SerializedProperty onePiecePhysBoneProp;
        private SerializedProperty enableLongCoatRefineProp;
        private SerializedProperty longCoatPresetProp;
        private SerializedProperty enableLongCoatBoneExtensionProp;
        private SerializedProperty longCoatBoneExtensionModeProp;
        private SerializedProperty longCoatTargetBoneCountProp;
        private SerializedProperty longCoatShortSkirtUsePrependedRootsOnlyProp;
        private SerializedProperty longCoatRootHeightOffsetMultiplierProp;
        private SerializedProperty longCoatHipWeightReductionProp;
        private SerializedProperty longCoatSpineWeightReductionProp;
        private SerializedProperty longCoatMoveFrontBonesOutwardProp;
        private SerializedProperty longCoatUseRotationConstraintsProp;
        private SerializedProperty longCoatUseFrontRootRotationConstraintsProp;
        private SerializedProperty longCoatMoveConstrainedRootsTowardUpperLegProp;
        private SerializedProperty longCoatAimFrontLimitsForwardProp;
        private SerializedProperty longCoatBonesProp;
        private SerializedProperty longCoatMatchOnePieceProp;
        private SerializedProperty longCoatUseUpperLegCollidersProp;
        private SerializedProperty longCoatUseLowerLegCollidersProp;
        private SerializedProperty longCoatUseFloorColliderProp;
        private SerializedProperty longCoatPhysBoneProp;
        private SerializedProperty constraintModeProp;
        private SerializedProperty addGeneratedDynamicsToVqtKeepListProp;
        private SerializedProperty verboseLogProp;

        private Language language;
        private bool advancedFoldout;
        private bool onePieceSettingsFoldout;
        private bool longCoatSettingsFoldout;
        private bool previewFailed;

        private void OnEnable()
        {
            enableOnePieceRefineProp = serializedObject.FindProperty("enableOnePieceRefine");
            onePiecePresetProp = serializedObject.FindProperty("onePiecePreset");
            enableOnePieceBoneExtensionProp = serializedObject.FindProperty("enableOnePieceBoneExtension");
            onePieceBoneExtensionModeProp = serializedObject.FindProperty("onePieceBoneExtensionMode");
            onePieceTargetBoneCountProp = serializedObject.FindProperty("onePieceTargetBoneCount");
            onePieceRootHeightOffsetMultiplierProp = serializedObject.FindProperty("onePieceRootHeightOffsetMultiplier");
            onePieceBonesProp = serializedObject.FindProperty("onePieceBones");
            onePieceHipWeightReductionProp = serializedObject.FindProperty("onePieceHipWeightReduction");
            onePieceMatchLongCoatProp = serializedObject.FindProperty("onePieceMatchLongCoat");
            onePieceUseUpperLegCollidersProp = serializedObject.FindProperty("onePieceUseUpperLegColliders");
            onePieceUseLowerLegCollidersProp = serializedObject.FindProperty("onePieceUseLowerLegColliders");
            onePieceUseFloorColliderProp = serializedObject.FindProperty("onePieceUseFloorCollider");
            onePieceUseFrontRootRotationConstraintsProp = serializedObject.FindProperty("onePieceUseFrontRootRotationConstraints");
            onePieceMoveFrontRootsTowardUpperLegProp = serializedObject.FindProperty("onePieceMoveFrontRootsTowardUpperLeg");
            onePiecePhysBoneProp = serializedObject.FindProperty("onePiecePhysBone");
            enableLongCoatRefineProp = serializedObject.FindProperty("enableLongCoatRefine");
            longCoatPresetProp = serializedObject.FindProperty("longCoatPreset");
            enableLongCoatBoneExtensionProp = serializedObject.FindProperty("enableLongCoatBoneExtension");
            longCoatBoneExtensionModeProp = serializedObject.FindProperty("longCoatBoneExtensionMode");
            longCoatTargetBoneCountProp = serializedObject.FindProperty("longCoatTargetBoneCount");
            longCoatShortSkirtUsePrependedRootsOnlyProp = serializedObject.FindProperty("longCoatShortSkirtUsePrependedRootsOnly");
            longCoatRootHeightOffsetMultiplierProp = serializedObject.FindProperty("longCoatRootHeightOffsetMultiplier");
            longCoatHipWeightReductionProp = serializedObject.FindProperty("longCoatHipWeightReduction");
            longCoatSpineWeightReductionProp = serializedObject.FindProperty("longCoatSpineWeightReduction");
            longCoatMoveFrontBonesOutwardProp = serializedObject.FindProperty("longCoatMoveFrontBonesOutward");
            longCoatUseRotationConstraintsProp = serializedObject.FindProperty("longCoatUseRotationConstraints");
            longCoatUseFrontRootRotationConstraintsProp = serializedObject.FindProperty("longCoatUseFrontRootRotationConstraints");
            longCoatMoveConstrainedRootsTowardUpperLegProp = serializedObject.FindProperty("longCoatMoveConstrainedRootsTowardUpperLeg");
            longCoatAimFrontLimitsForwardProp = serializedObject.FindProperty("longCoatAimFrontLimitsForward");
            longCoatBonesProp = serializedObject.FindProperty("longCoatBones");
            longCoatMatchOnePieceProp = serializedObject.FindProperty("longCoatMatchOnePiece");
            longCoatUseUpperLegCollidersProp = serializedObject.FindProperty("longCoatUseUpperLegColliders");
            longCoatUseLowerLegCollidersProp = serializedObject.FindProperty("longCoatUseLowerLegColliders");
            longCoatUseFloorColliderProp = serializedObject.FindProperty("longCoatUseFloorCollider");
            longCoatPhysBoneProp = serializedObject.FindProperty("longCoatPhysBone");
            constraintModeProp = serializedObject.FindProperty("constraintMode");
            addGeneratedDynamicsToVqtKeepListProp = serializedObject.FindProperty("addGeneratedDynamicsToVqtKeepList");
            verboseLogProp = serializedObject.FindProperty("verboseLog");

            language = (Language)EditorPrefs.GetInt(PrefKeyLanguage, 0);
            advancedFoldout = EditorPrefs.GetBool(PrefKeyAdvancedFoldout, false);
            onePieceSettingsFoldout = EditorPrefs.GetBool(PrefKeyOnePieceSettingsFoldout, false);
            longCoatSettingsFoldout = EditorPrefs.GetBool(PrefKeyLongCoatSettingsFoldout, false);

            SceneIconUtility.HideComponentIcon<YMVRoidSkirtRefine>();
            AutoDetectBonesAndAutoEnableIfNeeded((YMVRoidSkirtRefine)target);
        }

        public override void OnInspectorGUI()
        {
            var component = (YMVRoidSkirtRefine)target;
            var isPreviewing = YMVRoidSkirtRefinePreviewUtility.IsPreviewing(component);

            serializedObject.Update();
            onePieceBoneExtensionModeProp.enumValueIndex = (int)BoneExtensionMode.AppendToTip;
            longCoatBoneExtensionModeProp.enumValueIndex = (int)BoneExtensionMode.PrependToRoot;

            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max(previousLabelWidth, SettingsLabelWidth);
            bool changed;
            try
            {
                DrawTopRow(component, isPreviewing || YMVRoidSkirtRefinePreviewUtility.IsStarting(component));
                EditorGUILayout.Space(4);

                DrawMatchTargetWarning();
                DrawPlacementStatus(component);
                DrawMultipleComponentsWarning(component);
                EditorGUILayout.Space(4);

                DrawDynamicsUsageAndVqtKeepList(component);
                EditorGUILayout.Space(12);

                EditorGUI.BeginChangeCheck();
                DrawOnePieceSection();
                EditorGUILayout.Space(6);
                DrawLongCoatSection();
                EditorGUILayout.Space(4);
                DrawAdvancedSection();
                changed = EditorGUI.EndChangeCheck();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }

            serializedObject.ApplyModifiedProperties();

            if (changed)
            {
                EditorUtility.SetDirty(component);
                YMVRoidSkirtRefinePreviewUtility.SyncPhysBonesIfPreviewing(component);
            }
        }

        private void DrawTopRow(YMVRoidSkirtRefine component, bool isPreviewing)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (PreviewInspectorGui.DrawPreviewButton(isPreviewing, isPreviewing ? "Save" : "Preview"))
                {
                    serializedObject.ApplyModifiedProperties();
                    previewFailed = !YMVRoidSkirtRefinePreviewUtility.TogglePreview(component);
                    GUIUtility.ExitGUI();
                }

                PreviewInspectorGui.DrawStatus(false, previewFailed || YMVRoidSkirtRefinePreviewUtility.HasPreviewFailed());

                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                var newLanguage = (Language)EditorGUILayout.EnumPopup(language, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck())
                {
                    language = newLanguage;
                    EditorPrefs.SetInt(PrefKeyLanguage, (int)language);
                }
            }
        }

        private void DrawPlacementStatus(YMVRoidSkirtRefine component)
        {
            if (component == null) return;

            var parentDescriptor = component.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (parentDescriptor == null)
            {
                EditorGUILayout.HelpBox(
                    T(
                        "アバター配下に追加してください。 VRCAvatarDescriptorが見つかりません。",
                        "Add this component under an avatar. VRCAvatarDescriptor was not found."),
                    MessageType.Warning);
            }
        }

        private void DrawMultipleComponentsWarning(YMVRoidSkirtRefine component)
        {
            if (component == null) return;

            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            if (avatarRoot == null) return;

            var components = avatarRoot.GetComponentsInChildren<YMVRoidSkirtRefine>(true);
            if (components == null || components.Length <= 1) return;

            var selected = SelectPreferredComponentForBuild(components, avatarRoot);
            var thisWillBeUsed = selected == component;
            var message = thisWillBeUsed
                ? T(
                    "複数箇所で設定されています。このコンポーネントの設定値が使用されます。",
                    "This component is configured in multiple places. The values on this component will be used.")
                : T(
                    "複数箇所で設定されています。このコンポーネントでの設定は無視されます。",
                    "This component is configured in multiple places. The values on this component will be ignored.");

            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        private static YMVRoidSkirtRefine SelectPreferredComponentForBuild(
            YMVRoidSkirtRefine[] components,
            GameObject avatarRoot)
        {
            YMVRoidSkirtRefine best = components[0];
            var bestScore = int.MinValue;
            var root = avatarRoot != null ? avatarRoot.transform : null;

            for (var i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;

                var depth = PreviewCoordinator.GetDepthFromRoot(c.transform, root);
                var score = -depth * 10000 - i;
                if (score > bestScore)
                {
                    best = c;
                    bestScore = score;
                }
            }

            return best;
        }

        private void DrawMatchTargetWarning()
        {
            var onePieceMatches = enableOnePieceRefineProp.boolValue && onePieceMatchLongCoatProp.boolValue;
            var longCoatMatches = enableLongCoatRefineProp.boolValue && longCoatMatchOnePieceProp.boolValue;
            var missingTarget = (onePieceMatches && !enableLongCoatRefineProp.boolValue)
                || (longCoatMatches && !enableOnePieceRefineProp.boolValue)
                || (onePieceMatches && longCoatMatches);
            if (!missingTarget) return;

            EditorGUILayout.HelpBox(
                T(
                    "揺れを合わせる対象がありません。今の設定では動作しません。",
                    "There is no target to match swing bones. This configuration will not run."),
                MessageType.Warning);
        }

        private void DrawDynamicsUsageAndVqtKeepList(YMVRoidSkirtRefine component)
        {
            var avatarRoot = component != null ? PreviewCoordinator.FindAvatarRoot(component.gameObject) : null;
            var estimate = EstimateDynamicsUsage(component, avatarRoot);
            var message = string.Join(
                "\n",
                T("今の設定で増える Rotation Constraint 数", "Rotation Constraints added by current settings") + $": {estimate.GeneratedRotationConstraints}",
                T("スカート関連の PhysBone コンポーネント数", "Skirt-related PhysBone components") + $": {estimate.SourcePhysBones} \u2192 {estimate.GeneratedPhysBones}",
                T("スカート関連の PhysBone コライダー数", "Skirt-related PhysBone colliders") + $": {estimate.SourcePhysBoneColliders} \u2192 {estimate.GeneratedPhysBoneColliders}");
            EditorGUILayout.HelpBox(message, MessageType.Info);

            EditorGUILayout.PropertyField(
                addGeneratedDynamicsToVqtKeepListProp,
                TT(
                    "VQTのKeepリストに追加する",
                    "VRCQuestToolsのAvatar Dynamics削除を使う場合に、このツールが生成したPhysBoneとPhysBoneColliderをKeepリストへ追加します。",
                    "Add to VQT Keep List",
                    "Adds generated PhysBones and PhysBoneColliders to VRCQuestTools Avatar Dynamics keep lists."));

            if (!addGeneratedDynamicsToVqtKeepListProp.boolValue) return;

            if (!TryGetVqtKeepListStatus(avatarRoot, estimate, out var statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Warning);
            }
        }

        private static DynamicsUsageEstimate EstimateDynamicsUsage(YMVRoidSkirtRefine component, GameObject avatarRoot)
        {
            var estimate = new DynamicsUsageEstimate();
            if (component == null) return estimate;

            var onePieceMatchesLongCoat = component.enableOnePieceRefine && component.onePieceMatchLongCoat;
            var longCoatMatchesOnePiece = component.enableLongCoatRefine && component.longCoatMatchOnePiece;
            if ((onePieceMatchesLongCoat && longCoatMatchesOnePiece)
                || (onePieceMatchesLongCoat && !component.enableLongCoatRefine)
                || (longCoatMatchesOnePiece && !component.enableOnePieceRefine))
            {
                return estimate;
            }

            var buildsOnePiece = component.enableOnePieceRefine && !onePieceMatchesLongCoat;
            var buildsLongCoat = component.enableLongCoatRefine && !longCoatMatchesOnePiece;
            estimate.SourcePhysBones = CountSkirtRelatedSourcePhysBones(
                component,
                component.enableOnePieceRefine,
                component.enableLongCoatRefine);
            if (buildsOnePiece || buildsLongCoat)
            {
                estimate.SourcePhysBoneColliders = CountExistingLegPhysBoneColliders(avatarRoot);
            }

            if (onePieceMatchesLongCoat)
            {
                AddLongCoatGeneratedDynamics(component, estimate);
            }
            else if (longCoatMatchesOnePiece)
            {
                AddOnePieceGeneratedDynamics(component, estimate);
            }
            else
            {
                if (component.enableOnePieceRefine) AddOnePieceGeneratedDynamics(component, estimate);
                if (component.enableLongCoatRefine) AddLongCoatGeneratedDynamics(component, estimate);
            }

            return estimate;
        }

        private static int CountSkirtRelatedSourcePhysBones(
            YMVRoidSkirtRefine component,
            bool includeOnePiece,
            bool includeLongCoat)
        {
            if (component == null) return 0;

            var physBones = new HashSet<VRCPhysBone>();
            if (includeOnePiece) AddPhysBonesFromTargets(physBones, component.onePieceBones, false);
            if (includeLongCoat) AddPhysBonesFromTargets(physBones, component.longCoatBones, true);
            return physBones.Count;
        }

        private static void AddPhysBonesFromTargets(
            HashSet<VRCPhysBone> physBones,
            SkirtRefineBoneTargets targets,
            bool longCoat)
        {
            if (physBones == null || targets == null) return;

            AddPhysBonesFromTarget(physBones, targets.frontLeft, longCoat ? "L_CoatSkirtFront" : "L_SkirtFront");
            AddPhysBonesFromTarget(physBones, targets.frontRight, longCoat ? "R_CoatSkirtFront" : "R_SkirtFront");
            AddPhysBonesFromTarget(physBones, targets.sideLeft, longCoat ? "L_CoatSkirtSide" : "L_SkirtSide");
            AddPhysBonesFromTarget(physBones, targets.sideRight, longCoat ? "R_CoatSkirtSide" : "R_SkirtSide");
            AddPhysBonesFromTarget(physBones, targets.backLeft, longCoat ? "L_CoatSkirtBack" : "L_SkirtBack");
            AddPhysBonesFromTarget(physBones, targets.backRight, longCoat ? "R_CoatSkirtBack" : "R_SkirtBack");
        }

        private static void AddPhysBonesFromTarget(
            HashSet<VRCPhysBone> physBones,
            Transform target,
            string partialName)
        {
            if (physBones == null || target == null) return;

            if (!string.IsNullOrEmpty(partialName)
                && target.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                var descendant = FindDescendantByPartialName(target, partialName);
                if (descendant != null) target = descendant;
            }

            foreach (var physBone in target.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (physBone != null) physBones.Add(physBone);
            }
        }

        private static void AddOnePieceGeneratedDynamics(YMVRoidSkirtRefine component, DynamicsUsageEstimate estimate)
        {
            estimate.GeneratedPhysBones += 1;
            if (component.onePieceUseFrontRootRotationConstraints)
            {
                estimate.GeneratedPhysBones += 2;
                estimate.GeneratedRotationConstraints += 2;
            }

            estimate.GeneratedPhysBoneColliders += CountGeneratedLegColliders(
                component.onePieceUseUpperLegColliders,
                component.onePieceUseLowerLegColliders);
            if (component.onePieceUseFloorCollider) estimate.GeneratedPhysBoneColliders += 1;
        }

        private static void AddLongCoatGeneratedDynamics(YMVRoidSkirtRefine component, DynamicsUsageEstimate estimate)
        {
            if (component.longCoatUseRotationConstraints)
            {
                estimate.GeneratedPhysBones += 6;
                estimate.GeneratedRotationConstraints += 12;
            }
            else
            {
                estimate.GeneratedPhysBones += 1;
                if (component.longCoatUseFrontRootRotationConstraints)
                {
                    estimate.GeneratedPhysBones += 2;
                    estimate.GeneratedRotationConstraints += 2;
                }
            }

            estimate.GeneratedPhysBoneColliders += CountGeneratedLegColliders(
                component.longCoatUseUpperLegColliders,
                component.longCoatUseLowerLegColliders);
            if (component.longCoatUseFloorCollider) estimate.GeneratedPhysBoneColliders += 1;
        }

        private static int CountGeneratedLegColliders(bool upperLeg, bool lowerLeg)
        {
            var count = 0;
            if (upperLeg) count += 2;
            if (lowerLeg) count += 2;
            return count;
        }

        private static int CountExistingLegPhysBoneColliders(GameObject avatarRoot)
        {
            var animator = avatarRoot != null ? avatarRoot.GetComponentInChildren<Animator>(true) : null;
            if (animator == null) return 0;

            var colliders = new HashSet<VRCPhysBoneCollider>();
            AddExistingLegPhysBoneColliders(colliders, animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg));
            AddExistingLegPhysBoneColliders(colliders, animator.GetBoneTransform(HumanBodyBones.RightUpperLeg));
            AddExistingLegPhysBoneColliders(colliders, animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg));
            AddExistingLegPhysBoneColliders(colliders, animator.GetBoneTransform(HumanBodyBones.RightLowerLeg));
            return colliders.Count;
        }

        private static void AddExistingLegPhysBoneColliders(HashSet<VRCPhysBoneCollider> colliders, Transform leg)
        {
            if (colliders == null || leg == null) return;

            foreach (var collider in leg.GetComponentsInChildren<VRCPhysBoneCollider>(true))
            {
                if (collider == null) continue;
                if (collider.name.StartsWith("YM_VRoidSkirtRefine_", StringComparison.Ordinal)) continue;
                colliders.Add(collider);
            }
        }

        private bool TryGetVqtKeepListStatus(GameObject avatarRoot, DynamicsUsageEstimate estimate, out string message)
        {
            message = null;
            if (!TryFindVqtAvatarConverterSettings(avatarRoot, out var settings))
            {
                message = T(
                    "VQTが見つかりません。 このオプションは動作しません。",
                    "VQT was not found. This option will not run.");
                return false;
            }

            var animator = avatarRoot != null ? avatarRoot.GetComponentInChildren<Animator>(true) : null;
            var hips = animator != null ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            var keepPhysBones = CountObjectArrayFieldExcludingDescendants(settings, "physBonesToKeep", hips);
            var keepColliders = CountObjectArrayFieldExcludingDescendants(settings, "physBoneCollidersToKeep", hips);
            var totalPhysBones = keepPhysBones + estimate.GeneratedPhysBones;
            var totalColliders = keepColliders + estimate.GeneratedPhysBoneColliders;
            if (totalPhysBones > QuestPhysBoneComponentLimit || totalColliders > QuestPhysBoneColliderLimit)
            {
                message = T(
                    "制限数を超えています。 このオプションは動作しません。",
                    "The limit is exceeded. This option will not run.");
                return false;
            }

            return true;
        }

        private static bool TryFindVqtAvatarConverterSettings(GameObject avatarRoot, out Component settings)
        {
            settings = null;
            if (avatarRoot == null) return false;
            foreach (var component in avatarRoot.GetComponents<Component>())
            {
                if (component == null) continue;
                if (component.GetType().FullName != "KRT.VRCQuestTools.Components.AvatarConverterSettings") continue;
                settings = component;
                return true;
            }

            return false;
        }

        private static int CountObjectArrayField(Component component, string fieldName)
        {
            if (component == null) return 0;
            var field = component.GetType().GetField(fieldName);
            return field != null && field.GetValue(component) is Array array
                ? array.Cast<object>().OfType<Component>().Count(c => c != null)
                : 0;
        }

        private static int CountObjectArrayFieldExcludingDescendants(Component component, string fieldName, Transform excludedRoot)
        {
            if (component == null) return 0;
            var field = component.GetType().GetField(fieldName);
            if (field == null || !(field.GetValue(component) is Array array)) return 0;

            var count = 0;
            foreach (var item in array)
            {
                if (!(item is Component itemComponent) || itemComponent == null) continue;
                if (excludedRoot != null && itemComponent.transform != null && itemComponent.transform.IsChildOf(excludedRoot)) continue;
                count++;
            }

            return count;
        }

        private void DrawOnePieceSection()
        {
            DrawRefineSectionHeader(
                enableOnePieceRefineProp,
                T("One-Piece Refine", "One-Piece Refine"));

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(!enableOnePieceRefineProp.boolValue))
            {
                DrawPresetPopup(
                    onePiecePresetProp,
                    TT(
                        "プリセット",
                        "ワンピースの裾まわりを整える設定です。ロングスカートでは先端側へボーンを足し、丈を伸ばしたVRoid衣装でも自然に揺れやすくします。",
                        "Preset",
                        "Refines one-piece skirt hems. Long skirt presets append tip-side bones so lengthened VRoid outfits can swing more naturally."),
                    OnePiecePresetLabelsJa,
                    () =>
                    {
                        var preset = (OnePiecePreset)onePiecePresetProp.enumValueIndex;
                        ApplyOnePiecePhysBonePreset(onePiecePhysBoneProp, preset);
                        onePieceMatchLongCoatProp.boolValue = preset == OnePiecePreset.MatchLongCoat;
                        enableOnePieceBoneExtensionProp.boolValue = !IsShortOnePiecePreset(preset);
                        onePieceUseUpperLegCollidersProp.boolValue = UsesOnePieceUpperLegColliders(preset);
                        onePieceUseLowerLegCollidersProp.boolValue = UsesOnePieceLowerLegColliders(preset);
                        onePieceUseFloorColliderProp.boolValue = UsesOnePieceFloorCollider(preset);
                        onePieceUseFrontRootRotationConstraintsProp.boolValue = preset != OnePiecePreset.MatchLongCoat;
                        onePieceMoveFrontRootsTowardUpperLegProp.boolValue = false;
                        onePieceRootHeightOffsetMultiplierProp.floatValue = preset == OnePiecePreset.MatchLongCoat ? 0.0f : 0.4f;
                        onePieceHipWeightReductionProp.floatValue = 0.5f;
                    });

                if (enableOnePieceRefineProp.boolValue
                    && !YMVRoidSkirtRefinePreviewUtility.IsActivePlayPreview
                    && HasMissingBone(onePieceBonesProp))
                {
                    EditorGUILayout.HelpBox(
                        T(
                            "ボーンが指定されていないため、実行されません。",
                            "Bones are not assigned, so this will not run."),
                        MessageType.Warning);
                }

                DrawSettingsFoldout(
                    ref onePieceSettingsFoldout,
                    PrefKeyOnePieceSettingsFoldout,
                    () =>
                    {
                        DrawMatchSettings(
                            onePieceMatchLongCoatProp,
                            TT(
                                "ロングコートに合わせる",
                                "ワンピースのウェイトをロングコートの揺れボーンへ移し、元のワンピース揺れボーンを削除します。",
                                "Match Long Coat",
                                "Binds one-piece weights to the long coat swing bones and removes original one-piece swing bones."));
                        using (new EditorGUI.DisabledScope(onePieceMatchLongCoatProp.boolValue))
                        {
                            DrawBoneExtension(
                                onePieceBonesProp,
                                enableOnePieceBoneExtensionProp,
                                onePieceBoneExtensionModeProp,
                                onePieceTargetBoneCountProp,
                                onePieceHipWeightReductionProp,
                                onePieceRootHeightOffsetMultiplierProp,
                                null,
                                null,
                                null,
                                false,
                                true);
                            DrawOnePieceRotationConstraintSettings();
                            DrawLegColliderSettings(onePieceUseUpperLegCollidersProp, onePieceUseLowerLegCollidersProp, onePieceUseFloorColliderProp);
                            DrawPhysBoneSettings(onePiecePhysBoneProp);
                        }
                    });
            }
        }

        private void DrawLongCoatSection()
        {
            DrawRefineSectionHeader(
                enableLongCoatRefineProp,
                T("Long Coat Refine", "Long Coat Refine"));

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(!enableLongCoatRefineProp.boolValue))
            {
                DrawPresetPopup(
                    longCoatPresetProp,
                    TT(
                        "プリセット",
                        "ロングコートの裾まわりを整える設定です。膝下から始まるVRoidの揺れボーンに根本側のボーンを足し、脚の動きに追従しやすい揺れへ調整します。",
                        "Preset",
                        "Refines long coat hems. Presets prepend root-side bones to VRoid coat chains that start below the knee, improving swing that follows leg motion."),
                    LongCoatPresetLabelsJa,
                    () =>
                    {
                        var preset = (LongCoatPreset)longCoatPresetProp.enumValueIndex;
                        ApplyLongCoatPhysBonePreset(longCoatPhysBoneProp, preset);
                        longCoatMatchOnePieceProp.boolValue = preset == LongCoatPreset.MatchOnePiece;
                        enableLongCoatBoneExtensionProp.boolValue = true;
                        longCoatShortSkirtUsePrependedRootsOnlyProp.boolValue = IsShortLongCoatPreset(preset);
                        longCoatMoveFrontBonesOutwardProp.boolValue = preset == LongCoatPreset.OpenFront;
                        longCoatUseRotationConstraintsProp.boolValue = IsLongCoatLongSkirtPreset(preset);
                        longCoatUseFrontRootRotationConstraintsProp.boolValue = IsShortLongCoatPreset(preset) || preset == LongCoatPreset.OpenFront;
                        longCoatMoveConstrainedRootsTowardUpperLegProp.boolValue = true;
                        longCoatAimFrontLimitsForwardProp.boolValue = IsShortLongCoatPreset(preset) || IsLongCoatLongSkirtPreset(preset);
                        longCoatUseUpperLegCollidersProp.boolValue = UsesLongCoatUpperLegColliders(preset);
                        longCoatUseLowerLegCollidersProp.boolValue = UsesLongCoatLowerLegColliders(preset);
                        longCoatUseFloorColliderProp.boolValue = UsesLongCoatFloorCollider(preset);
                        longCoatRootHeightOffsetMultiplierProp.floatValue = preset == LongCoatPreset.OpenFront ? 2.0f : 1.0f;
                        longCoatHipWeightReductionProp.floatValue = preset == LongCoatPreset.OpenFront ? 0.4f : 0.4f;
                        longCoatSpineWeightReductionProp.floatValue = preset == LongCoatPreset.OpenFront ? 0.4f : 0.0f;
                    });

                if (enableLongCoatRefineProp.boolValue
                    && !YMVRoidSkirtRefinePreviewUtility.IsActivePlayPreview
                    && HasMissingBone(longCoatBonesProp))
                {
                    EditorGUILayout.HelpBox(
                        T(
                            "ボーンが指定されていないため、実行されません。",
                            "Bones are not assigned, so this will not run."),
                        MessageType.Warning);
                }

                DrawSettingsFoldout(
                    ref longCoatSettingsFoldout,
                    PrefKeyLongCoatSettingsFoldout,
                    () =>
                    {
                        DrawMatchSettings(
                            longCoatMatchOnePieceProp,
                            TT(
                                "ワンピースに合わせる",
                                "ロングコートのウェイトをワンピースの揺れボーンへ移し、元のロングコート揺れボーンを削除します。",
                                "Match One-Piece",
                                "Binds long coat weights to the one-piece swing bones and removes original long coat swing bones."));
                        using (new EditorGUI.DisabledScope(longCoatMatchOnePieceProp.boolValue))
                        {
                            DrawBoneExtension(
                                longCoatBonesProp,
                                enableLongCoatBoneExtensionProp,
                                longCoatBoneExtensionModeProp,
                                longCoatTargetBoneCountProp,
                                longCoatHipWeightReductionProp,
                                longCoatRootHeightOffsetMultiplierProp,
                                longCoatSpineWeightReductionProp,
                                longCoatShortSkirtUsePrependedRootsOnlyProp,
                                longCoatMoveFrontBonesOutwardProp,
                                false);
                            DrawLongCoatRotationConstraintSettings();
                            DrawLegColliderSettings(longCoatUseUpperLegCollidersProp, longCoatUseLowerLegCollidersProp, longCoatUseFloorColliderProp);
                            DrawPhysBoneSettings(longCoatPhysBoneProp);
                        }
                    });
            }
        }

        private static void DrawRefineSectionHeader(SerializedProperty enabledProp, string title)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var toggleRect = new Rect(rect.x, rect.y, 18f, rect.height);
            var labelRect = new Rect(toggleRect.xMax + 2f, rect.y, rect.width - 20f, rect.height);

            enabledProp.boolValue = EditorGUI.Toggle(toggleRect, enabledProp.boolValue);
            EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);
        }

        private static void DrawPresetPopup(
            SerializedProperty prop,
            GUIContent label,
            GUIContent[] labels,
            Action onChanged = null)
        {
            var next = EditorGUILayout.Popup(label, prop.enumValueIndex, labels);
            if (next >= 0 && next < prop.enumNames.Length)
            {
                var changed = prop.enumValueIndex != next;
                prop.enumValueIndex = next;
                if (changed) onChanged?.Invoke();
            }
        }

        private void DrawSettingsFoldout(ref bool foldout, string prefKey, Action drawContent)
        {
            EditorGUI.BeginChangeCheck();
            foldout = EditorGUILayout.Foldout(foldout, T("Settings", "Settings"), true);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(prefKey, foldout);
            }

            if (!foldout) return;

            using (new EditorGUI.IndentLevelScope())
            {
                drawContent?.Invoke();
            }
        }

        private void DrawBoneExtension(
            SerializedProperty bonesProp,
            SerializedProperty enableProp,
            SerializedProperty modeProp,
            SerializedProperty targetCountProp,
            SerializedProperty hipWeightReductionProp = null,
            SerializedProperty rootHeightOffsetMultiplierProp = null,
            SerializedProperty spineWeightReductionProp = null,
            SerializedProperty rootsOnlyProp = null,
            SerializedProperty moveFrontBonesOutwardProp = null,
            bool showFixedExtensionControls = true,
            bool useOnePieceRootHeightOffset = false)
        {
            EditorGUILayout.LabelField(T("Bone Extension", "Bone Extension"), EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                if (enableProp != null)
                {
                    DrawBoneField(bonesProp, "frontLeft", T("Front-Left", "Front-Left"));
                    DrawBoneField(bonesProp, "frontRight", T("Front-Right", "Front-Right"));
                    DrawBoneField(bonesProp, "sideLeft", T("Side-Left", "Side-Left"));
                    DrawBoneField(bonesProp, "sideRight", T("Side-Right", "Side-Right"));
                    DrawBoneField(bonesProp, "backLeft", T("Back-Left", "Back-Left"));
                    DrawBoneField(bonesProp, "backRight", T("Back-Right", "Back-Right"));
                }

                if (enableProp != null)
                {
                    EditorGUILayout.PropertyField(
                        enableProp,
                        TT(
                            "ボーン追加",
                            "既存チェーンの先端側へボーンを追加します。OFFでもPhysBone統合は行います。",
                            "Add Bones",
                            "Adds bones to the tip side of existing chains. PhysBone integration still runs when this is disabled."));
                }

                if (showFixedExtensionControls && modeProp != null)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.PropertyField(
                            modeProp,
                            TT(
                                "延長方式",
                                "ワンピースでは先端側へ追加します。",
                                "Extension Mode",
                                "One-piece skirts append bones to the tip side."));
                    }
                }

                if (showFixedExtensionControls && targetCountProp != null)
                {
                    EditorGUILayout.PropertyField(
                        targetCountProp,
                        TT(
                            "目標段数",
                            "揺れボーンチェーンをこの段数に揃えます。",
                            "Target Bone Count",
                            "Normalizes skirt chains to this bone count."));
                }

                if (rootsOnlyProp != null)
                {
                    EditorGUILayout.PropertyField(
                        rootsOnlyProp,
                        TT(
                            "下3段のボーンを除く",
                            "追加Rootだけを残し、既存のロングコート揺れボーンを削除します。",
                            "Remove Lower 3 Bones",
                            "Keeps only prepended roots and removes the original long coat swing bones."));
                }

                if (moveFrontBonesOutwardProp != null)
                {
                    EditorGUILayout.PropertyField(
                        moveFrontBonesOutwardProp,
                        TT(
                            "Frontを外側へずらす",
                            "前開きコート向けにFrontの揺れボーンを外側かつ少し後ろへずらします。",
                            "Move Front Outward",
                            "Moves front swing bones slightly outward and backward for open-front coats."));
                }

                if (rootHeightOffsetMultiplierProp != null)
                {
                    if (useOnePieceRootHeightOffset)
                    {
                        EditorGUILayout.Slider(
                            rootHeightOffsetMultiplierProp,
                            0.0f,
                            1.0f,
                            TT(
                                "付け根を上にずらす",
                                "1段目から2段目へ向かう方向の逆向きに、布に沿って付け根を上へ移動します。2段目と3段目も段階的に補間します。0で元位置、1で推定収束点付近まで移動します。",
                                "Raise Roots",
                                "Moves roots upward along the reverse direction from the first bone to the second bone. The second and third bones are blended progressively. 0 keeps original positions; 1 moves near the estimated convergence point."));
                    }
                    else
                    {
                        EditorGUILayout.Slider(
                            rootHeightOffsetMultiplierProp,
                            -1.0f,
                            2.0f,
                            TT(
                                "付け根を上へずらす",
                                "追加Rootの高さをUpperLeg付近へ補正します。-1で補正なし、0でUpperLegの高さ、1でUpperLegよりコライダー半径分上、2でその2倍上へ揃えます。",
                                "Raise Root Height",
                                "Raises generated root bones toward UpperLeg. -1 keeps the extended height, 0 aligns to UpperLeg height, 1 aligns one collider radius above UpperLeg, and 2 aligns two radii above."));
                    }
                }

                if (hipWeightReductionProp != null)
                {
                    EditorGUILayout.Slider(
                        hipWeightReductionProp,
                        0.0f,
                        1.0f,
                        TT(
                            "Hipのウェイトを弱める",
                            "揺れボーン/脚ボーンを置き換える頂点で、Hipウェイトをこの割合だけ揺れボーン側へ移します。1でHipウェイトを完全に移します。",
                            "Reduce Hip Weight",
                            "Moves this fraction of Hip weight to swing bones on vertices affected by skirt/leg reweighting. 1 fully removes Hip weight there."));
                }

                if (spineWeightReductionProp != null)
                {
                    EditorGUILayout.Slider(
                        spineWeightReductionProp,
                        0.0f,
                        1.0f,
                        TT(
                            "Spineのウェイトを弱める",
                            "揺れボーン/脚ボーンを置き換える頂点で、Spineウェイトをこの割合だけ揺れボーン側へ移します。1でSpineウェイトを完全に移します。",
                            "Reduce Spine Weight",
                            "Moves this fraction of Spine weight to swing bones on vertices affected by skirt/leg reweighting. 1 fully removes Spine weight there."));
                }
            }
        }

        private static void DrawBoneField(SerializedProperty bonesProp, string propertyName, string label)
        {
            EditorGUILayout.PropertyField(
                bonesProp.FindPropertyRelative(propertyName),
                new GUIContent(label));
        }

        private static void DrawPlaceholderLabel(string label)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        private static void DrawMatchSettings(SerializedProperty matchProp, GUIContent label)
        {
            EditorGUILayout.LabelField("Match", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(matchProp, label);
            }
        }

        private void DrawLegColliderSettings(
            SerializedProperty upperLegEnableProp,
            SerializedProperty lowerLegEnableProp,
            SerializedProperty floorEnableProp)
        {
            EditorGUILayout.LabelField(T("PhysBone Collider", "PhysBone Collider"), EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(
                    upperLegEnableProp,
                    TT(
                        "UpperLegに追加",
                        "左右UpperLeg配下の既存PhysBoneColliderを削除し、このツールのカプセルコライダーを追加します。",
                        "Add to UpperLeg",
                        "Removes existing PhysBoneColliders under both UpperLeg bones and adds generated capsule colliders."));
                EditorGUILayout.PropertyField(
                    lowerLegEnableProp,
                    TT(
                        "LowerLegに追加",
                        "左右LowerLeg配下の既存PhysBoneColliderを削除し、このツールのカプセルコライダーを追加します。",
                        "Add to LowerLeg",
                        "Removes existing PhysBoneColliders under both LowerLeg bones and adds generated capsule colliders."));
                if (floorEnableProp != null)
                {
                    EditorGUILayout.PropertyField(
                        floorEnableProp,
                        TT(
                            "床を追加",
                            "アバタールート直下に2.4m四方の床コライダーを追加し、対象の揺れボーンに設定します。",
                            "Add Floor",
                            "Adds a 2.4m square floor collider under the avatar root and assigns it to the target swing PhysBones."));
                }
            }
        }

        private void DrawPhysBoneSettings(SerializedProperty settingsProp)
        {
            EditorGUILayout.LabelField(T("PhysBone", "PhysBone"), EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField(T("Forces", "Forces"), EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawClampedFloatWithCurve(settingsProp, "pull", "pullCurve", TT("Pull", "引き戻しの強さです。", "Pull", "Pull strength."), 0f, 1f);
                    DrawClampedFloatWithCurve(settingsProp, "spring", "springCurve", TT("Spring", "ばねの強さです。", "Spring", "Spring strength."), 0f, 1f);
                    DrawClampedFloatWithCurve(settingsProp, "gravity", "gravityCurve", TT("Gravity", "重力の強さです。", "Gravity", "Gravity strength."), -1f, 1f);
                    DrawClampedFloatWithCurve(settingsProp, "gravityFalloff", "gravityFalloffCurve", TT("Gravity Falloff", "根本から先端への重力減衰です。", "Gravity Falloff", "Gravity falloff from root to tip."), 0f, 1f);
                    EditorGUILayout.PropertyField(
                        settingsProp.FindPropertyRelative("immobileType"),
                        TT("Immobile Type", "Immobileの基準です。", "Immobile Type", "Reference used by Immobile."));
                    DrawClampedFloatWithCurve(settingsProp, "immobile", "immobileCurve", TT("Immobile", "根本側のImmobile値です。", "Immobile", "Root immobile value."), 0f, 1f);
                }

                EditorGUILayout.LabelField(T("Limits", "Limits"), EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawLimitSettings(settingsProp);
                }

                EditorGUILayout.LabelField(T("Collision", "Collision"), EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawClampedFloatWithCurve(settingsProp, "radius", "radiusCurve", TT("Radius", "PhysBoneの当たり判定半径です。", "Radius", "PhysBone collision radius."), 0f, 1f);
                    DrawCollisionPermissionSettings(settingsProp);
                }

                EditorGUILayout.LabelField(T("Stretch & Squish", "Stretch & Squish"), EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawClampedFloatWithCurve(settingsProp, "stretchMotion", "stretchMotionCurve", TT("Stretch Motion", "Stretch & Squishの動き量です。", "Stretch Motion", "Stretch motion amount."), 0f, 1f);
                    DrawClampedFloatWithCurve(settingsProp, "maxStretch", "maxStretchCurve", TT("Max Stretch", "Stretch & Squishの伸び量です。", "Max Stretch", "Stretch amount."), 0f, 1f);
                    DrawClampedFloatWithCurve(settingsProp, "maxSquish", "maxSquishCurve", TT("Max Squish", "Stretch & Squishの縮み量です。", "Max Squish", "Squish amount."), 0f, 1f);
                }

                EditorGUILayout.LabelField(T("Grab & Pose", "Grab & Pose"), EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawPermissionSettings(
                        settingsProp,
                        "allowGrabbing",
                        "grabAllowSelf",
                        "grabAllowOthers",
                        TT("Allow Grabbing", "Grabを許可します。", "Allow Grabbing", "Allows grabbing."));
                    DrawPermissionSettings(
                        settingsProp,
                        "allowPosing",
                        "poseAllowSelf",
                        "poseAllowOthers",
                        TT("Allow Posing", "Poseを許可します。", "Allow Posing", "Allows posing."));
                    DrawClampedFloat(settingsProp, "grabMovement", TT("Grab Movement", "Grab時の追従量です。", "Grab Movement", "Grab movement amount."), 0f, 1f);
                    EditorGUILayout.PropertyField(
                        settingsProp.FindPropertyRelative("snapToHand"),
                        TT("Snap To Hand", "Grab時に手へスナップします。", "Snap To Hand", "Snaps to the hand while grabbed."));
                }
            }
        }

        private void DrawLimitSettings(SerializedProperty settingsProp)
        {
            var limitTypeProp = settingsProp.FindPropertyRelative("limitType");
            EditorGUILayout.PropertyField(
                limitTypeProp,
                TT("Limit Type", "揺れの制限方式です。Noneでは角度制限を使いません。", "Limit Type", "Motion limit type. None disables angular limits."));

            var limitType = (SkirtRefinePhysBoneLimitType)limitTypeProp.enumValueIndex;
            switch (limitType)
            {
                case SkirtRefinePhysBoneLimitType.Angle:
                case SkirtRefinePhysBoneLimitType.Hinge:
                    DrawClampedFloatWithCurve(
                        settingsProp,
                        "maxAngle",
                        "maxAngleCurve",
                        TT("Max Angle", "角度制限の大きさです。", "Max Angle", "Angular limit amount."),
                        0f,
                        180f);
                    DrawLimitRotation(settingsProp);
                    break;
                case SkirtRefinePhysBoneLimitType.Polar:
                    DrawClampedFloatWithCurve(
                        settingsProp,
                        "maxAngle",
                        "maxAngleCurve",
                        TT("Max Pitch", "Pitch方向の角度制限です。", "Max Pitch", "Pitch angular limit."),
                        0f,
                        180f);
                    DrawClampedFloatWithCurve(
                        settingsProp,
                        "maxYaw",
                        "maxYawCurve",
                        TT("Max Yaw", "Yaw方向の角度制限です。", "Max Yaw", "Yaw angular limit."),
                        0f,
                        90f);
                    DrawLimitRotation(settingsProp);
                    break;
            }
        }

        private void DrawLimitRotation(SerializedProperty settingsProp)
        {
            EditorGUILayout.PropertyField(
                settingsProp.FindPropertyRelative("limitRotation"),
                TT("Rotation", "Limit Rotationです。PitchはXです。", "Rotation", "Limit rotation. Pitch is X."));
        }

        private void DrawCollisionPermissionSettings(SerializedProperty settingsProp)
        {
            var allowCollisionProp = settingsProp.FindPropertyRelative("allowCollision");
            EditorGUILayout.PropertyField(
                allowCollisionProp,
                TT("Allow Collision", "PhysBoneのコライダー判定を有効にします。", "Allow Collision", "Enables PhysBone collider checks."));

            if ((SkirtRefinePhysBonePermission)allowCollisionProp.enumValueIndex != SkirtRefinePhysBonePermission.Other) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(
                    settingsProp.FindPropertyRelative("collisionContentTypes"),
                    TT("Content Types", "判定対象のDynamics種別です。", "Content Types", "Dynamics content types to collide with."));
                EditorGUILayout.PropertyField(
                    settingsProp.FindPropertyRelative("collisionAllowSelf"),
                    new GUIContent("Allow Self"));
                EditorGUILayout.PropertyField(
                    settingsProp.FindPropertyRelative("collisionAllowOthers"),
                    new GUIContent("Allow Others"));
            }
        }

        private static void DrawPermissionSettings(
            SerializedProperty settingsProp,
            string permissionPropertyName,
            string allowSelfPropertyName,
            string allowOthersPropertyName,
            GUIContent label)
        {
            var permissionProp = settingsProp.FindPropertyRelative(permissionPropertyName);
            EditorGUILayout.PropertyField(permissionProp, label);
            if ((SkirtRefinePhysBonePermission)permissionProp.enumValueIndex != SkirtRefinePhysBonePermission.Other) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(
                    settingsProp.FindPropertyRelative(allowSelfPropertyName),
                    new GUIContent("Allow Self"));
                EditorGUILayout.PropertyField(
                    settingsProp.FindPropertyRelative(allowOthersPropertyName),
                    new GUIContent("Allow Others"));
            }
        }

        private static void DrawClampedFloat(
            SerializedProperty settingsProp,
            string propertyName,
            GUIContent label,
            float min,
            float max)
        {
            var prop = settingsProp.FindPropertyRelative(propertyName);
            EditorGUILayout.Slider(prop, min, max, label);
            prop.floatValue = Mathf.Clamp(prop.floatValue, min, max);
        }

        private static void DrawClampedFloatWithCurve(
            SerializedProperty settingsProp,
            string propertyName,
            string curvePropertyName,
            GUIContent label,
            float min,
            float max)
        {
            DrawClampedFloat(settingsProp, propertyName, label, min, max);
            DrawCurveWithoutLabel(settingsProp.FindPropertyRelative(curvePropertyName));
        }

        private static void DrawCurveWithoutLabel(SerializedProperty curveProp)
        {
            var curve = curveProp.animationCurveValue ?? AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            curve = EditorGUILayout.CurveField(new GUIContent(" "), curve, Color.green, new Rect(0.0f, 0.0f, 1.0f, 1.0f));
            ClampCurve01(curve);
            curveProp.animationCurveValue = curve;
        }

        private static void ClampCurve01(AnimationCurve curve)
        {
            if (curve == null) return;

            var keys = curve.keys;
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i].time = Mathf.Clamp01(keys[i].time);
                keys[i].value = Mathf.Clamp01(keys[i].value);
            }

            curve.keys = keys;
        }

        private static void ApplyOnePiecePhysBonePreset(SerializedProperty settingsProp, OnePiecePreset preset)
        {
            if (preset == OnePiecePreset.ShortSkirtLight)
            {
                ApplyShortLightPhysBonePreset(settingsProp);
            }
            else if (preset == OnePiecePreset.LongSkirtLight)
            {
                ApplyShortLightPhysBonePreset(settingsProp, 0.1f, CreateConvexRadiusCurve());
            }
            else if (preset == OnePiecePreset.ShortSkirtHeavy)
            {
                ApplyShortHeavyPhysBonePreset(settingsProp);
            }
            else if (preset == OnePiecePreset.LongSkirtHeavy)
            {
                ApplyShortHeavyPhysBonePreset(settingsProp, 0.1f, CreateConvexRadiusCurve());
            }
            else
            {
                ApplyInitialPhysBonePreset(settingsProp);
            }
        }

        private static void ApplyLongCoatPhysBonePreset(SerializedProperty settingsProp, LongCoatPreset preset)
        {
            if (preset == LongCoatPreset.ShortSkirtLight)
            {
                ApplyShortLightPhysBonePreset(settingsProp);
            }
            else if (preset == LongCoatPreset.LongSkirtLight)
            {
                ApplyLongCoatLongSkirtLightPhysBonePreset(settingsProp);
            }
            else if (preset == LongCoatPreset.ShortSkirtHeavy)
            {
                ApplyShortHeavyPhysBonePreset(settingsProp);
            }
            else if (preset == LongCoatPreset.LongSkirtHeavy)
            {
                ApplyLongCoatLongSkirtHeavyPhysBonePreset(settingsProp);
            }
            else if (preset == LongCoatPreset.OpenFront)
            {
                ApplyOpenFrontPhysBonePreset(settingsProp, 0.08f);
            }
            else
            {
                ApplyInitialPhysBonePreset(settingsProp);
            }
        }

        private static void ApplyShortLightPhysBonePreset(SerializedProperty settingsProp, float radius = 0.05f, AnimationCurve radiusCurve = null)
        {
            ApplyPhysBoneValues(settingsProp, 0.1f, 0.6f, 0.0f, 0.0f, 0.8f, 0.7f, SkirtRefinePhysBoneLimitType.Hinge, 45.0f, -45.0f, SkirtRefinePhysBonePermission.False, SkirtRefinePhysBonePermission.Other, 0.0f, 0.0f, radius, radiusCurve);
        }

        private static void ApplyShortHeavyPhysBonePreset(SerializedProperty settingsProp, float radius = 0.05f, AnimationCurve radiusCurve = null)
        {
            ApplyPhysBoneValues(settingsProp, 0.18f, 0.45f, 0.25f, 0.35f, 0.9f, 0.7f, SkirtRefinePhysBoneLimitType.Hinge, 45.0f, -45.0f, SkirtRefinePhysBonePermission.False, SkirtRefinePhysBonePermission.Other, 0.0f, 0.0f, radius, radiusCurve);
        }

        private static void ApplyOpenFrontPhysBonePreset(SerializedProperty settingsProp, float radius = 0.05f)
        {
            ApplyPhysBoneValues(settingsProp, 0.18f, 0.45f, 0.25f, 0.35f, 0.9f, 0.7f, SkirtRefinePhysBoneLimitType.Polar, 45.0f, -45.0f, SkirtRefinePhysBonePermission.False, SkirtRefinePhysBonePermission.Other, 0.0f, 30.0f, radius, CreateConvexRadiusCurve(), AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f));
        }

        private static void ApplyLongCoatLongSkirtLightPhysBonePreset(SerializedProperty settingsProp)
        {
            ApplyShortLightPhysBonePreset(settingsProp, 0.05f, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
        }

        private static void ApplyLongCoatLongSkirtHeavyPhysBonePreset(SerializedProperty settingsProp)
        {
            ApplyShortHeavyPhysBonePreset(settingsProp, 0.05f, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
        }

        private static void ApplyInitialPhysBonePreset(SerializedProperty settingsProp)
        {
            ApplyPhysBoneValues(settingsProp, 0.2f, 0.2f, 0.0f, 0.0f, 0.0f, 1.0f, SkirtRefinePhysBoneLimitType.None, 45.0f, 0.0f, SkirtRefinePhysBonePermission.True, SkirtRefinePhysBonePermission.False, 0.0f);
        }

        private static void ApplyPhysBoneValues(
            SerializedProperty settingsProp,
            float pull,
            float spring,
            float gravity,
            float gravityFalloff,
            float immobile,
            float immobileTipMultiplier,
            SkirtRefinePhysBoneLimitType limitType,
            float maxAngle,
            float rotationPitch,
            SkirtRefinePhysBonePermission allowCollision,
            SkirtRefinePhysBonePermission allowGrabbing,
            float stretchAndSquish,
            float maxYaw = 0.0f,
            float radius = 0.05f,
            AnimationCurve radiusCurve = null,
            AnimationCurve maxYawCurve = null)
        {
            settingsProp.FindPropertyRelative("version").enumValueIndex = (int)SkirtRefinePhysBoneVersion.Version1_1;
            settingsProp.FindPropertyRelative("endpointPosition").vector3Value = Vector3.zero;
            settingsProp.FindPropertyRelative("ignoreOtherPhysBones").boolValue = true;
            settingsProp.FindPropertyRelative("multiChildType").enumValueIndex = (int)SkirtRefinePhysBoneMultiChildType.Ignore;
            settingsProp.FindPropertyRelative("pull").floatValue = pull;
            settingsProp.FindPropertyRelative("pullCurve").animationCurveValue = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settingsProp.FindPropertyRelative("spring").floatValue = spring;
            settingsProp.FindPropertyRelative("springCurve").animationCurveValue = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settingsProp.FindPropertyRelative("gravity").floatValue = gravity;
            settingsProp.FindPropertyRelative("gravityCurve").animationCurveValue = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settingsProp.FindPropertyRelative("gravityFalloff").floatValue = gravityFalloff;
            settingsProp.FindPropertyRelative("gravityFalloffCurve").animationCurveValue = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settingsProp.FindPropertyRelative("immobileType").enumValueIndex = (int)SkirtRefinePhysBoneImmobileType.World;
            settingsProp.FindPropertyRelative("immobile").floatValue = immobile;
            settingsProp.FindPropertyRelative("immobileTipMultiplier").floatValue = immobileTipMultiplier;
            settingsProp.FindPropertyRelative("immobileCurve").animationCurveValue = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, immobileTipMultiplier);
            settingsProp.FindPropertyRelative("radius").floatValue = radius;
            settingsProp.FindPropertyRelative("radiusCurve").animationCurveValue = radiusCurve ?? AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);
            settingsProp.FindPropertyRelative("limitType").enumValueIndex = (int)limitType;
            settingsProp.FindPropertyRelative("maxAngle").floatValue = maxAngle;
            settingsProp.FindPropertyRelative("maxAngleCurve").animationCurveValue = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settingsProp.FindPropertyRelative("maxYaw").floatValue = maxYaw;
            settingsProp.FindPropertyRelative("maxYawCurve").animationCurveValue = maxYawCurve ?? AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settingsProp.FindPropertyRelative("limitRotation").vector3Value = new Vector3(rotationPitch, 0.0f, 0.0f);
            settingsProp.FindPropertyRelative("allowCollision").enumValueIndex = (int)allowCollision;
            settingsProp.FindPropertyRelative("collisionContentTypes").intValue = (int)DynamicsUsageFlags.Everything;
            settingsProp.FindPropertyRelative("collisionAllowSelf").boolValue = true;
            settingsProp.FindPropertyRelative("collisionAllowOthers").boolValue = true;
            settingsProp.FindPropertyRelative("allowGrabbing").enumValueIndex = (int)allowGrabbing;
            settingsProp.FindPropertyRelative("allowPosing").enumValueIndex = (int)SkirtRefinePhysBonePermission.False;
            settingsProp.FindPropertyRelative("grabAllowSelf").boolValue = allowGrabbing == SkirtRefinePhysBonePermission.Other;
            settingsProp.FindPropertyRelative("grabAllowOthers").boolValue = false;
            settingsProp.FindPropertyRelative("poseAllowSelf").boolValue = false;
            settingsProp.FindPropertyRelative("poseAllowOthers").boolValue = false;
            settingsProp.FindPropertyRelative("snapToHand").boolValue = false;
            settingsProp.FindPropertyRelative("grabMovement").floatValue = 0.0f;
            settingsProp.FindPropertyRelative("maxStretch").floatValue = stretchAndSquish;
            settingsProp.FindPropertyRelative("maxStretchCurve").animationCurveValue = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settingsProp.FindPropertyRelative("maxSquish").floatValue = stretchAndSquish;
            settingsProp.FindPropertyRelative("maxSquishCurve").animationCurveValue = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settingsProp.FindPropertyRelative("stretchMotion").floatValue = stretchAndSquish;
            settingsProp.FindPropertyRelative("stretchMotionCurve").animationCurveValue = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settingsProp.FindPropertyRelative("isAnimated").boolValue = false;
            settingsProp.FindPropertyRelative("resetWhenDisabled").boolValue = true;
            settingsProp.FindPropertyRelative("parameter").stringValue = string.Empty;
            settingsProp.FindPropertyRelative("showGizmos").boolValue = true;
            settingsProp.FindPropertyRelative("boneOpacity").floatValue = 0.2f;
            settingsProp.FindPropertyRelative("limitOpacity").floatValue = 0.2f;
        }

        private static AnimationCurve CreateConvexRadiusCurve()
        {
            return new AnimationCurve(
                new Keyframe(0.0f, 0.0f, 0.0f, 0.0f),
                new Keyframe(0.5f, 0.35f, 1.0f, 1.0f),
                new Keyframe(1.0f, 1.0f, 2.0f, 2.0f));
        }

        private static bool IsShortOnePiecePreset(OnePiecePreset preset)
        {
            return preset == OnePiecePreset.ShortSkirtLight
                || preset == OnePiecePreset.ShortSkirtHeavy;
        }

        private static bool IsLongCoatLongSkirtPreset(LongCoatPreset preset)
        {
            return preset == LongCoatPreset.LongSkirtLight
                || preset == LongCoatPreset.LongSkirtHeavy;
        }

        private static bool IsShortLongCoatPreset(LongCoatPreset preset)
        {
            return preset == LongCoatPreset.ShortSkirtLight
                || preset == LongCoatPreset.ShortSkirtHeavy;
        }

        private static bool UsesOnePieceUpperLegColliders(OnePiecePreset preset)
        {
            return preset == OnePiecePreset.ShortSkirtLight
                || preset == OnePiecePreset.ShortSkirtHeavy
                || preset == OnePiecePreset.LongSkirtLight
                || preset == OnePiecePreset.LongSkirtHeavy;
        }

        private static bool UsesOnePieceLowerLegColliders(OnePiecePreset preset)
        {
            return preset == OnePiecePreset.LongSkirtLight
                || preset == OnePiecePreset.LongSkirtHeavy;
        }

        private static bool UsesOnePieceFloorCollider(OnePiecePreset preset)
        {
            return preset == OnePiecePreset.LongSkirtLight
                || preset == OnePiecePreset.LongSkirtHeavy;
        }

        private static bool UsesLongCoatUpperLegColliders(LongCoatPreset preset)
        {
            return preset == LongCoatPreset.ShortSkirtLight
                || preset == LongCoatPreset.ShortSkirtHeavy
                || preset == LongCoatPreset.OpenFront;
        }

        private static bool UsesLongCoatLowerLegColliders(LongCoatPreset preset)
        {
            return preset == LongCoatPreset.LongSkirtLight
                || preset == LongCoatPreset.LongSkirtHeavy
                || preset == LongCoatPreset.OpenFront;
        }

        private static bool UsesLongCoatFloorCollider(LongCoatPreset preset)
        {
            return preset == LongCoatPreset.OpenFront;
        }

        private void DrawOnePieceRotationConstraintSettings()
        {
            EditorGUILayout.LabelField(T("Rotation Constraint", "Rotation Constraint"), EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(
                    onePieceUseFrontRootRotationConstraintsProp,
                    TT(
                        "正面の付け根に使用",
                        "ONの場合、Frontの1段目をUpperLegにRotation Constraintで連動させ、Frontの2段目以降は房ごとのPhysBoneで揺らします。統合RootのPhysBoneからFrontは除外されます。",
                        "Use on Front Roots",
                        "When enabled, front first-stage bones follow UpperLeg with Rotation Constraint, while front bones from the second stage use per-chain PhysBones. Front chains are ignored by the unified root PhysBone."));
                using (new EditorGUI.DisabledScope(!onePieceUseFrontRootRotationConstraintsProp.boolValue))
                {
                    EditorGUILayout.PropertyField(
                        onePieceMoveFrontRootsTowardUpperLegProp,
                        TT(
                            "FrontをUpperLegへ寄せる",
                            "Frontの1段目をUpperLeg寄りへ移動し、Rotation Constraint時の食い込みを抑えます。Hipウェイト調整との相性確認用に切り替えできます。",
                            "Move Roots Toward UpperLeg",
                            "Moves front first-stage roots toward UpperLeg to reduce clipping when using Rotation Constraint. Toggle this to test interaction with Hip weight reduction."));
                }
            }
        }

        private void DrawLongCoatRotationConstraintSettings()
        {
            EditorGUILayout.LabelField(T("Rotation Constraint", "Rotation Constraint"), EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(
                    longCoatUseFrontRootRotationConstraintsProp,
                    TT(
                        "正面の付け根に使用",
                        "ONの場合、Frontの1段目をUpperLegにRotation Constraintで連動させ、Frontの2段目以降は房ごとのPhysBoneで揺らします。統合RootのPhysBoneからFrontは除外されます。",
                        "Use on Front Roots",
                        "When enabled, front first-stage bones follow UpperLeg with Rotation Constraint, while front bones from the second stage use per-chain PhysBones. Front chains are ignored by the unified root PhysBone."));
                if (EditorGUI.EndChangeCheck() && longCoatUseFrontRootRotationConstraintsProp.boolValue)
                {
                    longCoatUseRotationConstraintsProp.boolValue = false;
                }

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(
                    longCoatUseRotationConstraintsProp,
                    TT(
                        "上3段のボーンに使用",
                        "ONの場合、追加された上3段のうち1段目をUpperLeg、3段目をLowerLegにRotation Constraintで連動させ、下3段を房ごとのPhysBoneで揺らします。",
                        "Use on Upper 3 Bones",
                        "When enabled, the first generated upper-stage bone follows UpperLeg and the third follows LowerLeg via Rotation Constraint; the lower three bones use per-chain PhysBones."));
                if (EditorGUI.EndChangeCheck() && longCoatUseRotationConstraintsProp.boolValue)
                {
                    longCoatUseFrontRootRotationConstraintsProp.boolValue = false;
                }

                var canMoveConstrainedRoots = longCoatUseFrontRootRotationConstraintsProp.boolValue
                    || longCoatUseRotationConstraintsProp.boolValue;
                using (new EditorGUI.DisabledScope(!canMoveConstrainedRoots))
                {
                    EditorGUILayout.PropertyField(
                        longCoatMoveConstrainedRootsTowardUpperLegProp,
                        TT(
                            "FrontをUpperLegへ寄せる",
                            "Rotation ConstraintでUpperLegに連動する付け根側ボーンをUpperLeg寄りへ移動し、脚上げ時の食い込みを抑えます。Hip/Spineウェイト調整との相性確認用に切り替えできます。",
                            "Move Roots Toward UpperLeg",
                            "Moves root-side bones driven by UpperLeg Rotation Constraint toward UpperLeg to reduce clipping during leg motion. Toggle this to test interaction with Hip/Spine weight reduction."));
                }

                var canAimFrontLimits = longCoatUseFrontRootRotationConstraintsProp.boolValue
                    || longCoatUseRotationConstraintsProp.boolValue;
                using (new EditorGUI.DisabledScope(!canAimFrontLimits))
                {
                    EditorGUILayout.PropertyField(
                        longCoatAimFrontLimitsForwardProp,
                        TT(
                            "FrontのLimitsを正面に向ける",
                            "Frontに個別追加されるPhysBoneのLimitsだけを正面寄りに向けます。統合RootのPhysBoneには適用されません。",
                            "Aim Front Limits Forward",
                            "Aims only the per-front-chain PhysBone limits forward. This does not apply to unified-root PhysBones."));
                }
            }
        }

        private void DrawAdvancedSection()
        {
            EditorGUI.BeginChangeCheck();
            advancedFoldout = EditorGUILayout.Foldout(advancedFoldout, T("Advanced", "Advanced"), true);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(PrefKeyAdvancedFoldout, advancedFoldout);
            }

            if (!advancedFoldout) return;

            using (new EditorGUI.IndentLevelScope())
            {
                DrawConstraintModePopup();
                EditorGUILayout.PropertyField(
                    verboseLogProp,
                    TT(
                        "Verbose Log",
                        "ビルド時の検出ログを詳しく出力します。",
                        "Verbose Log",
                        "Outputs detailed detection logs during the build."));
            }
        }

        private void DrawConstraintModePopup()
        {
            var options = language == Language.Japanese ? ConstraintModeJa : ConstraintModeEn;
            var current = constraintModeProp.enumValueIndex;

            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.Popup(
                TT(
                    "Constraint Mode",
                    "VRChat ConstraintsとUnity Constraintsを選択します。Unity ConstraintsはVRChat以外用の互換オプションです。",
                    "Constraint Mode",
                    "Choose between VRChat Constraints and Unity Constraints. Unity Constraints is intended for non-VRChat compatibility workflows."),
                current,
                options);
            if (EditorGUI.EndChangeCheck())
            {
                constraintModeProp.enumValueIndex = next;
            }
        }

        private static bool HasMissingBone(SerializedProperty bonesProp)
        {
            return bonesProp.FindPropertyRelative("frontLeft").objectReferenceValue == null
                || bonesProp.FindPropertyRelative("frontRight").objectReferenceValue == null
                || bonesProp.FindPropertyRelative("sideLeft").objectReferenceValue == null
                || bonesProp.FindPropertyRelative("sideRight").objectReferenceValue == null
                || bonesProp.FindPropertyRelative("backLeft").objectReferenceValue == null
                || bonesProp.FindPropertyRelative("backRight").objectReferenceValue == null;
        }

        private static void AutoDetectBonesAndAutoEnableIfNeeded(YMVRoidSkirtRefine component)
        {
            if (component == null) return;

            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            var animator = avatarRoot != null
                ? avatarRoot.GetComponentInChildren<Animator>(true)
                : component.GetComponentInParent<Animator>(true);

            if (animator == null) return;

            var changed = false;
            var onePieceDetected = AutoDetectOnePieceBones(component.onePieceBones, animator);
            var longCoatDetected = AutoDetectLongCoatBones(component.longCoatBones, animator);
            changed |= onePieceDetected;
            changed |= longCoatDetected;
            changed |= AutoEnableRefinesIfNewOrCopied(component, onePieceDetected, longCoatDetected);

            if (changed)
            {
                EditorUtility.SetDirty(component);
            }
        }

        private static bool AutoEnableRefinesIfNewOrCopied(
            YMVRoidSkirtRefine component,
            bool onePieceDetected,
            bool longCoatDetected)
        {
            if (component == null) return false;

            var objectId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            if (string.IsNullOrEmpty(objectId) || component.autoEnableRefinesObjectId == objectId)
            {
                return false;
            }

            if (onePieceDetected && HasAllBones(component.onePieceBones) && !component.enableOnePieceRefine)
            {
                var hasLongCoatRefine = component.enableLongCoatRefine
                    || (longCoatDetected && HasAllBones(component.longCoatBones));
                component.enableOnePieceRefine = true;
                ApplyAutoDetectedOnePiecePreset(component, hasLongCoatRefine);
            }

            if (longCoatDetected && HasAllBones(component.longCoatBones) && !component.enableLongCoatRefine)
            {
                component.enableLongCoatRefine = true;
                ApplyAutoDetectedLongCoatPreset(component);
            }

            component.autoEnableRefinesObjectId = objectId;
            return true;
        }

        private static void ApplyAutoDetectedOnePiecePreset(YMVRoidSkirtRefine component, bool hasLongCoatTarget)
        {
            if (component == null) return;

            var serialized = new SerializedObject(component);
            var preset = hasLongCoatTarget ? OnePiecePreset.MatchLongCoat : OnePiecePreset.ShortSkirtLight;
            serialized.FindProperty("onePiecePreset").enumValueIndex = (int)preset;
            ApplyOnePiecePhysBonePreset(serialized.FindProperty("onePiecePhysBone"), preset);
            serialized.FindProperty("onePieceMatchLongCoat").boolValue = preset == OnePiecePreset.MatchLongCoat;
            serialized.FindProperty("enableOnePieceBoneExtension").boolValue = !IsShortOnePiecePreset(preset);
            serialized.FindProperty("onePieceUseUpperLegColliders").boolValue = UsesOnePieceUpperLegColliders(preset);
            serialized.FindProperty("onePieceUseLowerLegColliders").boolValue = UsesOnePieceLowerLegColliders(preset);
            serialized.FindProperty("onePieceUseFloorCollider").boolValue = UsesOnePieceFloorCollider(preset);
            serialized.FindProperty("onePieceUseFrontRootRotationConstraints").boolValue = preset != OnePiecePreset.MatchLongCoat;
            serialized.FindProperty("onePieceMoveFrontRootsTowardUpperLeg").boolValue = false;
            serialized.FindProperty("onePieceRootHeightOffsetMultiplier").floatValue = preset == OnePiecePreset.MatchLongCoat ? 0.0f : 0.4f;
            serialized.FindProperty("onePieceHipWeightReduction").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyAutoDetectedLongCoatPreset(YMVRoidSkirtRefine component)
        {
            if (component == null) return;

            var serialized = new SerializedObject(component);
            const LongCoatPreset preset = LongCoatPreset.LongSkirtHeavy;
            serialized.FindProperty("longCoatPreset").enumValueIndex = (int)preset;
            ApplyLongCoatPhysBonePreset(serialized.FindProperty("longCoatPhysBone"), preset);
            serialized.FindProperty("longCoatMatchOnePiece").boolValue = false;
            serialized.FindProperty("enableLongCoatBoneExtension").boolValue = true;
            serialized.FindProperty("longCoatShortSkirtUsePrependedRootsOnly").boolValue = false;
            serialized.FindProperty("longCoatMoveFrontBonesOutward").boolValue = false;
            serialized.FindProperty("longCoatUseRotationConstraints").boolValue = true;
            serialized.FindProperty("longCoatUseFrontRootRotationConstraints").boolValue = false;
            serialized.FindProperty("longCoatMoveConstrainedRootsTowardUpperLeg").boolValue = true;
            serialized.FindProperty("longCoatAimFrontLimitsForward").boolValue = true;
            serialized.FindProperty("longCoatUseUpperLegColliders").boolValue = UsesLongCoatUpperLegColliders(preset);
            serialized.FindProperty("longCoatUseLowerLegColliders").boolValue = UsesLongCoatLowerLegColliders(preset);
            serialized.FindProperty("longCoatUseFloorCollider").boolValue = UsesLongCoatFloorCollider(preset);
            serialized.FindProperty("longCoatRootHeightOffsetMultiplier").floatValue = preset == LongCoatPreset.OpenFront ? 2.0f : 1.0f;
            serialized.FindProperty("longCoatHipWeightReduction").floatValue = preset == LongCoatPreset.OpenFront ? 0.8f : 0.5f;
            serialized.FindProperty("longCoatSpineWeightReduction").floatValue = preset == LongCoatPreset.OpenFront ? 0.8f : 0.0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool HasAllBones(SkirtRefineBoneTargets bones)
        {
            return bones != null
                && bones.frontLeft != null
                && bones.frontRight != null
                && bones.sideLeft != null
                && bones.sideRight != null
                && bones.backLeft != null
                && bones.backRight != null;
        }

        private static bool AutoDetectOnePieceBones(SkirtRefineBoneTargets bones, Animator animator)
        {
            if (bones == null || animator == null) return false;

            var changed = false;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);

            changed |= AssignIfNull(bones, BoneTargetKind.FrontLeft, FindDescendantByPartialName(leftUpperLeg, "L_SkirtFront"));
            changed |= AssignIfNull(bones, BoneTargetKind.FrontRight, FindDescendantByPartialName(rightUpperLeg, "R_SkirtFront"));
            changed |= AssignIfNull(bones, BoneTargetKind.SideLeft, FindDescendantByPartialName(hips, "L_SkirtSide"));
            changed |= AssignIfNull(bones, BoneTargetKind.SideRight, FindDescendantByPartialName(hips, "R_SkirtSide"));
            changed |= AssignIfNull(bones, BoneTargetKind.BackLeft, FindDescendantByPartialName(leftUpperLeg, "L_SkirtBack"));
            changed |= AssignIfNull(bones, BoneTargetKind.BackRight, FindDescendantByPartialName(rightUpperLeg, "R_SkirtBack"));
            return changed;
        }

        private static bool AutoDetectLongCoatBones(SkirtRefineBoneTargets bones, Animator animator)
        {
            if (bones == null || animator == null) return false;

            var changed = false;
            var leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);

            changed |= AssignIfNull(bones, BoneTargetKind.FrontLeft, FindDescendantByPartialName(leftLowerLeg, "L_CoatSkirtFront"));
            changed |= AssignIfNull(bones, BoneTargetKind.FrontRight, FindDescendantByPartialName(rightLowerLeg, "R_CoatSkirtFront"));
            changed |= AssignIfNull(bones, BoneTargetKind.SideLeft, FindDescendantByPartialName(leftLowerLeg, "L_CoatSkirtSide"));
            changed |= AssignIfNull(bones, BoneTargetKind.SideRight, FindDescendantByPartialName(rightLowerLeg, "R_CoatSkirtSide"));
            changed |= AssignIfNull(bones, BoneTargetKind.BackLeft, FindDescendantByPartialName(leftLowerLeg, "L_CoatSkirtBack"));
            changed |= AssignIfNull(bones, BoneTargetKind.BackRight, FindDescendantByPartialName(rightLowerLeg, "R_CoatSkirtBack"));
            return changed;
        }

        private static Transform FindDescendantByPartialName(Transform root, string partialName)
        {
            if (root == null || string.IsNullOrEmpty(partialName)) return null;

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || string.IsNullOrEmpty(transform.name)) continue;
                if (transform.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return transform;
                }
            }

            return null;
        }

        private static bool AssignIfNull(SkirtRefineBoneTargets bones, BoneTargetKind kind, Transform value)
        {
            if (bones == null || value == null) return false;

            switch (kind)
            {
                case BoneTargetKind.FrontLeft:
                    if (bones.frontLeft != null) return false;
                    bones.frontLeft = value;
                    return true;
                case BoneTargetKind.FrontRight:
                    if (bones.frontRight != null) return false;
                    bones.frontRight = value;
                    return true;
                case BoneTargetKind.SideLeft:
                    if (bones.sideLeft != null) return false;
                    bones.sideLeft = value;
                    return true;
                case BoneTargetKind.SideRight:
                    if (bones.sideRight != null) return false;
                    bones.sideRight = value;
                    return true;
                case BoneTargetKind.BackLeft:
                    if (bones.backLeft != null) return false;
                    bones.backLeft = value;
                    return true;
                case BoneTargetKind.BackRight:
                    if (bones.backRight != null) return false;
                    bones.backRight = value;
                    return true;
                default:
                    return false;
            }
        }

        private string T(string ja, string en)
        {
            return language == Language.Japanese ? ja : en;
        }

        private GUIContent TT(string ja, string jaTooltip, string en, string enTooltip)
        {
            return language == Language.Japanese
                ? new GUIContent(ja, jaTooltip)
                : new GUIContent(en, enTooltip);
        }
    }
}
