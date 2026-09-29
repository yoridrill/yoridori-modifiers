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
        public bool verboseLog;
        [HideInInspector] public bool showMaterials;
        [HideInInspector] public bool showAdvanced;
        [HideInInspector] public bool isPreviewing;
        public List<OutlineMaterialSelection> materialSelections = new();
    }
}
