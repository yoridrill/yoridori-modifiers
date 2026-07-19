using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.HairLookKit
{
    [CustomEditor(typeof(YMHairLookKitComponent))]
    public sealed class YMHairLookKitComponentEditor : Editor
    {
        private enum Language
        {
            Japanese,
            English
        }

        private const string PrefKeyLanguage = "YMHairLookKitComponentEditor.Language";
        private const float CategorySpacing = 4f;
        private const float SectionTopSpacing = 10f;
        private const float AdvancedTopSpacing = 6f;
        private const float ToggleWidth = 16f;
        private const float MainLabelWidth = 92f;
        private const float SubLabelWidth = 72f;
        private const float Gap = 8f;
        private static readonly int[] AtlasSizeValues = { 256, 512, 1024, 2048, 4096, 8192 };
        private static readonly string[] AtlasSizeLabels = AtlasSizeValues.Select(size => $"{size} x {size}").ToArray();
        private Language _language;
        private List<Material> _materials;

        private void OnEnable()
        {
            _language = (Language)EditorPrefs.GetInt(PrefKeyLanguage, 0);
            var component = (YMHairLookKitComponent)target;
            _materials = YMHairLookKitDefaults.GetRendererMaterials(component);
            var undoGroup = Undo.GetCurrentGroup();
            serializedObject.Update();
            YMHairLookKitDefaults.EnsureFaceAndEyebrowMaterialsDetected(serializedObject, _materials);
            if (serializedObject.ApplyModifiedProperties())
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var component = (YMHairLookKitComponent)target;
            _materials ??= YMHairLookKitDefaults.GetRendererMaterials(component);
            var previewStateBefore = BuildPreviewStateKey(component);

            DrawTopBar(component);
            EditorGUILayout.Space(SectionTopSpacing);
            DrawSectionTitle(T("マテリアル最適化", "Material Optimization"));
            EditorGUILayout.Space(2f);
            DrawHairMerge(component);
            EditorGUILayout.Space(8f);
            DrawSectionTitle(T("lilToon固有機能", "lilToon-specific Features"));
            EditorGUILayout.Space(2f);
            DrawEyebrow(component);
            EditorGUILayout.Space(CategorySpacing);
            DrawFakeShadow(component);
            EditorGUILayout.Space(CategorySpacing);
            DrawOutline(component);
            EditorGUILayout.Space(AdvancedTopSpacing);
            DrawAdvanced(component);

            var changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                EditorUtility.SetDirty(component);
            }

            if (previewStateBefore != BuildPreviewStateKey(component) && YMHairLookKitPreviewUtility.IsPreviewing(component))
            {
                YMHairLookKitPreviewUtility.RestartPreviewIfActive(component);
            }
        }

        private void DrawTopBar(YMHairLookKitComponent component)
        {
            using var horizontal = new EditorGUILayout.HorizontalScope();
            if (PreviewInspectorGui.DrawPreviewButton(YMHairLookKitPreviewUtility.IsPreviewing(component)))
            {
                YMHairLookKitPreviewUtility.TogglePreview(component);
                EditorUtility.SetDirty(component);
            }
            var progress = YMHairLookKitPreviewUtility.IsProcessingPreview()
                ? "Processing..."
                : YMHairLookKitPreviewUtility.GetPreviewProgressMessage();
            PreviewInspectorGui.DrawStatus(
                YMHairLookKitPreviewUtility.IsProcessingPreview(),
                YMHairLookKitPreviewUtility.HasPreviewFailed(),
                progress);
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            var nextLanguage = (Language)EditorGUILayout.EnumPopup(_language, GUILayout.Width(90f));
            if (EditorGUI.EndChangeCheck())
            {
                _language = nextLanguage;
                EditorPrefs.SetInt(PrefKeyLanguage, (int)_language);
            }
        }

        private void DrawHairMerge(YMHairLookKitComponent component)
        {
            var prop = serializedObject.FindProperty(nameof(YMHairLookKitComponent.enableHairMerge));
            EditorGUI.BeginChangeCheck();
            prop.boolValue = EditorGUILayout.ToggleLeft(T("髪マテリアル結合", "Merge Hair Materials"), prop.boolValue);
            if (EditorGUI.EndChangeCheck() && prop.boolValue)
            {
                serializedObject.ApplyModifiedProperties();
                YMHairLookKitDefaults.ScanHairSelections(component);
                serializedObject.Update();
                _materials = YMHairLookKitDefaults.GetRendererMaterials(component);
            }
            if (!prop.boolValue) return;

            using (new EditorGUI.IndentLevelScope())
            {
                var hairSelectionsProp = serializedObject.FindProperty(nameof(YMHairLookKitComponent.hairSelections));
                var selectedCandidates = BuildSelectedHairCandidates(hairSelectionsProp);
                DrawHairMergeSummary(component, selectedCandidates);
                var showProp = serializedObject.FindProperty(nameof(YMHairLookKitComponent.showHairMaterials));
                showProp.boolValue = EditorGUILayout.Foldout(showProp.boolValue, T("マテリアル一覧", "Materials"), true);
                if (showProp.boolValue)
                {
                    for (var i = 0; i < hairSelectionsProp.arraySize; i++)
                    {
                        var entry = hairSelectionsProp.GetArrayElementAtIndex(i);
                        var materialProp = entry.FindPropertyRelative(nameof(HairMaterialSelection.material));
                        var selectedProp = entry.FindPropertyRelative(nameof(HairMaterialSelection.selected));
                        var row = EditorGUILayout.GetControlRect();
                        selectedProp.boolValue = EditorGUI.Toggle(new Rect(row.x, row.y, ToggleWidth, row.height), selectedProp.boolValue);
                        EditorGUI.ObjectField(new Rect(row.x + ToggleWidth + 4f, row.y, row.width - ToggleWidth - 4f, row.height), materialProp, typeof(Material), GUIContent.none);
                    }
                }

                DrawMaterialPopup(
                    serializedObject.FindProperty(nameof(YMHairLookKitComponent.representativeHairMaterialOverride)),
                    selectedCandidates,
                    TT(
                        "代表マテリアル",
                        "ここで指定したマテリアルの影色やアウトライン色などを結合後のマテリアルで使用します。",
                        "Representative Material",
                        "The merged material uses values such as shadow color and outline color from the material selected here."),
                    allowNone: false);
                DrawAtlasSizePopup(serializedObject.FindProperty(nameof(YMHairLookKitComponent.hairAtlasMaxSize)));
                EditorGUILayout.Space(2f);
                if (HairLookValidator.HasMixedMergeSettings(component))
                {
                    EditorGUILayout.HelpBox(T(
                        "異なるシェーダー/描画モード/Cull設定のマテリアルが同じ髪結合に含まれています。",
                        "Materials with different shaders/render modes/Cull settings are included in the same hair merge."),
                        MessageType.Warning);
                }
            }
        }

        private static void DrawSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var lineRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(lineRect, new Color(0.3f, 0.3f, 0.3f, 0.9f));
        }

        private void DrawEyebrow(YMHairLookKitComponent component)
        {
            var prop = serializedObject.FindProperty(nameof(YMHairLookKitComponent.enableEyebrowStencil));
            var enabled = DrawCategoryHairTargetRow(
                TT(
                    "眉ステンシル",
                    "髪の手前に眉を表示します。 このツールでは簡略化のためCutoutに変更します。",
                    "Eyebrow Stencil",
                    "Shows eyebrows in front of hair. This tool switches it to Cutout for simplicity."),
                prop,
                serializedObject.FindProperty(nameof(YMHairLookKitComponent.eyebrowHairTargetMode)),
                serializedObject.FindProperty(nameof(YMHairLookKitComponent.eyebrowHairMaterial)),
                () => AutoSelectEyebrow(component, force: true));
            if (!enabled) return;

            DrawMaterialPopupRow(T("顔", "Face"), serializedObject.FindProperty(nameof(YMHairLookKitComponent.eyebrowFaceMaterial)), _materials, true);
            DrawMaterialPopupRow(T("眉", "Eyebrow"), serializedObject.FindProperty(nameof(YMHairLookKitComponent.eyebrowMaterial)), _materials, true);
            DrawCategoryErrors(component, "Eyebrow");
        }

        private void DrawFakeShadow(YMHairLookKitComponent component)
        {
            var prop = serializedObject.FindProperty(nameof(YMHairLookKitComponent.enableFakeShadow));
            var enabled = DrawCategoryHairTargetRow(
                TT(
                    "FakeShadow",
                    "前髪の擬似落ち影を生成します。",
                    "FakeShadow",
                    "Generates pseudo drop shadow for bangs."),
                prop,
                serializedObject.FindProperty(nameof(YMHairLookKitComponent.fakeShadowHairTargetMode)),
                serializedObject.FindProperty(nameof(YMHairLookKitComponent.fakeShadowHairMaterial)),
                () => AutoSelectFakeShadow(component, force: true));
            if (!enabled) return;

            DrawMaterialPopupRow(T("顔", "Face"), serializedObject.FindProperty(nameof(YMHairLookKitComponent.fakeShadowFaceMaterial)), _materials, true);
            DrawPropertyRow(T("向き", "Direction"), serializedObject.FindProperty(nameof(YMHairLookKitComponent.fakeShadowDirection)));
            DrawPropertyRow(T("オフセット", "Offset"), serializedObject.FindProperty(nameof(YMHairLookKitComponent.fakeShadowOffset)));
            DrawFakeShadowCompositeModeRow(serializedObject.FindProperty(nameof(YMHairLookKitComponent.fakeShadowCompositeMode)));
            DrawCategoryErrors(component, "FakeShadow");
        }

        private void DrawOutline(YMHairLookKitComponent component)
        {
            var prop = serializedObject.FindProperty(nameof(YMHairLookKitComponent.enableHairOutlineCorrection));
            var modeProp = serializedObject.FindProperty(nameof(YMHairLookKitComponent.outlineHairTargetMode));
            var materialProp = serializedObject.FindProperty(nameof(YMHairLookKitComponent.outlineHairMaterial));
            var enabled = DrawCategoryHairTargetRow(
                TT(
                    "輪郭線補正",
                    "ハードエッジ向けのオプションです。頂点カラーに同一座標の法線の平均を焼き込み、輪郭線を整えます。",
                    "Outline Fix",
                    "Option for hard-edged meshes. Bakes averaged same-position normals into vertex colors to refine outlines."),
                prop,
                modeProp,
                materialProp,
                () => AutoSelectOutline(component, force: true));
            if (!enabled) return;

            var tipOptionsEnabled = serializedObject.FindProperty(nameof(YMHairLookKitComponent.enableHairMerge)).boolValue
                && modeProp.intValue == (int)YMHairLookKitComponent.HairTargetMode.MergedHair;
            using (new EditorGUI.DisabledScope(!tipOptionsEnabled))
            {
                DrawSliderRow(
                    TT(
                        "毛先の太さ",
                        "このツールで髪マテリアル結合を行うVRoid向けのオプションです。UV下端を毛先とし、毛先の輪郭線の太さを調整します。",
                        "Tip Width",
                        "VRoid option for hair material merge in this tool. Treats the lower UV edge as tip and adjusts tip outline thickness."),
                    serializedObject.FindProperty(nameof(YMHairLookKitComponent.hairTipOutlineWidth)),
                    0f,
                    1f);
                DrawSliderRow(
                    TT(
                        "毛先の範囲",
                        "このツールで髪マテリアル結合を行うVRoid向けのオプションです。大きくすると根本近くまで細くする範囲が広がります。",
                        "Tip Range",
                        "VRoid option for hair material merge in this tool. Larger values extend the thinning area closer to hair roots."),
                    serializedObject.FindProperty(nameof(YMHairLookKitComponent.hairTipRange)),
                    0f,
                    1f);
            }
            if (!tipOptionsEnabled)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.HelpBox(T(
                        "毛先の調整はこのツールで髪マテリアル結合を行うVRoid向けのオプションです。",
                        "Tip adjustment is a VRoid-oriented option for using hair material merge in this tool."),
                        MessageType.Info);
                }
            }
            DrawCategoryErrors(component, "Outline");
        }

        private void DrawHairMergeSummary(YMHairLookKitComponent component, IReadOnlyList<Material> selectedCandidates)
        {
            var shaderName = ResolveMergeShaderLabel(component, selectedCandidates);
            EditorGUILayout.HelpBox(
                T(
                    $"今の設定で結合されるマテリアル数: {selectedCandidates?.Count ?? 0}個\n結合先シェーダー: {shaderName}",
                    $"Materials merged with current settings: {selectedCandidates?.Count ?? 0}\nOutput shader: {shaderName}"),
                MessageType.Info);
        }

        private string ResolveMergeShaderLabel(YMHairLookKitComponent component, IReadOnlyList<Material> selectedCandidates)
        {
            if (selectedCandidates == null || selectedCandidates.Count == 0) return T("未設定", "None");

            var representative = component.representativeHairMaterialOverride != null
                && selectedCandidates.Contains(component.representativeHairMaterialOverride)
                    ? component.representativeHairMaterialOverride
                    : selectedCandidates[0];
            if (representative == null || representative.shader == null) return T("未設定", "None");

            if (representative.shader.name.IndexOf("liltoon", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "lilToon";
            }

            if (MToonDetector.IsMToonLike(representative) && HasMToonToLilToonComponent(component))
            {
                return "lilToon";
            }

            return representative.shader.name;
        }

        private static bool HasMToonToLilToonComponent(YMHairLookKitComponent component)
        {
            if (component == null) return false;
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            return root != null && root.GetComponentsInChildren<MToonToLilToonComponent>(true).Any(c => c != null);
        }

        private bool DrawCategoryHairTargetRow(
            GUIContent categoryLabel,
            SerializedProperty enabledProp,
            SerializedProperty modeProp,
            SerializedProperty materialProp,
            System.Action onEnabled)
        {
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            GetThreeColumnRects(row, out var categoryRect, out var labelRect, out var valueRect);

            var toggleRect = new Rect(categoryRect.x, categoryRect.y, ToggleWidth, categoryRect.height);
            var categoryLabelRect = new Rect(toggleRect.xMax + 2f, categoryRect.y, Mathf.Max(0f, categoryRect.width - ToggleWidth - 2f), categoryRect.height);
            EditorGUI.BeginChangeCheck();
            enabledProp.boolValue = EditorGUI.Toggle(toggleRect, enabledProp.boolValue);
            EditorGUI.LabelField(categoryLabelRect, categoryLabel);
            if (EditorGUI.EndChangeCheck() && enabledProp.boolValue)
            {
                onEnabled?.Invoke();
            }

            if (enabledProp.boolValue)
            {
                DrawHairTargetFields(labelRect, valueRect, modeProp, materialProp);
            }

            return enabledProp.boolValue;
        }

        private void DrawHairTargetFields(Rect labelRect, Rect valueRect, SerializedProperty modeProp, SerializedProperty materialProp)
        {
            _materials ??= new List<Material>();
            var labels = new[] { T("結合した髪マテリアル", "Merged Hair Material") }
                .Concat(_materials.Select(m => m != null ? m.name : "(null)"))
                .ToArray();
            var mode = (YMHairLookKitComponent.HairTargetMode)Mathf.Clamp(modeProp.intValue, 0, 1);
            var currentMaterial = materialProp.objectReferenceValue as Material;
            var currentIndex = mode == YMHairLookKitComponent.HairTargetMode.MergedHair
                ? 0
                : IndexOfMaterial(_materials, currentMaterial) + 1;
            currentIndex = Mathf.Clamp(currentIndex, 0, labels.Length - 1);
            EditorGUI.LabelField(labelRect, T("髪", "Hair"));
            var nextIndex = EditorGUI.Popup(valueRect, currentIndex, labels);
            if (nextIndex <= 0)
            {
                modeProp.intValue = (int)YMHairLookKitComponent.HairTargetMode.MergedHair;
                return;
            }

            modeProp.intValue = (int)YMHairLookKitComponent.HairTargetMode.Material;
            materialProp.objectReferenceValue = _materials[Mathf.Clamp(nextIndex - 1, 0, _materials.Count - 1)];
        }

        private void DrawMaterialPopupRow(string label, SerializedProperty prop, IReadOnlyList<Material> candidates, bool allowNone)
        {
            candidates ??= new List<Material>();
            var current = prop.objectReferenceValue as Material;
            var labels = allowNone
                ? new[] { T("未設定", "None") }.Concat(candidates.Select(m => m != null ? m.name : "(null)")).ToArray()
                : candidates.Select(m => m != null ? m.name : "(null)").ToArray();

            var row = EditorGUILayout.GetControlRect();
            GetThreeColumnRects(row, out _, out var labelRect, out var valueRect);
            EditorGUI.LabelField(labelRect, label);
            if (labels.Length == 0)
            {
                EditorGUI.Popup(valueRect, 0, new[] { T("候補なし", "No Candidates") });
                return;
            }

            var currentIndex = current != null ? IndexOfMaterial(candidates, current) : -1;
            if (allowNone) currentIndex++;
            currentIndex = Mathf.Clamp(currentIndex, 0, labels.Length - 1);
            var nextIndex = EditorGUI.Popup(valueRect, currentIndex, labels);
            prop.objectReferenceValue = allowNone
                ? nextIndex <= 0 ? null : candidates[nextIndex - 1]
                : candidates[Mathf.Clamp(nextIndex, 0, candidates.Count - 1)];
        }

        private void DrawPropertyRow(string label, SerializedProperty prop)
        {
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            GetThreeColumnRects(row, out _, out var labelRect, out var valueRect);
            EditorGUI.LabelField(labelRect, label);
            EditorGUI.PropertyField(valueRect, prop, GUIContent.none);
        }

        private void DrawSliderRow(GUIContent label, SerializedProperty prop, float min, float max)
        {
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            GetThreeColumnRects(row, out _, out var labelRect, out var valueRect);
            EditorGUI.LabelField(labelRect, label);
            prop.floatValue = EditorGUI.Slider(valueRect, prop.floatValue, min, max);
        }

        private void DrawFakeShadowCompositeModeRow(SerializedProperty prop)
        {
            if (prop == null) return;
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            GetThreeColumnRects(row, out _, out var labelRect, out var valueRect);
            EditorGUI.LabelField(labelRect, TT(
                "合成モード",
                "FakeShadowと顔の色を合成する方式です。",
                "Blend Mode",
                "Chooses how FakeShadow is composited with the face."));
            var options = new[] { T("乗算", "Multiply"), T("比較(暗)", "Darken") };
            prop.enumValueIndex = GUI.Toolbar(valueRect, Mathf.Clamp(prop.enumValueIndex, 0, options.Length - 1), options);
        }

        private static void GetThreeColumnRects(Rect row, out Rect firstRect, out Rect secondRect, out Rect valueRect)
        {
            firstRect = new Rect(row.x, row.y, ToggleWidth + 2f + MainLabelWidth, row.height);
            secondRect = new Rect(firstRect.xMax + Gap, row.y, SubLabelWidth, row.height);
            valueRect = new Rect(secondRect.xMax + 4f, row.y, Mathf.Max(0f, row.xMax - (secondRect.xMax + 4f)), row.height);
        }

        private void DrawCategoryErrors(YMHairLookKitComponent component, string category)
        {
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            var errors = HairLookValidator.BuildErrors(
                    component,
                    HairLookTargetResolver.CollectCurrentMaterials(root),
                    _language == Language.English)
                .Where(error => error.StartsWith($"{category}: ", System.StringComparison.Ordinal));
            foreach (var error in errors)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.HelpBox(error.Substring(category.Length + 2), MessageType.Error);
                }
            }
        }

        private void DrawAdvanced(YMHairLookKitComponent component)
        {
            var showProp = serializedObject.FindProperty(nameof(YMHairLookKitComponent.showAdvanced));
            showProp.boolValue = EditorGUILayout.Foldout(showProp.boolValue, "Advanced", true);
            if (!showProp.boolValue) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(YMHairLookKitComponent.verboseLog)), new GUIContent("Verbose Log"));
                EditorGUILayout.Space(4f);

                var rawButtonRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                var buttonRect = EditorGUI.IndentedRect(rawButtonRect);
                if (GUI.Button(buttonRect, "Reset Preview"))
                {
                    PreviewRecoveryUtility.ResetAllPreviewArtifacts();
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.HelpBox(
                    T(
                        "モデルが重複したり、見えない場合に押してください。\nPreview オブジェクトを削除し、Renderer を再表示します。",
                        "Use this if the avatar stays hidden, frozen, or stuck after Preview.\nThis removes temporary Preview objects and re-enables renderers."),
                    MessageType.Warning);
            }
        }

        private void AutoSelectEyebrow(YMHairLookKitComponent component, bool force = false)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(component, "Auto Select Eyebrow Stencil Materials");
            component.eyebrowHairTargetMode = component.enableHairMerge ? YMHairLookKitComponent.HairTargetMode.MergedHair : YMHairLookKitComponent.HairTargetMode.Material;
            if (force || component.eyebrowHairMaterial == null) component.eyebrowHairMaterial = YMHairLookKitDefaults.DetectDefaultHairMaterial(_materials);
            if (force || component.eyebrowFaceMaterial == null) component.eyebrowFaceMaterial = YMHairLookKitDefaults.DetectDefaultFaceMaterial(_materials);
            if (force || component.eyebrowMaterial == null) component.eyebrowMaterial = YMHairLookKitDefaults.DetectDefaultEyebrowMaterial(_materials);
            EditorUtility.SetDirty(component);
            serializedObject.Update();
        }

        private void AutoSelectFakeShadow(YMHairLookKitComponent component, bool force = false)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(component, "Auto Select FakeShadow Materials");
            component.fakeShadowHairTargetMode = component.enableHairMerge ? YMHairLookKitComponent.HairTargetMode.MergedHair : YMHairLookKitComponent.HairTargetMode.Material;
            if (force || component.fakeShadowHairMaterial == null) component.fakeShadowHairMaterial = YMHairLookKitDefaults.DetectDefaultHairMaterial(_materials);
            if (force || component.fakeShadowFaceMaterial == null) component.fakeShadowFaceMaterial = YMHairLookKitDefaults.DetectDefaultFaceMaterial(_materials);
            EditorUtility.SetDirty(component);
            serializedObject.Update();
        }

        private void AutoSelectOutline(YMHairLookKitComponent component, bool force = false)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(component, "Auto Select Outline Materials");
            component.outlineHairTargetMode = component.enableHairMerge ? YMHairLookKitComponent.HairTargetMode.MergedHair : YMHairLookKitComponent.HairTargetMode.Material;
            if (force || component.outlineHairMaterial == null) component.outlineHairMaterial = YMHairLookKitDefaults.DetectDefaultHairMaterial(_materials);
            EditorUtility.SetDirty(component);
            serializedObject.Update();
        }

        private static List<Material> BuildSelectedHairCandidates(SerializedProperty hairSelectionsProp)
        {
            var result = new List<Material>();
            if (hairSelectionsProp == null) return result;
            for (var i = 0; i < hairSelectionsProp.arraySize; i++)
            {
                var entry = hairSelectionsProp.GetArrayElementAtIndex(i);
                var material = entry.FindPropertyRelative(nameof(HairMaterialSelection.material)).objectReferenceValue as Material;
                var selected = entry.FindPropertyRelative(nameof(HairMaterialSelection.selected)).boolValue;
                if (material != null && selected && !result.Contains(material)) result.Add(material);
            }
            return result;
        }

        private bool DrawMaterialPopup(SerializedProperty prop, IReadOnlyList<Material> candidates, GUIContent label, bool allowNone)
        {
            candidates ??= new List<Material>();
            var current = prop.objectReferenceValue as Material;
            if (!allowNone && (current == null || !candidates.Contains(current)) && candidates.Count > 0)
            {
                prop.objectReferenceValue = candidates[0];
                current = candidates[0];
            }

            var labels = allowNone
                ? new[] { T("未設定", "None") }.Concat(candidates.Select(m => m != null ? m.name : "(null)")).ToArray()
                : candidates.Select(m => m != null ? m.name : "(null)").ToArray();
            if (labels.Length == 0)
            {
                EditorGUILayout.Popup(label, 0, new[] { T("候補なし", "No Candidates") });
                return false;
            }

            var currentIndex = current != null ? IndexOfMaterial(candidates, current) : -1;
            if (allowNone) currentIndex++;
            currentIndex = Mathf.Clamp(currentIndex, 0, labels.Length - 1);
            var nextIndex = EditorGUILayout.Popup(label, currentIndex, labels);
            var next = allowNone
                ? nextIndex <= 0 ? null : candidates[nextIndex - 1]
                : candidates[Mathf.Clamp(nextIndex, 0, candidates.Count - 1)];
            if (next == current) return false;
            prop.objectReferenceValue = next;
            return true;
        }

        private bool DrawMaterialPopup(SerializedProperty prop, IReadOnlyList<Material> candidates, string label, bool allowNone)
        {
            return DrawMaterialPopup(prop, candidates, new GUIContent(label), allowNone);
        }

        private GUIContent TT(string ja, string jaTooltip, string en, string enTooltip)
        {
            return _language == Language.Japanese
                ? new GUIContent(ja, jaTooltip)
                : new GUIContent(en, enTooltip);
        }

        private void DrawAtlasSizePopup(SerializedProperty prop)
        {
            if (prop == null) return;
            var currentIndex = System.Array.IndexOf(AtlasSizeValues, prop.intValue);
            if (currentIndex < 0)
            {
                currentIndex = System.Array.IndexOf(AtlasSizeValues, 2048);
                prop.intValue = 2048;
            }

            var nextIndex = EditorGUILayout.Popup(T("アトラス上限サイズ", "Atlas Max Size"), currentIndex, AtlasSizeLabels);
            prop.intValue = AtlasSizeValues[Mathf.Clamp(nextIndex, 0, AtlasSizeValues.Length - 1)];
        }

        private static string BuildPreviewStateKey(YMHairLookKitComponent component)
        {
            if (component == null) return string.Empty;
            return JsonUtility.ToJson(component);
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

        private string T(string ja, string en) => _language == Language.Japanese ? ja : en;
    }
}
