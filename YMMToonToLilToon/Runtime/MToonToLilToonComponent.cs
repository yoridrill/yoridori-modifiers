using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YoridoriModifiers.MToonToLilToon
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Yoridori Modifiers/YM MToon to lilToon")]
    public sealed class MToonToLilToonComponent : MonoBehaviour, IEditorOnly
    {
        public const float DefaultSilhouetteOpacity = 0.8f;
        public const float DefaultSilhouetteBlur = 0.15f;

        public enum FaceShadowMaskType
        {
            Strength = 1,
            Flat = 0,
            Sdf = 2
        }

        public Shader lilToonShader;
        public bool enableFaceShadowTuning;
        public Material faceShadowFaceMaterial;
        public Texture2D faceShadowSdfTexture;
        public FaceShadowMaskType faceShadowMaskType = FaceShadowMaskType.Flat;
        public float shadowStrengthMaskLod;
        public bool enableSilhouetteTransparency;
        public Material silhouetteClothingMaterial;
        public Material silhouetteBodyMaterial;
        public Color silhouetteShadowColor = new(0.35f, 0.25f, 0.3f, 1f);
        [Range(0f, 1f)] public float silhouetteOpacity = DefaultSilhouetteOpacity;
        public bool useSilhouetteRefractionBlur;
        [Range(0f, 1f)] public float silhouetteBlur = DefaultSilhouetteBlur;
        public bool disableShadowReceiveForFace;
        public bool disableRimShadeForFace;
        public bool disableBacklightStrengthForFace;
        public bool overrideBounds;
        public Transform boundsRootBone;
        public Vector3 boundsExtents = Vector3.one;
        public bool overrideAnchor;
        public Transform anchorOverride;
        public bool useToonStandardFallback;
        public LilToonGlobalOverrides globalOverrides = new();
        public bool verboseLog;
        [HideInInspector] public bool showAdvanced;
        public bool isPreviewing;

        [HideInInspector] public int scannedMaterialCount;
        [HideInInspector] public int convertedMaterialCount;
        [HideInInspector] public int skippedMaterialCount;
        [HideInInspector] public List<string> warnings = new();
        [HideInInspector] public List<string> unsupportedProperties = new();

        public void AutoAssignMeshSettingBones()
        {
            var animator = FindHumanoidAnimator();
            if (animator == null) return;

            boundsRootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
            anchorOverride = animator.GetBoneTransform(HumanBodyBones.Chest)
                ?? animator.GetBoneTransform(HumanBodyBones.Head);
        }

        public Transform ResolveAutomaticBoundsRootBone()
        {
            return FindHumanoidAnimator()?.GetBoneTransform(HumanBodyBones.Hips);
        }

        public Transform ResolveAutomaticAnchorOverride()
        {
            var animator = FindHumanoidAnimator();
            if (animator == null) return null;
            return animator.GetBoneTransform(HumanBodyBones.Chest)
                ?? animator.GetBoneTransform(HumanBodyBones.Head);
        }

        private Animator FindHumanoidAnimator()
        {
            var parentAnimators = GetComponentsInParent<Animator>(true);
            for (var i = 0; i < parentAnimators.Length; i++)
            {
                if (parentAnimators[i] != null
                    && parentAnimators[i].avatar != null
                    && parentAnimators[i].avatar.isHuman)
                {
                    return parentAnimators[i];
                }
            }

            var root = transform;
            while (root.parent != null)
            {
                root = root.parent;
            }

            var childAnimators = root.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < childAnimators.Length; i++)
            {
                if (childAnimators[i] != null
                    && childAnimators[i].avatar != null
                    && childAnimators[i].avatar.isHuman)
                {
                    return childAnimators[i];
                }
            }

            return null;
        }
    }
}
