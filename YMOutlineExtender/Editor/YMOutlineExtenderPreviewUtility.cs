using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using YoridoriModifiers.Core.Editor;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.OutlineExtender
{
    [InitializeOnLoad]
    internal static class YMOutlineExtenderPreviewUtility
    {
        private const string OwnerKey = "ym-outline-extender";
        private const string ToolName = "YM Outline Extender";
        private const string PreviewRootName = "__YoridoriOutlineExtenderPreviewRoot";
        private const string PreviewAvatarName = "__YoridoriOutlineExtenderPreviewAvatar";

        private static readonly PreviewRendererVisibilityScope Visibility = new();
        private static GameObject _sourceAvatarRoot;
        private static GameObject _previewRoot;
        private static GameObject _previewAvatar;
        private static GameObject _pendingAvatarRoot;
        private static string _progress = string.Empty;
        private static bool _processing;
        private static bool _failed;
        private static int _progressVersion;
        private static readonly HashSet<Object> SourceAssets = new();
        private static readonly HashSet<Object> TransientAssets = new();
        private static OutlineSdfCache _previewSdfCache = new();
        private static OutlineBakeCache _previewBakeCache = new();
        private static OutlineBoundaryPaddingCache _previewPaddingCache = new();

        static YMOutlineExtenderPreviewUtility()
        {
            SceneIconUtility.HideComponentIcon<YMOutlineExtenderComponent>();
            PreviewRecoveryUtility.RegisterResetHandler(OwnerKey, ResetOwnPreviewArtifacts);
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += StopPreview;
            CleanupOrphans();
        }

        internal static void TogglePreview(YMOutlineExtenderComponent component)
        {
            if (_processing) return;
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            if (root == null) return;
            if (IsPreviewing(root)) StopPreview();
            else QueueStart(root);
        }

        internal static void RestartPreviewIfActive(YMOutlineExtenderComponent component)
        {
            if (_processing) return;
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            if (root != null && IsPreviewing(root)) QueueStart(root, true);
        }

        internal static bool IsPreviewing(YMOutlineExtenderComponent component)
        {
            var root = component != null ? PreviewCoordinator.FindAvatarRoot(component.gameObject) : null;
            return root != null && IsPreviewing(root);
        }

        internal static bool IsProcessingPreview() => _processing;
        internal static bool HasPreviewFailed() => _failed;
        internal static string GetPreviewProgressMessage() => _progress;

        private static bool IsPreviewing(GameObject root) =>
            _sourceAvatarRoot == root && _previewAvatar != null;

        private static void QueueStart(GameObject root, bool preserveSdfCache = false)
        {
            StopPreviewInternal(preserveSdfCache);
            if (!PreviewCoordinator.TryBegin(OwnerKey, ToolName, root, false, out var failure))
            {
                LogUtility.PreviewSkipped(ToolName, failure);
                _failed = true;
                SetProgress(failure);
                return;
            }
            _pendingAvatarRoot = root;
            _processing = true;
            _failed = false;
            SetProgress("Preparing preview...");
            EditorApplication.delayCall += StartPendingPreview;
        }

        private static void StartPendingPreview()
        {
            var root = _pendingAvatarRoot;
            _pendingAvatarRoot = null;
            if (root == null)
            {
                _processing = false;
                SetProgress(string.Empty);
                return;
            }
            try
            {
                _sourceAvatarRoot = root;
                CollectReferencedAssets(root, SourceAssets, false);
                _previewRoot = new GameObject(PreviewRootName)
                {
                    hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor
                };
                _previewAvatar = Object.Instantiate(root, _previewRoot.transform);
                _previewAvatar.name = PreviewAvatarName;
                _previewAvatar.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;

                SetProgress("Converting MToon materials...");
                MToonToLilToonPreviewBridge.ApplyForChainedPreview(_previewAvatar, SetProgress);
                SetProgress("Generating outline SDF...");
                var component = SelectPreferredComponent(_previewAvatar);
                if (component != null && component.enabled && component.enableOutlineExtender)
                {
                    OutlineExtenderProcessor.ApplyPreview(component, _previewSdfCache, _previewBakeCache, _previewPaddingCache);
                    component.isPreviewing = true;
                }
                CollectReferencedAssets(_previewAvatar, TransientAssets, true);
                Visibility.Hide(root);
                SyncPreviewFlag(root, true);
            }
            catch
            {
                _failed = true;
                PreviewCoordinator.End(OwnerKey);
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
            => StopPreviewInternal(false);

        private static void StopPreviewInternal(bool preserveSdfCache)
        {
            Visibility.Restore();
            if (_previewRoot != null) Object.DestroyImmediate(_previewRoot);
            var cachedTextures = preserveSdfCache
                ? new HashSet<Texture2D>(_previewSdfCache.Textures.Concat(_previewBakeCache.Textures).Concat(_previewPaddingCache.Textures)
                    .Where(texture => texture != null))
                : null;
            foreach (var asset in TransientAssets.Where(asset => asset != null))
                if (!(asset is Texture2D texture) || cachedTextures == null || !cachedTextures.Contains(texture))
                    Object.DestroyImmediate(asset);
            TransientAssets.Clear();
            if (!preserveSdfCache)
            {
                _previewSdfCache.DestroyTextures();
                _previewSdfCache = new OutlineSdfCache();
                _previewBakeCache.DestroyTextures();
                _previewBakeCache = new OutlineBakeCache();
                _previewPaddingCache.DestroyTextures();
                _previewPaddingCache = new OutlineBoundaryPaddingCache();
            }
            SourceAssets.Clear();
            if (_sourceAvatarRoot != null) SyncPreviewFlag(_sourceAvatarRoot, false);
            _previewRoot = null;
            _previewAvatar = null;
            _sourceAvatarRoot = null;
            _pendingAvatarRoot = null;
            _processing = false;
            _failed = false;
            SetProgress(string.Empty);
            PreviewCoordinator.End(OwnerKey);
            CleanupOrphans();
            SceneView.RepaintAll();
        }

        private static YMOutlineExtenderComponent SelectPreferredComponent(GameObject root) =>
            root.GetComponentsInChildren<YMOutlineExtenderComponent>(true)
                .Where(c => c != null)
                .OrderBy(c => PreviewCoordinator.GetDepthFromRoot(c.transform, root.transform))
                .FirstOrDefault();

        private static void SyncPreviewFlag(GameObject root, bool previewing)
        {
            foreach (var component in root.GetComponentsInChildren<YMOutlineExtenderComponent>(true))
            {
                component.isPreviewing = previewing;
                EditorUtility.SetDirty(component);
            }
        }

        private static void ResetOwnPreviewArtifacts(GameObject root)
        {
            if (root == null || IsPreviewing(root)) StopPreview();
            CleanupOrphans();
            if (root != null)
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                SyncPreviewFlag(root, false);
            }
            SceneView.RepaintAll();
        }

        private static void CleanupOrphans()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (go != null && (go.name == PreviewRootName || go.name == PreviewAvatarName))
                    Object.DestroyImmediate(go);
        }

        private static void CollectReferencedAssets(GameObject root, HashSet<Object> result, bool transientOnly)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials.Where(material => material != null))
                {
                    AddAsset(material, result, transientOnly);
                    foreach (var property in material.GetTexturePropertyNames())
                        AddAsset(material.GetTexture(property), result, transientOnly);
                }
                AddAsset(OutlineRendererUtility.GetMesh(renderer), result, transientOnly);
            }
        }

        private static void AddAsset(Object asset, HashSet<Object> result, bool transientOnly)
        {
            if (asset == null) return;
            if (transientOnly && (EditorUtility.IsPersistent(asset) || SourceAssets.Contains(asset))) return;
            result.Add(asset);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode)
                StopPreview();
        }

        private static void SetProgress(string message)
        {
            _progress = message ?? string.Empty;
            _progressVersion++;
            InternalEditorUtility.RepaintAllViews();
        }

        private static void QueueFinishProcessing()
        {
            var version = ++_progressVersion;
            EditorApplication.delayCall += () =>
            {
                if (version != _progressVersion) return;
                _processing = false;
                _progress = string.Empty;
                InternalEditorUtility.RepaintAllViews();
            };
        }
    }
}
