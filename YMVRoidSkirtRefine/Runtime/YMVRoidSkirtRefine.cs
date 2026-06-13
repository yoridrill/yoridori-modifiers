using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.Dynamics;
using VRC.SDKBase;

namespace YoridoriModifiers.VRoidSkirtRefine
{
    public enum OnePiecePreset
    {
        ShortSkirtLight,
        ShortSkirtHeavy,
        LongSkirtLight,
        LongSkirtHeavy,
        MatchLongCoat,
    }

    public enum LongCoatPreset
    {
        ShortSkirtLight,
        ShortSkirtHeavy,
        LongSkirtLight,
        LongSkirtHeavy,
        OpenFront,
        MatchOnePiece,
    }

    public enum BoneExtensionMode
    {
        None,
        AppendToTip,
        PrependToRoot,
    }

    public enum SkirtRefinePhysBoneLimitType
    {
        None,
        Angle,
        Hinge,
        Polar,
    }

    public enum SkirtRefinePhysBonePermission
    {
        False,
        True,
        Other,
    }

    public enum SkirtRefinePhysBoneMultiChildType
    {
        Ignore,
        First,
        Average,
    }

    public enum SkirtRefinePhysBoneImmobileType
    {
        AllMotion,
        World,
    }

    public enum SkirtRefinePhysBoneVersion
    {
        Version1_0,
        Version1_1,
    }

    public enum SkirtRefineConstraintMode
    {
        VRChatConstraints,
        UnityConstraints
    }

    [Serializable]
    public sealed class SkirtRefinePhysBoneSettings
    {
        public SkirtRefinePhysBoneVersion version = SkirtRefinePhysBoneVersion.Version1_1;
        public List<Transform> ignoreTransforms = new List<Transform>();
        public bool ignoreOtherPhysBones = true;
        public Vector3 endpointPosition = Vector3.zero;
        public SkirtRefinePhysBoneMultiChildType multiChildType = SkirtRefinePhysBoneMultiChildType.Ignore;
        public float pull = 0.2f;
        public AnimationCurve pullCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        public float spring = 0.2f;
        public AnimationCurve springCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        public float gravity = 0.0f;
        public AnimationCurve gravityCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        public float gravityFalloff = 0.0f;
        public AnimationCurve gravityFalloffCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        public SkirtRefinePhysBoneImmobileType immobileType = SkirtRefinePhysBoneImmobileType.World;
        public float immobile = 0.0f;
        public float immobileTipMultiplier = 1.0f;
        public AnimationCurve immobileCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        public bool grabAllowSelf = false;
        public bool grabAllowOthers = false;
        public bool poseAllowSelf = false;
        public bool poseAllowOthers = false;
        public bool snapToHand = false;
        public float radius = 0.05f;
        public AnimationCurve radiusCurve = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);
        public List<VRCPhysBoneColliderBase> colliders = new List<VRCPhysBoneColliderBase>();
        public SkirtRefinePhysBoneLimitType limitType = SkirtRefinePhysBoneLimitType.None;
        public float maxAngle = 45.0f;
        public AnimationCurve maxAngleCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        public float maxYaw = 0.0f;
        public AnimationCurve maxYawCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        public Vector3 limitRotation = Vector3.zero;
        public SkirtRefinePhysBonePermission allowCollision = SkirtRefinePhysBonePermission.True;
        public DynamicsUsageFlags collisionContentTypes = DynamicsUsageFlags.Everything;
        public bool collisionAllowSelf = true;
        public bool collisionAllowOthers = true;
        public SkirtRefinePhysBonePermission allowGrabbing = SkirtRefinePhysBonePermission.False;
        public SkirtRefinePhysBonePermission allowPosing = SkirtRefinePhysBonePermission.False;
        public float grabMovement = 0.0f;
        public float maxStretch = 0.0f;
        public AnimationCurve maxStretchCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        public float maxSquish = 0.0f;
        public AnimationCurve maxSquishCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        public float stretchMotion = 0.0f;
        public AnimationCurve stretchMotionCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        public bool isAnimated = false;
        public bool resetWhenDisabled = true;
        public string parameter = string.Empty;
        public bool showGizmos = true;
        public float boneOpacity = 0.2f;
        public float limitOpacity = 0.2f;
    }

    [Serializable]
    public sealed class SkirtRefineBoneTargets
    {
        public Transform frontLeft;
        public Transform frontRight;
        public Transform sideLeft;
        public Transform sideRight;
        public Transform backLeft;
        public Transform backRight;

        public bool HasMissingBone()
        {
            return frontLeft == null
                || frontRight == null
                || sideLeft == null
                || sideRight == null
                || backLeft == null
                || backRight == null;
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Yoridori Modifiers/YM VRoid Skirt Refine")]
    public sealed class YMVRoidSkirtRefine : MonoBehaviour, IEditorOnly
    {
        [Tooltip("Enable one-piece and skirt hem refinement.")]
        [FormerlySerializedAs("enableSkirtRefine")]
        public bool enableOnePieceRefine = false;

        [Tooltip("One-piece refinement preset.")]
        public OnePiecePreset onePiecePreset = OnePiecePreset.ShortSkirtLight;

        [Tooltip("Enable one-piece skirt bone extension.")]
        public bool enableOnePieceBoneExtension = true;

        [Tooltip("How one-piece skirt bones are extended. One-piece uses Append To Tip.")]
        public BoneExtensionMode onePieceBoneExtensionMode = BoneExtensionMode.AppendToTip;

        [Tooltip("Target number of one-piece skirt chain bones.")]
        [Range(3, 12)]
        public int onePieceTargetBoneCount = 6;

        [Tooltip("Raise one-piece root bones along the root-side extension direction. 0 keeps original positions, 1 approaches the estimated convergence point.")]
        [Range(0.0f, 1.0f)]
        public float onePieceRootHeightOffsetMultiplier = 0.0f;

        [Tooltip("Auto-detected or manually assigned one-piece skirt bones.")]
        public SkirtRefineBoneTargets onePieceBones = new SkirtRefineBoneTargets();

        [Tooltip("Reduce Hip weight on vertices affected by one-piece skirt reweighting.")]
        [Range(0.0f, 1.0f)]
        public float onePieceHipWeightReduction = 0.5f;

        [Tooltip("Bind one-piece skirt vertices to the refined long coat swing bones and remove the original one-piece swing bones.")]
        public bool onePieceMatchLongCoat = false;

        [Tooltip("Add generated capsule PhysBone colliders under both UpperLeg bones for one-piece refinement.")]
        public bool onePieceUseUpperLegColliders = true;

        [Tooltip("Add generated capsule PhysBone colliders under both LowerLeg bones for one-piece refinement.")]
        public bool onePieceUseLowerLegColliders = true;

        [Tooltip("Add a generated floor PhysBone collider under the avatar root for one-piece refinement.")]
        public bool onePieceUseFloorCollider = false;

        [Tooltip("Use rotation constraints on the front root bones for one-piece refinement.")]
        public bool onePieceUseFrontRootRotationConstraints = false;

        [Tooltip("Move front one-piece root bones toward UpperLeg when front root rotation constraints are used.")]
        public bool onePieceMoveFrontRootsTowardUpperLeg = true;

        [Tooltip("PhysBone settings applied to the generated one-piece unified root.")]
        public SkirtRefinePhysBoneSettings onePiecePhysBone = new SkirtRefinePhysBoneSettings();

        [Tooltip("Enable long coat hem refinement.")]
        [FormerlySerializedAs("enableFrontOpenCoatSupport")]
        public bool enableLongCoatRefine = false;

        [Tooltip("Long coat refinement preset.")]
        public LongCoatPreset longCoatPreset = LongCoatPreset.LongSkirtHeavy;

        [Tooltip("Enable long coat skirt bone extension.")]
        public bool enableLongCoatBoneExtension = true;

        [Tooltip("How long coat skirt bones are extended. Long coat uses Prepend To Root.")]
        public BoneExtensionMode longCoatBoneExtensionMode = BoneExtensionMode.PrependToRoot;

        [Tooltip("Target number of long coat skirt chain bones.")]
        [Range(3, 12)]
        public int longCoatTargetBoneCount = 6;

        [Tooltip("For short skirt presets, keep only prepended root bones and remove the original long coat bones.")]
        public bool longCoatShortSkirtUsePrependedRootsOnly = false;

        [Tooltip("Raise generated long coat root bones toward UpperLeg. -1 keeps the extended height, 0 aligns to UpperLeg height, and positive values add generated UpperLeg collider radius.")]
        [Range(-1.0f, 2.0f)]
        public float longCoatRootHeightOffsetMultiplier = 1.0f;

        [Tooltip("Reduce Hip weight on vertices affected by long coat reweighting.")]
        [Range(0.0f, 1.0f)]
        public float longCoatHipWeightReduction = 0.5f;

        [Tooltip("Reduce Spine weight on vertices affected by long coat reweighting.")]
        [Range(0.0f, 1.0f)]
        public float longCoatSpineWeightReduction = 0.0f;

        [Tooltip("Move front long coat swing bones slightly outward and backward. Intended for open-front coats.")]
        public bool longCoatMoveFrontBonesOutward = false;

        [Tooltip("Use rotation constraints instead of a unified PhysBone root for long coat root stages.")]
        public bool longCoatUseRotationConstraints = false;

        [Tooltip("Use rotation constraints on the front root bones for long coat refinement.")]
        public bool longCoatUseFrontRootRotationConstraints = false;

        [Tooltip("Move constrained long coat root bones toward UpperLeg when rotation constraints are used.")]
        public bool longCoatMoveConstrainedRootsTowardUpperLeg = true;

        [Tooltip("Aim front per-chain PhysBone limits forward when long coat rotation constraint mode creates front per-chain PhysBones.")]
        public bool longCoatAimFrontLimitsForward = false;

        [Tooltip("Auto-detected or manually assigned long coat skirt bones.")]
        public SkirtRefineBoneTargets longCoatBones = new SkirtRefineBoneTargets();

        [Tooltip("Bind long coat vertices to the refined one-piece swing bones and remove the original long coat swing bones.")]
        public bool longCoatMatchOnePiece = false;

        [Tooltip("Add generated capsule PhysBone colliders under both UpperLeg bones for long coat refinement.")]
        public bool longCoatUseUpperLegColliders = false;

        [Tooltip("Add generated capsule PhysBone colliders under both LowerLeg bones for long coat refinement.")]
        public bool longCoatUseLowerLegColliders = true;

        [Tooltip("PhysBone settings applied to the generated long coat unified root.")]
        public SkirtRefinePhysBoneSettings longCoatPhysBone = new SkirtRefinePhysBoneSettings();

        [Tooltip("Constraint implementation used by this tool.")]
        public SkirtRefineConstraintMode constraintMode = SkirtRefineConstraintMode.VRChatConstraints;

        [Tooltip("Add generated PhysBones and PhysBoneColliders to VRCQuestTools Avatar Dynamics keep lists when available.")]
        public bool addGeneratedDynamicsToVqtKeepList = false;

        [Tooltip("Enable verbose build logging.")]
        public bool verboseLog = false;

        [HideInInspector]
        public string autoEnableRefinesObjectId = string.Empty;

        private void Reset()
        {
            enableOnePieceRefine = false;
            onePiecePreset = OnePiecePreset.ShortSkirtLight;
            enableOnePieceBoneExtension = false;
            onePieceRootHeightOffsetMultiplier = 0.4f;
            onePieceHipWeightReduction = 0.5f;
            onePieceMatchLongCoat = false;
            onePieceUseUpperLegColliders = true;
            onePieceUseLowerLegColliders = false;
            onePieceUseFloorCollider = false;
            onePieceUseFrontRootRotationConstraints = true;
            onePieceMoveFrontRootsTowardUpperLeg = false;
            ApplyShortLightPhysBoneDefaults(onePiecePhysBone);

            enableLongCoatRefine = false;
            longCoatPreset = LongCoatPreset.LongSkirtHeavy;
            longCoatMatchOnePiece = false;
            enableLongCoatBoneExtension = true;
            longCoatShortSkirtUsePrependedRootsOnly = false;
            longCoatRootHeightOffsetMultiplier = 1.0f;
            longCoatHipWeightReduction = 0.5f;
            longCoatSpineWeightReduction = 0.0f;
            longCoatMoveFrontBonesOutward = false;
            longCoatUseRotationConstraints = true;
            longCoatUseFrontRootRotationConstraints = false;
            longCoatMoveConstrainedRootsTowardUpperLeg = true;
            longCoatAimFrontLimitsForward = true;
            longCoatUseUpperLegColliders = false;
            longCoatUseLowerLegColliders = true;
            ApplyShortHeavyPhysBoneDefaults(longCoatPhysBone);
            longCoatPhysBone.radius = 0.05f;
            longCoatPhysBone.radiusCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        }

        private static void ApplyShortLightPhysBoneDefaults(SkirtRefinePhysBoneSettings settings)
        {
            if (settings == null) return;

            settings.version = SkirtRefinePhysBoneVersion.Version1_1;
            settings.ignoreOtherPhysBones = true;
            settings.endpointPosition = Vector3.zero;
            settings.multiChildType = SkirtRefinePhysBoneMultiChildType.Ignore;
            settings.pull = 0.1f;
            settings.pullCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settings.spring = 0.6f;
            settings.springCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settings.gravity = 0.0f;
            settings.gravityCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settings.gravityFalloff = 0.0f;
            settings.gravityFalloffCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settings.immobileType = SkirtRefinePhysBoneImmobileType.World;
            settings.immobile = 0.8f;
            settings.immobileTipMultiplier = 0.7f;
            settings.immobileCurve = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.7f);
            settings.radius = 0.05f;
            settings.radiusCurve = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);
            settings.limitType = SkirtRefinePhysBoneLimitType.Hinge;
            settings.maxAngle = 45.0f;
            settings.maxAngleCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settings.maxYaw = 0.0f;
            settings.maxYawCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settings.limitRotation = new Vector3(-45.0f, 0.0f, 0.0f);
            settings.allowCollision = SkirtRefinePhysBonePermission.False;
            settings.collisionContentTypes = DynamicsUsageFlags.Everything;
            settings.collisionAllowSelf = true;
            settings.collisionAllowOthers = true;
            settings.allowGrabbing = SkirtRefinePhysBonePermission.Other;
            settings.allowPosing = SkirtRefinePhysBonePermission.False;
            settings.grabAllowSelf = true;
            settings.grabAllowOthers = false;
            settings.poseAllowSelf = false;
            settings.poseAllowOthers = false;
            settings.snapToHand = false;
            settings.grabMovement = 0.0f;
            settings.maxStretch = 0.0f;
            settings.maxStretchCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settings.maxSquish = 0.0f;
            settings.maxSquishCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settings.stretchMotion = 0.0f;
            settings.stretchMotionCurve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            settings.isAnimated = false;
            settings.resetWhenDisabled = true;
            settings.parameter = string.Empty;
            settings.showGizmos = true;
            settings.boneOpacity = 0.2f;
            settings.limitOpacity = 0.2f;
        }

        private static void ApplyShortHeavyPhysBoneDefaults(SkirtRefinePhysBoneSettings settings)
        {
            ApplyShortLightPhysBoneDefaults(settings);
            if (settings == null) return;

            settings.pull = 0.18f;
            settings.spring = 0.45f;
            settings.gravity = 0.25f;
            settings.gravityFalloff = 0.35f;
            settings.immobile = 0.9f;
        }

        private static AnimationCurve CreateConvexRadiusCurve()
        {
            return new AnimationCurve(
                new Keyframe(0.0f, 0.0f, 0.0f, 0.0f),
                new Keyframe(0.5f, 0.25f, 1.0f, 1.0f),
                new Keyframe(1.0f, 1.0f, 2.0f, 2.0f));
        }
    }
}
