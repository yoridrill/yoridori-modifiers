using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.FacialMapper
{
    [CustomEditor(typeof(YMFacialMapper))]
    public sealed class FacialMapperComponentEditor : UnityEditor.Editor
    {
        private enum Language
        {
            Japanese,
            English
        }

        private const string PrefKeyLanguage = "FacialMapperComponentEditor.Language";
        private const string PrefKeySettingsFoldout = "FacialMapperComponentEditor.SettingsFoldout";
        private const string PrefKeyAdvancedFoldout = "FacialMapperComponentEditor.AdvancedFoldout";

        private Language _language;
        private bool _settingsFoldout;
        private bool _advancedFoldout;
        private int _presetIndex;
        private List<FacialMapperPresetLoader.Preset> _presets;

        private SerializedProperty _neutralProp;
        private SerializedProperty _handSignsProp;
        private SerializedProperty _presetMemoProp;
        private SerializedProperty _conflictPriorityProp;
        private SerializedProperty _writeDefaultsProp;
        private SerializedProperty _verboseLogProp;

        private void OnEnable()
        {
            _neutralProp = serializedObject.FindProperty("neutral");
            _handSignsProp = serializedObject.FindProperty("handSigns");
            _presetMemoProp = serializedObject.FindProperty("presetMemo");
            _conflictPriorityProp = serializedObject.FindProperty("conflictPriority");
            _writeDefaultsProp = serializedObject.FindProperty("writeDefaults");
            _verboseLogProp = serializedObject.FindProperty("verboseLog");

            _language = (Language)EditorPrefs.GetInt(PrefKeyLanguage, 0);
            _settingsFoldout = EditorPrefs.GetBool(PrefKeySettingsFoldout, false);
            _advancedFoldout = EditorPrefs.GetBool(PrefKeyAdvancedFoldout, false);
            SceneIconUtility.HideComponentIcon<YMFacialMapper>();
            ReloadPresets();
        }

        public override void OnInspectorGUI()
        {
            var component = (YMFacialMapper)target;
            EnsureHandSignSettings(component);

            serializedObject.Update();

            DrawTopRow(component);
            EditorGUILayout.Space(4);
            DrawPlacementStatus(component);
            DrawPresetRow(component);
            DrawPresetMemo();
            DrawSettings();

            DrawConflictPriority();
            EditorGUILayout.Space(6f);
            DrawAdvanced();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTopRow(YMFacialMapper component)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                var nextLanguage = (Language)EditorGUILayout.EnumPopup(_language, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck())
                {
                    _language = nextLanguage;
                    EditorPrefs.SetInt(PrefKeyLanguage, (int)_language);
                }
            }
        }

        private void DrawPlacementStatus(YMFacialMapper component)
        {
            var descriptor = component.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox(
                    T("アバター配下に追加してください。 VRCAvatarDescriptorが見つかりません。",
                        "Add this component under an avatar. VRCAvatarDescriptor was not found."),
                    MessageType.Warning);
                return;
            }

            var components = descriptor.GetComponentsInChildren<YMFacialMapper>(true);
            if (components.Length <= 1) return;

            var selected = SelectPreferredComponent(components, descriptor.gameObject);
            EditorGUILayout.HelpBox(
                selected == component
                    ? T("複数箇所で設定されています。 ビルド時はこのコンポーネントの設定値が使用されます。",
                        "This component is configured in multiple places. The values on this component will be used for the build.")
                    : T("複数箇所で設定されています。 ビルド時、このコンポーネントでの設定は無視されます。",
                        "This component is configured in multiple places. The values on this component will be ignored for the build."),
                MessageType.Warning);
        }

        private void DrawPresetRow(YMFacialMapper component)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var labels = _presets != null && _presets.Count > 0
                    ? _presets.Select(p => p.name).ToArray()
                    : new[] { "Empty" };
                _presetIndex = Mathf.Clamp(_presetIndex, 0, labels.Length - 1);
                EditorGUI.BeginChangeCheck();
                var nextPresetIndex = EditorGUILayout.Popup(_presetIndex, labels);
                if (EditorGUI.EndChangeCheck())
                {
                    _presetIndex = nextPresetIndex;
                    ApplyPreset(component, _presets[_presetIndex]);
                }

                if (GUILayout.Button("↻", GUILayout.Width(28f)))
                {
                    ReloadPresets();
                }

                if (GUILayout.Button("Export", GUILayout.Width(64f)))
                {
                    PresetNamePopup.Show(T("プリセット名", "Preset Name"), name => ExportPreset(component, name));
                }
            }
        }

        private void DrawPresetMemo()
        {
            var memoHeight = Mathf.Max(1f, EditorStyles.textArea.lineHeight) * 10f + EditorStyles.textArea.padding.vertical;
            _presetMemoProp.stringValue = EditorGUILayout.TextArea(
                _presetMemoProp.stringValue ?? string.Empty,
                GUILayout.Height(memoHeight));
        }

        private void DrawSettings()
        {
            EditorGUI.BeginChangeCheck();
            _settingsFoldout = EditorGUILayout.Foldout(_settingsFoldout, T("表情設定", "Expression Settings"), true);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(PrefKeySettingsFoldout, _settingsFoldout);
            }

            if (!_settingsFoldout) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField(T("Neutral", "Neutral"), EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawSlot("Shape Key", _neutralProp, true);
                }

                for (var i = 0; i < _handSignsProp.arraySize; i++)
                {
                    var settingProp = _handSignsProp.GetArrayElementAtIndex(i);
                    var signProp = settingProp.FindPropertyRelative("sign");
                    var sign = (YMFacialMapper.HandSign)signProp.enumValueIndex;
                    EditorGUILayout.Space(3f);
                    EditorGUILayout.LabelField(sign.ToString(), EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        DrawSlot("Shape Key L", settingProp.FindPropertyRelative("left"), true);
                        DrawSlot("Shape Key R", settingProp.FindPropertyRelative("right"), true);
                    }
                }

                EditorGUILayout.Space(6f);
            }
        }

        private void DrawSlot(string label, SerializedProperty slotProp, bool compact)
        {
            var eyelidLeftProp = slotProp.FindPropertyRelative("stopEyelidLeft");
            var eyelidRightProp = slotProp.FindPropertyRelative("stopEyelidRight");
            var visemeProp = slotProp.FindPropertyRelative("stopViseme");
            var shapeKeysProp = slotProp.FindPropertyRelative("shapeKeys");

            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var labelRect = new Rect(rect.x, rect.y, Mathf.Min(110f, rect.width * 0.45f), rect.height);
            var eyelidLeftRect = new Rect(labelRect.xMax + 4f, rect.y, 68f, rect.height);
            var eyelidRightRect = new Rect(eyelidLeftRect.xMax, rect.y, 68f, rect.height);
            var visemeRect = new Rect(eyelidRightRect.xMax, rect.y, 70f, rect.height);

            EditorGUI.LabelField(labelRect, label);
            eyelidLeftProp.boolValue = GUI.Toggle(eyelidLeftRect, eyelidLeftProp.boolValue, "Eyelid-L", EditorStyles.miniButtonLeft);
            eyelidRightProp.boolValue = GUI.Toggle(eyelidRightRect, eyelidRightProp.boolValue, "Eyelid-R", EditorStyles.miniButtonMid);
            visemeProp.boolValue = GUI.Toggle(visemeRect, visemeProp.boolValue, "Viseme", EditorStyles.miniButtonRight);

            DrawShapeKeyList(shapeKeysProp, compact);
        }

        private void DrawShapeKeyList(SerializedProperty shapeKeysProp, bool compact)
        {
            using (new EditorGUI.IndentLevelScope(compact ? 0 : 1))
            {
                for (var i = 0; i < shapeKeysProp.arraySize; i++)
                {
                    var itemProp = shapeKeysProp.GetArrayElementAtIndex(i);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        itemProp.stringValue = EditorGUILayout.TextField(itemProp.stringValue);
                        if (GUILayout.Button("-", EditorStyles.miniButton, GUILayout.Width(22f)))
                        {
                            shapeKeysProp.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        var index = shapeKeysProp.arraySize;
                        shapeKeysProp.InsertArrayElementAtIndex(index);
                        shapeKeysProp.GetArrayElementAtIndex(index).stringValue = string.Empty;
                    }
                }
            }
        }

        private void DrawConflictPriority()
        {
            var labels = new[]
            {
                T("右ハンドサインを優先", "Prefer Right Hand Sign"),
                T("左ハンドサインを優先", "Prefer Left Hand Sign")
            };
            _conflictPriorityProp.enumValueIndex = EditorGUILayout.Popup(
                T("排他衝突時の判定", "Conflict Resolution"),
                _conflictPriorityProp.enumValueIndex,
                labels);
        }

        private void DrawAdvanced()
        {
            EditorGUI.BeginChangeCheck();
            _advancedFoldout = EditorGUILayout.Foldout(_advancedFoldout, "Advanced", true);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(PrefKeyAdvancedFoldout, _advancedFoldout);
            }

            if (!_advancedFoldout) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(
                    _writeDefaultsProp,
                    new GUIContent(
                        "Write Defaults",
                        T(
                            "アバター内で統一している設定に合わせてください。 OFFでは各表情Clipにリセット値を含め、 ONでは有効なShape Keyのみを書き込みます。",
                            "Match the convention used by the avatar's FX Animator. OFF includes reset values in every expression clip; ON writes only active shape keys.")));
                EditorGUILayout.PropertyField(_verboseLogProp, new GUIContent("Verbose Log"));
            }
        }

        private void ReloadPresets()
        {
            var verbose = target is YMFacialMapper component && component.verboseLog;
            _presets = FacialMapperPresetLoader.LoadPresets(verbose);
            _presetIndex = Mathf.Clamp(_presetIndex, 0, _presets.Count - 1);
        }

        private void ApplyPreset(YMFacialMapper component, FacialMapperPresetLoader.Preset preset)
        {
            if (component == null || preset == null) return;
            Undo.RecordObject(component, "Apply YM Facial Mapper Preset");

            component.presetMemo = preset.memo ?? string.Empty;
            CopySlot(preset.neutral, component.neutral);
            EnsureHandSignSettings(component);

            if (preset.handSigns != null)
            {
                foreach (var signPreset in preset.handSigns)
                {
                    if (signPreset == null) continue;
                    if (!Enum.TryParse(signPreset.sign, true, out YMFacialMapper.HandSign sign)) continue;
                    var setting = component.handSigns.FirstOrDefault(s => s != null && s.sign == sign);
                    if (setting == null) continue;
                    CopySlot(signPreset.left, setting.left);
                    CopySlot(signPreset.right, setting.right);
                }
            }

            EditorUtility.SetDirty(component);
            serializedObject.Update();
        }

        private void ExportPreset(YMFacialMapper component, string presetName)
        {
            if (component == null || string.IsNullOrWhiteSpace(presetName)) return;

            var path = FacialMapperPresetLoader.UserPresetPath;
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) return;

            var fullPath = Path.Combine(projectRoot, path);
            FacialMapperPresetLoader.PresetFile file = null;

            if (File.Exists(fullPath))
            {
                try
                {
                    file = JsonUtility.FromJson<FacialMapperPresetLoader.PresetFile>(File.ReadAllText(fullPath, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog(
                        "YM Facial Mapper",
                        T($"既存のJSONを読み込めませんでした。\n{path}\n{ex.Message}",
                            $"Could not read the existing JSON.\n{path}\n{ex.Message}"),
                        "OK");
                    return;
                }

                if (file == null)
                {
                    EditorUtility.DisplayDialog(
                        "YM Facial Mapper",
                        T($"既存のJSONを読み込めませんでした。\n{path}",
                            $"Could not read the existing JSON.\n{path}"),
                        "OK");
                    return;
                }
            }

            file ??= new FacialMapperPresetLoader.PresetFile { version = 1, presets = Array.Empty<FacialMapperPresetLoader.Preset>() };

            var presets = file.presets != null
                ? file.presets.Where(p => p != null).ToList()
                : new List<FacialMapperPresetLoader.Preset>();
            var preset = BuildPreset(component, presetName.Trim());
            presets.Add(preset);
            file.version = Mathf.Max(1, file.version);
            file.presets = presets.ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, JsonUtility.ToJson(file, true), Encoding.UTF8);
            AssetDatabase.ImportAsset(path);
            ReloadPresets();

            var index = _presets.FindIndex(p => p != null && p.name == preset.name);
            if (index >= 0) _presetIndex = index;
        }

        private static FacialMapperPresetLoader.Preset BuildPreset(YMFacialMapper component, string presetName)
        {
            EnsureHandSignSettings(component);
            return new FacialMapperPresetLoader.Preset
            {
                name = presetName,
                memo = component.presetMemo ?? string.Empty,
                neutral = BuildSlot(component.neutral),
                handSigns = component.handSigns
                    .Where(s => s != null)
                    .Select(s => new FacialMapperPresetLoader.HandSignPreset
                    {
                        sign = s.sign.ToString(),
                        left = BuildSlot(s.left),
                        right = BuildSlot(s.right)
                    })
                    .ToArray()
            };
        }

        private static FacialMapperPresetLoader.Slot BuildSlot(YMFacialMapper.ExpressionSlot source)
        {
            return new FacialMapperPresetLoader.Slot
            {
                eyelidLeft = source != null && source.stopEyelidLeft,
                eyelidRight = source != null && source.stopEyelidRight,
                viseme = source != null && source.stopViseme,
                shapeKeys = source?.shapeKeys != null
                    ? source.shapeKeys.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray()
                    : Array.Empty<string>()
            };
        }

        private static void CopySlot(FacialMapperPresetLoader.Slot source, YMFacialMapper.ExpressionSlot destination)
        {
            if (destination == null) return;
            destination.stopEyelidLeft = source != null && source.eyelidLeft;
            destination.stopEyelidRight = source != null && source.eyelidRight;
            destination.stopViseme = source != null && source.viseme;
            destination.shapeKeys = source?.shapeKeys != null
                ? source.shapeKeys.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList()
                : new List<string>();
        }

        private static void EnsureHandSignSettings(YMFacialMapper component)
        {
            YMFacialMapperDefaults.EnsureHandSigns(component);
        }

        private static YMFacialMapper SelectPreferredComponent(YMFacialMapper[] components, GameObject avatarRoot)
        {
            var rootTransform = avatarRoot != null ? avatarRoot.transform : null;
            return components
                .Where(c => c != null)
                .OrderBy(c => PreviewCoordinator.GetDepthFromRoot(c.transform, rootTransform))
                .FirstOrDefault();
        }

        private string T(string ja, string en) => _language == Language.Japanese ? ja : en;

        private sealed class PresetNamePopup : EditorWindow
        {
            private Action<string> _onSubmit;
            private string _presetName = "New Preset";
            private string _title;

            public static void Show(string title, Action<string> onSubmit)
            {
                var window = CreateInstance<PresetNamePopup>();
                window._title = title;
                window._onSubmit = onSubmit;
                window.titleContent = new GUIContent(title);
                window.minSize = new Vector2(260f, 76f);
                window.maxSize = new Vector2(420f, 76f);
                window.ShowUtility();
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField(_title ?? "Preset Name");
                GUI.SetNextControlName("PresetName");
                _presetName = EditorGUILayout.TextField(_presetName);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                    {
                        Close();
                    }

                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_presetName)))
                    {
                        if (GUILayout.Button("OK", GUILayout.Width(80f)))
                        {
                            _onSubmit?.Invoke(_presetName.Trim());
                            Close();
                        }
                    }
                }

                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && !string.IsNullOrWhiteSpace(_presetName))
                {
                    _onSubmit?.Invoke(_presetName.Trim());
                    Close();
                    Event.current.Use();
                }

                EditorGUI.FocusTextInControl("PresetName");
            }
        }
    }
}
