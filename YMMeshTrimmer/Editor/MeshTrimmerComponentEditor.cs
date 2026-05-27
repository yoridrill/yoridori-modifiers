using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using nadena.dev.ndmf;
using YoridoriModifiers.Core.Editor;

[assembly: ExportsPlugin(typeof(YoridoriModifiers.MeshTrimmer.MeshTrimmerNdmfPlugin))]

namespace YoridoriModifiers.MeshTrimmer
{
[InitializeOnLoad]
[CustomEditor(typeof(MeshTrimmerComponent))]
public class MeshTrimmerComponentEditor : Editor
{
    private const string PreviewRootName = "__YoridoriMeshTrimmerPreviewRoot";
    private const string PreviewAvatarName = "__YoridoriMeshTrimmerPreviewAvatar";
    private const string LanguagePrefKey = "MeshTrimmerComponentEditor.Language";
    private const string ToolName = "YM Mesh Trimmer";

    private enum UiLanguage { English = 0, Japanese = 1 }
    private enum PreviewUpdateType { None, MeshOnly, TextureOnly, MeshAndTexture }

    private class RendererPreviewState
    {
        public SkinnedMeshRenderer renderer;
        public Mesh originalSharedMesh;
        public Material[] originalSharedMaterials;
        public bool originalEnabled;
        public bool originalForceRenderingOff;
        public GameObject previewObject;
        public SkinnedMeshRenderer previewRenderer;
        public Mesh previewMesh;
        public Material[] previewMaterials;
    }

    private class TexturePreviewState
    {
        public Texture2D originalTexture;
        public Texture2D previewTexture;
    }

    private class PreviewState
    {
        public readonly Dictionary<SkinnedMeshRenderer, RendererPreviewState> rendererStates = new Dictionary<SkinnedMeshRenderer, RendererPreviewState>();
        public readonly Dictionary<Texture2D, TexturePreviewState> textureStates = new Dictionary<Texture2D, TexturePreviewState>();
        public bool active;
        public PreviewUpdateType pending;
        public bool processing;
        public bool queued;
        public bool failed;
        public bool flushPendingImmediately;
        public string failureMessage;
        public GameObject sourceAvatarRoot;
        public GameObject previewRoot;
        public GameObject previewAvatar;
        public MeshTrimmerComponent previewComponent;
        public readonly PreviewRendererVisibilityScope hiddenSourceRenderers = new PreviewRendererVisibilityScope();
    }

    private static readonly Dictionary<int, PreviewState> PreviewByInstanceId = new Dictionary<int, PreviewState>();

    private UiLanguage _language;
    private string _lastFocusedControl;
    private bool _advancedFoldout;
    private bool _targetsFoldout = false;
    private int _lastHotControl;

    static MeshTrimmerComponentEditor()
    {
        SceneIconUtility.HideComponentIcon<MeshTrimmerComponent>();

        SubscribeEditorEvents();
        ScheduleOrphanPreviewCleanup();
    }

    private void OnEnable()
    {
        _language = (UiLanguage)EditorPrefs.GetInt(LanguagePrefKey, (int)UiLanguage.English);
        SubscribeEditorEvents();
    }

    private void OnDisable() { }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var trimmer = (MeshTrimmerComponent)target;
        var state = GetPreviewState(trimmer);

        DrawTopBar(trimmer, state);
        EditorGUILayout.Space(6f);

        EditorGUI.BeginChangeCheck();
        DrawBuildTargetEnables(trimmer);
        if (EditorGUI.EndChangeCheck())
        {
            QueuePreviewUpdate(state, PreviewUpdateType.MeshAndTexture);
        }

        EditorGUILayout.Space(6f);
        EditorGUI.BeginChangeCheck();
        DrawSetting("alphaThreshold");
        if (EditorGUI.EndChangeCheck())
        {
            QueuePreviewUpdate(state, PreviewUpdateType.MeshOnly);
        }

        EditorGUI.BeginChangeCheck();
        DrawSetting("maskDilatePixels");
        DrawSetting("maskCleanupPixels");
        DrawSetting("minimumFragmentSizePermille");
        if (EditorGUI.EndChangeCheck())
        {
            QueuePreviewUpdate(state, PreviewUpdateType.MeshOnly);
        }

        EnsureAutoDetectedTargets(trimmer, false);
        EditorGUILayout.Space(6f);
        DrawTargets(serializedObject.FindProperty("targets"), state);

        DrawAdvancedSection(trimmer);
        serializedObject.ApplyModifiedProperties();

        TryFlushPreviewUpdate(trimmer, state);
    }

    private void DrawBuildTargetEnables(MeshTrimmerComponent trimmer)
    {
        var windowsProp = serializedObject.FindProperty("enableForWindows");
        var androidProp = serializedObject.FindProperty("enableForAndroid");
        var iosProp = serializedObject.FindProperty("enableForiOS");

        Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        const float labelWidth = 128f;
        const float gap = 6f;
        var labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
        var buttonsRect = new Rect(labelRect.xMax + gap, rect.y, rect.width - labelWidth - gap, rect.height);
        float buttonWidth = buttonsRect.width / 3f;

        EditorGUI.LabelField(labelRect, T("有効ビルドターゲット", "Build Targets"));
        windowsProp.boolValue = GUI.Toggle(
            new Rect(buttonsRect.x, buttonsRect.y, buttonWidth, buttonsRect.height),
            windowsProp.boolValue,
            "Windows",
            EditorStyles.miniButtonLeft);
        androidProp.boolValue = GUI.Toggle(
            new Rect(buttonsRect.x + buttonWidth, buttonsRect.y, buttonWidth, buttonsRect.height),
            androidProp.boolValue,
            "Android",
            EditorStyles.miniButtonMid);
        iosProp.boolValue = GUI.Toggle(
            new Rect(buttonsRect.x + buttonWidth * 2f, buttonsRect.y, buttonWidth, buttonsRect.height),
            iosProp.boolValue,
            "iOS",
            EditorStyles.miniButtonRight);

        bool enableForWindows = windowsProp.boolValue;
        bool enableForAndroid = androidProp.boolValue;
        bool enableForiOS = iosProp.boolValue;

        if (!enableForWindows && !enableForAndroid && !enableForiOS)
        {
            EditorGUILayout.HelpBox(T("すべてのビルドターゲットで無効です。", "All build targets are disabled."), MessageType.Warning);
        }

        if (TryGetOverlappingTargetStatus(trimmer, enableForWindows, enableForAndroid, enableForiOS, out var thisWillBeUsed))
        {
            var message = thisWillBeUsed
                ? T("同一アバター内で同じビルドターゲット向けに複数のTrimmerが有効です。 ビルド時はこのコンポーネントの設定値が使用されます。",
                    "Multiple Trimmers are enabled for the same build target in this avatar. This component will be used for the build.")
                : T("同一アバター内で同じビルドターゲット向けに複数のTrimmerが有効です。 ビルド時、このコンポーネントでの設定は無視されます。",
                    "Multiple Trimmers are enabled for the same build target in this avatar. This component will be ignored for the build.");
            EditorGUILayout.HelpBox(
                message,
                MessageType.Warning);
        }
    }

    private static bool TryGetOverlappingTargetStatus(
        MeshTrimmerComponent trimmer,
        bool enableForWindows,
        bool enableForAndroid,
        bool enableForiOS,
        out bool thisWillBeUsed)
    {
        thisWillBeUsed = false;
        if (trimmer == null || trimmer.transform == null) return false;
        var root = PreviewCoordinator.FindAvatarRoot(trimmer.gameObject);
        if (root == null) return false;
        var trimmers = root.GetComponentsInChildren<MeshTrimmerComponent>(true);
        bool hasOverlap = false;
        foreach (var other in trimmers)
        {
            if (other == null) continue;
            if ((enableForWindows && other.enableForWindows) ||
                (enableForAndroid && other.enableForAndroid) ||
                (enableForiOS && other.enableForiOS))
            {
                if (other != trimmer)
                {
                    hasOverlap = true;
                }
            }
        }
        var firstOverlapping = SelectPreferredTrimmerForBuildTarget(trimmers, root, enableForWindows, enableForAndroid, enableForiOS);
        thisWillBeUsed = firstOverlapping == trimmer;
        return hasOverlap;
    }

    private static MeshTrimmerComponent SelectPreferredTrimmerForBuildTarget(
        MeshTrimmerComponent[] trimmers,
        GameObject avatarRoot,
        bool enableForWindows,
        bool enableForAndroid,
        bool enableForiOS)
    {
        if (trimmers == null || trimmers.Length == 0) return null;

        MeshTrimmerComponent best = null;
        var bestScore = int.MinValue;
        var rootTransform = avatarRoot != null ? avatarRoot.transform : null;
        for (var i = 0; i < trimmers.Length; i++)
        {
            var trimmer = trimmers[i];
            if (trimmer == null) continue;
            if (!((enableForWindows && trimmer.enableForWindows) ||
                (enableForAndroid && trimmer.enableForAndroid) ||
                (enableForiOS && trimmer.enableForiOS)))
            {
                continue;
            }

            var depth = PreviewCoordinator.GetDepthFromRoot(trimmer.transform, rootTransform);
            var score = -depth * 10000 - i;
            if (score <= bestScore) continue;
            best = trimmer;
            bestScore = score;
        }

        return best;
    }

    private void DrawAdvancedSection(MeshTrimmerComponent trimmer)
    {
        EditorGUILayout.Space();
        _advancedFoldout = EditorGUILayout.Foldout(_advancedFoldout, "Advanced", true);
        if (!_advancedFoldout)
        {
            return;
        }

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("debugEdgeCrossingRoutes"),
            new GUIContent(T("Verbose Log", "Verbose Log")));
        EditorGUILayout.Space(4f);

        var rawButtonRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        var buttonRect = EditorGUI.IndentedRect(rawButtonRect);
        if (GUI.Button(buttonRect, T("Reset Preview", "Reset Preview")))
        {
            ClearPreview(trimmer);
            CleanupOrphanPreviewObjects();
        }

        EditorGUILayout.HelpBox(
            T(
                "モデルが重複したり、見えない場合に押してください。\nPreview オブジェクトを削除し、Renderer を再表示します。",
                "Use this if the avatar stays hidden, frozen, or stuck after Preview.\nThis removes temporary Preview objects and re-enables renderers."),
            MessageType.Warning);
        EditorGUI.indentLevel--;
    }

    private void DrawTopBar(MeshTrimmerComponent trimmer, PreviewState state)
    {
        EditorGUILayout.BeginHorizontal();
        if (PreviewInspectorGui.DrawPreviewButton(state.active, "Preview"))
        {
            if (state.active)
            {
                ClearPreview(trimmer);
                state.failed = false;
            }
            else
            {
                state.failed = !RequestBuildPreview(trimmer, state, PreviewUpdateType.MeshAndTexture);
            }
        }
        PreviewInspectorGui.DrawStatus(state.processing, state.failed, state.active ? $"Polygons: {GetPreviewPolygonCount(state)}" : null, 150f);

        GUILayout.FlexibleSpace();
        EditorGUI.BeginChangeCheck();
        _language = (UiLanguage)EditorGUILayout.EnumPopup(_language, GUILayout.Width(90f));
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetInt(LanguagePrefKey, (int)_language);
        EditorGUILayout.EndHorizontal();
    }

    private void QueuePreviewUpdate(PreviewState state, PreviewUpdateType type, bool immediate = false)
    {
        if (!state.active) return;
        if (type == PreviewUpdateType.None) return;
        if (state.pending == PreviewUpdateType.None) state.pending = type;
        else if (state.pending != type) state.pending = PreviewUpdateType.MeshAndTexture;
        state.flushPendingImmediately |= immediate;
    }

    private static int GetPreviewPolygonCount(PreviewState state)
    {
        if (state == null || state.rendererStates == null) return 0;
        int triangles = 0;
        foreach (var kv in state.rendererStates)
        {
            var rs = kv.Value;
            var mesh = rs?.previewRenderer != null ? rs.previewRenderer.sharedMesh : rs?.previewMesh;
            if (mesh == null) continue;
            triangles += mesh.triangles != null ? mesh.triangles.Length / 3 : 0;
        }
        return triangles;
    }

    private void TryFlushPreviewUpdate(MeshTrimmerComponent trimmer, PreviewState state)
    {
        if (!state.active || state.pending == PreviewUpdateType.None) return;
        bool commit = state.flushPendingImmediately || IsPreviewCommitEvent(Event.current);
        if (!commit) return;

        if (!RequestBuildPreview(trimmer, state, state.pending))
        {
            state.failed = true;
            return;
        }
        state.failed = false;
        state.pending = PreviewUpdateType.None;
        state.flushPendingImmediately = false;
    }

    private bool IsPreviewCommitEvent(Event e)
    {
        bool focusLostCommit = false;
        string currentFocus = GUI.GetNameOfFocusedControl();
        if (!string.IsNullOrEmpty(_lastFocusedControl) && string.IsNullOrEmpty(currentFocus))
        {
            focusLostCommit = true;
        }
        _lastFocusedControl = currentFocus;

        bool hotControlReleasedCommit = _lastHotControl != 0 && GUIUtility.hotControl == 0;
        _lastHotControl = GUIUtility.hotControl;

        bool enterCommit = (e.type == EventType.KeyDown || e.type == EventType.KeyUp)
                           && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n' || e.character == '\r');
        bool commandEnterCommit = (e.type == EventType.ExecuteCommand || e.type == EventType.ValidateCommand)
                                  && (e.commandName == "Newline" || e.commandName == "SoftReturn");

        return e.type == EventType.MouseUp || enterCommit || commandEnterCommit || focusLostCommit || hotControlReleasedCommit;
    }

    private void DrawSetting(string name)
    {
        var prop = serializedObject.FindProperty(name);
        var content = GetSettingContent(name);
        var rect = EditorGUILayout.GetControlRect(true, EditorGUI.GetPropertyHeight(prop, GUIContent.none));
        var labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
        var fieldRect = new Rect(labelRect.xMax, rect.y, rect.xMax - labelRect.xMax, rect.height);

        EditorGUI.LabelField(labelRect, content);
        EditorGUI.PropertyField(fieldRect, prop, GUIContent.none);

        if (Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition))
        {
            GUI.tooltip = labelRect.Contains(Event.current.mousePosition) ? content.tooltip : string.Empty;
        }
    }
    private string T(string ja, string en) => _language == UiLanguage.Japanese ? ja : en;

    private GUIContent GetSettingContent(string name)
    {
        switch (name)
        {
            case "alphaThreshold":
                return new GUIContent(
                    T("アルファしきい値", "Alpha Threshold"),
                    T("この値以上のアルファを残す領域として扱います。テクスチャの塗り足し色には影響しません。",
                      "Pixels with alpha at or above this value are treated as kept. This does not affect texture fill colors."));
            case "maskDilatePixels":
                return new GUIContent(
                    T("マスク拡張 (px)", "Mask Expansion (px)"),
                    T("残す領域をピクセル単位で外側に広げます。境界が削れすぎる場合に大きくします。",
                      "Expands the kept mask outward in pixels. Increase this when edges are trimmed too aggressively."));
            case "maskCleanupPixels":
                return new GUIContent(
                    T("マスククリーンアップ (px)", "Mask Cleanup (px)"),
                    T("小さな孤立領域の削除、隙間埋め、小さい穴埋めをまとめて調整します。",
                      "Controls small island removal, gap closing, and small hole filling together."));
            case "minimumFragmentSizePermille":
                return new GUIContent(
                    T("微小ポリゴン除去 (‰)", "Minimum Fragment Size (‰)"),
                    T("トリミング後に残る極端に小さい破片を削除します。大きくすると細い部分も削れやすくなります。",
                      "Removes tiny fragments after trimming. Higher values can also remove narrow details."));
            case "minIntersectionT":
                return new GUIContent(T("最小交点t", "Min Intersection t"));
            case "maxIntersectionT":
                return new GUIContent(T("最大交点t", "Max Intersection t"));
            case "minTriangleUvArea":
                return new GUIContent(T("最小UV三角形面積", "Min Triangle UV Area"));
            case "minTriangleWorldArea":
                return new GUIContent(T("最小3D三角形面積", "Min Triangle World Area"));
            default:
                return new GUIContent(name);
        }
    }

    private void DrawTargets(SerializedProperty targetsProp, PreviewState state)
    {
        _targetsFoldout = EditorGUILayout.Foldout(_targetsFoldout, T("トリミング対象", "Trimming Targets"), true);
        if (!_targetsFoldout) return;

        const float previewSize = 64f;
        const float compactLabelWidth = 90f;

        for (int i = 0; i < targetsProp.arraySize; i++)
        {
            var targetProp = targetsProp.GetArrayElementAtIndex(i);
            var texProp = targetProp.FindPropertyRelative("mainTexture");
            var modeProp = targetProp.FindPropertyRelative("texturePostProcessMode");
            var fillColorProp = targetProp.FindPropertyRelative("fillColor");
            var enablePreSubdivideProp = targetProp.FindPropertyRelative("enablePreSubdivide");
            var preSubdivideLevelProp = targetProp.FindPropertyRelative("preSubdivideLevel");
            var usagesProp = targetProp.FindPropertyRelative("usages");

            Texture2D tex = texProp.objectReferenceValue as Texture2D;
            string materialNames = BuildMaterialNamesText(usagesProp);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            var previewRect = GUILayoutUtility.GetRect(previewSize, previewSize, GUILayout.Width(previewSize), GUILayout.Height(previewSize));
            if (tex != null)
            {
                bool hovered = previewRect.Contains(Event.current.mousePosition);
                EditorGUI.DrawPreviewTexture(previewRect, tex, null, ScaleMode.ScaleToFit);
                EditorGUIUtility.AddCursorRect(previewRect, MouseCursor.Link);
                if (hovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    PopupWindow.Show(previewRect, new UvPickerPopup(tex));
                    Event.current.Use();
                }
            }
            else EditorGUI.HelpBox(previewRect, "No Tex", MessageType.None);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(new GUIContent(materialNames, materialNames), EditorStyles.boldLabel);
            EditorGUILayout.Space(1f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(T("事前細分化", "Pre Subdivide"), GUILayout.Width(compactLabelWidth));
            EditorGUI.BeginChangeCheck();
            var enablePreSubdivide = EditorGUILayout.Toggle(enablePreSubdivideProp.boolValue, GUILayout.Width(18f));
            if (EditorGUI.EndChangeCheck())
            {
                enablePreSubdivideProp.boolValue = enablePreSubdivide;
                if (enablePreSubdivide && preSubdivideLevelProp.intValue <= 0)
                {
                    preSubdivideLevelProp.intValue = 1;
                }
                QueuePreviewUpdate(state, PreviewUpdateType.MeshOnly, immediate: true);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            var mode = (MeshTrimmerComponent.TexturePostProcessMode)modeProp.enumValueIndex;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(T("塗り足し", "Fill Mode"), GUILayout.Width(compactLabelWidth));
            var controlsRect = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            if (mode == MeshTrimmerComponent.TexturePostProcessMode.FillColor)
            {
                float half = (controlsRect.width - 4f) * 0.5f;
                var leftRect = new Rect(controlsRect.x, controlsRect.y, half, controlsRect.height);
                var rightRect = new Rect(controlsRect.x + half + 4f, controlsRect.y, half, controlsRect.height);
                mode = (MeshTrimmerComponent.TexturePostProcessMode)EditorGUI.EnumPopup(leftRect, mode);
                EditorGUI.PropertyField(rightRect, fillColorProp, GUIContent.none);
            }
            else
            {
                mode = (MeshTrimmerComponent.TexturePostProcessMode)EditorGUI.EnumPopup(controlsRect, mode);
            }
            EditorGUILayout.EndHorizontal();

            modeProp.enumValueIndex = (int)mode;
            if (EditorGUI.EndChangeCheck()) QueuePreviewUpdate(state, PreviewUpdateType.TextureOnly, immediate: true);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
    }

    private static string BuildMaterialNamesText(SerializedProperty usagesProp)
    {
        if (usagesProp == null || usagesProp.arraySize == 0) return "-";
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < usagesProp.arraySize; i++)
        {
            var usageProp = usagesProp.GetArrayElementAtIndex(i);
            var materialProp = usageProp.FindPropertyRelative("material");
            var mat = materialProp.objectReferenceValue as Material;
            if (mat == null) continue;
            if (!seen.Add(mat.name)) continue;
            names.Add(mat.name);
        }

        return names.Count > 0 ? string.Join(", ", names) : "-";
    }

    private static PreviewState GetPreviewState(MeshTrimmerComponent trimmer)
    {
        int id = trimmer.GetInstanceID();
        if (!PreviewByInstanceId.TryGetValue(id, out var state))
        {
            state = new PreviewState();
            PreviewByInstanceId[id] = state;
        }
        return state;
    }

    private static bool RequestBuildPreview(MeshTrimmerComponent trimmer, PreviewState state, PreviewUpdateType type)
    {
        if (trimmer == null || state.queued || state.processing) return false;
        var avatarRoot = PreviewCoordinator.FindAvatarRoot(trimmer.gameObject);
        if (!PreviewCoordinator.TryBegin(GetPreviewOwnerKey(trimmer), ToolName, avatarRoot, false, out var failure))
        {
            LogUtility.PreviewSkipped(ToolName, failure);
            state.failureMessage = failure;
            return false;
        }
        EnsureAutoDetectedTargets(trimmer, false);
        state.failed = false;
        state.failureMessage = string.Empty;
        state.sourceAvatarRoot = avatarRoot;
        state.queued = true;
        state.processing = true;
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        EditorApplication.delayCall += () =>
        {
            state.queued = false;
            if (trimmer == null) return;

            BuildPreview(trimmer, state, type);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        };

        return true;
    }

    private static string GetPreviewOwnerKey(MeshTrimmerComponent trimmer)
    {
        return trimmer == null ? "ym-mesh-trimmer:null" : $"ym-mesh-trimmer:{trimmer.GetInstanceID()}";
    }

    private static void BuildPreview(MeshTrimmerComponent trimmer, PreviewState state, PreviewUpdateType type)
    {
        if (trimmer == null) return;
        if (state.sourceAvatarRoot == null)
        {
            state.sourceAvatarRoot = PreviewCoordinator.FindAvatarRoot(trimmer.gameObject);
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {

            if (!state.active)
            {
                CreatePreviewAvatar(trimmer, state);
                SyncPreviewComponent(trimmer, state);
                if (state.previewComponent == null) throw new InvalidOperationException("Preview component is missing.");
                CaptureOriginals(state.previewComponent, state);
                state.active = true;
            }
            else
            {
                SyncPreviewComponent(trimmer, state);
                if (state.previewComponent == null) throw new InvalidOperationException("Preview component is missing.");
            }

            int meshCount = 0;
            int texCount = 0;

            if (type == PreviewUpdateType.MeshOnly || type == PreviewUpdateType.MeshAndTexture)
            {
                meshCount = RebuildPreviewMeshes(state.previewComponent, state);
            }

            if (type == PreviewUpdateType.TextureOnly || type == PreviewUpdateType.MeshAndTexture)
            {
                RebuildPreviewTexturesAndMaterials(state.previewComponent, state, ref texCount);
            }

            sw.Stop();
            LogUtility.Verbose(
                ToolName,
                trimmer.debugEdgeCrossingRoutes,
                "Preview",
                $"UpdateType={type}, Renderers={state.rendererStates.Count}, PreviewMeshes={meshCount}, PreviewTextures={texCount}, ElapsedMs={sw.ElapsedMilliseconds}");

            state.failed = false;
        }
        catch (Exception ex)
        {
            LogUtility.Error(ToolName, "Preview", $"Failed and restoring originals. {ex}");
            ClearPreview(trimmer);
            state.active = false;
            state.failed = true;
        }
        finally
        {
            state.processing = false;
        }
    }

    private static void CreatePreviewAvatar(MeshTrimmerComponent trimmer, PreviewState state)
    {
        if (state.sourceAvatarRoot == null) throw new InvalidOperationException("Preview avatar root is missing.");

        state.previewRoot = new GameObject(PreviewRootName)
        {
            hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor
        };
        state.previewAvatar = UnityEngine.Object.Instantiate(state.sourceAvatarRoot, state.previewRoot.transform);
        state.previewAvatar.name = PreviewAvatarName;
        state.previewAvatar.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
        state.previewAvatar.transform.SetPositionAndRotation(state.sourceAvatarRoot.transform.position, state.sourceAvatarRoot.transform.rotation);
        state.previewAvatar.transform.localScale = state.sourceAvatarRoot.transform.localScale;

        var path = PreviewCoordinator.BuildRelativePath(state.sourceAvatarRoot.transform, trimmer.transform);
        var previewTransform = string.IsNullOrEmpty(path) ? state.previewAvatar.transform : state.previewAvatar.transform.Find(path);
        state.previewComponent = previewTransform != null
            ? previewTransform.GetComponent<MeshTrimmerComponent>()
            : state.previewAvatar.GetComponentInChildren<MeshTrimmerComponent>(true);
        if (state.previewComponent == null)
        {
            state.previewComponent = state.previewAvatar.AddComponent<MeshTrimmerComponent>();
        }

        state.hiddenSourceRenderers.Hide(state.sourceAvatarRoot);
    }

    private static void SyncPreviewComponent(MeshTrimmerComponent source, PreviewState state)
    {
        var preview = state.previewComponent;
        if (source == null || preview == null || state.sourceAvatarRoot == null || state.previewAvatar == null) return;

        preview.enableForWindows = source.enableForWindows;
        preview.enableForAndroid = source.enableForAndroid;
        preview.enableForiOS = source.enableForiOS;
        preview.enableTexturePadding = source.enableTexturePadding;
        preview.alphaThreshold = source.alphaThreshold;
        preview.maskDilatePixels = source.maskDilatePixels;
        preview.maskCleanupPixels = source.maskCleanupPixels;
        preview.minimumFragmentSizePermille = source.minimumFragmentSizePermille;
        preview.maskClosePixels = source.maskClosePixels;
        preview.fillSmallHolesPixels = source.fillSmallHolesPixels;
        preview.removeSmallIslandsPixels = source.removeSmallIslandsPixels;
        preview.minTriangleUvArea = source.minTriangleUvArea;
        preview.minTriangleWorldArea = source.minTriangleWorldArea;
        preview.edgeCrossingMergeEpsilon = source.edgeCrossingMergeEpsilon;
        preview.edgeCrossingEndpointSnapEpsilon = source.edgeCrossingEndpointSnapEpsilon;
        preview.edgeCrossingCacheQuantizeStep = source.edgeCrossingCacheQuantizeStep;
        preview.edgeCrossingMinPolygonAreaRatio = source.edgeCrossingMinPolygonAreaRatio;
        preview.edgeCrossingMinChordLengthRatio = source.edgeCrossingMinChordLengthRatio;
        preview.trimAlgorithm = source.trimAlgorithm;
        preview.debugEdgeCrossingRoutes = source.debugEdgeCrossingRoutes;
        preview.targets = CloneTargetsForPreview(source, state);
    }

    private static List<MeshTrimmerComponent.TextureTargetSettings> CloneTargetsForPreview(MeshTrimmerComponent source, PreviewState state)
    {
        var result = new List<MeshTrimmerComponent.TextureTargetSettings>();
        if (source.targets == null) return result;

        foreach (var target in source.targets)
        {
            if (target == null) continue;
            var clone = new MeshTrimmerComponent.TextureTargetSettings
            {
                enabled = target.enabled,
                mainTexture = target.mainTexture,
                enableTextureFill = target.enableTextureFill,
                texturePostProcessMode = target.texturePostProcessMode,
                fillColor = target.fillColor,
                enablePreSubdivide = target.enablePreSubdivide,
                preSubdivideLevel = target.preSubdivideLevel,
                preSubdivideQuadAware = target.preSubdivideQuadAware,
                usages = new List<MeshTrimmerComponent.RendererSubMeshRef>()
            };

            foreach (var usage in target.usages)
            {
                if (usage == null) continue;
                clone.usages.Add(new MeshTrimmerComponent.RendererSubMeshRef
                {
                    renderer = FindPreviewRenderer(usage.renderer, state),
                    subMeshIndex = usage.subMeshIndex,
                    material = usage.material
                });
            }

            result.Add(clone);
        }

        return result;
    }

    private static SkinnedMeshRenderer FindPreviewRenderer(SkinnedMeshRenderer sourceRenderer, PreviewState state)
    {
        if (sourceRenderer == null || state.sourceAvatarRoot == null || state.previewAvatar == null) return null;
        var path = PreviewCoordinator.BuildRelativePath(state.sourceAvatarRoot.transform, sourceRenderer.transform);
        var previewTransform = string.IsNullOrEmpty(path) ? state.previewAvatar.transform : state.previewAvatar.transform.Find(path);
        return previewTransform != null ? previewTransform.GetComponent<SkinnedMeshRenderer>() : null;
    }

    private static int RebuildPreviewMeshes(MeshTrimmerComponent trimmer, PreviewState state)
    {
        int meshCount = 0;
        foreach (var kv in state.rendererStates)
        {
            var r = kv.Value;
            if (r.renderer == null || r.originalSharedMesh == null) continue;
            EnsurePreviewRenderer(r);

            if (r.previewMesh != null && r.previewMesh != r.originalSharedMesh)
            {
                UnityEngine.Object.DestroyImmediate(r.previewMesh);
                r.previewMesh = null;
            }

            // Never operate on the original model mesh instance during preview.
            // Prepare a disposable working copy, then let the trim processor build preview output from it.
            var working = UnityEngine.Object.Instantiate(r.originalSharedMesh);
            working.name = r.originalSharedMesh.name + " (YM Mesh Trimmer Working)";
            working.hideFlags = HideFlags.HideAndDontSave;
            r.previewRenderer.sharedMesh = working;
            r.previewRenderer.sharedMaterials = r.originalSharedMaterials;
            r.renderer.enabled = false;
            r.renderer.forceRenderingOff = true;
        }

        var usageRestore = new List<(MeshTrimmerComponent.RendererSubMeshRef usage, SkinnedMeshRenderer original)>();
        try
        {
            foreach (var target in trimmer.targets)
            {
                foreach (var usage in target.usages)
                {
                    if (usage == null || usage.renderer == null) continue;
                    if (!state.rendererStates.TryGetValue(usage.renderer, out var rp) || rp.previewRenderer == null) continue;
                    usageRestore.Add((usage, usage.renderer));
                    usage.renderer = rp.previewRenderer;
                }
            }

            MeshTrimProcessor.ApplyTrim(trimmer, false);
        }
        finally
        {
            foreach (var pair in usageRestore)
            {
                pair.usage.renderer = pair.original;
            }
        }
        foreach (var kv in state.rendererStates)
        {
            var r = kv.Value;
            if (r.previewRenderer == null) continue;
            r.previewMesh = r.previewRenderer.sharedMesh;
            if (r.previewMesh == null) continue;

            r.previewMesh.name = r.originalSharedMesh.name + " (YM Mesh Trimmer Preview)";
            r.previewMesh.hideFlags = HideFlags.HideAndDontSave;
            r.previewMesh.MarkDynamic();
            ApplyPreviewMaterials(r);
            meshCount++;
        }

        return meshCount;
    }

    private static void ApplyPreviewMaterials(RendererPreviewState state)
    {
        if (state?.previewRenderer == null) return;
        state.previewRenderer.sharedMaterials = state.previewMaterials ?? state.originalSharedMaterials;
    }

    private static void EnsurePreviewRenderer(RendererPreviewState r)
    {
        if (r.previewRenderer != null) return;
        if (r.renderer == null) return;

        var src = r.renderer;
        var go = new GameObject(src.gameObject.name + " (NDMF Preview)");
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetParent(src.transform.parent, false);
        go.transform.localPosition = src.transform.localPosition;
        go.transform.localRotation = src.transform.localRotation;
        go.transform.localScale = src.transform.localScale;

        var dst = go.AddComponent<SkinnedMeshRenderer>();
        dst.rootBone = src.rootBone;
        dst.bones = src.bones;
        dst.localBounds = src.localBounds;
        dst.quality = src.quality;
        dst.updateWhenOffscreen = src.updateWhenOffscreen;
        dst.skinnedMotionVectors = src.skinnedMotionVectors;
        dst.shadowCastingMode = src.shadowCastingMode;
        dst.receiveShadows = src.receiveShadows;
        dst.lightProbeUsage = src.lightProbeUsage;
        dst.reflectionProbeUsage = src.reflectionProbeUsage;
        dst.probeAnchor = src.probeAnchor;
        dst.allowOcclusionWhenDynamic = src.allowOcclusionWhenDynamic;

        r.previewObject = go;
        r.previewRenderer = dst;
    }

    private static void CaptureOriginals(MeshTrimmerComponent trimmer, PreviewState state)
    {
        state.rendererStates.Clear();
        foreach (var target in trimmer.targets)
        {
            foreach (var usage in target.usages)
            {
                if (usage == null || usage.renderer == null) continue;
                if (state.rendererStates.ContainsKey(usage.renderer)) continue;

                var r = new RendererPreviewState
                {
                    renderer = usage.renderer,
                    originalSharedMesh = usage.renderer.sharedMesh,
                    originalSharedMaterials = usage.renderer.sharedMaterials,
                    originalEnabled = usage.renderer.enabled,
                    originalForceRenderingOff = usage.renderer.forceRenderingOff
                };
                state.rendererStates.Add(usage.renderer, r);
            }
        }
    }

    private static void RebuildPreviewTexturesAndMaterials(MeshTrimmerComponent trimmer, PreviewState state, ref int textureFillExecCount)
    {
        bool useToonStandardShaderForPreview = trimmer != null && (trimmer.enableForAndroid || trimmer.enableForiOS);
        Shader toonStandardShader = useToonStandardShaderForPreview ? FindToonStandardShaderForPreview() : null;

        foreach (var texState in state.textureStates.Values)
        {
            if (texState.previewTexture != null) UnityEngine.Object.DestroyImmediate(texState.previewTexture);
        }
        state.textureStates.Clear();

        var processedMap = new Dictionary<Texture2D, Texture2D>();
        foreach (var target in trimmer.targets)
        {
            if (!target.enabled || !target.enableTextureFill || target.mainTexture == null || target.texturePostProcessMode == MeshTrimmerComponent.TexturePostProcessMode.None) continue;

            if (!processedMap.TryGetValue(target.mainTexture, out var processed))
            {
                if (!TexturePostProcessProcessor.TryCreateProcessedTextureForPreview(target.mainTexture, target.texturePostProcessMode, target.fillColor, trimmer, out processed))
                {
                    continue;
                }
                processed.name = target.mainTexture.name + " (YM Mesh Trimmer Preview)";
                processed.hideFlags = HideFlags.HideAndDontSave;
                processedMap[target.mainTexture] = processed;
                state.textureStates[target.mainTexture] = new TexturePreviewState { originalTexture = target.mainTexture, previewTexture = processed };
            }
        }

        foreach (var r in state.rendererStates.Values)
        {
            if (r.previewRenderer == null) continue;
            var mats = (Material[])r.originalSharedMaterials.Clone();
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                if (!MaterialMainTextureResolver.TryGetMainTexture(m, out var mainTex, out var prop)) continue;
                if (!processedMap.TryGetValue(mainTex, out var previewTex)) continue;

                var pm = new Material(m)
                {
                    name = m.name + " (YM Mesh Trimmer Preview)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (toonStandardShader != null)
                {
                    pm.shader = toonStandardShader;
                    CopyKnownCullModeProperties(m, pm);
                }
                pm.SetTexture(prop, previewTex);
                mats[i] = pm;
                textureFillExecCount++;
            }

            if (r.previewMaterials != null)
            {
                foreach (var old in r.previewMaterials)
                {
                    if (old != null) UnityEngine.Object.DestroyImmediate(old);
                }
            }

            r.previewMaterials = mats;
            r.previewRenderer.sharedMaterials = mats;
        }
    }


    private static Shader FindToonStandardShaderForPreview()
    {
        // Prefer VRChat mobile Toon Standard, fallback to Unlit/Texture when unavailable.
        string[] candidates =
        {
            "VRChat/Mobile/Toon Standard",
            "Unlit/Texture"
        };

        foreach (var shaderName in candidates)
        {
            var shader = Shader.Find(shaderName);
            if (shader != null) return shader;
        }

        return null;
    }

    private static void CopyKnownCullModeProperties(Material source, Material destination)
    {
        if (source == null || destination == null || !destination.HasProperty("_Culling")) return;

        string[] sourceCullProps =
        {
            "_M_CullMode", // MToon10
            "_CullMode",   // legacy MToon
            "_Cull"        // lilToon
        };

        foreach (var prop in sourceCullProps)
        {
            if (!source.HasProperty(prop)) continue;
            destination.SetFloat("_Culling", source.GetFloat(prop));
            return;
        }
    }

    private static void ClearPreview(MeshTrimmerComponent trimmer)
    {
        if (trimmer == null) return;
        var state = GetPreviewState(trimmer);
        foreach (var r in state.rendererStates.Values)
        {
            if (r.renderer != null)
            {
                r.renderer.enabled = r.originalEnabled;
                r.renderer.forceRenderingOff = r.originalForceRenderingOff;
            }
            if (r.previewMesh != null) UnityEngine.Object.DestroyImmediate(r.previewMesh);
            if (r.previewMaterials != null)
            {
                foreach (var pm in r.previewMaterials)
                {
                    if (pm != null) UnityEngine.Object.DestroyImmediate(pm);
                }
            }
            if (r.previewObject != null) UnityEngine.Object.DestroyImmediate(r.previewObject);
        }
        if (state.previewRoot != null) UnityEngine.Object.DestroyImmediate(state.previewRoot);
        state.hiddenSourceRenderers.Restore();

        foreach (var t in state.textureStates.Values)
        {
            if (t.previewTexture != null) UnityEngine.Object.DestroyImmediate(t.previewTexture);
        }

        state.rendererStates.Clear();
        state.textureStates.Clear();
        state.active = false;
        state.pending = PreviewUpdateType.None;
        state.flushPendingImmediately = false;
        state.processing = false;
        state.queued = false;
        state.previewRoot = null;
        state.previewAvatar = null;
        state.previewComponent = null;
        state.sourceAvatarRoot = null;
        state.failureMessage = string.Empty;
        PreviewCoordinator.End(GetPreviewOwnerKey(trimmer));
    }

    private static bool _subscribed;
    private static bool _cleanupScheduled;

    private static void SubscribeEditorEvents()
    {
        if (_subscribed) return;
        _subscribed = true;
        EditorSceneManager.sceneSaving += (_, __) => ClearAllPreviews();
        EditorApplication.quitting += ClearAllPreviews;
        AssemblyReloadEvents.beforeAssemblyReload += ClearAllPreviews;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) ClearAllPreviews();
        };
    }

    private static void ScheduleOrphanPreviewCleanup()
    {
        if (_cleanupScheduled) return;
        _cleanupScheduled = true;
        EditorApplication.delayCall += RunScheduledOrphanPreviewCleanup;
    }

    private static void RunScheduledOrphanPreviewCleanup()
    {
        _cleanupScheduled = false;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            ScheduleOrphanPreviewCleanup();
            return;
        }

        CleanupOrphanPreviewObjects();
    }

    internal static void ClearAllPreviews()
    {
        foreach (var obj in UnityEngine.Object.FindObjectsByType<MeshTrimmerComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ClearPreview(obj);
        }
        CleanupOrphanPreviewObjects();
    }

    private static void CleanupOrphanPreviewObjects()
    {
        foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go == null) continue;
            if (go.name == PreviewRootName || go.name == PreviewAvatarName)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }

    private static void AutoDetectTargets(MeshTrimmerComponent trimmer)
    {
        Undo.RecordObject(trimmer, "Auto Detect YM Mesh Trimmer Targets");
        trimmer.targets.Clear();

        Dictionary<Texture2D, MeshTrimmerComponent.TextureTargetSettings> grouped =
            new Dictionary<Texture2D, MeshTrimmerComponent.TextureTargetSettings>();

        var searchRoot = ResolveAutoDetectSearchRoot(trimmer);
        SkinnedMeshRenderer[] renderers = searchRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer.sharedMesh == null) continue;
            Material[] mats = renderer.sharedMaterials;
            int scanCount = Mathf.Min(renderer.sharedMesh.subMeshCount, mats.Length);
            for (int sub = 0; sub < scanCount; sub++)
            {
                Material mat = mats[sub];
                if (mat == null) continue;
                if (!ShouldProcessMaterial(mat)) continue;
                if (!MaterialMainTextureResolver.TryGetMainTexture(mat, out Texture2D tex, out _)) continue;

                if (!grouped.TryGetValue(tex, out var targetSettings))
                {
                    targetSettings = new MeshTrimmerComponent.TextureTargetSettings
                    {
                        enabled = true,
                        mainTexture = tex,
                        enableTextureFill = true,
                        texturePostProcessMode = MeshTrimmerComponent.TexturePostProcessMode.Solidify,
                        usages = new List<MeshTrimmerComponent.RendererSubMeshRef>()
                    };
                    grouped[tex] = targetSettings;
                }

                targetSettings.usages.Add(new MeshTrimmerComponent.RendererSubMeshRef { renderer = renderer, subMeshIndex = sub, material = mat });
            }
        }

        trimmer.targets.AddRange(grouped.Values);
        AutoFillColorResolver.Apply(trimmer, trimmer.targets);
        EditorUtility.SetDirty(trimmer);
    }

    internal static void EnsureAutoDetectedTargets(MeshTrimmerComponent trimmer, bool forceRefresh)
    {
        if (trimmer == null) return;
        if (forceRefresh || trimmer.targets == null || trimmer.targets.Count == 0 || HasStaleTargetReferences(trimmer))
        {
            var undoGroup = Undo.GetCurrentGroup();
            AutoDetectTargets(trimmer);
            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    private static GameObject ResolveAutoDetectSearchRoot(MeshTrimmerComponent trimmer)
    {
        if (trimmer == null) return null;
        if (trimmer.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
        {
            return trimmer.gameObject;
        }

        return PreviewCoordinator.FindAvatarRoot(trimmer.gameObject) ?? trimmer.gameObject;
    }

    private static bool HasStaleTargetReferences(MeshTrimmerComponent trimmer)
    {
        if (trimmer == null || trimmer.targets == null || trimmer.targets.Count == 0) return true;

        var avatarRoot = PreviewCoordinator.FindAvatarRoot(trimmer.gameObject);
        var rootTransform = avatarRoot != null ? avatarRoot.transform : trimmer.transform;
        var hasUsage = false;
        for (var i = 0; i < trimmer.targets.Count; i++)
        {
            var target = trimmer.targets[i];
            if (target == null || target.usages == null || target.usages.Count == 0) continue;
            for (var j = 0; j < target.usages.Count; j++)
            {
                var renderer = target.usages[j]?.renderer;
                if (renderer == null) continue;
                hasUsage = true;
                if (!PreviewCoordinator.IsUnderRoot(renderer.transform, rootTransform))
                {
                    return true;
                }

                var subMeshIndex = target.usages[j].subMeshIndex;
                var materials = renderer.sharedMaterials;
                if (subMeshIndex < 0 || subMeshIndex >= materials.Length)
                {
                    return true;
                }

                if (target.usages[j].material != materials[subMeshIndex])
                {
                    return true;
                }
            }
        }

        return !hasUsage;
    }

    private static bool ShouldProcessMaterial(Material mat)
    {
        if (mat == null) return false;
        if (mat.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent) return true;

        string renderType = mat.GetTag("RenderType", false, string.Empty);
        if (string.Equals(renderType, "Transparent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(renderType, "TransparentCutout", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (mat.HasProperty("_MToonSurface")) return Mathf.RoundToInt(mat.GetFloat("_MToonSurface")) != 0;
        if (mat.HasProperty("_BlendMode")) return Mathf.RoundToInt(mat.GetFloat("_BlendMode")) != 0; // legacy MToon
        if (mat.HasProperty("_TransparentMode")) return Mathf.RoundToInt(mat.GetFloat("_TransparentMode")) != 0; // lilToon
        if (mat.HasProperty("_Surface")) return Mathf.RoundToInt(mat.GetFloat("_Surface")) != 0; // URP/HDRP

        if (mat.IsKeywordEnabled("_ALPHATEST_ON") || mat.IsKeywordEnabled("_ALPHABLEND_ON") || mat.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
        {
            return true;
        }

        return false;
    }
}

public class MeshTrimmerNdmfPlugin : Plugin<MeshTrimmerNdmfPlugin>
{
    public override string QualifiedName => "jp.yoridrill.ym-mesh-trimmer";
    public override string DisplayName => "YM Mesh Trimmer";

    protected override void Configure()
    {
        var sequence = InPhase(BuildPhase.Transforming)
            .AfterPlugin("jp.yoridrill.ym-arm-patch")
            .BeforePlugin("jp.yoridrill.ym-mtoon-to-liltoon")
            .BeforePlugin("com.github.kurotu.vrc-quest-tools");

        sequence.Run("Run YM Mesh Trimmer", context =>
        {
            var avatarRoot = context.AvatarRootObject;
            if (avatarRoot == null) return;
            var trimmers = avatarRoot.GetComponentsInChildren<MeshTrimmerComponent>(true);
            MeshTrimmerComponentEditor.ClearAllPreviews();
            var selected = SelectPreferredTrimmerForCurrentBuildTarget(trimmers, avatarRoot);
            bool executedForCurrentPlatform = false;
            foreach (var trimmer in trimmers)
            {
                if (trimmer == null || !IsEnabledForCurrentBuildTarget(trimmer, avatarRoot)) continue;
                if (trimmer != selected || executedForCurrentPlatform)
                {
                    continue;
                }

                MeshTrimmerComponentEditor.EnsureAutoDetectedTargets(trimmer, false);
                MeshTrimProcessor.ApplyTrim(trimmer, true);
                TexturePostProcessProcessor.ApplyBuildTimeReplacement(trimmer);
                executedForCurrentPlatform = true;
            }

            // Remove all trimmer components from the generated avatar object to avoid AAO/VRChat validation warnings.
            foreach (var trimmer in trimmers)
            {
                if (trimmer == null) continue;
                UnityEngine.Object.DestroyImmediate(trimmer);
            }
        });
    }

    private static MeshTrimmerComponent SelectPreferredTrimmerForCurrentBuildTarget(
        MeshTrimmerComponent[] trimmers,
        GameObject avatarRoot)
    {
        if (trimmers == null || trimmers.Length == 0) return null;

        MeshTrimmerComponent best = null;
        var bestScore = int.MinValue;
        var rootTransform = avatarRoot != null ? avatarRoot.transform : null;
        for (var i = 0; i < trimmers.Length; i++)
        {
            var trimmer = trimmers[i];
            if (trimmer == null || !IsEnabledForCurrentBuildTarget(trimmer, avatarRoot)) continue;
            var depth = PreviewCoordinator.GetDepthFromRoot(trimmer.transform, rootTransform);
            var score = -depth * 10000 - i;
            if (score <= bestScore) continue;
            best = trimmer;
            bestScore = score;
        }

        return best;
    }

    private static bool IsEnabledForCurrentBuildTarget(MeshTrimmerComponent trimmer, GameObject avatarRoot)
    {
        switch (ResolveCurrentBuildTarget(avatarRoot))
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return trimmer.enableForWindows;
            case BuildTarget.Android:
                return trimmer.enableForAndroid;
            case BuildTarget.iOS:
                return trimmer.enableForiOS;
            default:
                return false;
        }
    }

    private static BuildTarget ResolveCurrentBuildTarget(GameObject avatarRoot)
    {
        var vqtBuildTarget = ResolveVrcQuestToolsBuildTarget(avatarRoot);
        if (vqtBuildTarget.HasValue)
        {
            return vqtBuildTarget.Value;
        }

        return EditorUserBuildSettings.activeBuildTarget;
    }

    private static BuildTarget? ResolveVrcQuestToolsBuildTarget(GameObject avatarRoot)
    {
        if (avatarRoot == null) return null;

        foreach (var component in avatarRoot.GetComponents<Component>())
        {
            if (component == null) continue;
            var type = component.GetType();
            if (type.FullName != "KRT.VRCQuestTools.Components.PlatformTargetSettings") continue;

            var buildTargetField = type.GetField("buildTarget");
            if (buildTargetField == null) return null;

            var buildTarget = buildTargetField.GetValue(component);
            if (buildTarget == null) return null;

            switch (buildTarget.ToString())
            {
                case "PC":
                    return BuildTarget.StandaloneWindows64;
                case "Android":
                    return BuildTarget.Android;
                default:
                    return null;
            }
        }

        return null;
    }
}

}
