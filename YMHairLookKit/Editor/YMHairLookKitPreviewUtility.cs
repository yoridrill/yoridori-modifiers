using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.HairLookKit
{
    [InitializeOnLoad]
    internal static class YMHairLookKitPreviewUtility
    {
        private const string PreviewRootName = "__YoridoriHairLookKitPreviewRoot";
        private const string PreviewAvatarName = "__YoridoriHairLookKitPreviewAvatar";
        private const string ToolName = "YM Hair Look Kit";

        private static GameObject _sourceAvatarRoot;
        private static GameObject _previewRoot;
        private static GameObject _previewAvatar;
        private static GameObject _pendingAvatarRoot;
        private static string _previewProgress = string.Empty;
        private static bool _isProcessingPreview;
        private static bool _previewFailed;
        private static int _progressVersion;
        private static readonly List<RendererState> HiddenRenderers = new();

        private struct RendererState
        {
            public Renderer renderer;
            public bool wasEnabled;
        }

        static YMHairLookKitPreviewUtility()
        {
            SceneIconUtility.HideComponentIcon<YMHairLookKitComponent>();
            PreviewRecoveryUtility.RegisterResetHandler("ym-hair-look-kit", ResetOwnPreviewArtifacts);
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += StopPreview;
            CleanupOrphanPreviewObjects();
        }

        internal static void TogglePreview(YMHairLookKitComponent component)
        {
            if (_isProcessingPreview) return;
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            if (avatarRoot == null) return;

            if (IsPreviewing(avatarRoot))
            {
                StopPreview();
                return;
            }

            QueueStartPreview(avatarRoot);
        }

        internal static void RestartPreviewIfActive(YMHairLookKitComponent component)
        {
            if (_isProcessingPreview) return;
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            if (avatarRoot == null || !IsPreviewing(avatarRoot)) return;
            QueueStartPreview(avatarRoot);
        }

        internal static bool IsPreviewing(YMHairLookKitComponent component)
        {
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            return avatarRoot != null && IsPreviewing(avatarRoot);
        }

        internal static string GetPreviewProgressMessage() => _previewProgress;
        internal static bool IsProcessingPreview() => _isProcessingPreview;
        internal static bool HasPreviewFailed() => _previewFailed;

        private static bool IsPreviewing(GameObject avatarRoot)
        {
            return _sourceAvatarRoot != null && _sourceAvatarRoot == avatarRoot && _previewAvatar != null;
        }

        private static void QueueStartPreview(GameObject avatarRoot)
        {
            StopPreview();
            if (!PreviewCoordinator.TryBegin("ym-hair-look-kit", ToolName, avatarRoot, false, out var failure))
            {
                LogUtility.PreviewSkipped(ToolName, failure);
                _previewFailed = true;
                SetProgress(failure);
                return;
            }

            _pendingAvatarRoot = avatarRoot;
            _isProcessingPreview = true;
            _previewFailed = false;
            SetProgress("Preparing preview...");
            EditorApplication.delayCall += StartPendingPreview;
        }

        private static void StartPendingPreview()
        {
            var avatarRoot = _pendingAvatarRoot;
            _pendingAvatarRoot = null;
            if (avatarRoot == null)
            {
                _isProcessingPreview = false;
                SetProgress(string.Empty);
                return;
            }

            try
            {
                _sourceAvatarRoot = avatarRoot;
                _previewRoot = new GameObject(PreviewRootName)
                {
                    hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor,
                };
                _previewAvatar = Object.Instantiate(avatarRoot, _previewRoot.transform);
                _previewAvatar.name = PreviewAvatarName;
                _previewAvatar.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;

                var previewComponents = _previewAvatar.GetComponentsInChildren<YMHairLookKitComponent>(true);
                var selectedComponent = SelectPreferredComponent(previewComponents, _previewAvatar);
                if (selectedComponent != null)
                {
                    YMHairLookKitProcessor.ApplyOnBuild(
                        selectedComponent,
                        onProgress: SetProgress,
                        route: YMHairLookKitProcessor.ProcessRoute.Preview,
                        runMToonPreviewStage: true);
                    selectedComponent.isPreviewing = true;
                }

                HideSourceRenderers(avatarRoot);
                SyncSourcePreviewFlag(avatarRoot, true);
                _previewFailed = false;
            }
            catch
            {
                _previewFailed = true;
                PreviewCoordinator.End("ym-hair-look-kit");
                throw;
            }
            finally
            {
                SetProgress("Finalizing preview...");
                QueueFinishProcessing();
                SceneView.RepaintAll();
            }
        }

        internal static void StopPreview()
        {
            RestoreSourceRenderers();

            if (_previewRoot != null)
            {
                Object.DestroyImmediate(_previewRoot);
            }

            if (_sourceAvatarRoot != null)
            {
                SyncSourcePreviewFlag(_sourceAvatarRoot, false);
            }

            _previewRoot = null;
            _previewAvatar = null;
            _sourceAvatarRoot = null;
            _pendingAvatarRoot = null;
            _isProcessingPreview = false;
            _previewFailed = false;
            SetProgress(string.Empty);
            PreviewCoordinator.End("ym-hair-look-kit");
            CleanupOrphanPreviewObjects();
            SceneView.RepaintAll();
        }

        private static void ResetOwnPreviewArtifacts(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                StopPreview();
                CleanupOrphanPreviewObjects();
                foreach (var component in Object.FindObjectsByType<YMHairLookKitComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var root = component != null ? PreviewCoordinator.FindAvatarRoot(component.gameObject) : null;
                    if (root == null) continue;
                    EnableRenderers(root);
                    SyncSourcePreviewFlag(root, false);
                }
                SceneView.RepaintAll();
                return;
            }

            if (IsPreviewing(avatarRoot)) StopPreview();
            CleanupOrphanPreviewObjects();
            EnableRenderers(avatarRoot);
            SyncSourcePreviewFlag(avatarRoot, false);
            SceneView.RepaintAll();
        }

        private static void HideSourceRenderers(GameObject avatarRoot)
        {
            RestoreSourceRenderers();
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                HiddenRenderers.Add(new RendererState { renderer = renderer, wasEnabled = renderer.enabled });
                renderer.enabled = false;
            }
        }

        private static void RestoreSourceRenderers()
        {
            foreach (var state in HiddenRenderers.Where(state => state.renderer != null))
            {
                state.renderer.enabled = state.wasEnabled;
            }
            HiddenRenderers.Clear();
        }

        private static void EnableRenderers(GameObject avatarRoot)
        {
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                renderer.enabled = true;
                EditorUtility.SetDirty(renderer);
            }
        }

        private static void SyncSourcePreviewFlag(GameObject avatarRoot, bool previewing)
        {
            foreach (var component in avatarRoot.GetComponentsInChildren<YMHairLookKitComponent>(true))
            {
                component.isPreviewing = previewing;
                EditorUtility.SetDirty(component);
            }
        }

        private static void CleanupOrphanPreviewObjects()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go == null) continue;
                if (go.name != PreviewRootName && go.name != PreviewAvatarName) continue;
                Object.DestroyImmediate(go);
            }
        }

        private static YMHairLookKitComponent SelectPreferredComponent(YMHairLookKitComponent[] components, GameObject avatarRoot)
        {
            if (components == null || components.Length == 0) return null;
            return components
                .Where(c => c != null)
                .OrderBy(c => PreviewCoordinator.GetDepthFromRoot(c.transform, avatarRoot != null ? avatarRoot.transform : null))
                .FirstOrDefault();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode)
            {
                StopPreview();
            }
        }

        private static void SetProgress(string message)
        {
            _previewProgress = message ?? string.Empty;
            _progressVersion++;
            InternalEditorUtility.RepaintAllViews();
        }

        private static void QueueFinishProcessing()
        {
            var version = ++_progressVersion;
            EditorApplication.delayCall += () =>
            {
                if (version != _progressVersion) return;
                _isProcessingPreview = false;
                _previewProgress = string.Empty;
                InternalEditorUtility.RepaintAllViews();
            };
        }
    }
}
