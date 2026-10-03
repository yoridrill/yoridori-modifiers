using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.OutlineExtender
{
    [CustomEditor(typeof(YMOutlineExtenderComponent))]
    public sealed partial class YMOutlineExtenderComponentEditor : Editor
    {
        private enum Language { Japanese, English }
        private const string PrefKeyLanguage = "YMOutlineExtenderComponentEditor.Language";
        private const float SectionTopSpacing = 10f;
        private const float AdvancedTopSpacing = 6f;
        private const float MethodButtonWidth = 64f;
        private const float FoldButtonGap = 3f;
        private const float ShaderButtonGap = 7f;
        private string previewStateKey;
        private OutlineHairMaterialControls hairControls;
        private string summary;
        private readonly List<string> errors = new();
        private Language language;
        private List<Material> materials;
        private string T(string ja, string en) => language == Language.Japanese ? ja : en;
        private GUIContent TT(string ja, string jaTip, string en, string enTip) =>
            language == Language.Japanese ? new GUIContent(ja, jaTip) : new GUIContent(en, enTip);

        private void OnEnable()
        {
            language = (Language)EditorPrefs.GetInt(PrefKeyLanguage, 0);
            RefreshInspectorState(true);
            EnableInspectorUpdates();
        }

        private void OnDisable() => DisableInspectorUpdates();

        private void RefreshInspectorState(bool scan)
        {
            rescanPending = false;
            if (scan) Scan();
            var component = (YMOutlineExtenderComponent)target;
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            hairControls = OutlineHairMaterialControls.Create(root);
            var materialSet = materials.ToHashSet();
            List<Material> Selected(IEnumerable<OutlineMaterialSelection> entries) => entries
                .Where(s => s != null && s.selected && materialSet.Contains(s.material) && !hairControls.IsSecondary(s.material))
                .Select(s => s.material).Distinct().ToList();
            var shaderSelected = Selected(component.materialSelections);
            var directSelected = Selected(component.bakeMaterialSelections);
            var boundarySelected = Selected(component.boundaryMaterialSelections);
            summary = T($"Shader: {shaderSelected.Count}個 / Draw: {directSelected.Count}個 / Fold: {boundarySelected.Count}個",
                $"Shader: {shaderSelected.Count} / Draw: {directSelected.Count} / Fold: {boundarySelected.Count}");
            errors.Clear();
            var hasConverter = root.GetComponentsInChildren<MToonToLilToonComponent>(true).Any(c => c != null && c.enabled);
            CacheCategoryErrors(shaderSelected, false, hasConverter);
            CacheCategoryErrors(directSelected, true, hasConverter);
            CacheBoundaryErrors(boundarySelected);
            RefreshDependencies(root);
            RequestPolygonEstimate(root, component, boundarySelected.Count > 0);
            previewStateKey = BuildPreviewStateKey(component);
            Repaint();
        }

        private void Scan()
        {
            var component = (YMOutlineExtenderComponent)target;
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            materials = OutlineMaterialUtility.CollectMaterials(root);
            // Preserve the old master switch's OFF state when migrating to row controls.
            if (!component.enableBoundaryFold)
            {
                Undo.RecordObject(component, "Migrate Outline Extender Fold Controls");
                foreach (var selection in component.boundaryMaterialSelections)
                    if (selection != null) selection.selected = false;
                component.enableBoundaryFold = true;
                EditorUtility.SetDirty(component);
            }
            bool HasAllMaterials(IEnumerable<OutlineMaterialSelection> selections)
            {
                var known = selections.Where(entry => entry != null && entry.material != null)
                    .Select(entry => entry.material).ToHashSet();
                return materials.All(known.Contains);
            }
            if (HasAllMaterials(component.materialSelections) && HasAllMaterials(component.bakeMaterialSelections)
                && HasAllMaterials(component.boundaryMaterialSelections)) return;
            Undo.RecordObject(component, "Scan Outline Extender Materials");
            var hasMToonConverter = root.GetComponentsInChildren<MToonToLilToonComponent>(true).Any(item => item != null);
            OutlineMaterialUtility.Synchronize(component.materialSelections, materials, hasMToonConverter);
            OutlineMaterialUtility.SynchronizeManualSelections(component.bakeMaterialSelections, materials);
            OutlineMaterialUtility.SynchronizeManualSelections(component.boundaryMaterialSelections, materials);
            EditorUtility.SetDirty(component);
        }

        public override void OnInspectorGUI()
        {
            lastGuiTime = EditorApplication.timeSinceStartup;
            serializedObject.Update();
            var component = (YMOutlineExtenderComponent)target;
            materials ??= OutlineMaterialUtility.CollectMaterials(
                PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject);
            var previewStateBefore = previewStateKey;
            DrawTopBar(component);
            EditorGUILayout.Space(SectionTopSpacing);

            var enabled = serializedObject.FindProperty(nameof(component.enableOutlineExtender));
            enabled.boolValue = EditorGUILayout.ToggleLeft(TT(
                "輪郭線を延長",
                "カットアウト境界やメッシュの開放端など、通常の輪郭線が出ないところに近い見た目の線が出るようにします。 lilToon, 新旧MToonに対応しています。",
                "Extend Outlines",
                "Adds lines resembling the regular outline where it would otherwise be missing, such as cutout boundaries and open mesh edges. Supports lilToon, legacy MToon and MToon10."), enabled.boolValue);
            if (enabled.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    var info = summary + "\n" + (polygonEstimateReady && !polygonEstimatePending
                        ? T($"Fold対象のポリゴン数: {polygonCounts.before:N0}→{polygonCounts.after:N0} (+{polygonCounts.after - polygonCounts.before:N0})",
                            $"Fold target polygons: {polygonCounts.before:N0}→{polygonCounts.after:N0} (+{polygonCounts.after - polygonCounts.before:N0})")
                        : T("Fold対象のポリゴン数: 計算中…", "Fold target polygons: Calculating…"));
                    EditorGUILayout.HelpBox(info, MessageType.Info);
                    DrawMaterialMatrix(component);
                    foreach (var error in errors) EditorGUILayout.HelpBox(error, MessageType.Error);
                    EditorGUILayout.Space(2f);
                    EditorGUILayout.Slider(
                        serializedObject.FindProperty(nameof(component.outlineWidthMultiplier)),
                        0f, 2f,
                        TT("太さ比率", "最終Materialの輪郭線の太さを基準にして、0～2倍で太さを調整します。1で同じ太さです。",
                            "Width Ratio", "Adjusts the width from 0 to 2 times the final material's outline width. A value of 1 uses the same width."));
                }
            }
            EditorGUILayout.Space(AdvancedTopSpacing);
            DrawAdvanced(component);
            var changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                EditorUtility.SetDirty(component);
                var state = BuildPreviewStateKey(component);
                if (state != previewStateBefore)
                {
                    RefreshInspectorState(false);
                    if (YMOutlineExtenderPreviewUtility.IsPreviewing(component))
                        YMOutlineExtenderPreviewUtility.RestartPreviewIfActive(component);
                }
            }
        }

        private void DrawTopBar(YMOutlineExtenderComponent component)
        {
            using var horizontal = new EditorGUILayout.HorizontalScope();
            if (PreviewInspectorGui.DrawPreviewButton(YMOutlineExtenderPreviewUtility.IsPreviewing(component)))
            {
                YMOutlineExtenderPreviewUtility.TogglePreview(component);
                EditorUtility.SetDirty(component);
            }
            var progress = YMOutlineExtenderPreviewUtility.IsProcessingPreview()
                ? "Processing..."
                : YMOutlineExtenderPreviewUtility.GetPreviewProgressMessage();
            PreviewInspectorGui.DrawStatus(
                YMOutlineExtenderPreviewUtility.IsProcessingPreview(),
                YMOutlineExtenderPreviewUtility.HasPreviewFailed(),
                progress);
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            var next = (Language)EditorGUILayout.EnumPopup(language, GUILayout.Width(90f));
            if (EditorGUI.EndChangeCheck()) { language = next; EditorPrefs.SetInt(PrefKeyLanguage, (int)language); RefreshInspectorState(false); }
        }

        private void DrawMaterialMatrix(YMOutlineExtenderComponent component)
        {
            var show = serializedObject.FindProperty(nameof(component.showMaterials));
            show.boolValue = EditorGUILayout.Foldout(show.boolValue, T("マテリアル一覧", "Materials"), true);
            if (!show.boolValue) return;
            var shaderLabel = TT("Shader", "lilToonカスタムシェーダーでカットアウト境界に輪郭線を描きます。 Draw方式より高精細です。 メインテクスチャのアルファチャンネルに境界までの距離情報を焼き込みます。",
                "Shader", "Draws outlines along cutout boundaries using a custom lilToon shader, with finer detail than Draw. Bakes the distance to the boundary into the main texture alpha channel.");
            var directLabel = TT("Draw", "メインテクスチャに輪郭線を直接描き込みます。 UV次第では荒くなりますが、カスタムシェーダーに変更することなく線を出すことが可能です。",
                "Draw", "Draws outlines directly into the main texture. Depending on the UV layout, lines may look coarse, but no custom shader is required.");
            var foldLabel = TT("Fold", "開放端に短い折り返しを追加し、既存の輪郭線が出るようにします。 他の方式と併用できます。",
                "Fold", "Adds a short folded strip at open mesh edges so the existing outline becomes visible. Can be combined with the other methods.");
            var shaderWidth = MethodButtonWidth;
            var directWidth = MethodButtonWidth;
            var foldWidth = MethodButtonWidth;
            var indent = EditorGUI.indentLevel;
            var rowIndent = EditorGUI.IndentedRect(new Rect(0f, 0f, 1000f, 0f)).x;
            EditorGUI.indentLevel = 0;
            try
            {
                Rect Column(Rect row, int index)
                {
                    var foldX = row.xMax - foldWidth;
                    var directX = foldX - FoldButtonGap - directWidth;
                    var shaderX = directX - shaderWidth;
                    return index == 0 ? new Rect(shaderX, row.y, shaderWidth, row.height)
                        : index == 1 ? new Rect(directX, row.y, directWidth, row.height)
                        : new Rect(foldX, row.y, foldWidth, row.height);
                }
                var shaderEntries = IndexSelections(serializedObject.FindProperty(nameof(component.materialSelections)));
                var directEntries = IndexSelections(serializedObject.FindProperty(nameof(component.bakeMaterialSelections)));
                var foldEntries = IndexSelections(serializedObject.FindProperty(nameof(component.boundaryMaterialSelections)));
                foreach (var material in materials)
                {
                    var shader = shaderEntries.GetValueOrDefault(material);
                    var direct = directEntries.GetValueOrDefault(material);
                    var fold = foldEntries.GetValueOrDefault(material);
                    if (shader == null || direct == null || fold == null) continue;
                    var rawRow = EditorGUILayout.GetControlRect(false);
                    var row = new Rect(rawRow.x + rowIndent, rawRow.y, rawRow.width - rowIndent, rawRow.height);
                    var field = new Rect(row.x, row.y, Mathf.Max(0f, Column(row, 0).x - row.x - ShaderButtonGap), row.height);
                    var secondary = hairControls?.IsSecondary(material) == true;
                    using (new EditorGUI.DisabledScope(secondary))
                    {
                        EditorGUI.ObjectField(field, GUIContent.none, material, typeof(Material), false);
                        var tip = secondary ? T($"髪マテリアル結合後は {hairControls.Representative(material).name} の設定を使用します。",
                            $"After hair merge, uses settings from {hairControls.Representative(material).name}.") : material.name;
                        GUI.Label(field, new GUIContent(string.Empty, tip));
                        DrawMatrixToggle(Column(row, 0), shader, direct, shaderLabel, EditorStyles.miniButtonLeft);
                        DrawMatrixToggle(Column(row, 1), direct, shader, directLabel, EditorStyles.miniButtonRight);
                        DrawMatrixToggle(Column(row, 2), fold, null, foldLabel, EditorStyles.miniButton);
                    }
                }
            }
            finally { EditorGUI.indentLevel = indent; }
        }

        private static void DrawMatrixToggle(Rect cell, SerializedProperty selected, SerializedProperty otherSelected,
            GUIContent label, GUIStyle style)
        {
            EditorGUI.BeginChangeCheck();
            var value = GUI.Toggle(cell, selected.boolValue, label, style);
            if (EditorGUI.EndChangeCheck()) ApplyExclusiveSelection(selected, otherSelected, value);
        }

        internal static void ApplyExclusiveSelection(SerializedProperty selected,
            SerializedProperty otherSelected, bool value)
        {
            selected.boolValue = value;
            if (value && otherSelected != null) otherSelected.boolValue = false;
        }

        private static Dictionary<Material, SerializedProperty> IndexSelections(SerializedProperty entries)
        {
            var result = new Dictionary<Material, SerializedProperty>();
            for (var i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var material = entry.FindPropertyRelative(nameof(OutlineMaterialSelection.material)).objectReferenceValue as Material;
                if (material != null) result.TryAdd(material, entry.FindPropertyRelative(nameof(OutlineMaterialSelection.selected)));
            }
            return result;
        }

        private void CacheCategoryErrors(List<Material> selected, bool bake, bool hasConverter)
        {
            var reasons = new Dictionary<string, List<string>>();
            foreach (var material in selected)
            {
                string reason;
                if (bake)
                {
                    if (OutlineMaterialUtility.TryGetBakeMaterialInfo(material, out _, out reason,
                            requireEnabledOutline: false)) continue;
                    reason = OutlineMaterialUtility.LocalizeReason(reason, language == Language.English);
                }
                else if (MToonDetector.IsMToonLike(material))
                {
                    if (hasConverter) continue;
                    reason = T("YM MToon to lilToonを追加してください。", "Add YM MToon to lilToon to convert the selected materials.");
                }
                else
                {
                    // A prior NDMF tool may still add an outline. The final pass performs
                    // the authoritative outline check and warns/skips if it remains off.
                    if (OutlineMaterialUtility.TryValidate(material, out reason, requireEnabledOutline: false)) continue;
                    reason = OutlineMaterialUtility.LocalizeReason(reason, language == Language.English);
                }
                if (!reasons.TryGetValue(reason, out var names)) reasons[reason] = names = new List<string>();
                names.Add(material.name);
            }
            // Match Hair Look Kit: actionable category errors, not an Info/Warning box
            // after every row. Conversion scheduled by another YM tool is not an error.
            foreach (var reason in reasons)
                errors.Add(reason.Key + "\n" + string.Join(", ", reason.Value));
        }

        private void CacheBoundaryErrors(IEnumerable<Material> selected)
        {
            var reasons = new Dictionary<string, List<string>>();
            foreach (var material in selected)
            {
                if (OutlineMaterialUtility.TryGetBoundaryMaterialInfo(material, out _, out var reason)) continue;
                reason = OutlineMaterialUtility.LocalizeReason(reason, language == Language.English);
                if (!reasons.TryGetValue(reason, out var names)) reasons[reason] = names = new List<string>();
                names.Add(material.name);
            }
            foreach (var reason in reasons)
                errors.Add(reason.Key + "\n" + string.Join(", ", reason.Value));
        }

        private void DrawAdvanced(YMOutlineExtenderComponent component)
        {
            var show = serializedObject.FindProperty(nameof(component.showAdvanced));
            show.boolValue = EditorGUILayout.Foldout(show.boolValue, "Advanced", true);
            if (!show.boolValue) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(component.verboseLog)), new GUIContent("Verbose Log"));
                EditorGUILayout.Space(4f);
                var resetRect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight));
                if (GUI.Button(resetRect, "Reset Preview"))
                {
                    PreviewRecoveryUtility.ResetAllPreviewArtifacts(); GUIUtility.ExitGUI();
                }
                EditorGUILayout.HelpBox(T(
                    "モデルが重複したり、見えない場合に押してください。\nPreview オブジェクトを削除し、Renderer を再表示します。",
                    "Use this if the avatar stays hidden, frozen, or stuck after Preview.\nThis removes temporary Preview objects and re-enables renderers."), MessageType.Warning);
            }
        }

        private static string BuildPreviewStateKey(YMOutlineExtenderComponent component)
        {
            if (component == null) return string.Empty;
            return component.enabled + "|" + component.enableOutlineExtender + "|" + component.outlineWidthMultiplier
                + "|F|" + component.enableBoundaryFold
                + ":" + component.boundaryFoldAngleDegrees
                + ":" + component.boundaryFoldLengthMultiplier
                + ":" + component.boundarySupportBandWidthMultiplier
                + ":" + component.taperBoundaryEndpoints
                + ":" + component.boundaryFillDistanceMultiplier
                + "|S|" + string.Join(";", component.materialSelections
                .Where(s => s != null)
                .Select(s => $"{(s.material != null ? s.material.GetInstanceID() : 0)}:{s.selected}"))
                + "|B|" + string.Join(";", component.bakeMaterialSelections.Where(s => s != null)
                    .Select(s => $"{(s.material != null ? s.material.GetInstanceID() : 0)}:{s.selected}"))
                + "|E|" + string.Join(";", component.boundaryMaterialSelections.Where(s => s != null)
                    .Select(s => $"{(s.material != null ? s.material.GetInstanceID() : 0)}:{s.selected}"));
        }
    }
}
