using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YoridoriModifiers.HairLookKit
{
    internal static class YMHairLookKitDefaults
    {
        internal static List<Material> GetRendererMaterials(Component component)
        {
            var root = component != null ? YoridoriModifiers.Core.Editor.PreviewCoordinator.FindAvatarRoot(component.gameObject) : null;
            if (root == null && component != null) root = component.gameObject;
            return GetRendererMaterials(root);
        }

        internal static List<Material> GetRendererMaterials(GameObject root)
        {
            if (root == null) return new List<Material>();
            return root.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer != null ? renderer.sharedMaterials : Array.Empty<Material>())
                .Where(material => material != null)
                .Distinct()
                .ToList();
        }

        internal static void ScanHairSelections(YMHairLookKitComponent component)
        {
            if (component == null) return;
            Undo.RecordObject(component, "Scan YM Hair Look Kit Materials");
            component.hairSelections = HairMaterialSelector.BuildDefaultSelections(GetRendererMaterials(component));
            if (component.representativeHairMaterialOverride == null)
            {
                component.representativeHairMaterialOverride = component.hairSelections.FirstOrDefault(s => s != null && s.selected)?.material;
            }
            EditorUtility.SetDirty(component);
        }

        internal static void ApplyVRoidDefaults(YMHairLookKitComponent component, GameObject avatarRoot)
        {
            if (component == null) return;
            Undo.RecordObject(component, "Configure YM Hair Look Kit");
            component.enableHairMerge = true;
            component.enableEyebrowStencil = true;
            component.enableFakeShadow = true;
            component.fakeShadowCompositeMode = YMHairLookKitComponent.FakeShadowCompositeMode.Multiply;
            component.enableHairOutlineCorrection = true;
            component.hairSelections = HairMaterialSelector.BuildDefaultSelections(GetRendererMaterials(avatarRoot));
            component.representativeHairMaterialOverride = component.hairSelections.FirstOrDefault(s => s != null && s.selected)?.material;
            var materials = GetRendererMaterials(avatarRoot);
            var defaultHair = DetectDefaultHairMaterial(materials);
            var defaultFace = DetectDefaultFaceMaterial(materials);
            component.eyebrowHairTargetMode = YMHairLookKitComponent.HairTargetMode.MergedHair;
            component.fakeShadowHairTargetMode = YMHairLookKitComponent.HairTargetMode.MergedHair;
            component.outlineHairTargetMode = YMHairLookKitComponent.HairTargetMode.MergedHair;
            component.eyebrowHairMaterial = defaultHair;
            component.fakeShadowHairMaterial = defaultHair;
            component.outlineHairMaterial = defaultHair;
            component.eyebrowFaceMaterial = defaultFace;
            component.fakeShadowFaceMaterial = defaultFace;
            component.eyebrowMaterial = DetectDefaultEyebrowMaterial(materials);
            EditorUtility.SetDirty(component);
        }

        internal static Material DetectDefaultHairMaterial(IReadOnlyList<Material> materials)
        {
            if (materials == null) return null;
            return materials.FirstOrDefault(m => IsHairName(m != null ? m.name : null));
        }

        internal static Material DetectDefaultFaceMaterial(IReadOnlyList<Material> materials)
        {
            if (materials == null) return null;
            var face = materials.FirstOrDefault(m => m != null
                && m.name.IndexOf("FACE", StringComparison.OrdinalIgnoreCase) >= 0
                && m.name.IndexOf("SKIN", StringComparison.OrdinalIgnoreCase) >= 0);
            if (face != null) return face;

            face = materials.FirstOrDefault(m => m != null
                && (m.name.IndexOf("FACE", StringComparison.OrdinalIgnoreCase) >= 0
                    || m.name.IndexOf("顔", StringComparison.OrdinalIgnoreCase) >= 0));
            if (face != null) return face;

            return materials.FirstOrDefault();
        }

        internal static Material DetectDefaultEyebrowMaterial(IReadOnlyList<Material> materials)
        {
            if (materials == null) return null;
            return materials.FirstOrDefault(m => m != null
                && (m.name.IndexOf("brow", StringComparison.OrdinalIgnoreCase) >= 0
                    || m.name.IndexOf("mayu", StringComparison.OrdinalIgnoreCase) >= 0
                    || m.name.IndexOf("眉", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        internal static bool IsHairName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("back", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
}
