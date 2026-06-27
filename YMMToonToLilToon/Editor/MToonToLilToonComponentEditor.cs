using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.MToonToLilToon
{
    [CustomEditor(typeof(MToonToLilToonComponent))]
    public sealed class MToonToLilToonComponentEditor : Editor
    {
        private const float OverrideGroupSpacing = 4f;
        private const float SectionHeadingSpacing = 8f;
        private const float SectionTopSpacing = 10f;
        private List<Material> _cachedRendererMaterials;

        private enum Language
        {
            Japanese,
            English
        }

        private const string PrefKeyLanguage = "MToonToLilToonComponentEditor.Language";
        private Language _language;

        private void OnEnable()
        {
            _language = (Language)EditorPrefs.GetInt(PrefKeyLanguage, 0);
            var component = (MToonToLilToonComponent)target;
            _cachedRendererMaterials = GetRendererMaterials(component);
            var undoGroup = Undo.GetCurrentGroup();
            serializedObject.Update();
            EnsureFaceMaterialsDetected(serializedObject, _cachedRendererMaterials);
            if (serializedObject.ApplyModifiedProperties())
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var component = (MToonToLilToonComponent)target;
            var previousPreviewing = MToonToLilToonPreviewUtility.IsPreviewing(component);
            var previewRelevantStateBefore = BuildPreviewRelevantStateKey(component);
            _cachedRendererMaterials ??= GetRendererMaterials(component);

            DrawPreviewButton(component);
            EditorGUILayout.Space(4f);
            DrawMultipleComponentsWarning(component);
            EditorGUILayout.Space(4f);
            var sharedFaceMaterialChanged = DrawSharedFaceMaterialSelector(component);
            var globalOverridesChanged = DrawLilToonUserSettings();
            DrawSpecificPartAdjustmentsHeading();
            EditorGUI.BeginChangeCheck();
            var directValueChanged = sharedFaceMaterialChanged;
            var hairSettingsChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            directValueChanged |= DrawFaceShadowTuningSection(component);
            var faceShadowSettingsChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            directValueChanged |= DrawAdvancedSection(component);
            var advancedSettingsChanged = EditorGUI.EndChangeCheck();

            var undoGroup = Undo.GetCurrentGroup();
            var serializedChanged = serializedObject.ApplyModifiedProperties();
            if (directValueChanged)
            {
                EditorUtility.SetDirty(component);
            }

            var previewRelevantChanged = previewRelevantStateBefore != BuildPreviewRelevantStateKey(component);
            var onlyGlobalOverridesChanged = previousPreviewing
                && previewRelevantChanged
                && !directValueChanged
                && globalOverridesChanged
                && !hairSettingsChanged
                && !faceShadowSettingsChanged
                && !advancedSettingsChanged;

            if (onlyGlobalOverridesChanged)
            {
                MToonToLilToonPreviewUtility.ApplyGlobalOverridesIfActive(component);
            }
            else if ((previewRelevantChanged || directValueChanged) && previousPreviewing)
            {
                MToonToLilToonPreviewUtility.RestartPreviewIfActive(component);
            }
        }

        private static string BuildPreviewRelevantStateKey(MToonToLilToonComponent component)
        {
            if (component == null) return string.Empty;

            var builder = new StringBuilder();
            AppendObject(builder, component.lilToonShader);
            builder.Append('|').Append(component.enableFaceShadowTuning);
            AppendObject(builder, component.faceShadowFaceMaterial);
            AppendObject(builder, component.faceShadowSdfTexture);
            builder.Append('|').Append((int)component.faceShadowMaskType);
            builder.Append('|').Append(component.shadowStrengthMaskLod);
            builder.Append('|').Append(component.disableShadowReceiveForFace);
            builder.Append('|').Append(component.disableRimShadeForFace);
            builder.Append('|').Append(component.disableBacklightStrengthForFace);
            builder.Append('|').Append(component.useToonStandardFallback);
            builder.Append('|').Append(component.verboseLog);
            AppendGlobalOverrides(builder, component.globalOverrides);
            return builder.ToString();
        }

        private static void AppendGlobalOverrides(StringBuilder builder, LilToonGlobalOverrides overrides)
        {
            builder.Append("|global:");
            if (overrides == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append(overrides.flipBackfaceNormal);
            builder.Append('|').Append(overrides.enableShadowReceive);
            builder.Append('|').Append(overrides.shadowReceive);
            builder.Append('|').Append(overrides.enableShadowBorder);
            AppendColor(builder, overrides.shadowBorderColor);
            builder.Append('|').Append(overrides.shadowBorderStrength);
            builder.Append('|').Append(overrides.enableBacklight);
            builder.Append('|').Append((int)overrides.backlightColorMode);
            AppendColor(builder, overrides.backlightColor);
            builder.Append('|').Append(overrides.backlightMainStrength);
            builder.Append('|').Append(overrides.backlightBorder);
            builder.Append('|').Append(overrides.backlightBlur);
            builder.Append('|').Append(overrides.enableRimShade);
            AppendColor(builder, overrides.rimShadeColor);
            builder.Append('|').Append(overrides.rimShadeBorder);
            builder.Append('|').Append(overrides.rimShadeBlur);
            builder.Append('|').Append(overrides.enableDistanceFade);
            AppendColor(builder, overrides.distanceFadeColor);
            builder.Append('|').Append(overrides.distanceFadeStrength);
            builder.Append('|').Append(overrides.outlineZBias);
        }

        private static void AppendObject(StringBuilder builder, Object obj)
        {
            builder.Append('|').Append(obj != null ? obj.GetInstanceID() : 0);
        }

        private static void AppendVector(StringBuilder builder, Vector3 value)
        {
            builder.Append('|').Append(value.x).Append(',').Append(value.y).Append(',').Append(value.z);
        }

        private static void AppendColor(StringBuilder builder, Color value)
        {
            builder.Append('|').Append(value.r).Append(',').Append(value.g).Append(',').Append(value.b).Append(',').Append(value.a);
        }

        private void DrawPreviewButton(MToonToLilToonComponent component)
        {
            using var horizontal = new EditorGUILayout.HorizontalScope();
            var previewing = MToonToLilToonPreviewUtility.IsPreviewing(component);

            if (PreviewInspectorGui.DrawPreviewButton(previewing))
            {
                MToonToLilToonPreviewUtility.TogglePreview(component);
                EditorUtility.SetDirty(component);
            }

            var progressMessage = MToonToLilToonPreviewUtility.IsProcessingPreview()
                ? "Processing..."
                : MToonToLilToonPreviewUtility.GetPreviewProgressMessage();
            PreviewInspectorGui.DrawStatus(
                MToonToLilToonPreviewUtility.IsProcessingPreview(),
                MToonToLilToonPreviewUtility.HasPreviewFailed(),
                progressMessage);
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            var nextLanguage = (Language)EditorGUILayout.EnumPopup(_language, GUILayout.Width(90f));
            if (EditorGUI.EndChangeCheck())
            {
                _language = nextLanguage;
                EditorPrefs.SetInt(PrefKeyLanguage, (int)_language);
            }
        }

        private bool DrawLilToonUserSettings()
        {
            EditorGUI.BeginChangeCheck();
            var overridesProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.globalOverrides));
            EditorGUILayout.Space(SectionTopSpacing);
            DrawUnderlinedSectionTitle(T("lilToon固有機能の一括設定", "Bulk Settings for lilToon-specific Features"));
            EditorGUILayout.Space(2f);
            DrawSingleOverrideToggle(
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.flipBackfaceNormal)),
                T("裏面の法線を反転", "Flip Backface Normal"));
            DrawOverrideGroup(
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.enableShadowReceive)),
                T("影を受け取る", "Receive Shadow"),
                T("強度", "Strength"),
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.shadowReceive)),
                T("顔だけ除外する", "Exclude Face Only"),
                serializedObject.FindProperty(nameof(MToonToLilToonComponent.disableShadowReceiveForFace)));
            DrawOverrideGroup(
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.enableShadowBorder)),
                T("影の境界", "Shadow Border"),
                T("色", "Color"),
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.shadowBorderColor)),
                T("幅", "Width"),
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.shadowBorderStrength)));
            DrawOverrideGroupRows(
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.enableRimShade)),
                new GUIContent(T("リムシェード", "Rim Shade")),
                new[]
                {
                    T("色", "Color"),
                    T("範囲", "Range"),
                    T("ぼかし", "Blur"),
                    T("顔だけ除外する", "Exclude Face Only")
                },
                new[]
                {
                    overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.rimShadeColor)),
                    overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.rimShadeBorder)),
                    overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.rimShadeBlur)),
                    serializedObject.FindProperty(nameof(MToonToLilToonComponent.disableRimShadeForFace))
                });
            DrawBacklightOverrideGroup(
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.enableBacklight)),
                TT(
                    "逆光ライト",
                    "ぼかしを広げるとSSS風の表現が可能です。 Bloomでエッジを強く光らせたい場合は、指定色でIntensityを3前後まで上げてください。",
                    "Backlight",
                    "Increasing blur enables an SSS-like appearance. To make edges glow strongly with Bloom, use a custom color and raise Intensity to around 3."),
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.backlightColorMode)),
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.backlightColor)),
                new[]
                {
                    T("メインカラーの強度", "Main Color Strength"),
                    T("範囲", "Range"),
                    T("ぼかし", "Blur"),
                    T("顔だけ除外する", "Exclude Face Only")
                },
                new[]
                {
                    overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.backlightMainStrength)),
                    overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.backlightBorder)),
                    overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.backlightBlur)),
                    serializedObject.FindProperty(nameof(MToonToLilToonComponent.disableBacklightStrengthForFace))
                });
            DrawOverrideGroup(
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.enableDistanceFade)),
                TT(
                    "距離フェード",
                    "すぐ目の前まで接近した部分を暗くすることができます。",
                    "Distance Fade",
                    "Darkens portions that are very close to the camera."),
                T("色", "Color"),
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.distanceFadeColor)),
                T("強度", "Strength"),
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.distanceFadeStrength)));
            EditorGUILayout.PropertyField(
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.outlineZBias)),
                TT(
                    "輪郭線のZ Bias",
                    "輪郭線を前後にずらします。折れジワの抑制や、シルエットだけに輪郭線を出すことが可能です。",
                    "Outline Z Bias",
                    "Moves outline forward/backward. Helps suppress fold artifacts and show outlines on silhouette only."));
            return EditorGUI.EndChangeCheck();
        }

        private static void DrawSingleOverrideToggle(SerializedProperty valueProp, string label)
        {
            var rowRect = EditorGUILayout.GetControlRect();
            valueProp.boolValue = EditorGUI.ToggleLeft(rowRect, label, valueProp.boolValue);
            EditorGUILayout.Space(OverrideGroupSpacing);
        }

        private void DrawSpecificPartAdjustmentsHeading()
        {
            EditorGUILayout.Space(SectionHeadingSpacing + 4f);
            DrawUnderlinedSectionTitle(T("特定部位への調整", "Adjustments for Specific Parts"));
            EditorGUILayout.Space(2f);
        }

        private static void DrawUnderlinedSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var lineRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(lineRect, new Color(0.3f, 0.3f, 0.3f, 0.9f));
        }

        private void DrawOverrideGroup(
            SerializedProperty enabledProp,
            string groupLabel,
            string firstLabel,
            SerializedProperty firstValueProp,
            string secondLabel,
            SerializedProperty secondValueProp)
        {
            DrawOverrideGroup(
                enabledProp,
                new GUIContent(groupLabel),
                firstLabel,
                firstValueProp,
                secondLabel,
                secondValueProp,
                addBottomSpacing: true);
        }

        private void DrawOverrideGroup(
            SerializedProperty enabledProp,
            GUIContent groupLabel,
            string firstLabel,
            SerializedProperty firstValueProp,
            string secondLabel,
            SerializedProperty secondValueProp)
        {
            DrawOverrideGroup(enabledProp, groupLabel, firstLabel, firstValueProp, secondLabel, secondValueProp, addBottomSpacing: true);
        }

        private void DrawOverrideGroupWithThirdRow(
            SerializedProperty enabledProp,
            string groupLabel,
            string firstLabel,
            SerializedProperty firstValueProp,
            string secondLabel,
            SerializedProperty secondValueProp,
            string thirdLabel,
            SerializedProperty thirdValueProp)
        {
            DrawOverrideGroupWithThirdRow(
                enabledProp,
                new GUIContent(groupLabel),
                firstLabel,
                firstValueProp,
                secondLabel,
                secondValueProp,
                thirdLabel,
                thirdValueProp);
        }

        private void DrawOverrideGroupWithThirdRow(
            SerializedProperty enabledProp,
            GUIContent groupLabel,
            string firstLabel,
            SerializedProperty firstValueProp,
            string secondLabel,
            SerializedProperty secondValueProp,
            string thirdLabel,
            SerializedProperty thirdValueProp)
        {
            DrawOverrideGroup(
                enabledProp,
                groupLabel,
                firstLabel,
                firstValueProp,
                secondLabel,
                secondValueProp,
                addBottomSpacing: false);

            var thirdRowRect = EditorGUILayout.GetControlRect();
            GetOverrideColumnRects(thirdRowRect, out var thirdCategoryRect, out var thirdItemLabelRect, out var thirdValueRect);
            DrawCategoryColumn(thirdCategoryRect, enabledProp, string.Empty, showToggle: false);
            using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
            {
                DrawTwoColumnPropertyRow(thirdItemLabelRect, thirdValueRect, thirdLabel, thirdValueProp);
            }

            EditorGUILayout.Space(OverrideGroupSpacing);
        }

        private void DrawBacklightOverrideGroup(
            SerializedProperty enabledProp,
            GUIContent groupLabel,
            SerializedProperty colorModeProp,
            SerializedProperty customColorProp,
            string[] labels,
            SerializedProperty[] valueProps)
        {
            var colorRowRect = EditorGUILayout.GetControlRect();
            GetOverrideColumnRects(colorRowRect, out var categoryRect, out var itemLabelRect, out var valueRect);
            DrawCategoryColumn(categoryRect, enabledProp, groupLabel, showToggle: true);
            using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
            {
                EditorGUI.LabelField(itemLabelRect, T("色", "Color"));
                var mode = (BacklightColorMode)colorModeProp.intValue;
                var options = new[]
                {
                    T("影色+Intensity 1", "Shade Color + Intensity 1"),
                    T("指定色", "Custom Color")
                };

                if (mode == BacklightColorMode.Custom)
                {
                    var halfWidth = (valueRect.width - 4f) * 0.5f;
                    var modeRect = new Rect(valueRect.x, valueRect.y, halfWidth, valueRect.height);
                    var colorRect = new Rect(valueRect.x + halfWidth + 4f, valueRect.y, halfWidth, valueRect.height);
                    mode = (BacklightColorMode)EditorGUI.Popup(modeRect, (int)mode, options);
                    EditorGUI.PropertyField(colorRect, customColorProp, GUIContent.none);
                }
                else
                {
                    mode = (BacklightColorMode)EditorGUI.Popup(valueRect, (int)mode, options);
                }

                colorModeProp.intValue = (int)mode;
            }

            for (var i = 0; i < labels.Length; i++)
            {
                var rowRect = EditorGUILayout.GetControlRect();
                GetOverrideColumnRects(rowRect, out categoryRect, out itemLabelRect, out valueRect);
                DrawCategoryColumn(categoryRect, enabledProp, GUIContent.none, showToggle: false);
                using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
                {
                    DrawTwoColumnPropertyRow(itemLabelRect, valueRect, labels[i], valueProps[i]);
                }
            }

            EditorGUILayout.Space(OverrideGroupSpacing);
        }

        private void DrawOverrideGroupRows(
            SerializedProperty enabledProp,
            GUIContent groupLabel,
            string[] labels,
            SerializedProperty[] valueProps)
        {
            if (labels == null || valueProps == null || labels.Length == 0 || labels.Length != valueProps.Length) return;

            for (var i = 0; i < labels.Length; i++)
            {
                var rowRect = EditorGUILayout.GetControlRect();
                GetOverrideColumnRects(rowRect, out var categoryRect, out var itemLabelRect, out var valueRect);
                DrawCategoryColumn(categoryRect, enabledProp, i == 0 ? groupLabel : GUIContent.none, showToggle: i == 0);
                using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
                {
                    DrawTwoColumnPropertyRow(itemLabelRect, valueRect, labels[i], valueProps[i]);
                }
            }

            EditorGUILayout.Space(OverrideGroupSpacing);
        }

        private void DrawOverrideGroup(
            SerializedProperty enabledProp,
            GUIContent groupLabel,
            string firstLabel,
            SerializedProperty firstValueProp,
            string secondLabel,
            SerializedProperty secondValueProp,
            bool addBottomSpacing)
        {
            var firstRowRect = EditorGUILayout.GetControlRect();
            GetOverrideColumnRects(firstRowRect, out var firstCategoryRect, out var firstItemLabelRect, out var firstValueRect);
            DrawCategoryColumn(firstCategoryRect, enabledProp, groupLabel, showToggle: true);
            using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
            {
                DrawTwoColumnPropertyRow(firstItemLabelRect, firstValueRect, firstLabel, firstValueProp);
            }

            var secondRowRect = EditorGUILayout.GetControlRect();
            GetOverrideColumnRects(secondRowRect, out var secondCategoryRect, out var secondItemLabelRect, out var secondValueRect);
            DrawCategoryColumn(secondCategoryRect, enabledProp, string.Empty, showToggle: false);
            using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
            {
                DrawTwoColumnPropertyRow(secondItemLabelRect, secondValueRect, secondLabel, secondValueProp);
            }

            if (addBottomSpacing)
            {
                EditorGUILayout.Space(OverrideGroupSpacing);
            }
        }

        private static void DrawCategoryColumn(Rect categoryRect, SerializedProperty enabledProp, string label, bool showToggle)
        {
            DrawCategoryColumn(categoryRect, enabledProp, new GUIContent(label), showToggle);
        }

        private static void DrawCategoryColumn(Rect categoryRect, SerializedProperty enabledProp, GUIContent label, bool showToggle)
        {
            if (showToggle)
            {
                enabledProp.boolValue = EditorGUI.ToggleLeft(categoryRect, label, enabledProp.boolValue);
                return;
            }
            EditorGUI.LabelField(categoryRect, label);
        }

        private static void DrawTwoColumnPropertyRow(Rect itemLabelRect, Rect valueRect, string label, SerializedProperty valueProp)
        {
            DrawTwoColumnPropertyRow(itemLabelRect, valueRect, new GUIContent(label), valueProp);
        }

        private static void DrawTwoColumnPropertyRow(Rect itemLabelRect, Rect valueRect, GUIContent label, SerializedProperty valueProp)
        {
            EditorGUI.LabelField(itemLabelRect, label);
            EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none);
        }

        private static void GetOverrideColumnRects(
            Rect rowRect,
            out Rect categoryRect,
            out Rect itemLabelRect,
            out Rect valueRect)
        {
            var unit = rowRect.width / 7f;
            categoryRect = new Rect(rowRect.x, rowRect.y, unit * 2f, rowRect.height);
            itemLabelRect = new Rect(categoryRect.xMax, rowRect.y, unit * 2f, rowRect.height);
            valueRect = new Rect(itemLabelRect.xMax, rowRect.y, unit * 3f, rowRect.height);
        }

        private bool DrawFaceShadowTuningSection(MToonToLilToonComponent component)
        {
            var changed = false;
            var enableFaceShadowTuningProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.enableFaceShadowTuning));
            DrawLeftToggle(enableFaceShadowTuningProp, T("顔の影を整える", "Tune Face Shadow"));
            if (!enableFaceShadowTuningProp.boolValue) return changed;

            using (new EditorGUI.IndentLevelScope())
            {
                DrawFaceShadowMaskSettings(component);
            }

            return changed;
        }

        private void DrawFaceShadowMaskSettings(MToonToLilToonComponent component)
        {
            var textureProperty = serializedObject.FindProperty(nameof(MToonToLilToonComponent.faceShadowSdfTexture));
            DrawFaceShadowMaskTypePopup();

            EditorGUILayout.PropertyField(
                textureProperty,
                TT(
                    "マスク",
                    "空の場合はマスクなしで実行されます。",
                    "Mask",
                    "If empty, conversion runs without a mask."));
            var lodProperty = serializedObject.FindProperty(nameof(MToonToLilToonComponent.shadowStrengthMaskLod));
            lodProperty.floatValue = EditorGUILayout.Slider(
                TT(
                    "LOD",
                    "マスク画像のぼかし量です。",
                    "LOD",
                    "Controls blur amount of the mask texture."),
                lodProperty.floatValue,
                0f,
                1f);
        }

        private void DrawFaceShadowMaskTypePopup()
        {
            var maskTypeProperty = serializedObject.FindProperty(nameof(MToonToLilToonComponent.faceShadowMaskType));
            if (maskTypeProperty == null) return;

            var options = new[]
            {
                T("強度", "Strength"),
                T("平面化", "Flat"),
                "SDF"
            };

            var currentType = (MToonToLilToonComponent.FaceShadowMaskType)maskTypeProperty.intValue;
            var currentIndex = currentType switch
            {
                MToonToLilToonComponent.FaceShadowMaskType.Strength => 0,
                MToonToLilToonComponent.FaceShadowMaskType.Flat => 1,
                MToonToLilToonComponent.FaceShadowMaskType.Sdf => 2,
                _ => 1
            };

            var nextIndex = EditorGUILayout.Popup(
                TT(
                    "マスクタイプ",
                    "顔影マスクの計算方式を選択します。",
                    "Mask Type",
                    "Chooses how face shadow mask is interpreted."),
                currentIndex,
                options);
            var nextType = nextIndex switch
            {
                0 => MToonToLilToonComponent.FaceShadowMaskType.Strength,
                1 => MToonToLilToonComponent.FaceShadowMaskType.Flat,
                2 => MToonToLilToonComponent.FaceShadowMaskType.Sdf,
                _ => MToonToLilToonComponent.FaceShadowMaskType.Flat
            };

            maskTypeProperty.intValue = (int)nextType;
        }

        private bool DrawAdvancedSection(MToonToLilToonComponent component)
        {
            var changed = false;
            EditorGUILayout.Space();

            var showAdvancedProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.showAdvanced));
            showAdvancedProp.boolValue = EditorGUILayout.Foldout(showAdvancedProp.boolValue, "Advanced", true);
            if (!showAdvancedProp.boolValue) return changed;

            using (new EditorGUI.IndentLevelScope())
            {
                var useToonStandardFallbackProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.useToonStandardFallback));
                useToonStandardFallbackProp.boolValue = EditorGUILayout.ToggleLeft(
                    T("Custom Safety FallbackをToon Standardにする", "Use Toon Standard for Custom Safety Fallback"),
                    useToonStandardFallbackProp.boolValue);
                if (useToonStandardFallbackProp.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        T(
                            "Toon Standardは、CutoutやTransparentは非対応です。 事前にメッシュをトリミングしたり、頬染めなどは削除しておく必要があります。\nFallbackのToon Standardは両面描画非対応です。",
                            "Toon Standard does not support Cutout or Transparent. You need to trim meshes and remove blush-like transparent effects beforehand.\nToon Standard fallback does not support double-sided rendering."),
                        MessageType.Warning);
                }
                var verboseLogProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.verboseLog));
                verboseLogProp.boolValue = EditorGUILayout.ToggleLeft("Verbose Log", verboseLogProp.boolValue);
                EditorGUILayout.Space(4f);

                var rawButtonRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                var buttonRect = EditorGUI.IndentedRect(rawButtonRect);
                if (GUI.Button(buttonRect, "Reset Preview"))
                {
                    PreviewRecoveryUtility.ResetAllPreviewArtifacts();
                    EditorUtility.SetDirty(component);
                    changed = true;
                }

                EditorGUILayout.HelpBox(
                    T(
                        "モデルが重複したり、見えない場合に押してください。\nPreview オブジェクトを削除し、Renderer を再表示します。",
                        "Use this if the avatar stays hidden, frozen, or stuck after Preview.\nThis removes temporary Preview objects and re-enables renderers."),
                    MessageType.Warning);
            }

            return changed;
        }

        private static List<Material> GetRendererMaterials(MToonToLilToonComponent component)
        {
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            var searchRoot = avatarRoot != null ? avatarRoot : component.gameObject;
            return searchRoot.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null)
                .Distinct()
                .ToList();
        }

        private void DrawMultipleComponentsWarning(MToonToLilToonComponent component)
        {
            if (component == null) return;
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            if (avatarRoot == null) return;

            var components = avatarRoot.GetComponentsInChildren<MToonToLilToonComponent>(true);
            if (components == null || components.Length <= 1) return;

            var selected = SelectPreferredComponentForBuild(components, avatarRoot);
            var thisWillBeUsed = selected == component;
            EditorGUILayout.HelpBox(
                thisWillBeUsed
                    ? T("複数箇所で設定されています。ビルド時はこのコンポーネントの設定値が使用されます。",
                        "This component is configured in multiple places. The values on this component will be used for the build.")
                    : T("複数箇所で設定されています。ビルド時、このコンポーネントでの設定は無視されます。",
                        "This component is configured in multiple places. The values on this component will be ignored for the build."),
                MessageType.Warning);
        }

        private static MToonToLilToonComponent SelectPreferredComponentForBuild(
            MToonToLilToonComponent[] components,
            GameObject avatarRoot)
        {
            if (components == null || components.Length == 0) return null;

            MToonToLilToonComponent best = null;
            var bestScore = int.MinValue;
            var rootTransform = avatarRoot != null ? avatarRoot.transform : null;
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null) continue;
                var depth = PreviewCoordinator.GetDepthFromRoot(component.transform, rootTransform);
                var score = -depth * 10000 - i;
                if (score <= bestScore) continue;
                best = component;
                bestScore = score;
            }

            return best;
        }

        private static Material DetectDefaultFaceMaterial(IReadOnlyList<Material> materials)
        {
            if (materials == null || materials.Count == 0) return null;

            var face = materials.FirstOrDefault(m => m != null
                && m.name.IndexOf("FACE", System.StringComparison.OrdinalIgnoreCase) >= 0
                && m.name.IndexOf("SKIN", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (face != null) return face;

            face = materials.FirstOrDefault(m => m != null
                && (m.name.IndexOf("FACE", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || m.name.IndexOf("顔", System.StringComparison.OrdinalIgnoreCase) >= 0));
            if (face != null) return face;

            return materials.FirstOrDefault();
        }

        private string T(string ja, string en)
        {
            return _language == Language.Japanese ? ja : en;
        }

        private GUIContent TT(string ja, string jaTooltip, string en, string enTooltip)
        {
            return _language == Language.Japanese
                ? new GUIContent(ja, jaTooltip)
                : new GUIContent(en, enTooltip);
        }

        private bool DrawSharedFaceMaterialSelector(MToonToLilToonComponent component)
        {
            var candidates = _cachedRendererMaterials ?? GetRendererMaterials(component);
            if (candidates.Count == 0) return false;

            var faceMaterialProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.faceShadowFaceMaterial));
            var labels = new[] { T("未設定", "None") }.Concat(candidates.Select(m => m != null ? m.name : "(null)")).ToArray();
            var currentFaceMaterial = faceMaterialProp.objectReferenceValue as Material;
            var currentIndex = currentFaceMaterial != null ? candidates.IndexOf(currentFaceMaterial) + 1 : 0;

            EditorGUI.BeginChangeCheck();
            var nextIndex = EditorGUILayout.Popup(
                TT(
                    "顔マテリアル",
                    "顔だけ除外する設定や、顔の影を整える機能などの対象を指定します。",
                    "Face Material",
                    "Specifies the target for face-only exclusions, face shadow tuning, and related features."),
                currentIndex,
                labels);
            if (!EditorGUI.EndChangeCheck()) return false;

            var nextMaterial = nextIndex <= 0 ? null : candidates[nextIndex - 1];
            faceMaterialProp.objectReferenceValue = nextMaterial;
            return true;
        }

        private bool EnsureFaceMaterialsDetected(MToonToLilToonComponent component)
        {
            return EnsureFaceMaterialsDetected(serializedObject, GetRendererMaterials(component));
        }

        private static bool EnsureFaceMaterialsDetected(SerializedObject serializedComponent, IReadOnlyList<Material> scannedMaterials)
        {
            if (serializedComponent == null || scannedMaterials == null || scannedMaterials.Count == 0) return false;

            var changed = false;
            var defaultFaceMaterial = DetectDefaultFaceMaterial(scannedMaterials);
            var faceMaterialProp = serializedComponent.FindProperty(nameof(MToonToLilToonComponent.faceShadowFaceMaterial));
            var faceMaterial = faceMaterialProp.objectReferenceValue as Material;
            if (faceMaterial == null || !scannedMaterials.Contains(faceMaterial))
            {
                faceMaterialProp.objectReferenceValue = defaultFaceMaterial;
                faceMaterial = defaultFaceMaterial;
                changed = true;
            }

            return changed;
        }

        private static void DrawLeftToggle(SerializedProperty boolProperty, string label)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                boolProperty.boolValue = EditorGUILayout.Toggle(boolProperty.boolValue, GUILayout.Width(18f));
                EditorGUILayout.LabelField(label);
            }
        }
    }
}
