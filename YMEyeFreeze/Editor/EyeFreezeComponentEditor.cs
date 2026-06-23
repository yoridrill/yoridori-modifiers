using System.Linq;
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
                    "目ボーンを初期状態で固定するモードを Exメニューに追加します。 コンストレイントが2つ増えます。",
                    "Adds an Ex Menu mode that returns eye bones to their initial rotation. Two more constraints will be added."),
                MessageType.Info);

            DrawPlacementStatus(component);
            EditorGUILayout.Space(4);

            EditorGUILayout.PropertyField(
                menuNameProp,
                TT(
                    "メニュー表示名",
                    "VRChatのExメニューに追加されるトグルの表示名です。",
                    "Menu Name",
                    "Name of the toggle added to the VRChat Ex Menu."));
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
                        "アバター配下に追加してください。 VRCAvatarDescriptorが見つかりません。",
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
                        ? T("複数箇所で設定されています。 ビルド時はこのコンポーネントの設定値が使用されます。",
                            "This component is configured in multiple places. The values on this component will be used for the build.")
                        : T("複数箇所で設定されています。 ビルド時、このコンポーネントでの設定は無視されます。",
                            "This component is configured in multiple places. The values on this component will be ignored for the build."),
                    MessageType.Warning);
                return;
            }

            DrawExpressionParameterCapacityWarning(parentDescriptor, component);
        }

        private void DrawExpressionParameterCapacityWarning(VRCAvatarDescriptor descriptor, YMEyeFreeze component)
        {
            if (descriptor == null || component == null) return;

            var parameterName = string.IsNullOrWhiteSpace(component.parameterName)
                ? "YM/EyeFreeze"
                : component.parameterName.Trim();
            if (string.IsNullOrWhiteSpace(parameterName)) return;

            var parameters = descriptor.expressionParameters;
            var list = parameters != null && parameters.parameters != null
                ? parameters.parameters
                : System.Array.Empty<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>();

            var existing = list.FirstOrDefault(parameter => parameter != null && parameter.name == parameterName);
            if (existing != null)
            {
                EditorGUILayout.HelpBox(
                    T(
                        $"Expression Parameters に `{parameterName}` が既に存在します。別の設定と名前が衝突している可能性があるため、YM Eye Freeze のパラメータ名を変更してください。",
                        $"`{parameterName}` already exists in Expression Parameters. It may conflict with another setup, so change the YM Eye Freeze parameter name."),
                    MessageType.Warning);
                return;
            }

            var usedCost = list.Sum(parameter => parameter == null
                ? 0
                : VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.TypeCost(parameter.valueType));
            var requiredCost = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.TypeCost(
                VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool);
            var maxCost = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.MAX_PARAMETER_COST;

            if (usedCost + requiredCost <= maxCost) return;

            EditorGUILayout.HelpBox(
                T(
                    $"Expression Parameters の容量が不足しています。`{parameterName}` を追加できないため、ビルド時に YM Eye Freeze のExメニュー項目は追加されません。現在 {usedCost}/{maxCost}、必要 {requiredCost} です。",
                    $"Expression Parameters are full. `{parameterName}` cannot be added, so the YM Eye Freeze Ex Menu control will not be added at build time. Current {usedCost}/{maxCost}, required {requiredCost}."),
                MessageType.Warning);
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
                EditorGUILayout.PropertyField(
                    parameterNameProp,
                    TT(
                        "パラメータ名",
                        "Expression Parametersに追加するBool名です。 既存のメニューやAnimatorと連携する場合だけ変更します。",
                        "Parameter Name",
                        "Bool name added to VRChat Expression Parameters. Change this only when linking with existing menus or animators."));
                EditorGUILayout.PropertyField(
                    savedProp,
                    TT(
                        "保存",
                        "ON/OFF状態をVRChatに保存し、 アバター再読み込み後も維持します。",
                        "Saved",
                        "Saves the ON/OFF state in VRChat so it persists after reloading the avatar."));
                EditorGUILayout.PropertyField(
                    syncedProp,
                    TT(
                        "同期",
                        "ON/OFF状態を他ユーザーにも同期します。 ローカルで使う場合はOFFにします。",
                        "Synced",
                        "Syncs the ON/OFF state to other users. Disable this for local-only use."));
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
