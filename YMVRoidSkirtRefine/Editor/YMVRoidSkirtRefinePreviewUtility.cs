using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.config;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
using YoridoriModifiers.Core.Editor;
using Object = UnityEngine.Object;

namespace YoridoriModifiers.VRoidSkirtRefine
{
    [InitializeOnLoad]
    internal static class YMVRoidSkirtRefinePreviewUtility
    {
        private const string ToolName = "YM VRoid Skirt Refine";
        private const string OwnerKey = "ym-vroid-skirt-refine-play-preview";
        private const string KeyActive = "YMVRoidSkirtRefinePreview.Active";
        private const string KeySaveRequested = "YMVRoidSkirtRefinePreview.SaveRequested";
        private const string KeyAvatarRootName = "YMVRoidSkirtRefinePreview.AvatarRootName";
        private const string KeyComponentPath = "YMVRoidSkirtRefinePreview.ComponentPath";
        private const string KeySavedJson = "YMVRoidSkirtRefinePreview.SavedJson";
        private const string KeyPreviewJson = "YMVRoidSkirtRefinePreview.PreviewJson";
        private const string KeyNdmfApplyOnPlayStored = "YMVRoidSkirtRefinePreview.NdmfApplyOnPlayStored";
        private const string KeyNdmfApplyOnPlayPrevious = "YMVRoidSkirtRefinePreview.NdmfApplyOnPlayPrevious";
        private const string PreviewSettingsObjectName = "__YMVRoidSkirtRefinePreviewSettings";

        private const string DanceLoopGuid = "6af08d7e879ca47638bab5230a34dd4f";
        private const string SprintLoopGuid = "74fba2b6d8cac43dc965c5e43ce1b44a";
        private const string SittingEnterGuid = "3d94cd0a724104454b9bb0fef0862f8d";
        private const string SittingExitGuid = "34f67d009d4e0489ab383c0ce6641a94";

        private static readonly PreviewClipSpec[] PreviewClipSpecs =
        {
            new PreviewClipSpec(DanceLoopGuid, 2),
            new PreviewClipSpec(SprintLoopGuid, 2),
            new PreviewClipSpec(SittingEnterGuid, 1),
            new PreviewClipSpec(SittingExitGuid, 1)
        };

        private static GameObject _avatarRoot;
        private static GameObject _settingsObject;
        private static YMVRoidSkirtRefine _component;
        private static YMPreviewMotionDriver _motionDriver;
        private static VRCPhysBone[] _onePiecePreviewPhysBones = Array.Empty<VRCPhysBone>();
        private static VRCPhysBone[] _longCoatPreviewPhysBones = Array.Empty<VRCPhysBone>();
        private static PreviewClipEntry[] _clips;
        private static bool _previewBuilt;
        private static bool _previewFailed;

        static YMVRoidSkirtRefinePreviewUtility()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += ClearSession;
        }

        internal static bool IsPreviewing(YMVRoidSkirtRefine component)
        {
            return component != null
                && EditorApplication.isPlaying
                && SessionState.GetBool(KeyActive, false)
                && (component == _component || MatchesStoredComponent(component));
        }

        internal static bool IsStarting(YMVRoidSkirtRefine component)
        {
            return component != null
                && EditorApplication.isPlayingOrWillChangePlaymode
                && SessionState.GetBool(KeyActive, false)
                && (component == _component || MatchesStoredComponent(component));
        }

        internal static bool HasPreviewFailed() => _previewFailed;

        internal static bool IsActivePlayPreview => EditorApplication.isPlaying && SessionState.GetBool(KeyActive, false);

        internal static YMVRoidSkirtRefine FindSourceComponentForPreview()
        {
            return IsActivePlayPreview ? FindStoredComponent() : null;
        }

        internal static bool TogglePreview(YMVRoidSkirtRefine component)
        {
            if (component == null) return false;

            if (EditorApplication.isPlaying)
            {
                SaveAndExitPreview(component);
                return true;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode) return true;

            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            if (avatarRoot == null)
            {
                LogUtility.PreviewSkipped(ToolName, "Preview target is missing.");
                _previewFailed = true;
                return false;
            }

            if (!PreviewCoordinator.TryBegin(OwnerKey, ToolName, avatarRoot, false, out var failure))
            {
                LogUtility.PreviewSkipped(ToolName, failure);
                _previewFailed = true;
                return false;
            }

            SessionState.SetBool(KeyActive, true);
            SessionState.SetBool(KeySaveRequested, false);
            SessionState.SetString(KeyAvatarRootName, avatarRoot.name);
            SessionState.SetString(KeyComponentPath, PreviewCoordinator.BuildRelativePath(avatarRoot.transform, component.transform));
            SessionState.SetString(KeyPreviewJson, JsonUtility.ToJson(PreviewSnapshot.From(component, avatarRoot.transform)));
            SessionState.EraseString(KeySavedJson);
            SuspendNdmfApplyOnPlayForPreview();

            _avatarRoot = avatarRoot;
            _component = component;
            _previewBuilt = false;
            _previewFailed = false;

            FocusSceneView();
            EditorApplication.EnterPlaymode();
            return true;
        }

        internal static void SyncPhysBonesIfPreviewing(YMVRoidSkirtRefine component)
        {
            if (!IsPreviewing(component)) return;
            SyncCapturedPreviewPhysBones(component);
            SceneView.RepaintAll();
        }

        private static void SaveAndExitPreview(YMVRoidSkirtRefine component)
        {
            if (component == null) return;

            SessionState.SetBool(KeySaveRequested, true);
            SessionState.SetString(KeySavedJson, JsonUtility.ToJson(PreviewSnapshot.From(component)));
            EditorApplication.ExitPlaymode();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    TryStartPlayPreview();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    if (SessionState.GetBool(KeyActive, false) && !SessionState.GetBool(KeySaveRequested, false))
                    {
                        SessionState.EraseString(KeySavedJson);
                    }
                    StopSampling();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    TryApplySavedValues();
                    ClearSession();
                    break;
            }
        }

        private static void TryStartPlayPreview()
        {
            if (!SessionState.GetBool(KeyActive, false)) return;

            _avatarRoot = FindStoredAvatarRoot();
            var snapshot = LoadPreviewSnapshot();
            if (_avatarRoot == null || snapshot == null)
            {
                _previewFailed = true;
                LogUtility.Warning(ToolName, "Preview target was not found after entering Play mode.");
                return;
            }

            _clips = LoadPreviewClips();
            if (_clips.Length == 0)
            {
                _previewFailed = true;
                LogUtility.Warning(ToolName, "Preview animation clips were not found.");
                return;
            }

            try
            {
                DisableRuntimeComponentsThatFightSampling(_avatarRoot);
                _settingsObject = new GameObject(PreviewSettingsObjectName)
                {
                    hideFlags = HideFlags.DontSave
                };
                _settingsObject.transform.SetParent(_avatarRoot.transform, false);
                _component = _settingsObject.AddComponent<YMVRoidSkirtRefine>();
                _component.hideFlags = HideFlags.DontSave;
                snapshot.ApplyTo(_component);
                snapshot.ApplyReferencesTo(_component, _avatarRoot.transform);
                var previewBuildResult = YMVRoidSkirtRefineNdmfPlugin.BuildForPreviewWithResult(_avatarRoot, _component);
                CapturePreviewBuildResult(previewBuildResult);
                var driver = _settingsObject.AddComponent<YMPreviewMotionDriver>();
                if (driver == null)
                {
                    throw new InvalidOperationException("Preview motion driver could not be added.");
                }
                driver.hideFlags = HideFlags.DontSave;
                driver.Initialize(
                    _avatarRoot,
                    _clips.Select(c => c != null ? c.Clip : null).ToArray(),
                    _clips.Select(c => c != null ? c.LoopCount : 1).ToArray());
                _motionDriver = driver;
                SyncCapturedPreviewPhysBones(_component);
                _previewBuilt = true;
                _previewFailed = false;
                SelectPreviewSettingsObject();
                FocusSceneView();
            }
            catch (Exception ex)
            {
                _previewFailed = true;
                LogUtility.Error(ToolName, "Preview", ex.ToString());
            }
        }

        private static void OnEditorUpdate()
        {
            if (!_previewBuilt || _avatarRoot == null || _clips == null || _clips.Length == 0) return;
            if (!EditorApplication.isPlaying)
            {
                StopSampling();
                return;
            }

            SceneView.RepaintAll();
        }

        private static PreviewClipEntry[] LoadPreviewClips()
        {
            return PreviewClipSpecs
                .Select(spec =>
                {
                    var path = AssetDatabase.GUIDToAssetPath(spec.Guid);
                    var clip = !string.IsNullOrEmpty(path)
                        ? AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
                        : null;
                    return clip != null ? new PreviewClipEntry(clip, spec.LoopCount) : null;
                })
                .Where(entry => entry != null)
                .ToArray();
        }

        private static void TryApplySavedValues()
        {
            if (!SessionState.GetBool(KeySaveRequested, false)) return;

            var json = SessionState.GetString(KeySavedJson, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            var component = FindStoredComponent();
            if (component == null) return;

            var snapshot = JsonUtility.FromJson<PreviewSnapshot>(json);
            if (snapshot == null) return;

            Undo.RecordObject(component, "Save YM VRoid Skirt Refine Preview");
            snapshot.ApplyTo(component);
            EditorUtility.SetDirty(component);
        }

        private static YMVRoidSkirtRefine FindStoredComponent()
        {
            var avatarName = SessionState.GetString(KeyAvatarRootName, string.Empty);
            var componentPath = SessionState.GetString(KeyComponentPath, string.Empty);

            foreach (var component in Object.FindObjectsOfType<YMVRoidSkirtRefine>(true))
            {
                if (component == null) continue;
                var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
                if (avatarRoot == null) continue;
                if (!string.IsNullOrEmpty(avatarName) && avatarRoot.name != avatarName) continue;

                var path = PreviewCoordinator.BuildRelativePath(avatarRoot.transform, component.transform);
                if (path == componentPath) return component;
            }

            return null;
        }

        private static bool MatchesStoredComponent(YMVRoidSkirtRefine component)
        {
            if (component == null) return false;
            var avatarRoot = PreviewCoordinator.FindAvatarRoot(component.gameObject);
            if (avatarRoot == null) return false;
            return avatarRoot.name == SessionState.GetString(KeyAvatarRootName, string.Empty)
                && PreviewCoordinator.BuildRelativePath(avatarRoot.transform, component.transform) == SessionState.GetString(KeyComponentPath, string.Empty);
        }

        private static void DisableRuntimeComponentsThatFightSampling(GameObject avatarRoot)
        {
            if (avatarRoot == null) return;

            foreach (var animator in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (animator != null) animator.enabled = false;
            }

            foreach (var station in avatarRoot.GetComponentsInChildren<VRCStation>(true))
            {
                if (station != null) station.enabled = false;
            }
        }

        private static void FocusSceneView()
        {
            EditorWindow.FocusWindowIfItsOpen<SceneView>();
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.Focus();
            }
            EditorApplication.delayCall += () =>
            {
                EditorWindow.FocusWindowIfItsOpen<SceneView>();
                if (SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.Focus();
                }
            };
        }

        private static void StopSampling()
        {
            _previewBuilt = false;
            _avatarRoot = null;
            _settingsObject = null;
            _component = null;
            _motionDriver = null;
            _onePiecePreviewPhysBones = Array.Empty<VRCPhysBone>();
            _longCoatPreviewPhysBones = Array.Empty<VRCPhysBone>();
            _clips = null;
        }

        private static void OnBeforeAssemblyReload()
        {
            StopSampling();
        }

        private static void ClearSession()
        {
            StopSampling();
            RestoreNdmfApplyOnPlay();
            PreviewCoordinator.End(OwnerKey);
            SessionState.SetBool(KeyActive, false);
            SessionState.SetBool(KeySaveRequested, false);
            SessionState.EraseString(KeyAvatarRootName);
            SessionState.EraseString(KeyComponentPath);
            SessionState.EraseString(KeySavedJson);
            SessionState.EraseString(KeyPreviewJson);
            SessionState.EraseBool(KeyNdmfApplyOnPlayStored);
            SessionState.EraseBool(KeyNdmfApplyOnPlayPrevious);
        }

        private static void SuspendNdmfApplyOnPlayForPreview()
        {
            if (!SessionState.GetBool(KeyNdmfApplyOnPlayStored, false))
            {
                SessionState.SetBool(KeyNdmfApplyOnPlayPrevious, Config.ApplyOnPlay);
                SessionState.SetBool(KeyNdmfApplyOnPlayStored, true);
            }

            if (Config.ApplyOnPlay)
            {
                Config.ApplyOnPlay = false;
                LogUtility.Info(ToolName, "Preview", "Temporarily disabled NDMF Apply On Play for skirt preview.");
            }
        }

        private static void RestoreNdmfApplyOnPlay()
        {
            if (!SessionState.GetBool(KeyNdmfApplyOnPlayStored, false)) return;

            var previous = SessionState.GetBool(KeyNdmfApplyOnPlayPrevious, true);
            if (Config.ApplyOnPlay != previous)
            {
                Config.ApplyOnPlay = previous;
                LogUtility.Info(ToolName, "Preview", $"Restored NDMF Apply On Play: {previous}");
            }
        }

        private static GameObject FindStoredAvatarRoot()
        {
            var avatarName = SessionState.GetString(KeyAvatarRootName, string.Empty);
            foreach (var animator in Object.FindObjectsOfType<Animator>(true))
            {
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman) continue;
                if (!string.IsNullOrEmpty(avatarName) && animator.gameObject.name != avatarName) continue;
                return animator.gameObject;
            }

            return null;
        }

        private static PreviewSnapshot LoadPreviewSnapshot()
        {
            var json = SessionState.GetString(KeyPreviewJson, string.Empty);
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PreviewSnapshot>(json);
        }

        private static void CapturePreviewBuildResult(YMVRoidSkirtRefineNdmfPlugin.PreviewBuildResult result)
        {
            _onePiecePreviewPhysBones = result?.OnePiecePhysBones
                ?.Where(physBone => physBone != null)
                .Distinct()
                .ToArray() ?? Array.Empty<VRCPhysBone>();
            _longCoatPreviewPhysBones = result?.LongCoatPhysBones
                ?.Where(physBone => physBone != null)
                .Distinct()
                .ToArray() ?? Array.Empty<VRCPhysBone>();

            LogUtility.Verbose(
                ToolName,
                _component != null && _component.verboseLog,
                "Preview",
                $"Captured preview build PhysBones. onePiece={_onePiecePreviewPhysBones.Length}, longCoat={_longCoatPreviewPhysBones.Length}");
        }

        private static void SyncCapturedPreviewPhysBones(YMVRoidSkirtRefine component)
        {
            if (component == null) return;

            foreach (var physBone in _onePiecePreviewPhysBones)
            {
                YMVRoidSkirtRefineNdmfPlugin.ApplyPreviewPhysBoneSettings(
                    physBone,
                    component.onePiecePhysBone);
            }

            foreach (var physBone in _longCoatPreviewPhysBones)
            {
                YMVRoidSkirtRefineNdmfPlugin.ApplyPreviewPhysBoneSettings(
                    physBone,
                    component.longCoatPhysBone);
            }
        }

        private static void SelectPreviewSettingsObject()
        {
            if (_settingsObject == null) return;

            Selection.activeGameObject = _settingsObject;
            EditorGUIUtility.PingObject(_settingsObject);
            EditorApplication.delayCall += () =>
            {
                if (_settingsObject == null) return;
                Selection.activeGameObject = _settingsObject;
                EditorGUIUtility.PingObject(_settingsObject);
            };
        }

        private sealed class PreviewClipSpec
        {
            public readonly string Guid;
            public readonly int LoopCount;

            public PreviewClipSpec(string guid, int loopCount)
            {
                Guid = guid;
                LoopCount = Mathf.Max(1, loopCount);
            }
        }

        internal sealed class PreviewClipEntry
        {
            public readonly AnimationClip Clip;
            public readonly int LoopCount;
            public readonly float ClipLength;
            public float Length => ClipLength * LoopCount;

            public PreviewClipEntry(AnimationClip clip, int loopCount)
            {
                Clip = clip;
                LoopCount = Mathf.Max(1, loopCount);
                ClipLength = Mathf.Max(0.001f, clip != null ? clip.length : 0.0f);
            }
        }

        [Serializable]
        private sealed class PreviewSnapshot
        {
            public BoneTargetsSnapshot onePieceBones;
            public BoneTargetsSnapshot longCoatBones;
            public bool enableOnePieceRefine;
            public OnePiecePreset onePiecePreset;
            public bool enableOnePieceBoneExtension;
            public BoneExtensionMode onePieceBoneExtensionMode;
            public int onePieceTargetBoneCount;
            public float onePieceRootHeightOffsetMultiplier;
            public float onePieceHipWeightReduction;
            public bool onePieceMatchLongCoat;
            public bool onePieceUseUpperLegColliders;
            public bool onePieceUseLowerLegColliders;
            public bool onePieceUseFloorCollider;
            public bool onePieceUseFrontRootRotationConstraints;
            public float onePieceMoveFrontRootsTowardUpperLeg;
            public PhysBoneSnapshot onePiecePhysBone;
            public bool enableLongCoatRefine;
            public LongCoatPreset longCoatPreset;
            public bool enableLongCoatBoneExtension;
            public BoneExtensionMode longCoatBoneExtensionMode;
            public int longCoatTargetBoneCount;
            public bool longCoatShortSkirtUsePrependedRootsOnly;
            public float longCoatRootHeightOffsetMultiplier;
            public float longCoatHipWeightReduction;
            public float longCoatSpineWeightReduction;
            public bool longCoatMoveFrontBonesOutward;
            public bool longCoatUseRotationConstraints;
            public bool longCoatUseFrontRootRotationConstraints;
            public float longCoatMoveConstrainedRootsTowardUpperLeg;
            public bool longCoatAimFrontLimitsForward;
            public bool longCoatMatchOnePiece;
            public bool longCoatUseUpperLegColliders;
            public bool longCoatUseLowerLegColliders;
            public bool longCoatUseFloorCollider;
            public PhysBoneSnapshot longCoatPhysBone;
            public SkirtRefineConstraintMode constraintMode;
            public bool addGeneratedDynamicsToVqtKeepList;
            public bool verboseLog;

            public static PreviewSnapshot From(YMVRoidSkirtRefine source, Transform avatarRoot = null)
            {
                return new PreviewSnapshot
                {
                    onePieceBones = BoneTargetsSnapshot.From(source.onePieceBones, avatarRoot),
                    longCoatBones = BoneTargetsSnapshot.From(source.longCoatBones, avatarRoot),
                    enableOnePieceRefine = source.enableOnePieceRefine,
                    onePiecePreset = source.onePiecePreset,
                    enableOnePieceBoneExtension = source.enableOnePieceBoneExtension,
                    onePieceBoneExtensionMode = source.onePieceBoneExtensionMode,
                    onePieceTargetBoneCount = source.onePieceTargetBoneCount,
                    onePieceRootHeightOffsetMultiplier = source.onePieceRootHeightOffsetMultiplier,
                    onePieceHipWeightReduction = source.onePieceHipWeightReduction,
                    onePieceMatchLongCoat = source.onePieceMatchLongCoat,
                    onePieceUseUpperLegColliders = source.onePieceUseUpperLegColliders,
                    onePieceUseLowerLegColliders = source.onePieceUseLowerLegColliders,
                    onePieceUseFloorCollider = source.onePieceUseFloorCollider,
                    onePieceUseFrontRootRotationConstraints = source.onePieceUseFrontRootRotationConstraints,
                    onePieceMoveFrontRootsTowardUpperLeg = source.onePieceMoveFrontRootsTowardUpperLeg,
                    onePiecePhysBone = PhysBoneSnapshot.From(source.onePiecePhysBone),
                    enableLongCoatRefine = source.enableLongCoatRefine,
                    longCoatPreset = source.longCoatPreset,
                    enableLongCoatBoneExtension = source.enableLongCoatBoneExtension,
                    longCoatBoneExtensionMode = source.longCoatBoneExtensionMode,
                    longCoatTargetBoneCount = source.longCoatTargetBoneCount,
                    longCoatShortSkirtUsePrependedRootsOnly = source.longCoatShortSkirtUsePrependedRootsOnly,
                    longCoatRootHeightOffsetMultiplier = source.longCoatRootHeightOffsetMultiplier,
                    longCoatHipWeightReduction = source.longCoatHipWeightReduction,
                    longCoatSpineWeightReduction = source.longCoatSpineWeightReduction,
                    longCoatMoveFrontBonesOutward = source.longCoatMoveFrontBonesOutward,
                    longCoatUseRotationConstraints = source.longCoatUseRotationConstraints,
                    longCoatUseFrontRootRotationConstraints = source.longCoatUseFrontRootRotationConstraints,
                    longCoatMoveConstrainedRootsTowardUpperLeg = source.longCoatMoveConstrainedRootsTowardUpperLeg,
                    longCoatAimFrontLimitsForward = source.longCoatAimFrontLimitsForward,
                    longCoatMatchOnePiece = source.longCoatMatchOnePiece,
                    longCoatUseUpperLegColliders = source.longCoatUseUpperLegColliders,
                    longCoatUseLowerLegColliders = source.longCoatUseLowerLegColliders,
                    longCoatUseFloorCollider = source.longCoatUseFloorCollider,
                    longCoatPhysBone = PhysBoneSnapshot.From(source.longCoatPhysBone),
                    constraintMode = source.constraintMode,
                    addGeneratedDynamicsToVqtKeepList = source.addGeneratedDynamicsToVqtKeepList,
                    verboseLog = source.verboseLog
                };
            }

            public void ApplyTo(YMVRoidSkirtRefine target)
            {
                target.enableOnePieceRefine = enableOnePieceRefine;
                target.onePiecePreset = onePiecePreset;
                target.enableOnePieceBoneExtension = enableOnePieceBoneExtension;
                target.onePieceBoneExtensionMode = onePieceBoneExtensionMode;
                target.onePieceTargetBoneCount = onePieceTargetBoneCount;
                target.onePieceRootHeightOffsetMultiplier = onePieceRootHeightOffsetMultiplier;
                target.onePieceHipWeightReduction = onePieceHipWeightReduction;
                target.onePieceMatchLongCoat = onePieceMatchLongCoat;
                target.onePieceUseUpperLegColliders = onePieceUseUpperLegColliders;
                target.onePieceUseLowerLegColliders = onePieceUseLowerLegColliders;
                target.onePieceUseFloorCollider = onePieceUseFloorCollider;
                target.onePieceUseFrontRootRotationConstraints = onePieceUseFrontRootRotationConstraints;
                target.onePieceMoveFrontRootsTowardUpperLeg = onePieceMoveFrontRootsTowardUpperLeg;
                onePiecePhysBone?.ApplyTo(target.onePiecePhysBone);
                target.enableLongCoatRefine = enableLongCoatRefine;
                target.longCoatPreset = longCoatPreset;
                target.enableLongCoatBoneExtension = enableLongCoatBoneExtension;
                target.longCoatBoneExtensionMode = longCoatBoneExtensionMode;
                target.longCoatTargetBoneCount = longCoatTargetBoneCount;
                target.longCoatShortSkirtUsePrependedRootsOnly = longCoatShortSkirtUsePrependedRootsOnly;
                target.longCoatRootHeightOffsetMultiplier = longCoatRootHeightOffsetMultiplier;
                target.longCoatHipWeightReduction = longCoatHipWeightReduction;
                target.longCoatSpineWeightReduction = longCoatSpineWeightReduction;
                target.longCoatMoveFrontBonesOutward = longCoatMoveFrontBonesOutward;
                target.longCoatUseRotationConstraints = longCoatUseRotationConstraints;
                target.longCoatUseFrontRootRotationConstraints = longCoatUseFrontRootRotationConstraints;
                target.longCoatMoveConstrainedRootsTowardUpperLeg = longCoatMoveConstrainedRootsTowardUpperLeg;
                target.longCoatAimFrontLimitsForward = longCoatAimFrontLimitsForward;
                target.longCoatMatchOnePiece = longCoatMatchOnePiece;
                target.longCoatUseUpperLegColliders = longCoatUseUpperLegColliders;
                target.longCoatUseLowerLegColliders = longCoatUseLowerLegColliders;
                target.longCoatUseFloorCollider = longCoatUseFloorCollider;
                longCoatPhysBone?.ApplyTo(target.longCoatPhysBone);
                target.constraintMode = constraintMode;
                target.addGeneratedDynamicsToVqtKeepList = addGeneratedDynamicsToVqtKeepList;
                target.verboseLog = verboseLog;
            }

            public void ApplyReferencesTo(YMVRoidSkirtRefine target, Transform avatarRoot)
            {
                onePieceBones?.ApplyTo(target.onePieceBones, avatarRoot);
                longCoatBones?.ApplyTo(target.longCoatBones, avatarRoot);
            }
        }

        [Serializable]
        private sealed class BoneTargetsSnapshot
        {
            public string frontLeft;
            public string frontRight;
            public string sideLeft;
            public string sideRight;
            public string backLeft;
            public string backRight;

            public static BoneTargetsSnapshot From(SkirtRefineBoneTargets source, Transform avatarRoot)
            {
                source ??= new SkirtRefineBoneTargets();
                return new BoneTargetsSnapshot
                {
                    frontLeft = ToPath(avatarRoot, source.frontLeft),
                    frontRight = ToPath(avatarRoot, source.frontRight),
                    sideLeft = ToPath(avatarRoot, source.sideLeft),
                    sideRight = ToPath(avatarRoot, source.sideRight),
                    backLeft = ToPath(avatarRoot, source.backLeft),
                    backRight = ToPath(avatarRoot, source.backRight)
                };
            }

            public void ApplyTo(SkirtRefineBoneTargets target, Transform avatarRoot)
            {
                if (target == null) return;
                target.frontLeft = FromPath(avatarRoot, frontLeft);
                target.frontRight = FromPath(avatarRoot, frontRight);
                target.sideLeft = FromPath(avatarRoot, sideLeft);
                target.sideRight = FromPath(avatarRoot, sideRight);
                target.backLeft = FromPath(avatarRoot, backLeft);
                target.backRight = FromPath(avatarRoot, backRight);
            }

            private static string ToPath(Transform root, Transform target)
            {
                return root != null && target != null
                    ? PreviewCoordinator.BuildRelativePath(root, target)
                    : string.Empty;
            }

            private static Transform FromPath(Transform root, string path)
            {
                if (root == null || string.IsNullOrEmpty(path)) return null;
                return root.Find(path);
            }
        }

        [Serializable]
        private sealed class PhysBoneSnapshot
        {
            public SkirtRefinePhysBoneVersion version;
            public bool ignoreOtherPhysBones;
            public Vector3 endpointPosition;
            public SkirtRefinePhysBoneMultiChildType multiChildType;
            public float pull;
            public AnimationCurve pullCurve;
            public float spring;
            public AnimationCurve springCurve;
            public float gravity;
            public AnimationCurve gravityCurve;
            public float gravityFalloff;
            public AnimationCurve gravityFalloffCurve;
            public SkirtRefinePhysBoneImmobileType immobileType;
            public float immobile;
            public float immobileTipMultiplier;
            public AnimationCurve immobileCurve;
            public bool grabAllowSelf;
            public bool grabAllowOthers;
            public bool poseAllowSelf;
            public bool poseAllowOthers;
            public bool snapToHand;
            public float radius;
            public AnimationCurve radiusCurve;
            public SkirtRefinePhysBoneLimitType limitType;
            public float maxAngle;
            public AnimationCurve maxAngleCurve;
            public float maxYaw;
            public AnimationCurve maxYawCurve;
            public Vector3 limitRotation;
            public SkirtRefinePhysBonePermission allowCollision;
            public DynamicsUsageFlags collisionContentTypes;
            public bool collisionAllowSelf;
            public bool collisionAllowOthers;
            public SkirtRefinePhysBonePermission allowGrabbing;
            public SkirtRefinePhysBonePermission allowPosing;
            public float grabMovement;
            public float maxStretch;
            public AnimationCurve maxStretchCurve;
            public float maxSquish;
            public AnimationCurve maxSquishCurve;
            public float stretchMotion;
            public AnimationCurve stretchMotionCurve;
            public bool isAnimated;
            public bool resetWhenDisabled;
            public string parameter;
            public bool showGizmos;
            public float boneOpacity;
            public float limitOpacity;

            public static PhysBoneSnapshot From(SkirtRefinePhysBoneSettings source)
            {
                source ??= new SkirtRefinePhysBoneSettings();
                return new PhysBoneSnapshot
                {
                    version = source.version,
                    ignoreOtherPhysBones = source.ignoreOtherPhysBones,
                    endpointPosition = source.endpointPosition,
                    multiChildType = source.multiChildType,
                    pull = source.pull,
                    pullCurve = CloneCurve(source.pullCurve),
                    spring = source.spring,
                    springCurve = CloneCurve(source.springCurve),
                    gravity = source.gravity,
                    gravityCurve = CloneCurve(source.gravityCurve),
                    gravityFalloff = source.gravityFalloff,
                    gravityFalloffCurve = CloneCurve(source.gravityFalloffCurve),
                    immobileType = source.immobileType,
                    immobile = source.immobile,
                    immobileTipMultiplier = source.immobileTipMultiplier,
                    immobileCurve = CloneCurve(source.immobileCurve),
                    grabAllowSelf = source.grabAllowSelf,
                    grabAllowOthers = source.grabAllowOthers,
                    poseAllowSelf = source.poseAllowSelf,
                    poseAllowOthers = source.poseAllowOthers,
                    snapToHand = source.snapToHand,
                    radius = source.radius,
                    radiusCurve = CloneCurve(source.radiusCurve),
                    limitType = source.limitType,
                    maxAngle = source.maxAngle,
                    maxAngleCurve = CloneCurve(source.maxAngleCurve),
                    maxYaw = source.maxYaw,
                    maxYawCurve = CloneCurve(source.maxYawCurve),
                    limitRotation = source.limitRotation,
                    allowCollision = source.allowCollision,
                    collisionContentTypes = source.collisionContentTypes,
                    collisionAllowSelf = source.collisionAllowSelf,
                    collisionAllowOthers = source.collisionAllowOthers,
                    allowGrabbing = source.allowGrabbing,
                    allowPosing = source.allowPosing,
                    grabMovement = source.grabMovement,
                    maxStretch = source.maxStretch,
                    maxStretchCurve = CloneCurve(source.maxStretchCurve),
                    maxSquish = source.maxSquish,
                    maxSquishCurve = CloneCurve(source.maxSquishCurve),
                    stretchMotion = source.stretchMotion,
                    stretchMotionCurve = CloneCurve(source.stretchMotionCurve),
                    isAnimated = source.isAnimated,
                    resetWhenDisabled = source.resetWhenDisabled,
                    parameter = source.parameter,
                    showGizmos = source.showGizmos,
                    boneOpacity = source.boneOpacity,
                    limitOpacity = source.limitOpacity
                };
            }

            public void ApplyTo(SkirtRefinePhysBoneSettings target)
            {
                if (target == null) return;
                target.version = version;
                target.ignoreOtherPhysBones = ignoreOtherPhysBones;
                target.endpointPosition = endpointPosition;
                target.multiChildType = multiChildType;
                target.pull = pull;
                target.pullCurve = CloneCurve(pullCurve);
                target.spring = spring;
                target.springCurve = CloneCurve(springCurve);
                target.gravity = gravity;
                target.gravityCurve = CloneCurve(gravityCurve);
                target.gravityFalloff = gravityFalloff;
                target.gravityFalloffCurve = CloneCurve(gravityFalloffCurve);
                target.immobileType = immobileType;
                target.immobile = immobile;
                target.immobileTipMultiplier = immobileTipMultiplier;
                target.immobileCurve = CloneCurve(immobileCurve);
                target.grabAllowSelf = grabAllowSelf;
                target.grabAllowOthers = grabAllowOthers;
                target.poseAllowSelf = poseAllowSelf;
                target.poseAllowOthers = poseAllowOthers;
                target.snapToHand = snapToHand;
                target.radius = radius;
                target.radiusCurve = CloneCurve(radiusCurve);
                target.limitType = limitType;
                target.maxAngle = maxAngle;
                target.maxAngleCurve = CloneCurve(maxAngleCurve);
                target.maxYaw = maxYaw;
                target.maxYawCurve = CloneCurve(maxYawCurve);
                target.limitRotation = limitRotation;
                target.allowCollision = allowCollision;
                target.collisionContentTypes = collisionContentTypes;
                target.collisionAllowSelf = collisionAllowSelf;
                target.collisionAllowOthers = collisionAllowOthers;
                target.allowGrabbing = allowGrabbing;
                target.allowPosing = allowPosing;
                target.grabMovement = grabMovement;
                target.maxStretch = maxStretch;
                target.maxStretchCurve = CloneCurve(maxStretchCurve);
                target.maxSquish = maxSquish;
                target.maxSquishCurve = CloneCurve(maxSquishCurve);
                target.stretchMotion = stretchMotion;
                target.stretchMotionCurve = CloneCurve(stretchMotionCurve);
                target.isAnimated = isAnimated;
                target.resetWhenDisabled = resetWhenDisabled;
                target.parameter = parameter;
                target.showGizmos = showGizmos;
                target.boneOpacity = boneOpacity;
                target.limitOpacity = limitOpacity;
            }

            private static AnimationCurve CloneCurve(AnimationCurve curve)
            {
                return curve != null ? new AnimationCurve(curve.keys) : null;
            }
        }
    }
}
