using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDKBase;

namespace NDMFVRoidArmPatch
{
    public enum TwistAxis
    {
        X,
        Y,
        Z
    }

    public enum ForearmTwistBoneType
    {
        None,
        AllTwist,
        SkinOnly
    }

    public enum ForearmTwistBoneCount
    {
        Count0 = 0,
        Count4 = 4,
        Count6 = 6,
        Count8 = 8
    }

    public enum ConstraintMode
    {
        VRChatConstraints,
        UnityConstraints
    }

    public enum PatchBuildOrder
    {
        AfterModularAvatar,
        BeforeModularAvatar
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("yoridrill/NDMF VRoid Arm Patch")]
    public sealed class NDMFVRoidArmPatchComponent : MonoBehaviour, IEditorOnly
    {
        [Header("Shoulder")]
        [Tooltip("Enable shoulder correction.")]
        public bool enableShoulderFix = true;

        [Tooltip("Shared shoulder position offset. Right side is mirrored internally.")]
        public Vector3 shoulderPositionOffset = Vector3.zero;

        [Tooltip("Shared shoulder rotation offset. Right side is mirrored internally.")]
        public Vector3 shoulderEulerOffset = new Vector3(0f, 0f, -10f);

        [Tooltip("Upper arm roll axis. Default is X.")]
        public TwistAxis upperArmRollAxis = TwistAxis.X;

        [Tooltip("How strongly the roll axis follows the original upper arm.")]
        [Range(0f, 1f)]
        public float upperArmRollWeight = 1f;

        [Header("Forearm")]
        [Tooltip("Enable forearm correction.")]
        [FormerlySerializedAs("enableWristFix")]
        public bool enableForearmFix = true;

        [Tooltip("Forearm thickness scale at root side.")]
        [FormerlySerializedAs("wristThicknessScale")]
        public float forearmThicknessRootScale = 0.8f;

        [Tooltip("Forearm thickness scale at tip side.")]
        public float forearmThicknessTipScale = 1.1f;

        [Tooltip("Forearm width scale at root side.")]
        [FormerlySerializedAs("wristWidthScale")]
        public float forearmWidthRootScale = 0.95f;

        [Tooltip("Forearm width scale at tip side.")]
        public float forearmWidthTipScale = 0.9f;

        [Tooltip("Root roll offset for twist aim.")]
        [Range(-90f, 90f)]
        public float forearmRootRollOffset = 0f;

        [Tooltip("Forearm roll axis. Default is X.")]
        [FormerlySerializedAs("wristRollAxis")]
        public TwistAxis forearmRollAxis = TwistAxis.X;

        [Tooltip("Forearm pitch axis used for twist extractor up vector.")]
        [FormerlySerializedAs("wristPitchAxis")]
        public TwistAxis forearmPitchAxis = TwistAxis.Z;

        [Tooltip("How strongly forearm roll follows the hand.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("wristRollWeight")]
        public float forearmRollWeight = 1f;


        [Tooltip("Legacy type setting. Twist target is controlled by Twist Target UI.")]
        [FormerlySerializedAs("wristTwistBoneType")]
        public ForearmTwistBoneType forearmTwistBoneType = ForearmTwistBoneType.None;

        [Tooltip("Number of forearm twist bones to use.")]
        [FormerlySerializedAs("wristTwistBoneCount")]
        public ForearmTwistBoneCount forearmTwistBoneCount = ForearmTwistBoneCount.Count0;

        [Tooltip("Twist target material name. Auto means AllTwist behavior.")]
        [FormerlySerializedAs("wristSkinMaterialName")]
        public string forearmSkinMaterialName = "Auto";

        [Header("Thumb")]
        [Tooltip("Enable thumb correction.")]
        public bool enableThumbFix = true;

        [Tooltip("Shared thumb rotation offset. Right side is mirrored internally.")]
        public Vector3 thumbEulerOffset = new Vector3(10f, 0f, 20f);

        [Tooltip("Constraint implementation used by this tool.")]
        public ConstraintMode constraintMode = ConstraintMode.VRChatConstraints;

        [Tooltip("When to run this tool relative to Modular Avatar.")]
        public PatchBuildOrder buildOrder = PatchBuildOrder.AfterModularAvatar;

        [Tooltip("Enable verbose logging.")]
        public bool verboseLog = false;
    }
}
