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
    }
}
