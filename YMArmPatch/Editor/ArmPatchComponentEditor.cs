using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.ArmPatch
{
    [CustomEditor(typeof(ArmPatchComponent))]
    public sealed class ArmPatchComponentEditor : UnityEditor.Editor
    {
        private enum Language
        {
            Japanese,
            English
        }

        private const string PrefKeyLanguage = "ArmPatchComponentEditor.Language";
        private const string PrefKeyAdvancedFoldout = "ArmPatchComponentEditor.AdvancedFoldout";

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

        private static readonly string[] BuildOrderJa =
        {
            "After Modular Avatar",
            "Before Modular Avatar"
        };

        private static readonly string[] BuildOrderEn =
        {
            "After Modular Avatar",
            "Before Modular Avatar"
        };

        private SerializedProperty enableShoulderFixProp;
        private SerializedProperty shoulderPositionOffsetProp;
        private SerializedProperty shoulderEulerOffsetProp;
        private SerializedProperty upperArmRollAxisProp;
        private SerializedProperty upperArmRollWeightProp;

        private SerializedProperty enableForearmFixProp;
        private SerializedProperty forearmElbowScaleProp;
        private SerializedProperty forearmWristScaleProp;
        private SerializedProperty forearmElbowRollOffsetProp;
        private SerializedProperty forearmRollAxisProp;
        private SerializedProperty forearmPitchAxisProp;
        private SerializedProperty forearmRollWeightProp;
        private SerializedProperty forearmTwistBoneTypeProp;
        private SerializedProperty forearmTwistBoneCountProp;
        private SerializedProperty forearmSkinMaterialNameProp;

        private SerializedProperty enableThumbFixProp;
        private SerializedProperty thumbEulerOffsetProp;

        private SerializedProperty constraintModeProp;
        private SerializedProperty buildOrderProp;
        private SerializedProperty verboseLogProp;

        private Language language;
        private bool advancedFoldout;
        private bool previewFailed;

        private const float MainLabelWidth = 84f;
        private const float SubLabelWidth = 110f;
        private const float Gap = 8f;
        private const float ToggleWidth = 16f;

        private void OnEnable()
        {
            enableShoulderFixProp = serializedObject.FindProperty("enableShoulderFix");
            shoulderPositionOffsetProp = serializedObject.FindProperty("shoulderPositionOffset");
            shoulderEulerOffsetProp = serializedObject.FindProperty("shoulderEulerOffset");
            upperArmRollAxisProp = serializedObject.FindProperty("upperArmRollAxis");
            upperArmRollWeightProp = serializedObject.FindProperty("upperArmRollWeight");

            enableForearmFixProp = serializedObject.FindProperty("enableForearmFix");
            foreach (var t in targets)
            {
                if (t is ArmPatchComponent component) component.MigrateSerializedValuesIfNeeded();
            }

            forearmElbowScaleProp = serializedObject.FindProperty("forearmElbowScale");
            forearmWristScaleProp = serializedObject.FindProperty("forearmWristScale");
            forearmElbowRollOffsetProp = serializedObject.FindProperty("forearmElbowRollOffset");
            forearmRollAxisProp = serializedObject.FindProperty("forearmRollAxis");
            forearmPitchAxisProp = serializedObject.FindProperty("forearmPitchAxis");
            forearmRollWeightProp = serializedObject.FindProperty("forearmRollWeight");
            forearmTwistBoneTypeProp = serializedObject.FindProperty("forearmTwistBoneType");
            forearmTwistBoneCountProp = serializedObject.FindProperty("forearmTwistBoneCount");
            forearmSkinMaterialNameProp = serializedObject.FindProperty("forearmSkinMaterialName");

            enableThumbFixProp = serializedObject.FindProperty("enableThumbFix");
            thumbEulerOffsetProp = serializedObject.FindProperty("thumbEulerOffset");

            constraintModeProp = serializedObject.FindProperty("constraintMode");
            buildOrderProp = serializedObject.FindProperty("buildOrder");
            verboseLogProp = serializedObject.FindProperty("verboseLog");

            language = (Language)EditorPrefs.GetInt(PrefKeyLanguage, 0);
            advancedFoldout = EditorPrefs.GetBool(PrefKeyAdvancedFoldout, false);
        }

        public override void OnInspectorGUI()
        {
            var component = (ArmPatchComponent)target;
            var avatarRoot = FindAvatarRootForComponent(component);
            bool isPreviewing = ArmPatchPreviewUtility.IsPreviewing(avatarRoot);

            serializedObject.Update();
            EditorGUI.BeginChangeCheck();

            DrawTopRow(component, isPreviewing);
            EditorGUILayout.Space(4);

            DrawMultipleComponentsWarning(component, avatarRoot);
            EditorGUILayout.Space(4);

            DrawInfoBox();
            EditorGUILayout.Space(6);

            DrawShoulderRows();
            EditorGUILayout.Space(2);
            DrawForearmRows(component);
            EditorGUILayout.Space(2);
            DrawThumbRow();
            EditorGUILayout.Space(8);

            DrawAdvancedSection();

            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            if (changed && isPreviewing)
            {
                ArmPatchPreviewUtility.RestartPreviewIfActive(component);
            }
        }

        private void DrawTopRow(ArmPatchComponent component, bool isPreviewing)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (PreviewInspectorGui.DrawPreviewButton(isPreviewing, T("Preview", "Preview")))
                {
                    serializedObject.ApplyModifiedProperties();
                    previewFailed = !ArmPatchPreviewUtility.TogglePreview(component);
                    GUIUtility.ExitGUI();
                }

                PreviewInspectorGui.DrawStatus(false, previewFailed);

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

        private void DrawInfoBox()
        {
            int constraintCount = 0;

            if (enableShoulderFixProp.boolValue) constraintCount += 4;
            if (enableForearmFixProp.boolValue)
            {
                int forearmCount = forearmTwistBoneCountProp != null ? forearmTwistBoneCountProp.intValue : 0;
                int perSide = forearmCount == 0 ? 2 : 2 + forearmCount;
                constraintCount += perSide * 2;
            }
            if (enableThumbFixProp.boolValue) constraintCount += 6;

            string message = T(
                $"・肘周辺がわずかに歪む場合があります。\n・現在の設定で増えるコンストレイント数: {constraintCount} 個",
                $"• Elbow area may deform slightly.\n• Additional constraints with current settings: {constraintCount}"
            );

            EditorGUILayout.HelpBox(message, MessageType.Info);
        }

        private void DrawMultipleComponentsWarning(ArmPatchComponent component, GameObject avatarRoot)
        {
            if (component == null || avatarRoot == null) return;

            var components = avatarRoot.GetComponentsInChildren<ArmPatchComponent>(true);
            if (components == null || components.Length <= 1) return;

            var selected = SelectPreferredComponentForBuild(components, avatarRoot);
            bool thisWillBeUsed = selected == component;

            string message = thisWillBeUsed
                ? T("複数箇所で設定されています。 このコンポーネントの設定値が使用されます。", "This component is configured in multiple places. The values on this component will be used.")
                : T("複数箇所で設定されています。 このコンポーネントでの設定は無視されます。", "This component is configured in multiple places. The values on this component will be ignored.");

            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        private static ArmPatchComponent SelectPreferredComponentForBuild(
            ArmPatchComponent[] components,
            GameObject avatarRoot)
        {
            ArmPatchComponent best = components[0];
            int bestScore = int.MinValue;
            Transform root = avatarRoot != null ? avatarRoot.transform : null;

            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                int depth = PreviewCoordinator.GetDepthFromRoot(c.transform, root);
                int score = -depth * 10000 - i;
                if (score > bestScore)
                {
                    best = c;
                    bestScore = score;
                }
            }

            return best;
        }
        private void DrawShoulderRows()
        {
            DrawShoulderMainRow();

            using (new EditorGUI.DisabledScope(!enableShoulderFixProp.boolValue))
            {
                DrawShoulderSubRow(
                    T("Roll Weight", "Roll Weight"),
                    upperArmRollWeightProp,
                    T("ねじれ軸だけ元 UpperArm に寄せる強さ。", "How strongly the twist axis follows the original UpperArm.")
                );

                DrawShoulderSubRow(
                    T("Position Offset", "Position Offset"),
                    shoulderPositionOffsetProp,
                    T(
                        "肩ボーンの位置オフセット。 Yに0.01ほど入れると VRM Converter for VRChat と近い結果になります。",
                        "Local position offset relative to the shoulder bone."
                    )
                );

                DrawShoulderSubRow(
                    T("Euler Offset", "Euler Offset"),
                    shoulderEulerOffsetProp,
                    T(
                        "肩に加える回転オフセット。 右肩は内部で自動反転して適用されます。",
                        "Rotation offset applied to shoulders. The right shoulder is mirrored internally."
                    )
                );
            }
        }

        private void DrawShoulderMainRow()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

            Rect toggleRect = new Rect(rect.x, rect.y, ToggleWidth, rect.height);
            Rect mainLabelRect = new Rect(toggleRect.xMax + 2f, rect.y, MainLabelWidth, rect.height);
            Rect subLabelRect = new Rect(mainLabelRect.xMax + Gap, rect.y, SubLabelWidth, rect.height);
            Rect valueRect = new Rect(subLabelRect.xMax + 4f, rect.y, rect.xMax - (subLabelRect.xMax + 4f), rect.height);

            enableShoulderFixProp.boolValue = EditorGUI.Toggle(toggleRect, enableShoulderFixProp.boolValue);
            EditorGUI.LabelField(mainLabelRect, new GUIContent(
                T("Shoulder Fix", "Shoulder Fix"),
                T("なで肩やいかり肩を補正できます。 肘の位置は変えないため補正した分だけ上腕が伸び縮みします。", "Can correct sloped or raised shoulders. Elbow position is not moved, so upper arms stretch/shrink by the correction amount.")
            ));

            using (new EditorGUI.DisabledScope(!enableShoulderFixProp.boolValue))
            {
                EditorGUI.LabelField(
                    subLabelRect,
                    new GUIContent(
                        T("Roll Axis", "Roll Axis"),
                        T("ねじれ補正で使う軸。初期値は X。", "Axis used for twist correction. Default is X.")
                    )
                );
                DrawAxisToolbar(valueRect, upperArmRollAxisProp);
            }
        }

        private void DrawForearmRows(ArmPatchComponent component)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

            Rect toggleRect = new Rect(rect.x, rect.y, ToggleWidth, rect.height);
            Rect mainLabelRect = new Rect(toggleRect.xMax + 2f, rect.y, MainLabelWidth, rect.height);
            Rect subLabelRect = new Rect(mainLabelRect.xMax + Gap, rect.y, SubLabelWidth, rect.height);
            Rect valueRect = new Rect(subLabelRect.xMax + 4f, rect.y, rect.xMax - (subLabelRect.xMax + 4f), rect.height);

            enableForearmFixProp.boolValue = EditorGUI.Toggle(toggleRect, enableForearmFixProp.boolValue);
            EditorGUI.LabelField(
                mainLabelRect,
                new GUIContent(
                    T("Forearm Fix", "Forearm Fix"),
                    T(
                        "前腕の見た目骨に、 スケール補正とツイスト補正を同時に適用します。",
                        "Applies both scale correction and forearm twist correction to the forearm display bone."
                    )
                )
            );

            using (new EditorGUI.DisabledScope(!enableForearmFixProp.boolValue))
            {
                EditorGUI.LabelField(
                    subLabelRect,
                    new GUIContent(
                        T("Roll Axis", "Roll Axis"),
                        T("ねじれ補正で使う軸。初期値はX。", "Axis used for twist correction. Default is X.")
                    )
                );
                DrawAxisToolbar(valueRect, forearmRollAxisProp);
                DrawForearmPitchAxisRow(component); // pitch after roll in requested order
                DrawTwistBoneCountRow();
                DrawTwistTargetRow(component);
                using (new EditorGUI.DisabledScope(forearmTwistBoneCountProp.intValue == 0))
                {
                    DrawSubRowSlider(
                        T("Elbow Roll Offset", "Elbow Roll Offset"),
                        forearmElbowRollOffsetProp,
                        -90f,
                        90f,
                        T("肘付近の初期姿勢を補正できます。 VRoid製着物の場合は Twist Target で肌を指定して、 Offsetを-70ほどにすると、袖が下を向きます。", "Can correct initial posture near the elbow. For VRoid kimono, after selecting skin in Twist Target, setting this offset to -90 can point sleeves downward.")
                    );
                }
                DrawScaleRow(T("Elbow Scale", "Elbow Scale"), forearmElbowScaleProp, T("肘側の前腕スケール。\nTwist Bone Count が0のときは、 Wrist Scale との平均値が使用されます。", "Forearm scale at the elbow side. When Twist Bone Count is 0, the average with Wrist Scale is used."));
                DrawScaleRow(T("Wrist Scale", "Wrist Scale"), forearmWristScaleProp, T("手首側の前腕スケール。\nTwist Bone Count が0のときは、 Elbow Scale との平均値が使用されます。", "Forearm scale at the wrist side. When Twist Bone Count is 0, the average with Elbow Scale is used."));
            }
        }

        private void DrawForearmPitchAxisRow(ArmPatchComponent component)
        {
            EnsurePitchAxis(component);
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect spacerRect = new Rect(rect.x, rect.y, ToggleWidth, rect.height);
            Rect mainLabelRect = new Rect(spacerRect.xMax + 2f, rect.y, MainLabelWidth, rect.height);
            Rect subLabelRect = new Rect(mainLabelRect.xMax + Gap, rect.y, SubLabelWidth, rect.height);
            Rect valueRect = new Rect(subLabelRect.xMax + 4f, rect.y, rect.xMax - (subLabelRect.xMax + 4f), rect.height);
            EditorGUI.LabelField(mainLabelRect, GUIContent.none);
            EditorGUI.LabelField(subLabelRect, new GUIContent(T("Pitch Axis", "Pitch Axis"), T("手首を大きく反らせると前腕がねじれる場合は、 この軸を変えると改善します。\nRoll Axis と同じ軸は選べず、 選ぼうとすると自動で適切な軸へ変更します。", "If the forearm twists too sharply when bending the wrist far back, changing this axis can help. You cannot choose the same axis as Roll Axis; if you try, it is automatically changed to an appropriate axis.")));

            int roll = forearmRollAxisProp.enumValueIndex;
            var labels = new[] { "X", "Y", "Z" };
            int current = forearmPitchAxisProp.enumValueIndex;
            if (current == roll) current = DetectPitchAxis(component, roll);

            int next = GUI.Toolbar(valueRect, current, labels);
            forearmPitchAxisProp.enumValueIndex = next;
            if (forearmPitchAxisProp.enumValueIndex == roll)
            {
                forearmPitchAxisProp.enumValueIndex = DetectPitchAxis(component, roll);
            }
        }

        private void DrawScaleRow(string label, SerializedProperty property, string tooltip)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect spacerRect = new Rect(rect.x, rect.y, ToggleWidth, rect.height);
            Rect mainLabelRect = new Rect(spacerRect.xMax + 2f, rect.y, MainLabelWidth, rect.height);
            Rect subLabelRect = new Rect(mainLabelRect.xMax + Gap, rect.y, SubLabelWidth, rect.height);
            Rect valueRect = new Rect(subLabelRect.xMax + 4f, rect.y, rect.xMax - (subLabelRect.xMax + 4f), rect.height);
            EditorGUI.LabelField(mainLabelRect, GUIContent.none);
            EditorGUI.LabelField(subLabelRect, new GUIContent(label, tooltip));
            EditorGUI.PropertyField(valueRect, property, GUIContent.none);
        }

        private void EnsurePitchAxis(ArmPatchComponent component)
        {
            if (forearmPitchAxisProp.enumValueIndex == forearmRollAxisProp.enumValueIndex)
            {
                forearmPitchAxisProp.enumValueIndex = DetectPitchAxis(component, forearmRollAxisProp.enumValueIndex);
            }
        }

        private int DetectPitchAxis(ArmPatchComponent component, int rollAxisIndex)
        {
            var avatarRoot = FindAvatarRootForComponent(component);
            var hand = avatarRoot != null ? avatarRoot.GetComponentInChildren<Animator>(true)?.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            if (hand == null) return rollAxisIndex == 0 ? 1 : 0;
            Vector3 avatarUp = avatarRoot.transform.up.normalized;
            int[] candidates = rollAxisIndex == 0 ? new[] { 1, 2 } : rollAxisIndex == 1 ? new[] { 0, 2 } : new[] { 0, 1 };
            int handBackAxis = candidates[0];
            float best = float.NegativeInfinity;
            foreach (var a in candidates)
            {
                Vector3 axisWorld = hand.TransformDirection(a == 0 ? Vector3.right : a == 1 ? Vector3.up : Vector3.forward).normalized;
                float dot = Mathf.Abs(Vector3.Dot(axisWorld, avatarUp));
                if (dot > best) { best = dot; handBackAxis = a; }
            }
            for (int a = 0; a < 3; a++) if (a != rollAxisIndex && a != handBackAxis) return a;
            return candidates[0];
        }

        private void DrawThumbRow()
        {
            DrawInlineRow(
                enableThumbFixProp,
                T("Thumb Fix", "Thumb Fix"),
                T("親指の初期姿勢を補正できます。", "Can correct the initial thumb posture."),
                thumbEulerOffsetProp,
                T("Euler Offset", "Euler Offset"),
                T(
                    "親指全体に加える回転オフセット。 右手は内部で自動反転して適用されます。 VRM0.0経由のVRoidは(0,-15,-15)がおすすめです。",
                    "Shared thumb rotation offset. Right side is mirrored internally."
                )
            );
        }

        private void DrawTwistTargetRow(ArmPatchComponent component)
        {
            var candidates = CollectMaterialCandidates(component);
            var options = new List<string> { "Auto" };
            options.AddRange(candidates);
            int selected = Mathf.Max(0, options.IndexOf(string.IsNullOrEmpty(forearmSkinMaterialNameProp.stringValue) ? "Auto" : forearmSkinMaterialNameProp.stringValue));
            using (new EditorGUI.DisabledScope(forearmTwistBoneCountProp.intValue == 0))
            {
                int next = DrawSubRowPopup(T("Twist Target", "Twist Target"), options.ToArray(), selected, T("Autoの場合、 前腕関連のウェイトのある部位を全てねじります。 肌を指定すると残りの服は固定されるため、 着物袖などが手首に振り回されなくなりますが、 手袋などが乱れます。", "Auto twists all forearm-related weighted parts; selecting skin fixes non-skin clothes but may disturb gloves."));
                forearmSkinMaterialNameProp.stringValue = options[next];
            }
            if (forearmTwistBoneCountProp.intValue != 0 && forearmSkinMaterialNameProp.stringValue != "Auto" && !HasForearmWeightsOnMaterial(component, forearmSkinMaterialNameProp.stringValue))
            {
                EditorGUILayout.HelpBox("前腕関連のウェイトが設定されていない部位のため、正常に動作しません。", MessageType.Warning);
            }
        }

        private void DrawTwistBoneCountRow()
        {
            string[] countLabels = { "0", "4", "6", "8" };
            int[] enumIndices =
            {
                (int)ForearmTwistBoneCount.Count0,
                (int)ForearmTwistBoneCount.Count4,
                (int)ForearmTwistBoneCount.Count6,
                (int)ForearmTwistBoneCount.Count8
            };

            int selectedIdx = Array.IndexOf(enumIndices, forearmTwistBoneCountProp.intValue);
            if (selectedIdx < 0) selectedIdx = 0;

            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect spacerRect = new Rect(rect.x, rect.y, ToggleWidth, rect.height);
            Rect mainLabelRect = new Rect(spacerRect.xMax + 2f, rect.y, MainLabelWidth, rect.height);
            Rect subLabelRect = new Rect(mainLabelRect.xMax + Gap, rect.y, SubLabelWidth, rect.height);
            Rect valueRect = new Rect(subLabelRect.xMax + 4f, rect.y, rect.xMax - (subLabelRect.xMax + 4f), rect.height);

            EditorGUI.LabelField(mainLabelRect, GUIContent.none);
            EditorGUI.LabelField(subLabelRect, new GUIContent(
                T("Twist Bone Count", "Twist Bone Count"),
                T(
                    "0は前腕をねじらず手首に追従させるため、肘が乱れることがありますが、長袖ならほとんど気になりません。 腕が露出している場合は8をおすすめします。",
                    "0 follows the wrist without forearm twist, so elbows may look unstable, but this is usually fine with long sleeves. If arms are exposed, 8 is recommended."
                )));
            int next = GUI.Toolbar(valueRect, selectedIdx, countLabels);
            forearmTwistBoneCountProp.intValue = enumIndices[next];
            forearmTwistBoneTypeProp.enumValueIndex = forearmTwistBoneCountProp.intValue == 0 ? (int)ForearmTwistBoneType.None : (forearmSkinMaterialNameProp.stringValue == "Auto" || string.IsNullOrEmpty(forearmSkinMaterialNameProp.stringValue) ? (int)ForearmTwistBoneType.AllTwist : (int)ForearmTwistBoneType.SkinOnly);
        }

        private int DrawSubRowPopup(string label, string[] options, int selectedIndex, string tooltip)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect spacerRect = new Rect(rect.x, rect.y, ToggleWidth, rect.height);
            Rect mainLabelRect = new Rect(spacerRect.xMax + 2f, rect.y, MainLabelWidth, rect.height);
            Rect subLabelRect = new Rect(mainLabelRect.xMax + Gap, rect.y, SubLabelWidth, rect.height);
            Rect valueRect = new Rect(subLabelRect.xMax + 4f, rect.y, rect.xMax - (subLabelRect.xMax + 4f), rect.height);

            EditorGUI.LabelField(mainLabelRect, GUIContent.none);
            EditorGUI.LabelField(subLabelRect, new GUIContent(label, tooltip));
            return EditorGUI.Popup(valueRect, selectedIndex, options);
        }

        private static void DrawAxisToolbar(Rect rect, SerializedProperty axisProperty)
        {
            string[] axisLabels = { "X", "Y", "Z" };
            axisProperty.enumValueIndex = GUI.Toolbar(rect, axisProperty.enumValueIndex, axisLabels);
        }

        private static List<string> CollectMaterialCandidates(ArmPatchComponent component)
        {
            var results = new List<string>();
            if (component == null) return results;

            var avatarRoot = FindAvatarRootForComponent(component);
            Transform searchRoot = avatarRoot != null ? avatarRoot.transform : component.transform;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var renderer in searchRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat == null || string.IsNullOrEmpty(mat.name)) continue;
                if (seen.Add(mat.name)) results.Add(mat.name);
            }
            return results;
        }

        private static bool HasForearmWeightsOnMaterial(ArmPatchComponent component, string materialName)
        {
            var root = FindAvatarRootForComponent(component);
            if (root == null || string.IsNullOrEmpty(materialName)) return false;
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null) return false;
            var leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            var rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || smr.sharedMaterials == null) continue;
                var mesh = smr.sharedMesh;
                var bones = smr.bones;
                int[] relevant = { Array.IndexOf(bones, leftLower), Array.IndexOf(bones, rightLower), Array.IndexOf(bones, leftHand), Array.IndexOf(bones, rightHand) };
                var targetVertices = new HashSet<int>();
                for (int sub = 0; sub < Mathf.Min(mesh.subMeshCount, smr.sharedMaterials.Length); sub++)
                {
                    var mat = smr.sharedMaterials[sub];
                    if (mat == null || mat.name != materialName) continue;
                    foreach (var vi in mesh.GetTriangles(sub)) targetVertices.Add(vi);
                }
                if (targetVertices.Count == 0) continue;
                var weights = mesh.boneWeights;
                foreach (var vi in targetVertices)
                {
                    if (vi < 0 || vi >= weights.Length) continue;
                    var bw = weights[vi];
                    foreach (var idx in relevant)
                    {
                        if (idx < 0) continue;
                        if ((bw.boneIndex0 == idx && bw.weight0 > 1e-6f) || (bw.boneIndex1 == idx && bw.weight1 > 1e-6f) || (bw.boneIndex2 == idx && bw.weight2 > 1e-6f) || (bw.boneIndex3 == idx && bw.weight3 > 1e-6f))
                            return true;
                    }
                }
            }
            return false;
        }

        private void DrawSubRowSlider(string label, SerializedProperty property, float min, float max, string tooltip)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect spacerRect = new Rect(rect.x, rect.y, ToggleWidth, rect.height);
            Rect mainLabelRect = new Rect(spacerRect.xMax + 2f, rect.y, MainLabelWidth, rect.height);
            Rect subLabelRect = new Rect(mainLabelRect.xMax + Gap, rect.y, SubLabelWidth, rect.height);
            Rect valueRect = new Rect(subLabelRect.xMax + 4f, rect.y, rect.xMax - (subLabelRect.xMax + 4f), rect.height);

            EditorGUI.LabelField(mainLabelRect, GUIContent.none);
            EditorGUI.LabelField(subLabelRect, new GUIContent(label, tooltip));
            property.floatValue = EditorGUI.Slider(valueRect, property.floatValue, min, max);
        }

        private void DrawShoulderSubRow(string label, SerializedProperty property, string tooltip)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

            Rect spacerRect = new Rect(rect.x, rect.y, ToggleWidth, rect.height);
            Rect mainLabelRect = new Rect(spacerRect.xMax + 2f, rect.y, MainLabelWidth, rect.height);
            Rect subLabelRect = new Rect(mainLabelRect.xMax + Gap, rect.y, SubLabelWidth, rect.height);
            Rect valueRect = new Rect(subLabelRect.xMax + 4f, rect.y, rect.xMax - (subLabelRect.xMax + 4f), rect.height);

            EditorGUI.LabelField(mainLabelRect, GUIContent.none);
            EditorGUI.LabelField(subLabelRect, new GUIContent(label, tooltip));
            EditorGUI.PropertyField(valueRect, property, GUIContent.none);
        }

        private void DrawInlineRow(
            SerializedProperty enableProp,
            string label,
            string labelTooltip,
            SerializedProperty valueProp,
            string valueLabel,
            string valueTooltip)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

            Rect toggleRect = new Rect(rect.x, rect.y, ToggleWidth, rect.height);
            Rect labelRect = new Rect(toggleRect.xMax + 2f, rect.y, MainLabelWidth, rect.height);
            Rect valueLabelRect = new Rect(labelRect.xMax + Gap, rect.y, SubLabelWidth, rect.height);
            Rect valueRect = new Rect(valueLabelRect.xMax + 4f, rect.y, rect.xMax - (valueLabelRect.xMax + 4f), rect.height);

            enableProp.boolValue = EditorGUI.Toggle(toggleRect, enableProp.boolValue);
            EditorGUI.LabelField(labelRect, new GUIContent(label, labelTooltip));

            using (new EditorGUI.DisabledScope(!enableProp.boolValue))
            {
                EditorGUI.LabelField(valueLabelRect, new GUIContent(valueLabel, valueTooltip));
                EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none);
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

            EditorGUI.indentLevel++;

            DrawConstraintModePopup();
            DrawBuildOrderPopup();

            EditorGUILayout.PropertyField(verboseLogProp, new GUIContent(T("Verbose Log", "Verbose Log")));
            EditorGUILayout.Space(4);

            Rect rawButtonRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect buttonRect = EditorGUI.IndentedRect(rawButtonRect);
            if (GUI.Button(buttonRect, T("Reset Preview", "Reset Preview")))
            {
                ArmPatchPreviewUtility.ResetAllPreviewArtifacts();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.HelpBox(
                T(
                    "モデルが重複したり、見えない場合に押してください。\nPreview オブジェクトを削除し、Renderer を再表示します。",
                    "Use this if the avatar stays hidden, frozen, or stuck after Preview.\nThis removes temporary Preview objects and re-enables renderers."
                ),
                MessageType.Warning
            );

            EditorGUI.indentLevel--;
        }

        private void DrawConstraintModePopup()
        {
            var options = language == Language.Japanese ? ConstraintModeJa : ConstraintModeEn;
            int current = constraintModeProp.enumValueIndex;

            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(
                new GUIContent(
                    T("Constraint Mode", "Constraint Mode"),
                    T(
                        "Unity Constraintsは、 VRChat以外用のオプションです。",
                        "Choose between VRChat Constraints and Unity Constraints. Unity Constraints is intended for compatibility-oriented workflows where later conversion may be handled by other tools."
                    )
                ),
                current,
                options
            );
            if (EditorGUI.EndChangeCheck())
            {
                constraintModeProp.enumValueIndex = next;
            }
        }

        private void DrawBuildOrderPopup()
        {
            var options = language == Language.Japanese ? BuildOrderJa : BuildOrderEn;
            int current = buildOrderProp.enumValueIndex;

            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(
                new GUIContent(
                    T("Build Order", "Build Order"),
                    T(
                        "Afterは、MAで後から追加される衣装やパーツも処理対象にできます。 Beforeは、このツールで追加したコンストレイントをMAで処理したい場合に使用します。",
                        "After Modular Avatar is recommended when clothing or parts are added later by Modular Avatar. Before Modular Avatar is for workflows where generated content needs to exist before later conversion steps."
                    )
                ),
                current,
                options
            );
            if (EditorGUI.EndChangeCheck())
            {
                buildOrderProp.enumValueIndex = next;
            }
        }

        private static GameObject FindAvatarRootForComponent(ArmPatchComponent component)
        {
            if (component == null) return null;

            Transform current = component.transform;
            while (current != null)
            {
                var animator = current.GetComponent<Animator>();
                if (animator != null && animator.avatar != null && animator.avatar.isHuman)
                {
                    return animator.gameObject;
                }

                current = current.parent;
            }

            return null;
        }

        private string T(string ja, string en)
        {
            return language == Language.Japanese ? ja : en;
        }
    }
}
