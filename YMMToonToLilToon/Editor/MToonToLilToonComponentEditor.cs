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
        private const float HairSelectionToggleColumnWidth = 26f;

        private List<Material> _cachedRendererMaterials;
        private float? _pendingHairTipOutlineWidth;
        private float? _pendingHairTipRange;

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
            if (ShouldAutoScanHairSelectionsOnEnable(component, _cachedRendererMaterials))
            {
                ScanMaterials(serializedObject, component);
                _cachedRendererMaterials = GetRendererMaterials(component);
            }
            else
            {
                EnsureFaceMaterialsDetected(serializedObject, _cachedRendererMaterials);
            }
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
            var directValueChanged = sharedFaceMaterialChanged | DrawHairMergeToggle(component, out var requestHairScan);
            var hairSettingsChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            directValueChanged |= DrawFaceShadowTuningSection(component);
            var faceShadowSettingsChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            directValueChanged |= DrawAdvancedSection(component);
            var advancedSettingsChanged = EditorGUI.EndChangeCheck();

            var undoGroup = Undo.GetCurrentGroup();
            var serializedChanged = serializedObject.ApplyModifiedProperties();
            if (requestHairScan)
            {
                serializedObject.Update();
                ScanMaterials(serializedObject, component);
                _cachedRendererMaterials = GetRendererMaterials(component);
                serializedChanged |= serializedObject.ApplyModifiedProperties();
                if (serializedChanged)
                {
                    Undo.CollapseUndoOperations(undoGroup);
                }
                directValueChanged = true;
            }
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
            builder.Append('|').Append(component.enableHairMerge);
            builder.Append('|').Append(component.enableHairOutlineCorrection);
            builder.Append('|').Append(component.hairTipOutlineWidth);
            builder.Append('|').Append(component.hairTipRange);
            AppendHairSelections(builder, component.hairSelections);
            AppendObject(builder, component.representativeHairMaterialOverride);
            builder.Append('|').Append(component.enableEyebrowStencil);
            AppendObject(builder, component.eyebrowStencilMaterial);
            AppendObject(builder, component.fakeShadowFaceMaterial);
            builder.Append('|').Append(component.enableFakeShadow);
            AppendVector(builder, component.fakeShadowDirection);
            builder.Append('|').Append(component.fakeShadowOffset);
            builder.Append('|').Append(component.enableFaceShadowTuning);
            AppendObject(builder, component.faceShadowFaceMaterial);
            AppendObject(builder, component.faceShadowSdfTexture);
            builder.Append('|').Append((int)component.faceShadowMaskType);
            builder.Append('|').Append(component.shadowStrengthMaskLod);
            builder.Append('|').Append(component.disableShadowReceiveForFace);
            builder.Append('|').Append(component.disableBacklightStrengthForFace);
            builder.Append('|').Append(component.useToonStandardFallback);
            builder.Append('|').Append(component.verboseLog);
            AppendGlobalOverrides(builder, component.globalOverrides);
            return builder.ToString();
        }

        private static void AppendHairSelections(StringBuilder builder, IReadOnlyList<HairMaterialSelection> selections)
        {
            builder.Append("|hair:");
            if (selections == null)
            {
                builder.Append("null");
                return;
            }

            for (var i = 0; i < selections.Count; i++)
            {
                var selection = selections[i];
                builder.Append('[');
                AppendObject(builder, selection != null ? selection.material : null);
                builder.Append(',').Append(selection != null && selection.selected);
                builder.Append(']');
            }
        }

        private static void AppendGlobalOverrides(StringBuilder builder, LilToonGlobalOverrides overrides)
        {
            builder.Append("|global:");
            if (overrides == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append(overrides.enableShadowReceive);
            builder.Append('|').Append(overrides.shadowReceive);
            builder.Append('|').Append(overrides.enableShadowBorder);
            AppendColor(builder, overrides.shadowBorderColor);
            builder.Append('|').Append(overrides.shadowBorderStrength);
            builder.Append('|').Append(overrides.enableBacklight);
            AppendColor(builder, overrides.backlightColor);
            builder.Append('|').Append(overrides.backlightMainStrength);
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
            DrawOverrideGroupWithThirdRow(
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.enableBacklight)),
                TT(
                    "逆光ライト",
                    "撮影用にBloomで強く光らせたい場合は、HDRカラーのIntensityを3前後まで上げてください。",
                    "Backlight",
                    "For strong bloom in renders, raise HDR color intensity to around 3."),
                T("色", "Color"),
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.backlightColor)),
                T("メインカラーの強度", "Main Color Strength"),
                overridesProp.FindPropertyRelative(nameof(LilToonGlobalOverrides.backlightMainStrength)),
                T("顔だけ除外する", "Exclude Face Only"),
                serializedObject.FindProperty(nameof(MToonToLilToonComponent.disableBacklightStrengthForFace)));
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

        private static bool DrawDeferredSlider(Rect rect, SerializedProperty valueProp, ref float? pendingValue)
        {
            var displayValue = pendingValue ?? valueProp.floatValue;
            var nextValue = EditorGUI.Slider(rect, displayValue, 0f, 1f);
            if (!Mathf.Approximately(nextValue, displayValue))
            {
                pendingValue = nextValue;
            }

            if (!pendingValue.HasValue) return false;

            var shouldCommit = Event.current.type == EventType.MouseUp
                || (GUIUtility.hotControl == 0 && Event.current.type == EventType.Repaint);
            if (!shouldCommit) return false;

            var committedValue = Mathf.Clamp01(pendingValue.Value);
            pendingValue = null;
            if (Mathf.Approximately(valueProp.floatValue, committedValue)) return false;

            valueProp.floatValue = committedValue;
            return true;
        }

        private static bool DrawDeferredLabeledSlider(
            Rect labelRect,
            Rect valueRect,
            GUIContent label,
            SerializedProperty valueProp,
            ref float? pendingValue)
        {
            EditorGUI.LabelField(labelRect, label);
            if (valueProp == null) return false;
            return DrawDeferredSlider(valueRect, valueProp, ref pendingValue);
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

        private static void GetHairAdjustmentColumnRects(
            Rect rowRect,
            out Rect categoryRect,
            out Rect itemLabelRect,
            out Rect valueRect)
        {
            var unit = rowRect.width / 4f;
            categoryRect = new Rect(rowRect.x, rowRect.y, unit, rowRect.height);
            itemLabelRect = new Rect(categoryRect.xMax, rowRect.y, unit, rowRect.height);
            valueRect = new Rect(itemLabelRect.xMax, rowRect.y, unit * 2f, rowRect.height);
        }

        private bool DrawHairMergeToggle(MToonToLilToonComponent component, out bool requestHairScan)
        {
            requestHairScan = false;
            var changed = false;
            var enableHairMergeProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.enableHairMerge));
            EditorGUI.BeginChangeCheck();
            DrawLeftToggle(enableHairMergeProp, T("髪周りのルック調整", "Hair Look Adjustments"));
            var mergeToggleChanged = EditorGUI.EndChangeCheck();
            if (mergeToggleChanged)
            {
                changed = true;
                if (enableHairMergeProp.boolValue)
                {
                    requestHairScan = true;
                }
                else
                {
                    serializedObject.FindProperty(nameof(MToonToLilToonComponent.hairSelections)).ClearArray();
                }
                EditorUtility.SetDirty(component);
            }

            if (!enableHairMergeProp.boolValue) return changed;

            using (new EditorGUI.IndentLevelScope())
            {
                changed |= DrawHairSelections(component);
                EditorGUILayout.Space(OverrideGroupSpacing + 4f);
                var enableEyebrowStencilProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.enableEyebrowStencil));
                var eyebrowRowRect = EditorGUILayout.GetControlRect();
                GetHairAdjustmentColumnRects(eyebrowRowRect, out var eyebrowCategoryRect, out var eyebrowLabelRect, out var eyebrowValueRect);
                DrawCategoryColumn(
                    eyebrowCategoryRect,
                    enableEyebrowStencilProp,
                    TT(
                        "眉ステンシル",
                        "髪の手前に眉を表示します。 このツールでは簡略化のためCutoutに変更します。",
                        "Eyebrow Stencil",
                        "Shows eyebrows in front of hair. This tool switches it to Cutout for simplicity."),
                    showToggle: true);
                using (new EditorGUI.DisabledScope(!enableEyebrowStencilProp.boolValue))
                {
                    EditorGUI.LabelField(eyebrowLabelRect, T("眉マテリアル", "Eyebrow Material"));
                    changed |= DrawEyebrowStencilMaterialSelector(component, eyebrowValueRect);
                }
                EditorGUILayout.Space(OverrideGroupSpacing);

                var enableFakeShadowProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.enableFakeShadow));
                var fakeShadowDirectionProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.fakeShadowDirection));
                var fakeShadowOffsetProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.fakeShadowOffset));
                var enableHairOutlineCorrectionProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.enableHairOutlineCorrection));
                var hairTipRangeProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.hairTipRange));

                var fakeShadowFirstRowRect = EditorGUILayout.GetControlRect();
                GetHairAdjustmentColumnRects(fakeShadowFirstRowRect, out var fakeShadowCategoryRect, out var fakeShadowDirectionLabelRect, out var fakeShadowDirectionValueRect);
                DrawCategoryColumn(
                    fakeShadowCategoryRect,
                    enableFakeShadowProp,
                    TT(
                        "FakeShadow",
                        "前髪の擬似落ち影を生成します。",
                        "FakeShadow",
                        "Generates pseudo drop shadow for bangs."),
                    showToggle: true);
                using (new EditorGUI.DisabledScope(!enableFakeShadowProp.boolValue))
                {
                    DrawTwoColumnPropertyRow(fakeShadowDirectionLabelRect, fakeShadowDirectionValueRect, T("向き", "Direction"), fakeShadowDirectionProp);
                }

                var fakeShadowSecondRowRect = EditorGUILayout.GetControlRect();
                GetHairAdjustmentColumnRects(fakeShadowSecondRowRect, out var fakeShadowSecondCategoryRect, out var fakeShadowOffsetLabelRect, out var fakeShadowOffsetValueRect);
                DrawCategoryColumn(fakeShadowSecondCategoryRect, enableFakeShadowProp, string.Empty, showToggle: false);
                using (new EditorGUI.DisabledScope(!enableFakeShadowProp.boolValue))
                {
                    DrawTwoColumnPropertyRow(fakeShadowOffsetLabelRect, fakeShadowOffsetValueRect, T("オフセット", "Offset"), fakeShadowOffsetProp);
                }

                EditorGUILayout.Space(OverrideGroupSpacing);
                var outlineCorrectionRowRect = EditorGUILayout.GetControlRect();
                GetHairAdjustmentColumnRects(outlineCorrectionRowRect, out var outlineCorrectionCategoryRect, out var outlineCorrectionLabelRect, out var outlineCorrectionValueRect);
                DrawCategoryColumn(
                    outlineCorrectionCategoryRect,
                    enableHairOutlineCorrectionProp,
                    TT(
                        "輪郭線補正",
                        "ハードエッジ向けのオプションです。頂点カラーに同一座標の法線の平均を焼き込み、輪郭線を整えます。",
                        "Outline Correction",
                        "Option for hard-edged meshes. Bakes averaged same-position normals into vertex colors to refine outlines."),
                    showToggle: true);
                using (new EditorGUI.DisabledScope(!enableHairOutlineCorrectionProp.boolValue))
                {
                    var hairTipOutlineWidthProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.hairTipOutlineWidth));
                    changed |= DrawDeferredLabeledSlider(
                        outlineCorrectionLabelRect,
                        outlineCorrectionValueRect,
                        TT(
                            "毛先の太さ",
                            "UV下端を毛先とし、毛先の輪郭線の太さを調整します。",
                            "Tip Width",
                            "Treats the lower UV edge as tip and adjusts tip outline thickness."),
                        hairTipOutlineWidthProp,
                        ref _pendingHairTipOutlineWidth);
                }

                var tipRangeRowRect = EditorGUILayout.GetControlRect();
                GetHairAdjustmentColumnRects(tipRangeRowRect, out var tipRangeCategoryRect, out var tipRangeLabelRect, out var tipRangeValueRect);
                DrawCategoryColumn(tipRangeCategoryRect, enableHairOutlineCorrectionProp, string.Empty, showToggle: false);
                using (new EditorGUI.DisabledScope(!enableHairOutlineCorrectionProp.boolValue))
                {
                    changed |= DrawDeferredLabeledSlider(
                        tipRangeLabelRect,
                        tipRangeValueRect,
                        TT(
                            "毛先の範囲",
                            "大きくすると根本近くまで細くする範囲が広がります。",
                            "Tip Range",
                            "Larger values extend the thinning area closer to hair roots."),
                        hairTipRangeProp,
                        ref _pendingHairTipRange);
                }
            }

            return changed;
        }

        private bool DrawFaceShadowTuningSection(MToonToLilToonComponent component)
        {
            var changed = false;
            EditorGUILayout.Space();
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

        private bool DrawEyebrowStencilMaterialSelector(MToonToLilToonComponent component, Rect valueRect)
        {
            var candidates = _cachedRendererMaterials ?? GetRendererMaterials(component);
            if (candidates.Count == 0)
            {
                EditorGUI.Popup(valueRect, 0, new[] { T("未設定", "None") });
                return false;
            }

            var eyebrowProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.eyebrowStencilMaterial));
            var currentEyebrowMaterial = eyebrowProp.objectReferenceValue as Material;
            if (currentEyebrowMaterial == null || !candidates.Contains(currentEyebrowMaterial))
            {
                eyebrowProp.objectReferenceValue = DetectDefaultEyebrowMaterial(candidates);
                currentEyebrowMaterial = eyebrowProp.objectReferenceValue as Material;
            }

            var labels = new[] { T("未設定", "None") }.Concat(candidates.Select(m => m != null ? m.name : "(null)")).ToArray();
            var currentIndex = currentEyebrowMaterial != null
                ? candidates.IndexOf(currentEyebrowMaterial) + 1
                : 0;

            var nextIndex = EditorGUI.Popup(valueRect, currentIndex, labels);
            var nextMaterial = nextIndex <= 0 ? null : candidates[nextIndex - 1];
            if (nextMaterial == currentEyebrowMaterial) return false;

            eyebrowProp.objectReferenceValue = nextMaterial;
            return true;
        }

        private bool DrawHairSelections(MToonToLilToonComponent component)
        {
            if (!component.enableHairMerge) return false;

            var hairSelectionsProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.hairSelections));
            var changed = false;
            EditorGUILayout.HelpBox(
                T(
                    "この機能が有効な場合は髪マテリアルを結合します。\n結合されたくないマテリアルは対象から外してください。",
                    "When this feature is enabled, hair materials are merged.\nExclude any materials you do not want to merge."),
                MessageType.Info);
            var showHairMaterialsProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.showHairMaterials));
            showHairMaterialsProp.boolValue = EditorGUILayout.Foldout(
                showHairMaterialsProp.boolValue,
                T("結合対象", "Merge Targets"),
                true);
            if (!showHairMaterialsProp.boolValue) return false;

            if (hairSelectionsProp == null || hairSelectionsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(T("まだスキャンされていません。", "No materials scanned yet."), MessageType.Info);
                return false;
            }

            for (var i = 0; i < hairSelectionsProp.arraySize; i++)
            {
                var entryProp = hairSelectionsProp.GetArrayElementAtIndex(i);
                if (entryProp == null) continue;
                var materialProp = entryProp.FindPropertyRelative(nameof(HairMaterialSelection.material));
                var selectedProp = entryProp.FindPropertyRelative(nameof(HairMaterialSelection.selected));
                if (selectedProp == null || materialProp == null) continue;

                var rowRect = EditorGUILayout.GetControlRect();
                var toggleRect = new Rect(rowRect.x, rowRect.y, HairSelectionToggleColumnWidth, rowRect.height);
                var materialRect = new Rect(
                    rowRect.x + HairSelectionToggleColumnWidth,
                    rowRect.y,
                    Mathf.Max(0f, rowRect.width - HairSelectionToggleColumnWidth),
                    rowRect.height);

                var nextSelected = EditorGUI.Toggle(toggleRect, selectedProp.boolValue);
                if (nextSelected != selectedProp.boolValue)
                {
                    selectedProp.boolValue = nextSelected;
                    changed = true;
                }

                EditorGUI.ObjectField(materialRect, materialProp, typeof(Material), GUIContent.none);
            }

            EditorGUILayout.Space(4f);
            var representativeProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.representativeHairMaterialOverride));
            if (representativeProp != null)
            {
                var selectedCandidates = BuildSelectedHairCandidates(hairSelectionsProp);
                changed |= DrawRepresentativeHairMaterialPopup(representativeProp, selectedCandidates);
            }

            return changed;
        }


        private List<Material> BuildSelectedHairCandidates(SerializedProperty hairSelectionsProp)
        {
            var selectedCandidates = new List<Material>();
            if (hairSelectionsProp == null) return selectedCandidates;

            for (var i = 0; i < hairSelectionsProp.arraySize; i++)
            {
                var entryProp = hairSelectionsProp.GetArrayElementAtIndex(i);
                if (entryProp == null) continue;
                var materialProp = entryProp.FindPropertyRelative(nameof(HairMaterialSelection.material));
                var selectedProp = entryProp.FindPropertyRelative(nameof(HairMaterialSelection.selected));
                if (materialProp == null || selectedProp == null || !selectedProp.boolValue) continue;
                var material = materialProp.objectReferenceValue as Material;
                if (material == null || selectedCandidates.Contains(material)) continue;
                selectedCandidates.Add(material);
            }

            return selectedCandidates;
        }

        private bool DrawRepresentativeHairMaterialPopup(SerializedProperty representativeProp, IReadOnlyList<Material> selectedCandidates)
        {
            if (representativeProp == null) return false;

            if (selectedCandidates == null || selectedCandidates.Count == 0)
            {
                representativeProp.objectReferenceValue = null;
                EditorGUILayout.HelpBox(T("代表マテリアル候補がありません。結合対象にチェックを入れてください。", "No representative candidates. Check merge targets."), MessageType.Info);
                return false;
            }

            var changed = false;
            var currentMaterial = representativeProp.objectReferenceValue as Material;
            if (currentMaterial == null || !selectedCandidates.Contains(currentMaterial))
            {
                representativeProp.objectReferenceValue = selectedCandidates[0];
                currentMaterial = selectedCandidates[0];
                changed = true;
            }

            var labels = selectedCandidates.Select(m => m != null ? m.name : "(null)").ToArray();
            var currentIndex = Mathf.Max(0, IndexOfMaterial(selectedCandidates, currentMaterial));
            var nextIndex = EditorGUILayout.Popup(
                TT(
                    "代表マテリアル",
                    "ここで指定したマテリアルの影色やアウトライン色などを結合後のマテリアルで使用します。",
                    "Representative Material",
                    "The merged material uses values such as shadow color and outline color from the material selected here."),
                currentIndex,
                labels);
            nextIndex = Mathf.Clamp(nextIndex, 0, selectedCandidates.Count - 1);
            var nextMaterial = selectedCandidates[nextIndex];
            if (nextMaterial != currentMaterial)
            {
                representativeProp.objectReferenceValue = nextMaterial;
                changed = true;
            }

            return changed;
        }

        private static int IndexOfMaterial(IReadOnlyList<Material> materials, Material target)
        {
            if (materials == null) return -1;
            for (var i = 0; i < materials.Count; i++)
            {
                if (materials[i] == target) return i;
            }

            return -1;
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

        private static bool HasExternalHairSelectionReference(MToonToLilToonComponent component, IReadOnlyCollection<Material> scannedMaterials)
        {
            if (component == null || component.hairSelections == null || component.hairSelections.Count == 0) return false;
            if (scannedMaterials == null || scannedMaterials.Count == 0) return true;

            for (var i = 0; i < component.hairSelections.Count; i++)
            {
                var selection = component.hairSelections[i];
                if (selection == null || selection.material == null) continue;
                if (!scannedMaterials.Contains(selection.material)) return true;
            }

            return false;
        }

        private static bool ShouldAutoScanHairSelectionsOnEnable(MToonToLilToonComponent component, IReadOnlyCollection<Material> scannedMaterials)
        {
            if (component == null || !component.enableHairMerge) return false;
            if (component.hairSelections == null || component.hairSelections.Count == 0) return true;
            return HasExternalHairSelectionReference(component, scannedMaterials);
        }

        private static void ScanMaterials(SerializedObject serializedComponent, MToonToLilToonComponent component)
        {
            if (serializedComponent == null || component == null) return;
            var scannedMaterials = GetRendererMaterials(component);
            var hairSelectionsProp = serializedComponent.FindProperty(nameof(MToonToLilToonComponent.hairSelections));
            hairSelectionsProp.ClearArray();

            if (scannedMaterials.Count == 0)
            {
                return;
            }

            var selections = HairMaterialSelector.BuildDefaultSelections(
                scannedMaterials.Where(m => m != null && MToonDetector.IsMToonLike(m)));
            for (var i = 0; i < selections.Count; i++)
            {
                hairSelectionsProp.InsertArrayElementAtIndex(i);
                var entryProp = hairSelectionsProp.GetArrayElementAtIndex(i);
                entryProp.FindPropertyRelative(nameof(HairMaterialSelection.material)).objectReferenceValue = selections[i].material;
                entryProp.FindPropertyRelative(nameof(HairMaterialSelection.selected)).boolValue = selections[i].selected;
            }

            EnsureFaceMaterialsDetected(serializedComponent, scannedMaterials);

            var eyebrowProp = serializedComponent.FindProperty(nameof(MToonToLilToonComponent.eyebrowStencilMaterial));
            var eyebrowMaterial = eyebrowProp.objectReferenceValue as Material;
            if (eyebrowMaterial == null || !scannedMaterials.Contains(eyebrowMaterial))
            {
                eyebrowProp.objectReferenceValue = DetectDefaultEyebrowMaterial(scannedMaterials);
            }
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

        private static Material DetectDefaultEyebrowMaterial(IReadOnlyList<Material> materials)
        {
            if (materials == null || materials.Count == 0) return null;

            var eyebrow = materials.FirstOrDefault(m => m != null
                && (m.name.IndexOf("EYEBROW", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || m.name.IndexOf("BROW", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || m.name.IndexOf("眉", System.StringComparison.OrdinalIgnoreCase) >= 0));
            if (eyebrow != null) return eyebrow;

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
            var fakeShadowMaterialProp = serializedObject.FindProperty(nameof(MToonToLilToonComponent.fakeShadowFaceMaterial));
            var labels = new[] { T("未設定", "None") }.Concat(candidates.Select(m => m != null ? m.name : "(null)")).ToArray();
            var currentFaceMaterial = faceMaterialProp.objectReferenceValue as Material;
            var currentIndex = currentFaceMaterial != null ? candidates.IndexOf(currentFaceMaterial) + 1 : 0;

            EditorGUI.BeginChangeCheck();
            var nextIndex = EditorGUILayout.Popup(
                TT(
                    "顔マテリアル",
                    "顔だけ除外する設定やFakeShadow、顔の影を整える機能などの対象を指定します。",
                    "Face Material",
                    "Specifies the target for face-only exclusions, FakeShadow, face shadow tuning, and related features."),
                currentIndex,
                labels);
            if (!EditorGUI.EndChangeCheck()) return false;

            var nextMaterial = nextIndex <= 0 ? null : candidates[nextIndex - 1];
            faceMaterialProp.objectReferenceValue = nextMaterial;
            fakeShadowMaterialProp.objectReferenceValue = nextMaterial;
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

            var fakeShadowMaterialProp = serializedComponent.FindProperty(nameof(MToonToLilToonComponent.fakeShadowFaceMaterial));
            var fakeShadowMaterial = fakeShadowMaterialProp.objectReferenceValue as Material;
            if (fakeShadowMaterial == null || !scannedMaterials.Contains(fakeShadowMaterial))
            {
                fakeShadowMaterialProp.objectReferenceValue = faceMaterial;
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
