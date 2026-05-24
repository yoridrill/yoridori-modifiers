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
                    "Eye LookとBlinkを一時停止し、 目ボーンを初期状態で固定するモードを Exメニューに追加します。",
                    "Adds an Ex Menu mode that pauses Eye Look and Blink, and fixes eye bones at their initial state."),
                MessageType.Info);

            DrawAvatarRootWarning(component);
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

        private void DrawAvatarRootWarning(YMEyeFreeze component)
        {
            if (component == null) return;

            var ownDescriptor = component.GetComponent<VRCAvatarDescriptor>();
            if (ownDescriptor != null) return;

            var parentDescriptor = component.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (parentDescriptor == null)
            {
                EditorGUILayout.HelpBox(
                    T(
                        "AvatarRoot に追加してください。VRCAvatarDescriptor が見つかりません。",
                        "Add this component to AvatarRoot. VRCAvatarDescriptor was not found."),
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                T(
                    "AvatarRoot 以外に追加されています。AvatarRoot に追加してください。",
                    "This component is not on AvatarRoot. Add it to AvatarRoot."),
                MessageType.Warning);
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
