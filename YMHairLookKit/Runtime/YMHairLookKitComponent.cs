using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YoridoriModifiers.HairLookKit
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Yoridori Modifiers/YM Hair Look Kit")]
    public sealed class YMHairLookKitComponent : MonoBehaviour, IEditorOnly
    {
        public enum HairTargetMode
        {
            MergedHair = 0,
            Material = 1
        }

        public enum FakeShadowCompositeMode
        {
            Multiply = 0,
            Darken = 1,
        }

        public bool enableHairMerge;
        public List<HairMaterialSelection> hairSelections = new();
        public Material representativeHairMaterialOverride;
        public int hairAtlasMaxSize = 2048;

        public bool enableEyebrowStencil;
        public HairTargetMode eyebrowHairTargetMode = HairTargetMode.MergedHair;
        public Material eyebrowHairMaterial;
        public Material eyebrowFaceMaterial;
        public Material eyebrowMaterial;

        public bool enableFakeShadow;
        public HairTargetMode fakeShadowHairTargetMode = HairTargetMode.MergedHair;
        public Material fakeShadowHairMaterial;
        public Material fakeShadowFaceMaterial;
        public Vector3 fakeShadowDirection = new(1f, 4f, 2f);
        public float fakeShadowOffset = 0.005f;
        public FakeShadowCompositeMode fakeShadowCompositeMode = FakeShadowCompositeMode.Multiply;

        public bool enableHairOutlineCorrection;
        public HairTargetMode outlineHairTargetMode = HairTargetMode.MergedHair;
        public Material outlineHairMaterial;
        [Range(0f, 1f)] public float hairTipOutlineWidth = 0.2f;
        [Range(0f, 1f)] public float hairTipRange = 0.3f;

        public bool verboseLog;
        [HideInInspector] public bool showAdvanced;
        [HideInInspector] public bool showHairMaterials;
        public bool isPreviewing;

        [HideInInspector] public List<string> warnings = new();
        [HideInInspector] public List<string> errors = new();
    }
}
