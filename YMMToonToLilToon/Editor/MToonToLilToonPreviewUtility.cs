using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.MToonToLilToon
{
    [InitializeOnLoad]
    internal static class MToonToLilToonPreviewUtility
    {
        private const string PreviewRootName = "__YoridoriMToonToLilToonPreviewRoot";
        private const string PreviewAvatarName = "__YoridoriMToonToLilToonPreviewAvatar";
        private const string ToolName = "YM MToon to lilToon";

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

        static MToonToLilToonPreviewUtility()
        {
            SceneIconUtility.HideComponentIcon<MToonToLilToonComponent>();
            PreviewRecoveryUtility.RegisterResetHandler("ym-mtoon-to-liltoon", ResetOwnPreviewArtifacts);

            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += StopPreview;
            CleanupOrphanPreviewObjects();
        }

        internal static void TogglePreview(MToonToLilToonComponent component)
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

        internal static void RestartPreviewIfActive(MToonToLilToonComponent component)
        {
            if (_isProcessingPreview) return;
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            if (avatarRoot == null || !IsPreviewing(avatarRoot)) return;

            QueueStartPreview(avatarRoot);
        }

        internal static void ApplyGlobalOverridesIfActive(MToonToLilToonComponent sourceComponent)
        {
            if (_isProcessingPreview) return;
            if (sourceComponent == null) return;
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(sourceComponent.gameObject);
            if (avatarRoot == null || !IsPreviewing(avatarRoot) || _previewAvatar == null) return;

            var sourcePath = BuildRelativePath(avatarRoot.transform, sourceComponent.transform);
            var previewTransform = string.IsNullOrEmpty(sourcePath)
                ? _previewAvatar.transform
                : _previewAvatar.transform.Find(sourcePath);
            if (previewTransform == null) return;

            var previewComponent = previewTransform.GetComponent<MToonToLilToonComponent>();
            if (previewComponent == null) return;

            MToonToLilToonProcessor.ApplyGlobalOverridesToConvertedMaterials(
                previewComponent,
                sourceComponent.globalOverrides,
                sourceComponent.disableShadowReceiveForFace,
                sourceComponent.disableBacklightStrengthForFace);
            SceneView.RepaintAll();
        }

        internal static string GetPreviewProgressMessage() => _previewProgress;
        internal static bool IsProcessingPreview() => _isProcessingPreview;
        internal static bool HasPreviewFailed() => _previewFailed;


        internal static bool HasStalePreviewState(MToonToLilToonComponent component)
        {
            if (component == null) return false;
            if (IsPreviewing(component)) return false;
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            if (avatarRoot == null) return false;
            return avatarRoot.GetComponentsInChildren<MToonToLilToonComponent>(true).Any(c => c != null && c.isPreviewing);
        }

        internal static void ResetSavedPreviewState(MToonToLilToonComponent component)
        {
            if (component == null) return;
            PreviewRecoveryUtility.ResetAllPreviewArtifacts();
        }

        private static void ResetOwnPreviewArtifacts(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                StopPreview();
                CleanupOrphanPreviewObjects();

                var roots = new HashSet<GameObject>();
                foreach (var component in Object.FindObjectsByType<MToonToLilToonComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var root = component != null ? PreviewCoordinator.FindAvatarRoot(component.gameObject) : null;
                    if (root == null || !roots.Add(root)) continue;
                    EnableRenderers(root);
                    SyncSourcePreviewFlag(root, false);
                }

                SceneView.RepaintAll();
                return;
            }

            if (IsPreviewing(avatarRoot))
            {
                StopPreview();
            }

            CleanupOrphanPreviewObjects();
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                renderer.enabled = true;
                EditorUtility.SetDirty(renderer);
            }

            SyncSourcePreviewFlag(avatarRoot, false);
            SceneView.RepaintAll();
        }

        private static void EnableRenderers(GameObject avatarRoot)
        {
            if (avatarRoot == null) return;

            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                renderer.enabled = true;
                EditorUtility.SetDirty(renderer);
            }
        }

        internal static bool IsPreviewing(MToonToLilToonComponent component)
        {
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            return avatarRoot != null && IsPreviewing(avatarRoot);
        }

        private static bool IsPreviewing(GameObject avatarRoot)
        {
            return _sourceAvatarRoot != null && _sourceAvatarRoot == avatarRoot && _previewAvatar != null;
        }

        private static void QueueStartPreview(GameObject avatarRoot)
        {
            StopPreview();
            MToonToLilToonProcessor.ClearHairMergeCache();
            if (!PreviewCoordinator.TryBegin("ym-mtoon-to-liltoon", ToolName, avatarRoot, false, out var failure))
            {
                LogUtility.PreviewSkipped(ToolName, failure);
                _previewFailed = true;
                SetProgress(failure);
                return;
            }
            _pendingAvatarRoot = avatarRoot;
            _isProcessingPreview = true;
            _previewFailed = false;
            SetProgress("Processing...");
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

                var previewComponents = _previewAvatar.GetComponentsInChildren<MToonToLilToonComponent>(true);
                var selectedComponent = SelectPreferredComponent(previewComponents, _previewAvatar);
                foreach (var component in previewComponents)
                {
                    if (component != selectedComponent) continue;
                    MToonToLilToonProcessor.ApplyOnBuild(component, SetProgress, MToonToLilToonProcessor.ConversionRoute.Preview);
                    component.isPreviewing = true;
                }

                HideSourceRenderers(avatarRoot);
                SyncSourcePreviewFlag(avatarRoot, true);
                _previewFailed = false;
            }
            catch
            {
                _previewFailed = true;
                PreviewCoordinator.End("ym-mtoon-to-liltoon");
                throw;
            }
            finally
            {
                QueueFinishProcessing();
                SceneView.RepaintAll();
            }
        }

        internal static void StopPreview()
        {
            RestoreSourceRenderers();
            MToonToLilToonProcessor.ClearHairMergeCache();

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
            PreviewCoordinator.End("ym-mtoon-to-liltoon");
            CleanupOrphanPreviewObjects();
            SceneView.RepaintAll();
        }

        private static void HideSourceRenderers(GameObject avatarRoot)
        {
            RestoreSourceRenderers();
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                HiddenRenderers.Add(new RendererState
                {
                    renderer = renderer,
                    wasEnabled = renderer.enabled,
                });
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

        private static void SyncSourcePreviewFlag(GameObject avatarRoot, bool previewing)
        {
            foreach (var component in avatarRoot.GetComponentsInChildren<MToonToLilToonComponent>(true))
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

        private static MToonToLilToonComponent SelectPreferredComponent(
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

        private static string BuildRelativePath(Transform root, Transform target)
        {
            return PreviewCoordinator.BuildRelativePath(root, target);
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
