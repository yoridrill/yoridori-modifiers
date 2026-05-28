using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.ArmPatch
{
    [InitializeOnLoad]
    internal static class ArmPatchPreviewUtility
    {
        private const string PreviewRootName = "__NDMF_VRoidArmPatch_PreviewRoot";
        private const string PreviewAvatarName = "__NDMF_VRoidArmPatch_PreviewAvatar";
        private const string ToolName = "YM Arm Patch";

        private static GameObject _sourceAvatarRoot;
        private static ArmPatchComponent _sourceComponent;
        private static GameObject _previewRoot;
        private static GameObject _previewAvatar;
        private static AnimationClip _previewClip;
        private static double _previewStartTime;
        private static bool _isPlaying;

        private static readonly List<RendererState> HiddenSourceRenderers = new List<RendererState>();

        private static readonly string[] ClipCandidatePaths =
        {
            "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/Proxy Anim/proxy_idle.anim",
            "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/ProxyAnim/proxy_idle.anim",
            "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/ProxyAnim/proxy_idle3.anim"
        };

        private struct RendererState
        {
            public Renderer renderer;
            public bool wasEnabled;
        }

        static ArmPatchPreviewUtility()
        {
            SceneIconUtility.HideComponentIcon<ArmPatchComponent>();

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            CleanupOrphanPreviewObjects();
        }

        internal static bool IsPlaying => _isPlaying;

        internal static bool IsPreviewing(GameObject avatarRoot)
        {
            return _isPlaying && _sourceAvatarRoot == avatarRoot;
        }

        internal static bool TogglePreview(ArmPatchComponent component)
        {
            if (component == null) return false;

            var avatarRoot = FindHumanoidAvatarRoot(component.transform);
            if (avatarRoot == null)
            {
                LogUtility.PreviewSkipped(ToolName, "Humanoid Animator not found.");
                return false;
            }

            if (IsPreviewing(avatarRoot))
            {
                StopPreview();
                return true;
            }

            return StartPreview(avatarRoot, component);
        }

        internal static void RestartPreviewIfActive(ArmPatchComponent component)
        {
            if (component == null) return;

            var avatarRoot = FindHumanoidAvatarRoot(component.transform);
            if (avatarRoot == null || !IsPreviewing(avatarRoot)) return;

            StartPreview(avatarRoot, component);
        }

        internal static void ResetAllPreviewArtifacts()
        {
            StopPreview();
            CleanupOrphanPreviewObjects();
            EnableAllHumanoidRenderers();
            SceneView.RepaintAll();
        }

        internal static void StopPreview()
        {
            if (_isPlaying)
            {
                EditorApplication.update -= OnEditorUpdate;
                _isPlaying = false;
            }

            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }

            RestoreSourceRenderers();

            if (_previewRoot != null)
            {
                Object.DestroyImmediate(_previewRoot);
            }

            _previewRoot = null;
            _previewAvatar = null;
            _previewClip = null;
            _sourceAvatarRoot = null;
            _sourceComponent = null;
            PreviewCoordinator.End("ym-arm-patch");

            CleanupOrphanPreviewObjects();
            SceneView.RepaintAll();
        }

        private static bool StartPreview(GameObject avatarRoot, ArmPatchComponent sourceComponent)
        {
            StopPreview();
            if (!PreviewCoordinator.TryBegin("ym-arm-patch", ToolName, avatarRoot, true, out var failure))
            {
                LogUtility.PreviewSkipped(ToolName, failure);
                return false;
            }

            _previewClip = LoadPreviewClip();
            if (_previewClip == null)
            {
                LogUtility.Warning(ToolName, "Preview clip not found.");
                PreviewCoordinator.End("ym-arm-patch");
                return false;
            }

            _sourceAvatarRoot = avatarRoot;
            _sourceComponent = sourceComponent;

            _previewRoot = new GameObject(PreviewRootName);
            _previewRoot.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;

            _previewAvatar = Object.Instantiate(avatarRoot, _previewRoot.transform);
            _previewAvatar.name = PreviewAvatarName;
            _previewAvatar.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
            _previewAvatar.transform.SetPositionAndRotation(avatarRoot.transform.position, avatarRoot.transform.rotation);
            _previewAvatar.transform.localScale = avatarRoot.transform.localScale;

            DisableProblematicComponents(_previewAvatar);

            var previewComponent = _previewAvatar.GetComponentInChildren<ArmPatchComponent>(true);
            if (previewComponent == null)
            {
                previewComponent = _previewAvatar.AddComponent<ArmPatchComponent>();
            }

            CopyComponentValues(sourceComponent, previewComponent);
            ArmPatchNdmfPlugin.BuildPatchRig(_previewAvatar, previewComponent, sourceComponent.verboseLog);

            HideSourceRenderers(_sourceAvatarRoot);

            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }

            _previewStartTime = EditorApplication.timeSinceStartup;
            _isPlaying = true;
            EditorApplication.update += OnEditorUpdate;

            SceneView.RepaintAll();
            return true;
        }

        private static void OnEditorUpdate()
        {
            if (!_isPlaying || _previewAvatar == null || _previewClip == null || _sourceComponent == null || _sourceAvatarRoot == null)
            {
                StopPreview();
                return;
            }

            float time = 0f;
            if (_previewClip.length > 0f)
            {
                double elapsed = EditorApplication.timeSinceStartup - _previewStartTime;
                time = (float)(elapsed % _previewClip.length);
            }

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(_previewAvatar, _previewClip, time);
            AnimationMode.EndSampling();

            SceneView.RepaintAll();
        }

        private static void OnBeforeAssemblyReload()
        {
            ResetAllPreviewArtifacts();
        }

        private static void OnEditorQuitting()
        {
            ResetAllPreviewArtifacts();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode ||
                state == PlayModeStateChange.EnteredPlayMode)
            {
                ResetAllPreviewArtifacts();
            }
        }

        private static GameObject FindHumanoidAvatarRoot(Transform start)
        {
            if (start == null) return null;

            var current = start;
            while (current != null)
            {
                var animator = current.GetComponent<Animator>();
                if (animator != null && animator.avatar != null && animator.avatar.isHuman)
                {
                    return animator.gameObject;
                }

                current = current.parent;
            }

            var animatorInParents = start.GetComponentInParent<Animator>(true);
            if (animatorInParents != null && animatorInParents.avatar != null && animatorInParents.avatar.isHuman)
            {
                return animatorInParents.gameObject;
            }

            return null;
        }

        private static AnimationClip LoadPreviewClip()
        {
            for (int i = 0; i < ClipCandidatePaths.Length; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCandidatePaths[i]);
                if (clip != null) return clip;
            }

            var guids = AssetDatabase.FindAssets("proxy_idle t:AnimationClip");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null) return clip;
            }

            return null;
        }

        private static void DisableProblematicComponents(GameObject root)
        {
            if (root == null) return;

            var components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;

                string typeName = c.GetType().FullName;
                if (string.IsNullOrEmpty(typeName)) continue;

                if (typeName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor")
                {
                    Object.DestroyImmediate(c);
                    continue;
                }

                if (c is Behaviour behaviour)
                {
                    if (typeName.Contains("PhysBone") || typeName.Contains("Contact"))
                    {
                        behaviour.enabled = false;
                    }
                }
            }
        }

        private static void HideSourceRenderers(GameObject avatarRoot)
        {
            RestoreSourceRenderers();

            if (avatarRoot == null) return;

            HiddenSourceRenderers.Clear();

            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null) return;

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                HiddenSourceRenderers.Add(new RendererState
                {
                    renderer = r,
                    wasEnabled = r.enabled
                });

                r.enabled = false;
            }
        }

        private static void RestoreSourceRenderers()
        {
            for (int i = 0; i < HiddenSourceRenderers.Count; i++)
            {
                var state = HiddenSourceRenderers[i];
                if (state.renderer != null)
                {
                    state.renderer.enabled = state.wasEnabled;
                }
            }

            HiddenSourceRenderers.Clear();
        }

        private static void EnableAllHumanoidRenderers()
        {
            var animators = Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman) continue;

                var renderers = animator.gameObject.GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < renderers.Length; j++)
                {
                    if (renderers[j] != null)
                    {
                        renderers[j].enabled = true;
                    }
                }
            }
        }

        private static void CleanupOrphanPreviewObjects()
        {
            var objects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < objects.Length; i++)
            {
                var go = objects[i];
                if (go == null) continue;

                if (go.name == PreviewRootName || go.name == PreviewAvatarName)
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        private static void CopyComponentValues(ArmPatchComponent src, ArmPatchComponent dst)
        {
            dst.enableShoulderFix = src.enableShoulderFix;
            dst.shoulderPositionOffset = src.shoulderPositionOffset;
            dst.shoulderEulerOffset = src.shoulderEulerOffset;
            dst.upperArmRollAxis = src.upperArmRollAxis;
            dst.upperArmRollWeight = src.upperArmRollWeight;
            dst.enableForearmFix = src.enableForearmFix;
            src.MigrateSerializedValuesIfNeeded();
            dst.forearmElbowScale = src.forearmElbowScale;
            dst.forearmWristScale = src.forearmWristScale;
            dst.forearmElbowRollOffset = src.forearmElbowRollOffset;
            dst.forearmRollAxis = src.forearmRollAxis;
            dst.forearmPitchAxis = src.forearmPitchAxis;
            dst.forearmRollWeight = src.forearmRollWeight;
            dst.forearmTwistBoneType = src.forearmTwistBoneType;
            dst.forearmTwistBoneCount = src.forearmTwistBoneCount;
            dst.forearmSkinMaterialName = src.forearmSkinMaterialName;
            dst.enableThumbFix = src.enableThumbFix;
            dst.thumbEulerOffset = src.thumbEulerOffset;
            dst.verboseLog = src.verboseLog;
        }
    }
}
