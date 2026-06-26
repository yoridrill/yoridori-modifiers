using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.HairLookKit
{
    internal static class HairLookValidator
    {
        internal static List<string> BuildErrors(YMHairLookKitComponent component, IReadOnlyList<Material> currentMaterials, bool english = false)
        {
            var errors = new List<string>();
            if (component == null) return errors;
            var allowMToonWithConverter = HairLookTargetResolver.HasMToonToLilToonComponent(component);

            var mergedUnavailable = !component.enableHairMerge;
            var mergedContainsEyebrowHair = HairLookTargetResolver.IsSelectedForMerge(component, component.eyebrowHairMaterial);
            var mergedContainsFakeShadowHair = HairLookTargetResolver.IsSelectedForMerge(component, component.fakeShadowHairMaterial);
            var mergedContainsOutlineHair = HairLookTargetResolver.IsSelectedForMerge(component, component.outlineHairMaterial);

            if (component.enableEyebrowStencil)
            {
                var unsupportedLilToonTargets = new List<string>();
                if (component.eyebrowHairTargetMode == YMHairLookKitComponent.HairTargetMode.MergedHair && mergedUnavailable)
                {
                    errors.Add($"Eyebrow: {T("結合した髪マテリアルを選んでいますが、髪マテリアル結合がオフです。", "Merged Hair Material is selected, but Merge Hair Materials is disabled.", english)}");
                }
                if (component.eyebrowHairTargetMode == YMHairLookKitComponent.HairTargetMode.MergedHair && !HairLookTargetResolver.AreMergeTargetsSupported(component, currentMaterials, allowMToonWithConverter))
                {
                    unsupportedLilToonTargets.Add(T("髪", "Hair", english));
                }
                if (component.eyebrowHairTargetMode == YMHairLookKitComponent.HairTargetMode.Material && mergedContainsEyebrowHair)
                {
                    errors.Add($"Eyebrow: {T("選んだ髪マテリアルは髪マテリアル結合で結合されます。", "The selected hair material will be merged by Merge Hair Materials.", english)}");
                }
                if (component.eyebrowHairTargetMode == YMHairLookKitComponent.HairTargetMode.Material
                    && !HairLookTargetResolver.IsResolvedSupported(component.eyebrowHairMaterial, currentMaterials, allowMToonWithConverter))
                {
                    unsupportedLilToonTargets.Add(T("髪", "Hair", english));
                }
                if (!HairLookTargetResolver.IsResolvedSupported(component.eyebrowFaceMaterial, currentMaterials, allowMToonWithConverter))
                {
                    unsupportedLilToonTargets.Add(T("顔", "Face", english));
                }
                if (!HairLookTargetResolver.IsResolvedSupported(component.eyebrowMaterial, currentMaterials, allowMToonWithConverter))
                {
                    unsupportedLilToonTargets.Add(T("眉", "Eyebrow", english));
                }
                AddUnsupportedLilToonError(errors, "Eyebrow", unsupportedLilToonTargets, english);
            }

            if (component.enableFakeShadow)
            {
                var unsupportedLilToonTargets = new List<string>();
                if (component.fakeShadowHairTargetMode == YMHairLookKitComponent.HairTargetMode.MergedHair && mergedUnavailable)
                {
                    errors.Add($"FakeShadow: {T("結合した髪マテリアルを選んでいますが、髪マテリアル結合がオフです。", "Merged Hair Material is selected, but Merge Hair Materials is disabled.", english)}");
                }
                if (component.fakeShadowHairTargetMode == YMHairLookKitComponent.HairTargetMode.MergedHair && !HairLookTargetResolver.AreMergeTargetsSupported(component, currentMaterials, allowMToonWithConverter))
                {
                    unsupportedLilToonTargets.Add(T("髪", "Hair", english));
                }
                if (component.fakeShadowHairTargetMode == YMHairLookKitComponent.HairTargetMode.Material && mergedContainsFakeShadowHair)
                {
                    errors.Add($"FakeShadow: {T("選んだ髪マテリアルは髪マテリアル結合で結合されます。", "The selected hair material will be merged by Merge Hair Materials.", english)}");
                }
                if (component.fakeShadowHairTargetMode == YMHairLookKitComponent.HairTargetMode.Material
                    && !HairLookTargetResolver.IsResolvedSupported(component.fakeShadowHairMaterial, currentMaterials, allowMToonWithConverter))
                {
                    unsupportedLilToonTargets.Add(T("髪", "Hair", english));
                }
                if (!HairLookTargetResolver.IsResolvedSupported(component.fakeShadowFaceMaterial, currentMaterials, allowMToonWithConverter))
                {
                    unsupportedLilToonTargets.Add(T("顔", "Face", english));
                }
                AddUnsupportedLilToonError(errors, "FakeShadow", unsupportedLilToonTargets, english);
            }

            if (component.enableHairOutlineCorrection)
            {
                var unsupportedLilToonTargets = new List<string>();
                if (component.outlineHairTargetMode == YMHairLookKitComponent.HairTargetMode.MergedHair && mergedUnavailable)
                {
                    errors.Add($"Outline: {T("結合した髪マテリアルを選んでいますが、髪マテリアル結合がオフです。", "Merged Hair Material is selected, but Merge Hair Materials is disabled.", english)}");
                }
                if (component.outlineHairTargetMode == YMHairLookKitComponent.HairTargetMode.MergedHair && !HairLookTargetResolver.AreMergeTargetsSupported(component, currentMaterials, allowMToonWithConverter))
                {
                    unsupportedLilToonTargets.Add(T("髪", "Hair", english));
                }
                if (component.outlineHairTargetMode == YMHairLookKitComponent.HairTargetMode.Material && mergedContainsOutlineHair)
                {
                    errors.Add($"Outline: {T("選んだ髪マテリアルは髪マテリアル結合で結合されます。", "The selected hair material will be merged by Merge Hair Materials.", english)}");
                }
                if (component.outlineHairTargetMode == YMHairLookKitComponent.HairTargetMode.Material && !HairLookTargetResolver.IsResolvedSupported(component.outlineHairMaterial, currentMaterials, allowMToonWithConverter))
                {
                    unsupportedLilToonTargets.Add(T("髪", "Hair", english));
                }
                AddUnsupportedLilToonError(errors, "Outline", unsupportedLilToonTargets, english);
            }

            return errors;
        }

        internal static bool HasMixedMergeSettings(YMHairLookKitComponent component)
        {
            var selected = component?.hairSelections?
                .Where(s => s != null && s.selected && s.material != null)
                .Select(s => s.material)
                .Distinct()
                .ToList();
            if (selected == null || selected.Count <= 1) return false;

            var shaderName = NormalizeShaderName(selected[0]);
            var renderType = RenderTypeResolver.ResolveFromMaterial(selected[0]);
            var cullMode = CullModeResolver.ResolveFromMaterial(selected[0]);
            return selected.Any(m =>
                NormalizeShaderName(m) != shaderName
                || RenderTypeResolver.ResolveFromMaterial(m) != renderType
                || CullModeResolver.ResolveFromMaterial(m) != cullMode);
        }

        private static void AddUnsupportedLilToonError(List<string> errors, string category, IEnumerable<string> targets, bool english)
        {
            var distinctTargets = targets?
                .Where(target => !string.IsNullOrEmpty(target))
                .Distinct()
                .ToList();
            if (distinctTargets == null || distinctTargets.Count == 0) return;

            errors.Add(english
                ? $"{category}: Materials that cannot be processed as lilToon are selected for {JoinEnglishList(distinctTargets)}."
                : $"{category}: lilToonとして処理できないマテリアルが{JoinJapaneseList(distinctTargets)}で選ばれています。");
        }

        private static string JoinJapaneseList(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0) return string.Empty;
            if (values.Count == 1) return values[0];
            return string.Join("と", values);
        }

        private static string JoinEnglishList(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0) return string.Empty;
            if (values.Count == 1) return values[0];
            if (values.Count == 2) return $"{values[0]} and {values[1]}";
            return $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}";
        }

        private static string T(string ja, string en, bool english)
        {
            return english ? en : ja;
        }

        private static string NormalizeShaderName(Material material)
        {
            return material != null && material.shader != null ? material.shader.name : string.Empty;
        }
    }
}
