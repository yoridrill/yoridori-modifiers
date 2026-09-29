using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.OutlineExtender
{
    [CustomEditor(typeof(YMOutlineExtenderComponent))]
    public sealed class YMOutlineExtenderComponentEditor : Editor
    {
        private enum Language { Japanese, English }
        private const string PrefKeyLanguage = "YMOutlineExtenderComponentEditor.Language";
        private const float SectionTopSpacing = 10f;
        private const float AdvancedTopSpacing = 6f;
        private const float ToggleWidth = 16f;
        private Language language;
        private List<Material> materials;
        private string T(string ja, string en) => language == Language.Japanese ? ja : en;
        private GUIContent TT(string ja, string jaTip, string en, string enTip) =>
            language == Language.Japanese ? new GUIContent(ja, jaTip) : new GUIContent(en, enTip);

        private void OnEnable()
        {
            language = (Language)EditorPrefs.GetInt(PrefKeyLanguage, 0);
            Scan();
        }

        private void Scan()
        {
            var component = (YMOutlineExtenderComponent)target;
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            materials = OutlineMaterialUtility.CollectMaterials(root);
            if (materials.All(m => component.materialSelections.Any(s => s != null && s.material == m))) return;
            Undo.RecordObject(component, "Scan Outline Extender Materials");
            OutlineMaterialUtility.Synchronize(component.materialSelections, materials);
            EditorUtility.SetDirty(component);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var component = (YMOutlineExtenderComponent)target;
            materials ??= OutlineMaterialUtility.CollectMaterials(
                PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject);
            var previewStateBefore = BuildPreviewStateKey(component);
            DrawTopBar(component);
            EditorGUILayout.Space(SectionTopSpacing);
            DrawSectionTitle(T("lilToon カスタムシェーダー", "lilToon Custom Shader"));
            EditorGUILayout.Space(2f);
            var enabled = serializedObject.FindProperty(nameof(component.enableOutlineExtender));
            enabled.boolValue = EditorGUILayout.ToggleLeft(TT(
                "カットアウト境界へ輪郭線を延長",
                "カットアウトかつ輪郭線が設定されているとき、 アルファ境界の透明側に lilToon輪郭線の色・太さに合わせた線を追加します。 MToon10・旧MToonも YM MToon to lilToon などで変換される場合は設定可能です。",
                "Extend Outlines to Cutout Boundaries",
                "When a material uses Cutout with outlines enabled, adds a line on the transparent side of its alpha boundary using the lilToon outline color and width. MToon10 and legacy MToon materials can also be configured when they will be converted by YM MToon to lilToon or another tool."), enabled.boolValue);
            if (enabled.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    var selected = component.materialSelections.Where(s => s != null && s.selected && materials.Contains(s.material))
                        .Select(s => s.material).Distinct().ToList();
                    EditorGUILayout.HelpBox(T(
                        $"今の設定で処理されるマテリアル数: {selected.Count}個\n出力シェーダー: lilToon Outline Extender",
                        $"Materials processed with current settings: {selected.Count}\nOutput shader: lilToon Outline Extender"), MessageType.Info);
                    var show = serializedObject.FindProperty(nameof(component.showMaterials));
                    show.boolValue = EditorGUILayout.Foldout(show.boolValue, T("マテリアル一覧", "Materials"), true);
                    if (show.boolValue)
                    {
                        var entries = serializedObject.FindProperty(nameof(component.materialSelections));
                        var shown = new HashSet<Material>();
                        for (var i = 0; i < entries.arraySize; i++)
                        {
                            var entry = entries.GetArrayElementAtIndex(i);
                            var material = entry.FindPropertyRelative(nameof(OutlineMaterialSelection.material));
                            if (!(material.objectReferenceValue is Material m) || !materials.Contains(m) || !shown.Add(m)) continue;
                            DrawMaterialRow(material, entry.FindPropertyRelative(nameof(OutlineMaterialSelection.selected)));
                        }
                    }
                    EditorGUILayout.Space(2f);
                    EditorGUILayout.Slider(
                        serializedObject.FindProperty(nameof(component.outlineWidthMultiplier)),
                        0f,
                        2f,
                        TT(
                            "太さ比率",
                            "最終lilToon Materialの輪郭線の太さを基準にして、 0～2倍で太さを調整します。 1で同じ太さです。",
                            "Width Ratio",
                            "Adjusts the width from 0 to 2 times the final lilToon material's outline width. A value of 1 uses the same width."));
                    EditorGUILayout.Space(2f);
                    DrawCategoryErrors(component, selected);
                }
            }
            EditorGUILayout.Space(AdvancedTopSpacing);
            DrawAdvanced(component);
            var changed = serializedObject.ApplyModifiedProperties();
            if (changed) EditorUtility.SetDirty(component);
            if (previewStateBefore != BuildPreviewStateKey(component)
                && YMOutlineExtenderPreviewUtility.IsPreviewing(component))
                YMOutlineExtenderPreviewUtility.RestartPreviewIfActive(component);
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
            if (EditorGUI.EndChangeCheck()) { language = next; EditorPrefs.SetInt(PrefKeyLanguage, (int)language); }
        }

        private static void DrawMaterialRow(SerializedProperty material, SerializedProperty selected)
        {
            var row = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false));
            var toggle = new Rect(row.x, row.y, ToggleWidth, row.height);
            var field = new Rect(toggle.xMax + 4f, row.y, Mathf.Max(0, row.xMax - toggle.xMax - 4f), row.height);
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            try
            {
                selected.boolValue = EditorGUI.Toggle(toggle, selected.boolValue);
                EditorGUI.ObjectField(field, material, typeof(Material), GUIContent.none);
            }
            finally { EditorGUI.indentLevel = indent; }
        }

        private static void DrawSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var line = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(line, new Color(0.3f, 0.3f, 0.3f, 0.9f));
        }

        private void DrawCategoryErrors(YMOutlineExtenderComponent component, List<Material> selected)
        {
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            var hasConverter = root.GetComponentsInChildren<MToonToLilToonComponent>(true).Any(c => c != null);
            var reasons = new Dictionary<string, List<string>>();
            foreach (var material in selected)
            {
                string reason;
                if (MToonDetector.IsMToonLike(material))
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
                EditorGUILayout.HelpBox(reason.Key + "\n" + string.Join(", ", reason.Value), MessageType.Error);
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
            return component.enableOutlineExtender + "|" + component.outlineWidthMultiplier + "|" + string.Join(";", component.materialSelections
                .Where(s => s != null)
                .Select(s => $"{(s.material != null ? s.material.GetInstanceID() : 0)}:{s.selected}"));
        }
    }
}
