using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.EyeFreeze
{
    [CustomEditor(typeof(YMEyeFreeze))]
    public sealed class EyeFreezeComponentEditor : UnityEditor.Editor
    {
        private enum Language
        {
            Japanese,
            English
        }

        private const string PrefKeyLanguage = "EyeFreezeComponentEditor.Language";
        private const string PrefKeyAdvancedFoldout = "EyeFreezeComponentEditor.AdvancedFoldout";

        private SerializedProperty menuNameProp;
        private SerializedProperty parameterNameProp;
        private SerializedProperty savedProp;
        private SerializedProperty syncedProp;

        private Language language;
        private bool advancedFoldout;

        private void OnEnable()
        {
            menuNameProp = serializedObject.FindProperty("menuName");
            parameterNameProp = serializedObject.FindProperty("parameterName");
            savedProp = serializedObject.FindProperty("saved");
            syncedProp = serializedObject.FindProperty("synced");
            language = (Language)EditorPrefs.GetInt(PrefKeyLanguage, 0);
            advancedFoldout = EditorPrefs.GetBool(PrefKeyAdvancedFoldout, false);

            SceneIconUtility.HideComponentIcon<YMEyeFreeze>();
        }

        public override void OnInspectorGUI()
        {
            var component = (YMEyeFreeze)target;

            serializedObject.Update();

            DrawTopRow();
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                T(
                    "目ボーンを初期状態で固定するモードを Exメニューに追加します。",
                    "Adds an Ex Menu mode that returns eye bones to their initial rotation."),
                MessageType.Info);

            DrawPlacementStatus(component);
            EditorGUILayout.Space(4);

            EditorGUILayout.PropertyField(menuNameProp, new GUIContent(T("メニュー表示名", "Menu Name")));
            EditorGUILayout.Space(4);
            DrawAdvancedSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTopRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
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

        private void DrawPlacementStatus(YMEyeFreeze component)
        {
            if (component == null) return;

            var parentDescriptor = component.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (parentDescriptor == null)
            {
                EditorGUILayout.HelpBox(
                    T(
                        "アバター配下に追加してください。VRCAvatarDescriptor が見つかりません。",
                        "Add this component under an avatar. VRCAvatarDescriptor was not found."),
                    MessageType.Warning);
                return;
            }

            var avatarRoot = parentDescriptor.gameObject;
            var components = avatarRoot.GetComponentsInChildren<YMEyeFreeze>(true);
            if (components != null && components.Length > 1)
            {
                var selected = SelectPreferredComponentForBuild(components, avatarRoot);
                var thisWillBeUsed = selected == component;
                EditorGUILayout.HelpBox(
                    thisWillBeUsed
                        ? T("複数箇所で設定されています。ビルド時はこのコンポーネントの設定値が使用されます。",
                            "This component is configured in multiple places. The values on this component will be used for the build.")
                        : T("複数箇所で設定されています。ビルド時、このコンポーネントでの設定は無視されます。",
                            "This component is configured in multiple places. The values on this component will be ignored for the build."),
                    MessageType.Warning);
                return;
            }

        }

        private static YMEyeFreeze SelectPreferredComponentForBuild(YMEyeFreeze[] components, GameObject avatarRoot)
        {
            if (components == null || components.Length == 0) return null;
            var rootTransform = avatarRoot != null ? avatarRoot.transform : null;
            YMEyeFreeze best = null;
            var bestDepth = int.MaxValue;

            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null) continue;
                var depth = PreviewCoordinator.GetDepthFromRoot(component.transform, rootTransform);
                if (best == null || depth < bestDepth)
                {
                    best = component;
                    bestDepth = depth;
                }
            }

            return best;
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
                EditorGUILayout.PropertyField(parameterNameProp, new GUIContent(T("パラメータ名", "Parameter Name")));
                EditorGUILayout.PropertyField(savedProp, new GUIContent(T("Saved", "Saved")));
                EditorGUILayout.PropertyField(syncedProp, new GUIContent(T("Synced", "Synced")));
            }
        }

        private string T(string ja, string en)
        {
            return language == Language.Japanese ? ja : en;
        }
    }
}
