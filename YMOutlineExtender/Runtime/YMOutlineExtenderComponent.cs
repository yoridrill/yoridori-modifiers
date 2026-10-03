using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YoridoriModifiers.OutlineExtender
{
    [Serializable]
    public sealed class OutlineMaterialSelection
    {
        public Material material;
        public bool selected;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Yoridori Modifiers/YM Outline Extender")]
    public sealed class YMOutlineExtenderComponent : MonoBehaviour, IEditorOnly
    {
        public bool enableOutlineExtender = true;
        [Range(0f, 2f)] public float outlineWidthMultiplier = 1f;
        // Legacy master switch, migrated to the per-material controls by the Inspector.
        [HideInInspector] public bool enableBoundaryFold = true;
        [HideInInspector] public int boundaryFoldAngleDegrees = 5;
        [HideInInspector] public float boundaryFoldLengthMultiplier = 2f;
        [HideInInspector] public float boundarySupportBandWidthMultiplier = 0.5f;
        [HideInInspector] public bool taperBoundaryEndpoints = true;
        // Legacy serialized value. Filling now follows enableBoundaryFold.
        [HideInInspector] public bool fillBoundaryTransparency = true;
        [HideInInspector] public float boundaryFillDistanceMultiplier = 1f;
        public bool verboseLog;
        [HideInInspector] public bool showMaterials;
        [HideInInspector] public bool showBakeMaterials;
        [HideInInspector] public bool showBoundaryMaterials;
        [HideInInspector] public bool showAdvanced;
        [HideInInspector] public bool isPreviewing;
        public List<OutlineMaterialSelection> materialSelections = new();
        public List<OutlineMaterialSelection> bakeMaterialSelections = new();
        public List<OutlineMaterialSelection> boundaryMaterialSelections = new();
    }
}
