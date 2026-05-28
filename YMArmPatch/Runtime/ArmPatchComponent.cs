using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDKBase;

namespace YoridoriModifiers.ArmPatch
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
    [AddComponentMenu("Yoridori Modifiers/YM Arm Patch")]
    public sealed class ArmPatchComponent : MonoBehaviour, IEditorOnly
    {
        [Header("Shoulder")]
        [Tooltip("Enable shoulder correction.")]
        public bool enableShoulderFix = false;

        [Tooltip("Shared shoulder position offset. Right side is mirrored internally.")]
        public Vector3 shoulderPositionOffset = Vector3.zero;

        [Tooltip("Shared shoulder rotation offset. Right side is mirrored internally.")]
        public Vector3 shoulderEulerOffset = Vector3.zero;

        [Tooltip("Upper arm roll axis. Default is X.")]
        public TwistAxis upperArmRollAxis = TwistAxis.X;

        [Tooltip("How strongly the roll axis follows the original upper arm.")]
        [Range(0f, 1f)]
        public float upperArmRollWeight = 1f;

        [Header("Forearm")]
        [Tooltip("Enable forearm correction.")]
        [FormerlySerializedAs("enableWristFix")]
        public bool enableForearmFix = false;

        [Tooltip("Forearm scale at elbow side.")]
        public Vector3 forearmElbowScale = Vector3.one;

        [Tooltip("Forearm scale at wrist side.")]
        public Vector3 forearmWristScale = Vector3.one;

        [Tooltip("Elbow roll offset for twist aim.")]
        [Range(-90f, 90f)]
        [FormerlySerializedAs("forearmRootRollOffset")]
        public float forearmElbowRollOffset = 0f;

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
        public bool enableThumbFix = false;

        [Tooltip("Shared thumb rotation offset. Right side is mirrored internally.")]
        public Vector3 thumbEulerOffset = Vector3.zero;

        [Tooltip("Constraint implementation used by this tool.")]
        public ConstraintMode constraintMode = ConstraintMode.VRChatConstraints;

        [Tooltip("When to run this tool relative to Modular Avatar.")]
        public PatchBuildOrder buildOrder = PatchBuildOrder.AfterModularAvatar;

        [Tooltip("Enable verbose logging.")]
        public bool verboseLog = false;

        [SerializeField, HideInInspector]
        private int armPatchSerializedVersion = 0;

        [SerializeField, HideInInspector, FormerlySerializedAs("wristThicknessScale")]
        private float forearmThicknessRootScale = 1f;

        [SerializeField, HideInInspector]
        private float forearmThicknessTipScale = 1f;

        [SerializeField, HideInInspector, FormerlySerializedAs("wristWidthScale")]
        private float forearmWidthRootScale = 1f;

        [SerializeField, HideInInspector]
        private float forearmWidthTipScale = 1f;

        private const int CurrentSerializedVersion = 1;

        public void MigrateSerializedValuesIfNeeded()
        {
            if (armPatchSerializedVersion >= CurrentSerializedVersion) return;

            forearmElbowScale = BuildLegacyScaleVector(forearmRollAxis, forearmThicknessRootScale, forearmWidthRootScale);
            forearmWristScale = BuildLegacyScaleVector(forearmRollAxis, forearmThicknessTipScale, forearmWidthTipScale);
            armPatchSerializedVersion = CurrentSerializedVersion;
        }

        private void OnValidate()
        {
            MigrateSerializedValuesIfNeeded();
        }

        private static Vector3 BuildLegacyScaleVector(TwistAxis twistAxis, float thicknessScale, float widthScale)
        {
            switch (twistAxis)
            {
                case TwistAxis.X:
                    return new Vector3(1f, thicknessScale, widthScale);
                case TwistAxis.Y:
                    return new Vector3(widthScale, 1f, thicknessScale);
                case TwistAxis.Z:
                    return new Vector3(widthScale, thicknessScale, 1f);
                default:
                    return new Vector3(1f, thicknessScale, widthScale);
            }
        }
    }
}
