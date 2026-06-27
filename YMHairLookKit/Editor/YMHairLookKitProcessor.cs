using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using YoridoriModifiers.Core.Editor;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.HairLookKit
{
    internal static class YMHairLookKitProcessor
    {
        private const string ToolName = "YM Hair Look Kit";

        internal enum ProcessRoute
        {
            Preview,
            Build
        }

        internal static void ApplyOnBuild(
            YMHairLookKitComponent component,
            BuildContext buildContext = null,
            Action<string> onProgress = null,
            ProcessRoute route = ProcessRoute.Build,
            bool runMToonPreviewStage = false)
        {
            if (component == null) return;
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            if (root == null) return;

            component.warnings = new List<string>();
            component.errors = new List<string>();

            if (runMToonPreviewStage)
            {
                RunMToonPreviewStage(root, onProgress);
            }

            var currentMaterials = HairLookTargetResolver.CollectCurrentMaterials(root);
            var errors = HairLookValidator.BuildErrors(component, currentMaterials);
            component.errors = errors;

            var warnings = new List<string>();
            var selectedConfiguredMaterials = component.hairSelections
                .Where(s => s != null && s.selected && s.material != null)
                .Select(s => s.material)
                .ToList();
            var canonicalByMergeMaterial = component.enableHairMerge
                ? HairLookTargetResolver.ResolveMaterialCanonicalMap(selectedConfiguredMaterials, currentMaterials)
                : new Dictionary<Material, Material>();
            var selectedMergeMaterials = canonicalByMergeMaterial.Keys.ToHashSet();
            var representative = HairLookTargetResolver.ResolveCurrentMaterialReference(component.representativeHairMaterialOverride, currentMaterials)
                ?? selectedMergeMaterials.FirstOrDefault();

            var mergedResults = new List<HairMaterialMerger.Result>();
            var enableOutlineCorrection = HairLookFeatureApplier.ShouldApplyOutlineCorrection(component, errors, route, root);
            var enableMergedOutline = enableOutlineCorrection
                && component.outlineHairTargetMode == YMHairLookKitComponent.HairTargetMode.MergedHair
                && component.enableHairMerge;

            if (component.enableHairOutlineCorrection && route == ProcessRoute.Build && HairLookFeatureApplier.IsMobileBuildTarget(root))
            {
                warnings.Add("hair outline correction skipped for mobile build target");
            }

            if (component.enableHairMerge && selectedMergeMaterials.Count > 0)
            {
                onProgress?.Invoke("Merging hair materials...");
                var result = HairMaterialMerger.MergeAvatar(
                    root,
                    selectedMergeMaterials,
                    canonicalByMergeMaterial,
                    representative,
                    enableMergedOutline,
                    component.hairTipOutlineWidth,
                    component.hairTipRange,
                    component.hairAtlasMaxSize,
                    warnings,
                    component.verboseLog,
                    onProgress,
                    buildContext);
                if (result != null) mergedResults.Add(result);
            }

            currentMaterials = HairLookTargetResolver.CollectCurrentMaterials(root);
            var mutationContext = new HairLookMaterialMutationContext(
                root,
                buildContext,
                mergedResults.Where(result => result?.mergedMaterial != null).Select(result => result.mergedMaterial));
            HairLookFeatureApplier.ApplyEyebrow(component, currentMaterials, mergedResults, errors, onProgress, mutationContext);
            HairLookFeatureApplier.ApplyFakeShadow(component, root, currentMaterials, mergedResults, errors, warnings, onProgress, buildContext, mutationContext);
            HairLookFeatureApplier.ApplyOutline(component, root, currentMaterials, errors, route, onProgress, buildContext, mutationContext);

            component.warnings = warnings;
            if (component.verboseLog)
            {
                LogUtility.Info(ToolName, $"warnings={component.warnings.Count}, errors={component.errors.Count}", component);
            }
        }

        private static void RunMToonPreviewStage(GameObject root, Action<string> onProgress)
        {
            onProgress?.Invoke("Converting MToon materials...");
            MToonToLilToonPreviewBridge.ApplyForChainedPreview(root, onProgress);
        }
    }
}
