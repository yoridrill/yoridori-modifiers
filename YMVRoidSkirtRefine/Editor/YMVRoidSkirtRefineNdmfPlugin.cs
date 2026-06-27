using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone;
using VRC.SDK3.Dynamics.PhysBone.Components;
using YoridoriModifiers.Core.Editor;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(YoridoriModifiers.VRoidSkirtRefine.YMVRoidSkirtRefineNdmfPlugin))]

namespace YoridoriModifiers.VRoidSkirtRefine
{
    public sealed class YMVRoidSkirtRefineNdmfPlugin : Plugin<YMVRoidSkirtRefineNdmfPlugin>
    {
        private const string ToolName = "YM VRoid Skirt Refine";
        private const string QualifiedPluginName = "jp.yoridrill.ym-vroid-skirt-refine";
        private const string OnePieceUnifiedRootName = "YM_VRoidSkirtRefine_OnePieceRoot";
        private const string LongCoatUnifiedRootName = "YM_VRoidSkirtRefine_LongCoatRoot";
        private const float LongCoatFrontOutwardOffset = 0.04f;
        private const float LongCoatFrontBackwardOffset = 0.015f;
        private const float LongCoatRootLiftLastRootFactor = 0.35f;
        private const float LongCoatUpperSkirtCoverageVirtualSpanFactor = 1.0f;
        private const float GeneratedUpperLegColliderRadiusRatio = 0.24f;
        private const float GeneratedLowerLegColliderRadiusRatio = 0.16f;
        private const float GeneratedFallbackLegColliderHeight = 0.5f;
        private const int NearestJudgmentWeightFalloffPower = 8;
        private const int TopologyWeightJaggednessCorrectionIterations = 5;
        private const float TopologyWeightJaggednessCorrectionStrength = 1.0f;
        private const float TopologyWeightJaggednessThreshold = 0.01f;
        private const int TopologyWeightDirectionalSearchDepth = 10;
        private const float GeometricTargetSourceMinimumWeight = 0.001f;
        private const int QuestPhysBoneComponentLimit = 8;
        private const int QuestPhysBoneColliderLimit = 16;

        public override string QualifiedName => QualifiedPluginName;
        public override string DisplayName => ToolName;

        internal static PreviewBuildResult BuildForPreviewWithResult(GameObject avatarRoot, YMVRoidSkirtRefine component)
        {
            if (avatarRoot == null || component == null) return PreviewBuildResult.Empty;
            var results = Build(avatarRoot, component, null);
            return new PreviewBuildResult(
                SelectGeneratedPhysBones(results, RefineKind.OnePiece),
                SelectGeneratedPhysBones(results, RefineKind.LongCoat));
        }

        private static VRCPhysBone[] SelectGeneratedPhysBones(List<RefineResult> results, RefineKind kind)
        {
            return results != null
                ? results
                    .Where(result => result != null && result.Kind == kind)
                    .SelectMany(result => result.GeneratedPhysBones)
                    .Where(physBone => physBone != null)
                    .Distinct()
                    .ToArray()
                : Array.Empty<VRCPhysBone>();
        }

        internal static void ApplyPreviewPhysBoneSettings(
            VRCPhysBone physBone,
            SkirtRefinePhysBoneSettings settings)
        {
            if (physBone == null) return;

            var multiChildType = physBone.transform != null
                && (physBone.transform.name == OnePieceUnifiedRootName || physBone.transform.name == LongCoatUnifiedRootName)
                    ? SkirtRefinePhysBoneMultiChildType.Ignore
                    : SkirtRefinePhysBoneMultiChildType.First;
            ApplyPhysBoneSettings(
                physBone,
                settings,
                physBone.colliders,
                physBone.endpointPosition,
                multiChildType,
                physBone.ignoreTransforms);
            physBone.configHasUpdated = true;
            physBone.collidersHaveUpdated = true;
        }

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("jp.yoridrill.ym-arm-patch")
                .BeforePlugin("jp.yoridrill.ym-mesh-trimmer")
                .BeforePlugin("jp.yoridrill.ym-mtoon-to-liltoon")
                .BeforePlugin("com.github.kurotu.vrc-quest-tools")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Build YM VRoid Skirt Refine", Execute);
        }

        private static void Execute(BuildContext context)
        {
            if (context == null || context.AvatarRootObject == null) return;

            var components = context.AvatarRootObject.GetComponentsInChildren<YMVRoidSkirtRefine>(true);
            if (components == null || components.Length == 0) return;

            var component = SelectPreferredComponent(components, context.AvatarRootObject);
            if (component == null)
            {
                RemoveComponents(components);
                return;
            }

            try
            {
                ErrorReport.WithContextObject(component, () =>
                {
                    Build(context.AvatarRootObject, component, context);
                });
            }
            finally
            {
                RemoveComponents(components);
            }
        }

        private static YMVRoidSkirtRefine SelectPreferredComponent(YMVRoidSkirtRefine[] components, GameObject avatarRoot)
        {
            var rootTransform = avatarRoot != null ? avatarRoot.transform : null;
            return components
                .Where(c => c != null)
                .OrderByDescending(c => c.transform == rootTransform)
                .ThenBy(c => GetDepthFromRoot(c.transform, rootTransform))
                .FirstOrDefault();
        }

        private static List<RefineResult> Build(GameObject avatarRoot, YMVRoidSkirtRefine component, BuildContext context)
        {
            if (avatarRoot == null || component == null) return new List<RefineResult>();
            UnpackPrefabInstanceForBuild(avatarRoot, component);
            var refineResults = new List<RefineResult>();

            var onePieceMatchesLongCoat = component.enableOnePieceRefine && component.onePieceMatchLongCoat;
            var longCoatMatchesOnePiece = component.enableLongCoatRefine && component.longCoatMatchOnePiece;
            if ((onePieceMatchesLongCoat && longCoatMatchesOnePiece)
                || (onePieceMatchesLongCoat && !component.enableLongCoatRefine)
                || (longCoatMatchesOnePiece && !component.enableOnePieceRefine))
            {
                Debug.LogWarning($"[{ToolName}] Match target is missing. Skirt refine matching skipped.");
                return refineResults;
            }

            if (onePieceMatchesLongCoat)
            {
                var longCoatResult = BuildLongCoatRefine(avatarRoot, component, context);
                refineResults.Add(longCoatResult);
                MatchOnePieceToLongCoat(avatarRoot, component, longCoatResult, context);
            }
            else if (longCoatMatchesOnePiece)
            {
                var onePieceResult = BuildOnePieceRefine(avatarRoot, component, context);
                refineResults.Add(onePieceResult);
                MatchLongCoatToOnePiece(avatarRoot, component, onePieceResult, context);
            }
            else
            {
                if (component.enableOnePieceRefine)
                {
                    refineResults.Add(BuildOnePieceRefine(avatarRoot, component, context));
                }

                if (component.enableLongCoatRefine)
                {
                    refineResults.Add(BuildLongCoatRefine(avatarRoot, component, context));
                }
            }

            TryAddGeneratedDynamicsToVqtKeepList(avatarRoot, component, refineResults);

            if (component.verboseLog)
            {
                Debug.Log(
                    $"[{ToolName}] Build pass finished. " +
                    $"onePiece={component.enableOnePieceRefine}, longCoat={component.enableLongCoatRefine}");
            }

            return refineResults;
        }

        private static void TryAddGeneratedDynamicsToVqtKeepList(
            GameObject avatarRoot,
            YMVRoidSkirtRefine component,
            List<RefineResult> refineResults)
        {
            if (avatarRoot == null || component == null || !component.addGeneratedDynamicsToVqtKeepList) return;

            if (!TryFindVqtAvatarConverterSettings(avatarRoot, out var settings))
            {
                VerboseWarning(component, "VQT was not found. Generated dynamics were not added to the VQT keep list.");
                return;
            }

            var generatedPhysBones = refineResults != null
                ? refineResults.SelectMany(r => r != null ? r.GeneratedPhysBones : Enumerable.Empty<VRCPhysBone>())
                    .Where(pb => pb != null)
                    .Distinct()
                    .Cast<Component>()
                    .ToList()
                : new List<Component>();
            var generatedColliders = refineResults != null
                ? refineResults.SelectMany(r => r != null ? r.GeneratedPhysBoneColliders : Enumerable.Empty<VRCPhysBoneColliderBase>())
                    .Where(c => c != null)
                    .Distinct()
                    .Cast<Component>()
                    .ToList()
                : new List<Component>();
            var removedPhysBones = refineResults != null
                ? refineResults.SelectMany(r => r != null ? r.RemovedPhysBones : Enumerable.Empty<VRCPhysBone>())
                    .Where(pb => pb != null)
                    .Distinct()
                    .Cast<Component>()
                    .ToList()
                : new List<Component>();
            var removedColliders = refineResults != null
                ? refineResults.SelectMany(r => r != null ? r.RemovedPhysBoneColliders : Enumerable.Empty<VRCPhysBoneColliderBase>())
                    .Where(c => c != null)
                    .Distinct()
                    .Cast<Component>()
                    .ToList()
                : new List<Component>();

            var changed = RemoveObjectsFromArrayField(settings, "physBonesToKeep", removedPhysBones);
            changed |= RemoveObjectsFromArrayField(settings, "physBoneCollidersToKeep", removedColliders);

            var additionalPhysBoneCount = CountMissingArrayItems(settings, "physBonesToKeep", generatedPhysBones);
            var additionalColliderCount = CountMissingArrayItems(settings, "physBoneCollidersToKeep", generatedColliders);
            var totalPhysBoneCount = CountObjectArrayField(settings, "physBonesToKeep") + additionalPhysBoneCount;
            var totalColliderCount = CountObjectArrayField(settings, "physBoneCollidersToKeep") + additionalColliderCount;
            if (totalPhysBoneCount > QuestPhysBoneComponentLimit || totalColliderCount > QuestPhysBoneColliderLimit)
            {
                VerboseWarning(component, "VQT keep list limits would be exceeded. Generated dynamics were not added to the VQT keep list.");
                return;
            }

            changed |= AddObjectsToArrayField(settings, "physBonesToKeep", generatedPhysBones);
            changed |= AddObjectsToArrayField(settings, "physBoneCollidersToKeep", generatedColliders);
            if (changed && component.verboseLog)
            {
                Debug.Log(
                    $"[{ToolName}] Added generated dynamics to VQT keep list. " +
                    $"physBones={generatedPhysBones.Count}, colliders={generatedColliders.Count}");
            }
        }

        private static bool TryFindVqtAvatarConverterSettings(GameObject avatarRoot, out Component settings)
        {
            settings = null;
            if (avatarRoot == null) return false;

            foreach (var component in avatarRoot.GetComponents<Component>())
            {
                if (component == null) continue;
                if (component.GetType().FullName != "KRT.VRCQuestTools.Components.AvatarConverterSettings") continue;
                settings = component;
                return true;
            }

            return false;
        }

        private static void VerboseWarning(YMVRoidSkirtRefine component, string message)
        {
            if (component == null || !component.verboseLog) return;
            Debug.LogWarning($"[{ToolName}] {message}", component);
        }

        private static int CountObjectArrayField(Component component, string fieldName)
        {
            if (component == null) return 0;
            var field = component.GetType().GetField(fieldName);
            return field != null && field.GetValue(component) is Array array
                ? array.Cast<object>().OfType<Component>().Count(c => c != null)
                : 0;
        }

        private static int CountMissingArrayItems(Component component, string fieldName, List<Component> additions)
        {
            if (component == null || additions == null || additions.Count == 0) return 0;
            var field = component.GetType().GetField(fieldName);
            if (field == null) return 0;

            var existing = field.GetValue(component) as Array;
            var existingObjects = existing != null
                ? existing.Cast<object>().OfType<Component>().ToList()
                : new List<Component>();
            return additions.Count(addition => addition != null && !existingObjects.Contains(addition));
        }

        private static bool AddObjectsToArrayField(Component component, string fieldName, List<Component> additions)
        {
            if (component == null || additions == null || additions.Count == 0) return false;
            var field = component.GetType().GetField(fieldName);
            if (field == null) return false;

            var elementType = field.FieldType.GetElementType();
            if (elementType == null) return false;

            var existing = field.GetValue(component) as Array;
            var objects = existing != null
                ? existing.Cast<object>().OfType<Component>().Where(c => c != null).Cast<object>().ToList()
                : new List<object>();
            var changed = false;
            foreach (var addition in additions)
            {
                if (addition == null || !elementType.IsAssignableFrom(addition.GetType())) continue;
                if (objects.Contains(addition)) continue;
                objects.Add(addition);
                changed = true;
            }

            if (!changed) return false;

            var next = Array.CreateInstance(elementType, objects.Count);
            for (var i = 0; i < objects.Count; i++)
            {
                next.SetValue(objects[i], i);
            }

            field.SetValue(component, next);
            EditorUtility.SetDirty(component);
            return true;
        }

        private static bool RemoveObjectsFromArrayField(Component component, string fieldName, List<Component> removals)
        {
            if (component == null) return false;
            var field = component.GetType().GetField(fieldName);
            if (field == null) return false;

            var elementType = field.FieldType.GetElementType();
            if (elementType == null) return false;

            var existing = field.GetValue(component) as Array;
            if (existing == null) return false;

            var removalSet = removals != null
                ? new HashSet<Component>(removals.Where(c => c != null))
                : new HashSet<Component>();
            var objects = existing
                .Cast<object>()
                .OfType<Component>()
                .Where(c => c != null && !removalSet.Contains(c))
                .Cast<object>()
                .ToList();

            if (objects.Count == existing.Length) return false;

            var next = Array.CreateInstance(elementType, objects.Count);
            for (var i = 0; i < objects.Count; i++)
            {
                next.SetValue(objects[i], i);
            }

            field.SetValue(component, next);
            EditorUtility.SetDirty(component);
            return true;
        }

        private static RefineResult BuildOnePieceRefine(GameObject avatarRoot, YMVRoidSkirtRefine component, BuildContext context)
        {
            var animator = avatarRoot.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogWarning($"[{ToolName}] Humanoid Animator not found. One-piece refine skipped.");
                return RefineResult.Empty;
            }

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
            {
                Debug.LogWarning($"[{ToolName}] Hips bone not found. One-piece refine skipped.");
                return RefineResult.Empty;
            }

            var sourceGroups = CreateSourceChainGroups(
                    DetectOnePieceChains(component.onePieceBones),
                    FindOnePieceSiblingSourceChains)
                .Where(g => g.Primary != null && g.Primary.SwingBones.Count > 0)
                .ToList();
            if (sourceGroups.Count == 0)
            {
                Debug.LogWarning($"[{ToolName}] One-piece skirt bones are not assigned. One-piece refine skipped.");
                return RefineResult.Empty;
            }

            var unifiedRoot = CreateOrGetUnifiedRoot(hips);
            var oldToNewBoneMap = new Dictionary<Transform, Transform>();
            var chainReweightInfos = new List<ChainReweightInfo>();
            var processedChains = new List<LongCoatProcessedChain>();
            var generatedPhysBones = new List<VRCPhysBone>();
            var removedPhysBones = new List<VRCPhysBone>();

            var chainsToDelete = new List<OnePieceChain>();
            foreach (var group in sourceGroups)
            {
                var chain = group.Primary;
                if (chain.SwingRoot == null) continue;

                AddManagementBoneRebinds(group.AllChains, chain.SwingRoot, oldToNewBoneMap);
                var sourcePhysBones = FindSourcePhysBones(avatarRoot, group.AllChains);
                removedPhysBones.AddRange(sourcePhysBones);
                RemovePhysBones(sourcePhysBones);
                ReparentSwingRootToUnifiedRoot(chain, unifiedRoot, oldToNewBoneMap, component.verboseLog);

                var finalBones = chain.SwingBones.ToList();
                if (component.enableOnePieceBoneExtension)
                {
                    finalBones = ExtendChainToTargetCount(
                        chain,
                        Mathf.Max(3, component.onePieceTargetBoneCount),
                        component.verboseLog);
                }

                if (component.onePieceUseFrontRootRotationConstraints
                    && component.onePieceMoveFrontRootsTowardUpperLeg > 1e-6f
                    && IsFrontChain(chain))
                {
                    AdjustConstrainedRoots(finalBones, ResolveUpperLegForChain(animator, chain), 1, component.onePieceMoveFrontRootsTowardUpperLeg);
                }

                EnsureLinearChainHierarchy(finalBones);
                AddDistanceReweightInfos(
                    chainReweightInfos,
                    chain.Label,
                    GetSourceSwingBones(group.AllChains),
                    new[] { finalBones });
                processedChains.Add(new LongCoatProcessedChain(chain, finalBones));
                chainsToDelete.AddRange(group.AllChains.Where(c => c != chain));
            }

            ApplyOnePieceRootHeightOffset(
                processedChains,
                component.onePieceRootHeightOffsetMultiplier,
                component.verboseLog);
            foreach (var processed in processedChains)
            {
                NormalizeOnePieceChainRotations(
                    processed.FinalBones,
                    ResolveSkirtCenter(animator),
                    processed.Chain != null ? processed.Chain.Label : "OnePiece",
                    component.verboseLog);
            }

            var vqtSettings = GetVqtSettingsForKeepList(avatarRoot, component);
            var removedPhysBoneColliders = new List<VRCPhysBoneColliderBase>();
            var onePieceColliders = CreateLegCapsuleColliders(
                animator,
                component.onePieceUseUpperLegColliders,
                component.onePieceUseLowerLegColliders,
                component.verboseLog,
                vqtSettings,
                removedPhysBoneColliders);
            AddFloorCollider(
                hips.parent,
                onePieceColliders,
                "YM_VRoidSkirtRefine_OnePieceFloorCollider",
                "one-piece",
                component.onePieceUseFloorCollider,
                component.verboseLog);

            RebindDeletedManagementBones(avatarRoot, oldToNewBoneMap, component.verboseLog);
            ReweightSkirtVertices(
                avatarRoot,
                chainReweightInfos,
                hips,
                component.onePieceHipWeightReduction,
                animator != null ? animator.GetBoneTransform(HumanBodyBones.Spine) : null,
                0.0f,
                component,
                false,
                context);
            DeleteSourceChains(chainsToDelete);

            var rootPhysBone = unifiedRoot.gameObject.GetComponent<VRCPhysBone>();
            if (rootPhysBone == null)
            {
                rootPhysBone = unifiedRoot.gameObject.AddComponent<VRCPhysBone>();
            }
            rootPhysBone.rootTransform = unifiedRoot;
            var onePieceFrontIgnoreTransforms = component.onePieceUseFrontRootRotationConstraints
                ? GetFrontRootIgnoreTransforms(processedChains)
                : null;
            ApplyPhysBoneSettings(
                rootPhysBone,
                component.onePiecePhysBone,
                onePieceColliders,
                component.enableOnePieceBoneExtension
                    ? Vector3.zero
                    : EstimateAverageEndpointPosition(processedChains),
                SkirtRefinePhysBoneMultiChildType.Ignore,
                onePieceFrontIgnoreTransforms);
            generatedPhysBones.Add(rootPhysBone);

            if (component.onePieceUseFrontRootRotationConstraints)
            {
                generatedPhysBones.AddRange(BuildFrontRootRotationConstraintMode(
                    animator,
                    processedChains,
                    component.onePiecePhysBone,
                    onePieceColliders,
                    component.onePieceFrontRootRotationConstraintWeight,
                    ToConstraintImplementationMode(component.constraintMode),
                    false,
                    component.verboseLog));
            }

            if (component.verboseLog)
            {
                Debug.Log($"[{ToolName}] One-piece refine finished. chains={sourceGroups.Count}, root={GetPath(unifiedRoot)}");
            }

            return new RefineResult(RefineKind.OnePiece, processedChains, generatedPhysBones, onePieceColliders, removedPhysBones, removedPhysBoneColliders);
        }

        private static void UnpackPrefabInstanceForBuild(GameObject avatarRoot, YMVRoidSkirtRefine component)
        {
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(component != null ? component.gameObject : avatarRoot)
                ?? PrefabUtility.GetOutermostPrefabInstanceRoot(avatarRoot)
                ?? PrefabUtility.GetNearestPrefabInstanceRoot(component != null ? component.gameObject : avatarRoot)
                ?? PrefabUtility.GetNearestPrefabInstanceRoot(avatarRoot);
            if (root == null || !PrefabUtility.IsPartOfPrefabInstance(root)) return;

            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        private static List<OnePieceChain> DetectOnePieceChains(SkirtRefineBoneTargets targets)
        {
            var result = new List<OnePieceChain>();
            if (targets == null) return result;

            AddChain(result, "Front-Left", targets.frontLeft, "L_SkirtFront");
            AddChain(result, "Front-Right", targets.frontRight, "R_SkirtFront");
            AddChain(result, "Side-Left", targets.sideLeft, "L_SkirtSide");
            AddChain(result, "Side-Right", targets.sideRight, "R_SkirtSide");
            AddChain(result, "Back-Left", targets.backLeft, "L_SkirtBack");
            AddChain(result, "Back-Right", targets.backRight, "R_SkirtBack");
            return result;
        }

        private static void MatchOnePieceToLongCoat(GameObject avatarRoot, YMVRoidSkirtRefine component, RefineResult longCoatResult, BuildContext context)
        {
            if (avatarRoot == null || component == null || longCoatResult == null || longCoatResult.IsEmpty)
            {
                Debug.LogWarning($"[{ToolName}] Long coat match target was not found. One-piece match skipped.");
                return;
            }

            var chains = DetectOnePieceChains(component.onePieceBones)
                .SelectMany(FindOnePieceSiblingSourceChains)
                .Where(c => c != null && c.SwingRoot != null)
                .GroupBy(c => c.SwingRoot)
                .Select(g => g.First())
                .Where(c => c != null && c.SwingBones.Count > 0)
                .ToList();
            var animator = avatarRoot.GetComponentInChildren<Animator>(true);
            var hips = animator != null ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            MatchChainsToTarget(
                avatarRoot,
                chains,
                longCoatResult,
                false,
                null,
                false,
                hips,
                component.onePieceHipWeightReduction,
                animator != null ? animator.GetBoneTransform(HumanBodyBones.Spine) : null,
                0.0f,
                component,
                false,
                context);
        }

        private static void MatchLongCoatToOnePiece(GameObject avatarRoot, YMVRoidSkirtRefine component, RefineResult onePieceResult, BuildContext context)
        {
            if (avatarRoot == null || component == null || onePieceResult == null || onePieceResult.IsEmpty)
            {
                Debug.LogWarning($"[{ToolName}] One-piece match target was not found. Long coat match skipped.");
                return;
            }

            var animator = avatarRoot.GetComponentInChildren<Animator>(true);
            var chains = DetectLongCoatChains(component.longCoatBones)
                .SelectMany(FindLongCoatSiblingSourceChains)
                .Where(c => c != null && c.SwingRoot != null)
                .GroupBy(c => c.SwingRoot)
                .Select(g => g.First())
                .Where(c => c != null && c.SwingBones.Count > 0)
                .ToList();
            var hips = animator != null ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            MatchChainsToTarget(
                avatarRoot,
                chains,
                onePieceResult,
                true,
                animator,
                true,
                hips,
                component.longCoatHipWeightReduction,
                animator != null ? animator.GetBoneTransform(HumanBodyBones.Spine) : null,
                component.longCoatSpineWeightReduction,
                component,
                true,
                context);
        }

        private static void MatchChainsToTarget(
            GameObject avatarRoot,
            List<OnePieceChain> sourceChains,
            RefineResult targetResult,
            bool includeLongCoatLegWeights,
            Animator animator,
            bool includeLongCoatSiblingSourceChains,
            Transform hipBone,
            float hipWeightReduction,
            Transform spineBone,
            float spineWeightReduction,
            YMVRoidSkirtRefine component,
            bool allowCoverageAboveFirstBone,
            BuildContext context)
        {
            if (avatarRoot == null || sourceChains == null || sourceChains.Count == 0 || targetResult == null || targetResult.IsEmpty) return;

            var targetBoneChains = targetResult.GetFinalBoneChains();
            if (targetBoneChains.Count == 0)
            {
                Debug.LogWarning($"[{ToolName}] Matching target bones were not found. Skirt refine matching skipped.");
                return;
            }

            var chainReweightInfos = new List<ChainReweightInfo>();
            var chainsToDelete = new List<OnePieceChain>();
            var sourceBones = new List<Transform>();
            var sourcePhysBones = new List<VRCPhysBone>();
            foreach (var chain in sourceChains)
            {
                var sourceChainGroup = includeLongCoatSiblingSourceChains
                    ? FindLongCoatSiblingSourceChains(chain)
                    : new List<OnePieceChain> { chain };
                sourceBones.AddRange(sourceChainGroup.SelectMany(c => c.SwingBones).Where(b => b != null));
                sourcePhysBones.AddRange(FindSourcePhysBones(avatarRoot, sourceChainGroup));
                chainsToDelete.AddRange(sourceChainGroup);
            }

            sourceBones = sourceBones.Distinct().ToList();
            if (sourceBones.Count == 0)
            {
                Debug.LogWarning($"[{ToolName}] Matching chains were not found. Skirt refine matching skipped.");
                return;
            }

            RemovePhysBones(sourcePhysBones.Distinct().ToList());
            var sharedSourceBones = includeLongCoatLegWeights ? GetLongCoatSharedLegSourceBones(animator) : null;
            AddDistanceReweightInfos(
                chainReweightInfos,
                "Match",
                sourceBones,
                targetBoneChains,
                sharedSourceBones);

            ReweightSkirtVertices(avatarRoot, chainReweightInfos, hipBone, hipWeightReduction, spineBone, spineWeightReduction, component, allowCoverageAboveFirstBone, context);
            foreach (var chain in chainsToDelete
                         .Where(c => c != null && c.SwingRoot != null)
                         .GroupBy(c => c.SwingRoot)
                         .Select(g => g.First()))
            {
                DeleteMatchedSourceChain(chain);
            }

            if (component != null && component.verboseLog)
            {
                Debug.Log($"[{ToolName}] Matched skirt swing bones to target chains. chains={chainReweightInfos.Count}");
            }
        }

        private static List<OnePieceChain> FindLongCoatSiblingSourceChains(OnePieceChain chain)
        {
            var result = new List<OnePieceChain>();
            if (chain == null || chain.SwingRoot == null)
            {
                return result;
            }

            result.Add(chain);
            var partialName = GetLongCoatPartialName(chain.Label);
            var parent = chain.SwingRoot.parent;
            if (parent == null || string.IsNullOrEmpty(partialName))
            {
                return result;
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null || child == chain.SwingRoot) continue;
                if (child.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var swingBones = BuildLinearChain(child);
                if (swingBones.Count == 0) continue;
                result.Add(new OnePieceChain
                {
                    Label = chain.Label,
                    ManagerRoot = null,
                    SwingRoot = child,
                    SwingBones = swingBones,
                    SourcePhysBones = child.GetComponentsInChildren<VRCPhysBone>(true)
                        .Where(pb => pb != null)
                        .ToList()
                });
            }

            return result
                .Where(c => c != null && c.SwingRoot != null)
                .GroupBy(c => c.SwingRoot)
                .Select(g => g.First())
                .ToList();
        }

        private static List<OnePieceChain> FindOnePieceSiblingSourceChains(OnePieceChain chain)
        {
            var result = new List<OnePieceChain>();
            if (chain == null || chain.SwingRoot == null)
            {
                return result;
            }

            result.Add(chain);
            var partialName = GetOnePiecePartialName(chain.Label);
            var sourceRoot = chain.ManagerRoot != null ? chain.ManagerRoot : chain.SwingRoot;
            var parent = sourceRoot.parent;
            if (parent == null || string.IsNullOrEmpty(partialName))
            {
                return result;
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null || child == sourceRoot) continue;
                if (child.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var sibling = CreateOnePieceChainFromTarget(chain.Label, child, partialName);
                if (sibling != null)
                {
                    result.Add(sibling);
                }
            }

            return result
                .Where(c => c != null && c.SwingRoot != null)
                .GroupBy(c => c.SwingRoot)
                .Select(g => g.First())
                .ToList();
        }

        private static List<SourceChainGroup> CreateSourceChainGroups(
            IEnumerable<OnePieceChain> sourceChains,
            Func<OnePieceChain, List<OnePieceChain>> siblingResolver)
        {
            var result = new List<SourceChainGroup>();
            var usedRoots = new HashSet<Transform>();
            if (sourceChains == null) return result;

            foreach (var source in sourceChains)
            {
                if (source == null || source.SwingRoot == null || usedRoots.Contains(source.SwingRoot)) continue;

                var siblings = siblingResolver != null
                    ? siblingResolver(source)
                    : new List<OnePieceChain> { source };
                var groupChains = siblings
                    .Where(c => c != null && c.SwingRoot != null)
                    .GroupBy(c => c.SwingRoot)
                    .Select(g => g.First())
                    .ToList();
                if (groupChains.Count == 0) continue;

                foreach (var chain in groupChains)
                {
                    usedRoots.Add(chain.SwingRoot);
                }

                result.Add(new SourceChainGroup(groupChains[0], groupChains));
            }

            return result;
        }

        private static List<Transform> GetSourceSwingBones(IEnumerable<OnePieceChain> chains)
        {
            return chains == null
                ? new List<Transform>()
                : chains
                    .Where(c => c != null && c.SwingBones != null)
                    .SelectMany(c => c.ManagerRoot != null
                        ? c.SwingBones.Concat(new[] { c.ManagerRoot })
                        : c.SwingBones)
                    .Where(b => b != null)
                    .Distinct()
                    .ToList();
        }

        private static void AddManagementBoneRebinds(
            IEnumerable<OnePieceChain> chains,
            Transform replacement,
            Dictionary<Transform, Transform> oldToNewBoneMap)
        {
            if (chains == null || replacement == null || oldToNewBoneMap == null) return;

            foreach (var chain in chains)
            {
                if (chain == null || chain.ManagerRoot == null) continue;
                oldToNewBoneMap[chain.ManagerRoot] = replacement;
            }
        }

        private static OnePieceChain CreateOnePieceChainFromTarget(string label, Transform target, string partialName)
        {
            if (target == null) return null;
            if (!string.IsNullOrEmpty(partialName)
                && target.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                var descendant = FindDescendantByPartialName(target, partialName);
                if (descendant != null) target = descendant;
            }

            var physBones = target.GetComponentsInChildren<VRCPhysBone>(true);
            var sourcePhysBone = physBones.FirstOrDefault(pb => pb != null);
            var swingRoot = sourcePhysBone != null ? ResolvePhysBoneRoot(sourcePhysBone) : ResolveFirstLinearChild(target);
            if (swingRoot == null) swingRoot = target;

            var manager = target != swingRoot && IsAncestorOf(target, swingRoot) ? target : null;
            var swingBones = BuildLinearChain(swingRoot);
            if (swingBones.Count == 0) return null;

            return new OnePieceChain
            {
                Label = label,
                ManagerRoot = manager,
                SwingRoot = swingRoot,
                SwingBones = swingBones,
                SourcePhysBones = physBones.Where(pb => pb != null).ToList()
            };
        }

        private static string GetOnePiecePartialName(string label)
        {
            if (string.IsNullOrEmpty(label)) return string.Empty;
            var side = label.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0 ? "R" : "L";
            if (label.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0) return $"{side}_SkirtFront";
            if (label.IndexOf("Side", StringComparison.OrdinalIgnoreCase) >= 0) return $"{side}_SkirtSide";
            if (label.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0) return $"{side}_SkirtBack";
            return string.Empty;
        }

        private static string GetLongCoatPartialName(string label)
        {
            if (string.IsNullOrEmpty(label)) return string.Empty;
            var side = label.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0 ? "R" : "L";
            if (label.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0) return $"{side}_CoatSkirtFront";
            if (label.IndexOf("Side", StringComparison.OrdinalIgnoreCase) >= 0) return $"{side}_CoatSkirtSide";
            if (label.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0) return $"{side}_CoatSkirtBack";
            return string.Empty;
        }

        private static RefineResult BuildLongCoatRefine(GameObject avatarRoot, YMVRoidSkirtRefine component, BuildContext context)
        {
            var animator = avatarRoot.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogWarning($"[{ToolName}] Humanoid Animator not found. Long coat refine skipped.");
                return RefineResult.Empty;
            }

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
            {
                Debug.LogWarning($"[{ToolName}] Hips bone not found. Long coat refine skipped.");
                return RefineResult.Empty;
            }

            var sourceGroups = CreateSourceChainGroups(
                    DetectLongCoatChains(component.longCoatBones),
                    FindLongCoatSiblingSourceChains)
                .Where(g => g.Primary != null && g.Primary.SwingBones.Count > 0)
                .ToList();
            if (sourceGroups.Count == 0)
            {
                Debug.LogWarning($"[{ToolName}] Long coat skirt bones are not assigned. Long coat refine skipped.");
                return RefineResult.Empty;
            }

            var useUpperStageRotationConstraints = component.longCoatUseRotationConstraints;
            var useFrontRootRotationConstraints = component.longCoatUseFrontRootRotationConstraints && !useUpperStageRotationConstraints;
            var unifiedRoot = CreateFreshNamedRoot(hips, LongCoatUnifiedRootName);
            var chainsByLabel = sourceGroups
                .GroupBy(g => g.Primary.Label, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Primary, StringComparer.OrdinalIgnoreCase);

            var chainReweightInfos = new List<ChainReweightInfo>();
            var processedChains = new List<LongCoatProcessedChain>();
            var originalChainsToDelete = new List<List<Transform>>();
            var chainsToDelete = new List<OnePieceChain>();
            var generatedPhysBones = new List<VRCPhysBone>();
            var removedPhysBones = new List<VRCPhysBone>();

            foreach (var group in sourceGroups)
            {
                var chain = group.Primary;
                if (chain.SwingRoot == null) continue;

                var sourcePhysBones = FindSourcePhysBones(avatarRoot, group.AllChains);
                removedPhysBones.AddRange(sourcePhysBones);
                RemovePhysBones(sourcePhysBones);

                List<Transform> finalBones;
                if (component.enableLongCoatBoneExtension)
                {
                    finalBones = PrependLongCoatChain(
                        chain,
                        Mathf.Max(3, component.longCoatTargetBoneCount),
                        component.longCoatShortSkirtUsePrependedRootsOnly,
                        component.longCoatMoveConstrainedRootsTowardUpperLeg > 1e-6f
                            && IsFrontChain(chain)
                            && (useUpperStageRotationConstraints || useFrontRootRotationConstraints)
                            ? ResolveUpperLegForChain(animator, chain)
                            : null,
                        useFrontRootRotationConstraints && IsFrontChain(chain) ? 1 : 3,
                        component.longCoatMoveConstrainedRootsTowardUpperLeg,
                        component.longCoatMoveFrontBonesOutward ? avatarRoot.transform : null,
                        component.longCoatMoveFrontBonesOutward
                            ? ResolveFrontOutwardTargetWorldX(chainsByLabel, animator, chain, Mathf.Max(3, component.longCoatTargetBoneCount), component.longCoatShortSkirtUsePrependedRootsOnly)
                            : null,
                        ResolveSideTargetWorldY(chainsByLabel, chain, Mathf.Max(3, component.longCoatTargetBoneCount), component.longCoatShortSkirtUsePrependedRootsOnly),
                        ResolveUpperLegForChain(animator, chain),
                        ResolveLowerLegForChain(animator, chain),
                        component.longCoatRootHeightOffsetMultiplier,
                        component.verboseLog);
                }
                else
                {
                    finalBones = chain.SwingBones.ToList();
                }

                if (unifiedRoot != null && finalBones.Count > 0)
                {
                    finalBones[0].SetParent(unifiedRoot, true);
                }

                EnsureLinearChainHierarchy(finalBones);

                if (useUpperStageRotationConstraints)
                {
                    if (finalBones.Count > 3)
                    {
                        NormalizePhysBoneLimitAxes(finalBones.Skip(3).ToList(), hips.position, chain, component.verboseLog);
                    }
                }
                else
                {
                    NormalizeLongCoatChainRotations(finalBones, hips.position, chain.Label, component.verboseLog);
                    if (useFrontRootRotationConstraints && IsFrontChain(chain))
                    {
                        NormalizePhysBoneLimitAxes(finalBones.Skip(1).ToList(), hips.position, chain, component.verboseLog);
                    }
                }

                AddDistanceReweightInfos(
                    chainReweightInfos,
                    chain.Label,
                    GetSourceSwingBones(group.AllChains),
                    new[] { finalBones },
                    GetLongCoatSharedLegSourceBones(animator));
                processedChains.Add(new LongCoatProcessedChain(chain, finalBones));

                if (component.longCoatShortSkirtUsePrependedRootsOnly)
                {
                    originalChainsToDelete.AddRange(group.AllChains.Select(c => c.SwingBones.ToList()));
                }
                else
                {
                    chainsToDelete.AddRange(group.AllChains.Where(c => c != chain));
                }
            }

            var vqtSettings = GetVqtSettingsForKeepList(avatarRoot, component);
            var removedPhysBoneColliders = new List<VRCPhysBoneColliderBase>();
            var longCoatColliders = CreateLegCapsuleColliders(
                animator,
                component.longCoatUseUpperLegColliders,
                component.longCoatUseLowerLegColliders,
                component.verboseLog,
                vqtSettings,
                removedPhysBoneColliders);
            AddFloorCollider(
                hips.parent,
                longCoatColliders,
                "YM_VRoidSkirtRefine_LongCoatFloorCollider",
                "long coat",
                component.longCoatUseFloorCollider,
                component.verboseLog);

            if (!useUpperStageRotationConstraints)
            {
                var rootPhysBone = unifiedRoot.gameObject.GetComponent<VRCPhysBone>();
                if (rootPhysBone == null)
                {
                    rootPhysBone = unifiedRoot.gameObject.AddComponent<VRCPhysBone>();
                }
                rootPhysBone.rootTransform = unifiedRoot;
                var longCoatFrontIgnoreTransforms = useFrontRootRotationConstraints
                    ? GetFrontRootIgnoreTransforms(processedChains)
                    : null;
                ApplyPhysBoneSettings(
                    rootPhysBone,
                    component.longCoatPhysBone,
                    longCoatColliders,
                    component.longCoatShortSkirtUsePrependedRootsOnly
                        ? EstimateAverageEndpointPosition(processedChains)
                        : Vector3.zero,
                    SkirtRefinePhysBoneMultiChildType.Ignore,
                    longCoatFrontIgnoreTransforms);
                generatedPhysBones.Add(rootPhysBone);
            }
            else
            {
                var rootPhysBone = unifiedRoot.gameObject.GetComponent<VRCPhysBone>();
                if (rootPhysBone != null)
                {
                    Object.DestroyImmediate(rootPhysBone);
                }
            }

            ReweightSkirtVertices(
                avatarRoot,
                chainReweightInfos,
                hips,
                component.longCoatHipWeightReduction,
                animator != null ? animator.GetBoneTransform(HumanBodyBones.Spine) : null,
                component.longCoatSpineWeightReduction,
                component,
                true,
                context);
            foreach (var originalChain in originalChainsToDelete)
            {
                DeleteOriginalChainBones(originalChain);
            }
            DeleteSourceChains(chainsToDelete);

            if (useUpperStageRotationConstraints)
            {
                generatedPhysBones.AddRange(BuildLongCoatRotationConstraintMode(
                    animator,
                    processedChains,
                    component.longCoatPhysBone,
                    longCoatColliders,
                    ToConstraintImplementationMode(component.constraintMode),
                    component.longCoatFrontUpperRotationConstraintWeight,
                    component.longCoatSideUpperRotationConstraintWeight,
                    component.longCoatBackUpperRotationConstraintWeight,
                    component.longCoatAimFrontLimitsForward,
                    component.verboseLog));
            }
            else if (useFrontRootRotationConstraints)
            {
                generatedPhysBones.AddRange(BuildFrontRootRotationConstraintMode(
                    animator,
                    processedChains,
                    component.longCoatPhysBone,
                    longCoatColliders,
                    component.longCoatFrontRootRotationConstraintWeight,
                    ToConstraintImplementationMode(component.constraintMode),
                    component.longCoatAimFrontLimitsForward,
                    component.verboseLog));
            }

            if (component.verboseLog)
            {
                Debug.Log(
                    $"[{ToolName}] Long coat refine finished. chains={sourceGroups.Count}, " +
                    $"unifiedRoot={GetPath(unifiedRoot)}");
            }

            return new RefineResult(RefineKind.LongCoat, processedChains, generatedPhysBones, longCoatColliders, removedPhysBones, removedPhysBoneColliders);
        }

        private static List<OnePieceChain> DetectLongCoatChains(SkirtRefineBoneTargets targets)
        {
            var result = new List<OnePieceChain>();
            if (targets == null) return result;

            AddLongCoatChain(result, "Front-Left", targets.frontLeft, "L_CoatSkirtFront");
            AddLongCoatChain(result, "Front-Right", targets.frontRight, "R_CoatSkirtFront");
            AddLongCoatChain(result, "Side-Left", targets.sideLeft, "L_CoatSkirtSide");
            AddLongCoatChain(result, "Side-Right", targets.sideRight, "R_CoatSkirtSide");
            AddLongCoatChain(result, "Back-Left", targets.backLeft, "L_CoatSkirtBack");
            AddLongCoatChain(result, "Back-Right", targets.backRight, "R_CoatSkirtBack");
            return result;
        }

        private static void AddLongCoatChain(List<OnePieceChain> chains, string label, Transform target, string partialName)
        {
            if (target == null) return;
            if (target.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                var descendant = FindDescendantByPartialName(target, partialName);
                if (descendant != null) target = descendant;
            }

            var physBones = target.GetComponentsInChildren<VRCPhysBone>(true);
            var sourcePhysBone = physBones.FirstOrDefault(pb => pb != null);
            var swingRoot = sourcePhysBone != null ? ResolvePhysBoneRoot(sourcePhysBone) : target;
            if (swingRoot == null) swingRoot = target;

            var swingBones = BuildLinearChain(swingRoot);
            if (swingBones.Count == 0) return;

            chains.Add(new OnePieceChain
            {
                Label = label,
                ManagerRoot = null,
                SwingRoot = swingRoot,
                SwingBones = swingBones,
                SourcePhysBones = physBones.Where(pb => pb != null).ToList()
            });
        }

        private static void AddChain(List<OnePieceChain> chains, string label, Transform target, string partialName)
        {
            if (target == null) return;
            if (target.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                var descendant = FindDescendantByPartialName(target, partialName);
                if (descendant != null) target = descendant;
            }

            var physBones = target.GetComponentsInChildren<VRCPhysBone>(true);
            var sourcePhysBone = physBones.FirstOrDefault(pb => pb != null);
            Transform swingRoot = sourcePhysBone != null ? ResolvePhysBoneRoot(sourcePhysBone) : ResolveFirstLinearChild(target);
            if (swingRoot == null) swingRoot = target;

            var manager = target != swingRoot && IsAncestorOf(target, swingRoot) ? target : null;
            var swingBones = BuildLinearChain(swingRoot);
            if (swingBones.Count == 0) return;

            chains.Add(new OnePieceChain
            {
                Label = label,
                ManagerRoot = manager,
                SwingRoot = swingRoot,
                SwingBones = swingBones,
                SourcePhysBones = physBones.Where(pb => pb != null).ToList()
            });
        }

        private static Transform ResolvePhysBoneRoot(VRCPhysBone physBone)
        {
            if (physBone == null) return null;
            return physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
        }

        private static Transform ResolveFirstLinearChild(Transform target)
        {
            if (target == null) return null;
            if (target.childCount == 1) return target.GetChild(0);
            return target;
        }

        private static List<Transform> BuildLinearChain(Transform root)
        {
            var chain = new List<Transform>();
            var current = root;
            while (current != null)
            {
                chain.Add(current);
                current = SelectNextChainChild(current);
            }

            return chain;
        }

        private static Transform SelectNextChainChild(Transform current)
        {
            if (current == null || current.childCount == 0) return null;
            if (current.childCount == 1) return current.GetChild(0);

            for (var i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                if (child != null && child.name.IndexOf("_end", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child;
                }
            }

            return null;
        }

        private static Transform CreateOrGetUnifiedRoot(Transform hips)
        {
            return CreateFreshNamedRoot(hips, OnePieceUnifiedRootName);
        }

        private static Transform CreateFreshNamedRoot(Transform parent, string rootName)
        {
            var existing = parent.Find(rootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var root = new GameObject(rootName).transform;
            root.SetParent(parent, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            return root;
        }

        private static void ReparentSwingRootToUnifiedRoot(
            OnePieceChain chain,
            Transform unifiedRoot,
            Dictionary<Transform, Transform> oldToNewBoneMap,
            bool verboseLog)
        {
            if (chain == null || chain.SwingRoot == null || unifiedRoot == null) return;

            var manager = chain.ManagerRoot;
            chain.SwingRoot.SetParent(unifiedRoot, true);

            if (manager != null)
            {
                oldToNewBoneMap[manager] = chain.SwingRoot;
                if (manager.childCount == 0)
                {
                    Object.DestroyImmediate(manager.gameObject);
                }
            }

            if (verboseLog)
            {
                Debug.Log($"[{ToolName}] [{chain.Label}] Reparented swing root: {GetPath(chain.SwingRoot)}");
            }
        }

        private static List<Transform> ExtendChainToTargetCount(OnePieceChain chain, int targetCount, bool verboseLog)
        {
            var bones = chain.SwingBones.Where(b => b != null).ToList();
            if (bones.Count == 0 || bones.Count >= targetCount) return bones;

            var direction = EstimateChainTipDirection(bones);

            while (bones.Count < targetCount)
            {
                var last = bones[^1];
                var newBone = new GameObject(BuildAppendedBoneName(chain.Label, bones.Count)).transform;
                newBone.position = last.position + direction;
                newBone.rotation = chain.SwingRoot != null ? chain.SwingRoot.rotation : last.rotation;
                newBone.localScale = chain.SwingRoot != null ? chain.SwingRoot.localScale : Vector3.one;
                newBone.SetParent(last, true);
                bones.Add(newBone);
            }

            if (verboseLog)
            {
                Debug.Log($"[{ToolName}] [{chain.Label}] Extended one-piece chain to {bones.Count} bones.");
            }

            return bones;
        }

        private static Vector3 EstimateChainTipDirection(IReadOnlyList<Transform> bones)
        {
            if (bones == null || bones.Count == 0) return Vector3.down * 0.05f;

            for (var i = bones.Count - 1; i > 0; i--)
            {
                if (bones[i] == null || bones[i - 1] == null) continue;
                var delta = bones[i].position - bones[i - 1].position;
                if (delta.sqrMagnitude > 1e-8f) return delta;
            }

            return bones[^1].TransformVector(Vector3.down * 0.05f);
        }

        private static string BuildAppendedBoneName(string chainLabel, int index)
        {
            return $"YM_OnePiece_{SanitizeNameToken(chainLabel)}_Append{index:00}";
        }

        private static void ApplyOnePieceRootHeightOffset(
            List<LongCoatProcessedChain> processedChains,
            float amount,
            bool verboseLog)
        {
            amount = Mathf.Clamp01(amount);
            if (processedChains == null || processedChains.Count == 0 || amount <= 1e-5f) return;

            var entries = processedChains
                .Where(processed => processed != null && processed.FinalBones != null && processed.FinalBones.Count >= 2)
                .Select(processed =>
                {
                    var root = processed.FinalBones[0];
                    var second = processed.FinalBones[1];
                    if (root == null || second == null) return null;

                    var direction = root.position - second.position;
                    if (direction.sqrMagnitude <= 1e-8f) return null;

                    var third = processed.FinalBones.Count > 2 ? processed.FinalBones[2] : null;
                    return new OnePieceRootHeightEntry(root, second, third, processed.FinalBones, direction.normalized);
                })
                .Where(entry => entry != null)
                .ToList();
            if (entries.Count == 0) return;

            var convergence = EstimateLineConvergence(entries);
            foreach (var entry in entries)
            {
                var projectedDistance = Vector3.Dot(convergence - entry.RootPosition, entry.Direction);
                if (projectedDistance <= 1e-5f) continue;

                var delta = entry.Direction * (projectedDistance * amount);
                entry.Root.position = entry.RootPosition + delta;
                if (entry.Second != null) entry.Second.position = entry.SecondPosition + delta * (2.0f / 3.0f);
                if (entry.Third != null) entry.Third.position = entry.ThirdPosition + delta * (1.0f / 3.0f);
                entry.RestoreLowerBonePositions();
            }

            if (verboseLog)
            {
                Debug.Log($"[{ToolName}] Raised one-piece roots along root-side extension lines. amount={amount:0.###}");
            }
        }

        private static Vector3 EstimateLineConvergence(List<OnePieceRootHeightEntry> entries)
        {
            if (entries == null || entries.Count == 0) return Vector3.zero;

            var a00 = 0.0f;
            var a01 = 0.0f;
            var a02 = 0.0f;
            var a11 = 0.0f;
            var a12 = 0.0f;
            var a22 = 0.0f;
            var b = Vector3.zero;
            foreach (var entry in entries)
            {
                var d = entry.Direction.normalized;
                var m00 = 1.0f - d.x * d.x;
                var m01 = -d.x * d.y;
                var m02 = -d.x * d.z;
                var m11 = 1.0f - d.y * d.y;
                var m12 = -d.y * d.z;
                var m22 = 1.0f - d.z * d.z;
                var p = entry.RootPosition;

                a00 += m00;
                a01 += m01;
                a02 += m02;
                a11 += m11;
                a12 += m12;
                a22 += m22;
                b.x += m00 * p.x + m01 * p.y + m02 * p.z;
                b.y += m01 * p.x + m11 * p.y + m12 * p.z;
                b.z += m02 * p.x + m12 * p.y + m22 * p.z;
            }

            if (TrySolveSymmetric3x3(a00, a01, a02, a11, a12, a22, b, out var result))
            {
                return result;
            }

            var fallback = Vector3.zero;
            foreach (var entry in entries)
            {
                var firstStep = entry.Second != null
                    ? Vector3.Distance(entry.RootPosition, entry.SecondPosition)
                    : 0.05f;
                fallback += entry.RootPosition + entry.Direction * firstStep * 3.0f;
            }

            return fallback / entries.Count;
        }

        private static bool TrySolveSymmetric3x3(
            float a00,
            float a01,
            float a02,
            float a11,
            float a12,
            float a22,
            Vector3 b,
            out Vector3 result)
        {
            var det =
                a00 * (a11 * a22 - a12 * a12)
                - a01 * (a01 * a22 - a12 * a02)
                + a02 * (a01 * a12 - a11 * a02);
            if (Mathf.Abs(det) <= 1e-8f)
            {
                result = Vector3.zero;
                return false;
            }

            result = new Vector3(
                (b.x * (a11 * a22 - a12 * a12) - a01 * (b.y * a22 - a12 * b.z) + a02 * (b.y * a12 - a11 * b.z)) / det,
                (a00 * (b.y * a22 - a12 * b.z) - b.x * (a01 * a22 - a12 * a02) + a02 * (a01 * b.z - b.y * a02)) / det,
                (a00 * (a11 * b.z - b.y * a12) - a01 * (a01 * b.z - b.y * a02) + b.x * (a01 * a12 - a11 * a02)) / det);
            return true;
        }

        private static void NormalizeOnePieceChainRotations(
            IReadOnlyList<Transform> bones,
            Vector3 center,
            string chainLabel,
            bool verboseLog)
        {
            if (bones == null || bones.Count == 0) return;

            var positions = bones
                .Select(b => b != null ? b.position : Vector3.zero)
                .ToArray();

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;

                var direction = ResolveChainDirection(bones, positions, i);
                if (direction.sqrMagnitude <= 1e-8f) continue;

                var upAxis = -direction.normalized;
                var radial = positions[i] - center;
                radial.y = 0f;
                var forwardAxis = radial.sqrMagnitude > 1e-8f
                    ? Vector3.ProjectOnPlane(radial.normalized, upAxis)
                    : Vector3.ProjectOnPlane(bone.forward, upAxis);
                if (forwardAxis.sqrMagnitude <= 1e-8f)
                {
                    forwardAxis = Vector3.ProjectOnPlane(Vector3.forward, upAxis);
                }
                if (forwardAxis.sqrMagnitude <= 1e-8f) continue;

                bone.rotation = Quaternion.LookRotation(forwardAxis.normalized, upAxis);
            }

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;
                bone.position = positions[i];
            }

            if (verboseLog)
            {
                Debug.Log($"[{ToolName}] [{chainLabel}] Normalized one-piece chain rotations for unified PhysBone.");
            }
        }

        private sealed class OnePieceRootHeightEntry
        {
            public readonly Transform Root;
            public readonly Transform Second;
            public readonly Transform Third;
            public readonly Vector3 Direction;
            public readonly Vector3 RootPosition;
            public readonly Vector3 SecondPosition;
            public readonly Vector3 ThirdPosition;
            private readonly List<(Transform Bone, Vector3 Position)> lowerBonePositions;

            public OnePieceRootHeightEntry(
                Transform root,
                Transform second,
                Transform third,
                IReadOnlyList<Transform> finalBones,
                Vector3 direction)
            {
                Root = root;
                Second = second;
                Third = third;
                Direction = direction;
                RootPosition = root != null ? root.position : Vector3.zero;
                SecondPosition = second != null ? second.position : Vector3.zero;
                ThirdPosition = third != null ? third.position : Vector3.zero;
                lowerBonePositions = finalBones != null
                    ? finalBones
                        .Skip(3)
                        .Where(bone => bone != null)
                        .Select(bone => (bone, bone.position))
                        .ToList()
                    : new List<(Transform Bone, Vector3 Position)>();
            }

            public void RestoreLowerBonePositions()
            {
                foreach (var (bone, position) in lowerBonePositions)
                {
                    if (bone == null) continue;
                    bone.position = position;
                }
            }
        }

        private static string SanitizeNameToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Skirt";
            return value.Replace("-", string.Empty).Replace(" ", string.Empty);
        }

        private static List<Transform> PrependLongCoatChain(
            OnePieceChain chain,
            int targetCount,
            bool rootsOnly,
            Transform rotationConstraintSource,
            int rotationConstraintAdjustedCount,
            float rotationConstraintMoveStrength,
            Transform frontOutwardReference,
            float? frontOutwardTargetWorldX,
            float? sideTargetWorldY,
            Transform upperLegHeightReference,
            Transform lowerLegHeightReference,
            float rootHeightOffsetMultiplier,
            bool verboseLog)
        {
            var existingBones = chain.SwingBones.Where(b => b != null).ToList();
            if (existingBones.Count == 0) return existingBones;

            var originalRoot = existingBones[0];
            var originalParent = originalRoot.parent;
            var prependCount = rootsOnly
                ? 3
                : Mathf.Max(0, targetCount - existingBones.Count);
            if (prependCount <= 0) return existingBones;

            var direction = EstimateChainRootDirection(existingBones);
            var step = direction.magnitude > 1e-6f ? direction : Vector3.up * 0.05f;
            var referenceRotation = GetLongCoatPhysBoneReferenceRotation(chain, originalRoot.rotation);
            var prepended = new List<Transform>(prependCount);

            for (var i = 0; i < prependCount; i++)
            {
                var root = new GameObject(BuildPrependedBoneName(originalRoot.name, i)).transform;
                root.position = originalRoot.position - step * (prependCount - i);
                root.rotation = referenceRotation;
                root.localScale = originalRoot.localScale;
                prepended.Add(root);
            }

            AdjustConstrainedRoots(prepended, rotationConstraintSource, rotationConstraintAdjustedCount, rotationConstraintMoveStrength);
            AlignSideLongCoatRootHeight(chain.Label, prepended, sideTargetWorldY);
            LiftLongCoatRootsAboveUpperLeg(prepended, upperLegHeightReference, lowerLegHeightReference, rootHeightOffsetMultiplier);

            prepended[0].SetParent(originalParent, true);
            for (var i = 1; i < prepended.Count; i++)
            {
                prepended[i].SetParent(prepended[i - 1], true);
            }

            originalRoot.SetParent(prepended[^1], true);

            if (verboseLog)
            {
                Debug.Log(
                    $"[{ToolName}] [{chain.Label}] Prepended long coat roots. " +
                    $"existing={existingBones.Count}, prepended={prependCount}, rootsOnly={rootsOnly}");
            }

            if (rootsOnly)
            {
                MoveFrontLongCoatBonesOutward(chain.Label, prepended, frontOutwardReference, frontOutwardTargetWorldX);
                return prepended;
            }

            var finalBones = new List<Transform>(prepended.Count + existingBones.Count);
            finalBones.AddRange(prepended);
            finalBones.AddRange(existingBones);
            MoveFrontLongCoatBonesOutward(chain.Label, finalBones, frontOutwardReference, frontOutwardTargetWorldX);
            return finalBones;
        }

        private static void EnsureLinearChainHierarchy(IReadOnlyList<Transform> bones)
        {
            if (bones == null || bones.Count <= 1) return;

            for (var i = 1; i < bones.Count; i++)
            {
                var bone = bones[i];
                var parent = bones[i - 1];
                if (bone == null || parent == null || bone.parent == parent) continue;
                bone.SetParent(parent, true);
            }
        }

        private static void NormalizeLongCoatChainRotations(
            IReadOnlyList<Transform> bones,
            Vector3 center,
            string chainLabel,
            bool verboseLog)
        {
            if (bones == null || bones.Count == 0) return;

            var positions = bones
                .Select(b => b != null ? b.position : Vector3.zero)
                .ToArray();

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;

                var direction = ResolveChainDirection(bones, positions, i);
                if (direction.sqrMagnitude <= 1e-8f) continue;

                var upAxis = -direction.normalized;
                var radial = positions[i] - center;
                radial.y = 0f;
                var forwardAxis = radial.sqrMagnitude > 1e-8f
                    ? Vector3.ProjectOnPlane(radial.normalized, upAxis)
                    : Vector3.ProjectOnPlane(bone.forward, upAxis);
                if (forwardAxis.sqrMagnitude <= 1e-8f)
                {
                    forwardAxis = Vector3.ProjectOnPlane(Vector3.forward, upAxis);
                }
                if (forwardAxis.sqrMagnitude <= 1e-8f) continue;

                bone.rotation = Quaternion.LookRotation(forwardAxis.normalized, upAxis);
            }

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;
                bone.position = positions[i];
            }

            if (verboseLog)
            {
                Debug.Log($"[{ToolName}] [{chainLabel}] Normalized long coat chain rotations for unified PhysBone.");
            }
        }

        private static void NormalizePhysBoneLimitAxes(
            IReadOnlyList<Transform> bones,
            Vector3 center,
            OnePieceChain chain,
            bool verboseLog)
        {
            if (bones == null || bones.Count == 0) return;

            NormalizeLongCoatChainRotations(
                bones,
                center,
                chain != null ? chain.Label : "Skirt",
                false);

            if (verboseLog)
            {
                Debug.Log(
                    $"[{ToolName}] [{(chain != null ? chain.Label : "Skirt")}] " +
                    "Normalized PhysBone limit axes for rotation-constraint chain.");
            }
        }

        private static Vector3 ResolveSkirtCenter(Animator animator)
        {
            if (animator == null) return Vector3.zero;

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            return hips != null ? hips.position : animator.transform.position;
        }

        private static Vector3 ResolveChainDirection(IReadOnlyList<Transform> bones, Vector3[] positions, int index)
        {
            if (bones == null || positions == null || index < 0 || index >= bones.Count) return Vector3.zero;

            if (index + 1 < bones.Count && bones[index + 1] != null)
            {
                return positions[index + 1] - positions[index];
            }

            if (index > 0 && bones[index - 1] != null)
            {
                return positions[index] - positions[index - 1];
            }

            return Vector3.zero;
        }

        private static void MoveFrontLongCoatBonesOutward(
            string chainLabel,
            List<Transform> bones,
            Transform reference,
            float? targetWorldX)
        {
            if (reference == null || bones == null || bones.Count == 0 || string.IsNullOrEmpty(chainLabel)) return;
            if (chainLabel.IndexOf("Front", StringComparison.OrdinalIgnoreCase) < 0) return;

            var positions = bones.Select(b => b != null ? b.position : Vector3.zero).ToArray();
            if (targetWorldX.HasValue)
            {
                for (var i = 0; i < bones.Count; i++)
                {
                    var bone = bones[i];
                    if (bone == null) continue;

                    var position = positions[i];
                    position.x = targetWorldX.Value;
                    position -= reference.forward * LongCoatFrontBackwardOffset;
                    bone.position = position;
                }

                return;
            }

            var sideSign = chainLabel.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0 ? 1f : -1f;
            var offset = Vector3.right * (LongCoatFrontOutwardOffset * sideSign)
                - reference.forward * LongCoatFrontBackwardOffset;

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;
                bone.position = positions[i] + offset;
            }
        }

        private static float? ResolveFrontOutwardTargetWorldX(
            Dictionary<string, OnePieceChain> chainsByLabel,
            Animator animator,
            OnePieceChain frontChain,
            int targetCount,
            bool rootsOnly)
        {
            if (chainsByLabel == null || frontChain == null || string.IsNullOrEmpty(frontChain.Label)) return null;
            if (frontChain.Label.IndexOf("Front", StringComparison.OrdinalIgnoreCase) < 0) return null;

            var sideLabel = frontChain.Label.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Side-Right"
                : "Side-Left";
            var sideReference = default(Vector3?);
            if (chainsByLabel.TryGetValue(sideLabel, out var sideChain) && sideChain != null)
            {
                sideReference = rootsOnly
                    ? EstimateLongCoatFirstFinalBonePosition(sideChain, targetCount, rootsOnly)
                    : sideChain.SwingBones.FirstOrDefault(b => b != null)?.position;
            }

            var upperLegReference = ResolveUpperLegForChain(animator, frontChain);
            if (sideReference.HasValue && upperLegReference != null)
            {
                return (sideReference.Value.x + upperLegReference.position.x) * 0.5f;
            }

            var frontReference = frontChain.SwingBones.FirstOrDefault(b => b != null) ?? frontChain.SwingRoot;
            if (frontReference == null || upperLegReference == null) return null;

            var sideSign = frontChain.Label.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0 ? 1f : -1f;
            var currentOutwardDistance = Mathf.Abs(frontReference.position.x - upperLegReference.position.x);
            return upperLegReference.position.x + sideSign * Mathf.Max(currentOutwardDistance + LongCoatFrontOutwardOffset, LongCoatFrontOutwardOffset);
        }

        private static float? ResolveSideTargetWorldY(
            Dictionary<string, OnePieceChain> chainsByLabel,
            OnePieceChain sideChain,
            int targetCount,
            bool rootsOnly)
        {
            if (chainsByLabel == null || sideChain == null || string.IsNullOrEmpty(sideChain.Label)) return null;
            if (sideChain.Label.IndexOf("Side", StringComparison.OrdinalIgnoreCase) < 0) return null;

            var isRight = sideChain.Label.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0;
            var frontLabel = isRight ? "Front-Right" : "Front-Left";
            var backLabel = isRight ? "Back-Right" : "Back-Left";
            var targets = new List<float>(2);
            if (chainsByLabel.TryGetValue(frontLabel, out var frontChain) && frontChain != null)
            {
                var frontPosition = EstimateLongCoatFirstFinalBonePosition(frontChain, targetCount, rootsOnly);
                if (frontPosition.HasValue) targets.Add(frontPosition.Value.y);
            }

            if (chainsByLabel.TryGetValue(backLabel, out var backChain) && backChain != null)
            {
                var backPosition = EstimateLongCoatFirstFinalBonePosition(backChain, targetCount, rootsOnly);
                if (backPosition.HasValue) targets.Add(backPosition.Value.y);
            }

            if (targets.Count == 0) return null;
            return targets.Average();
        }

        private static void AlignSideLongCoatRootHeight(
            string chainLabel,
            List<Transform> prependedRoots,
            float? targetWorldY)
        {
            if (!targetWorldY.HasValue || prependedRoots == null || prependedRoots.Count == 0 || string.IsNullOrEmpty(chainLabel)) return;
            if (chainLabel.IndexOf("Side", StringComparison.OrdinalIgnoreCase) < 0) return;

            var firstRoot = prependedRoots.FirstOrDefault(root => root != null);
            if (firstRoot == null) return;

            var offset = targetWorldY.Value - firstRoot.position.y;
            if (Mathf.Abs(offset) <= 1e-6f) return;

            foreach (var root in prependedRoots)
            {
                if (root == null) continue;
                var position = root.position;
                position.y += offset;
                root.position = position;
            }
        }

        private static void LiftLongCoatRootsAboveUpperLeg(
            List<Transform> prependedRoots,
            Transform upperLeg,
            Transform lowerLeg,
            float rootHeightOffsetMultiplier)
        {
            if (prependedRoots == null || prependedRoots.Count == 0 || upperLeg == null) return;

            var firstRoot = prependedRoots.FirstOrDefault(root => root != null);
            if (firstRoot == null) return;

            rootHeightOffsetMultiplier = Mathf.Clamp(rootHeightOffsetMultiplier, -1.0f, 2.0f);
            if (rootHeightOffsetMultiplier <= -1.0f + 1e-6f) return;

            var radius = CalculateLegColliderHeight(upperLeg, lowerLeg) * GeneratedUpperLegColliderRadiusRatio;
            var targetY = rootHeightOffsetMultiplier < 0.0f
                ? Mathf.Lerp(firstRoot.position.y, upperLeg.position.y, rootHeightOffsetMultiplier + 1.0f)
                : upperLeg.position.y + radius * rootHeightOffsetMultiplier;
            var offset = targetY - firstRoot.position.y;
            if (Mathf.Abs(offset) <= 1e-6f) return;

            for (var i = 0; i < prependedRoots.Count; i++)
            {
                var root = prependedRoots[i];
                if (root == null) continue;
                var position = root.position;
                position.y += offset * CalculateLongCoatRootLiftFactor(i, prependedRoots.Count, offset);
                root.position = position;
            }
        }

        private static float CalculateLongCoatRootLiftFactor(int index, int count, float offset)
        {
            if (offset <= 1e-6f || count <= 1 || index <= 0) return 1.0f;

            var t = Mathf.Clamp01(index / Mathf.Max(1.0f, count - 1.0f));
            return Mathf.Lerp(1.0f, LongCoatRootLiftLastRootFactor, t);
        }

        private static Vector3? EstimateLongCoatFirstFinalBonePosition(
            OnePieceChain chain,
            int targetCount,
            bool rootsOnly)
        {
            if (chain == null || chain.SwingBones == null) return null;

            var existingBones = chain.SwingBones.Where(b => b != null).ToList();
            if (existingBones.Count == 0) return null;

            var prependCount = rootsOnly
                ? 3
                : Mathf.Max(0, targetCount - existingBones.Count);
            if (prependCount <= 0) return existingBones[0].position;

            var direction = EstimateChainRootDirection(existingBones);
            var step = direction.magnitude > 1e-6f ? direction : Vector3.up * 0.05f;
            return existingBones[0].position - step * prependCount;
        }

        private static Quaternion GetLongCoatPhysBoneReferenceRotation(OnePieceChain chain, Quaternion fallback)
        {
            if (chain == null || chain.SourcePhysBones == null) return fallback;

            var sourcePhysBone = chain.SourcePhysBones.FirstOrDefault(pb => pb != null);
            if (sourcePhysBone == null) return fallback;

            var sourceRoot = ResolvePhysBoneRoot(sourcePhysBone);
            return sourceRoot != null ? sourceRoot.rotation : sourcePhysBone.transform.rotation;
        }

        private static void AdjustConstrainedRoots(
            IReadOnlyList<Transform> roots,
            Transform source,
            int adjustedCount,
            float moveStrength)
        {
            if (roots == null || roots.Count == 0 || source == null || adjustedCount <= 0) return;
            moveStrength = Mathf.Clamp01(moveStrength);
            if (moveStrength <= 1e-6f) return;

            var constrainedCount = Mathf.Min(adjustedCount, roots.Count);
            for (var i = 0; i < constrainedCount; i++)
            {
                var root = roots[i];
                if (root == null) continue;

                var ratio = constrainedCount <= 1
                    ? 0.8f
                    : Mathf.Lerp(0.8f, 0.35f, i / (float)(constrainedCount - 1));
                var sourceAligned = new Vector3(source.position.x, root.position.y, source.position.z);
                root.position = Vector3.Lerp(root.position, sourceAligned, ratio * moveStrength);
            }
        }

        private static Vector3 EstimateChainRootDirection(IReadOnlyList<Transform> bones)
        {
            if (bones == null || bones.Count <= 1) return Vector3.down * 0.05f;

            for (var i = 0; i < bones.Count - 1; i++)
            {
                if (bones[i] == null || bones[i + 1] == null) continue;
                var delta = bones[i + 1].position - bones[i].position;
                if (delta.sqrMagnitude > 1e-8f) return delta;
            }

            return Vector3.down * 0.05f;
        }

        private static string BuildPrependedBoneName(string rootBoneName, int index)
        {
            var safeName = string.IsNullOrEmpty(rootBoneName) ? "LongCoat" : rootBoneName;
            return $"{safeName}_YMRoot{index + 1:00}";
        }

        private static void DeleteOriginalChainBones(List<Transform> originalBones)
        {
            if (originalBones == null || originalBones.Count == 0) return;

            var root = originalBones[0];
            if (root != null)
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        private static void DeleteMatchedSourceChain(OnePieceChain chain)
        {
            if (chain == null) return;

            var deleteRoot = chain.ManagerRoot != null ? chain.ManagerRoot : chain.SwingRoot;
            if (deleteRoot != null)
            {
                Object.DestroyImmediate(deleteRoot.gameObject);
            }
        }

        private static void DeleteSourceChains(IEnumerable<OnePieceChain> chains)
        {
            if (chains == null) return;

            foreach (var chain in chains
                         .Where(c => c != null)
                         .GroupBy(c => c.ManagerRoot != null ? c.ManagerRoot : c.SwingRoot)
                         .Select(g => g.Key)
                         .Where(root => root != null))
            {
                Object.DestroyImmediate(chain.gameObject);
            }
        }

        private static void RemovePhysBones(List<VRCPhysBone> physBones)
        {
            if (physBones == null) return;

            for (var i = 0; i < physBones.Count; i++)
            {
                var physBone = physBones[i];
                if (physBone == null) continue;
                Object.DestroyImmediate(physBone);
            }
        }

        private static List<VRCPhysBone> FindSourcePhysBones(GameObject avatarRoot, IEnumerable<OnePieceChain> chains)
        {
            var chainList = chains?
                .Where(c => c != null)
                .ToList();
            if (chainList == null || chainList.Count == 0)
            {
                return new List<VRCPhysBone>();
            }

            var sourceBoneSet = new HashSet<Transform>(
                chainList.SelectMany(c => c.SwingBones ?? new List<Transform>())
                    .Where(t => t != null));
            foreach (var chain in chainList)
            {
                if (chain.SwingRoot != null) sourceBoneSet.Add(chain.SwingRoot);
                if (chain.ManagerRoot != null) sourceBoneSet.Add(chain.ManagerRoot);
            }

            var localPhysBones = chainList
                .SelectMany(c => c.SourcePhysBones ?? new List<VRCPhysBone>())
                .Where(pb => pb != null);
            var avatarPhysBones = avatarRoot != null
                ? avatarRoot.GetComponentsInChildren<VRCPhysBone>(true)
                    .Where(pb =>
                    {
                        var root = ResolvePhysBoneRoot(pb);
                        return root != null && sourceBoneSet.Contains(root);
                    })
                : Enumerable.Empty<VRCPhysBone>();

            return localPhysBones
                .Concat(avatarPhysBones)
                .Where(pb => pb != null)
                .Distinct()
                .ToList();
        }

        private static List<VRCPhysBoneColliderBase> CreateLegCapsuleColliders(
            Animator animator,
            bool includeUpperLeg,
            bool includeLowerLeg,
            bool verboseLog,
            Component vqtSettings,
            List<VRCPhysBoneColliderBase> removedColliders)
        {
            var colliders = new List<VRCPhysBoneColliderBase>();
            if (animator == null) return colliders;

            var leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            RemoveExistingLegColliders(leftUpperLeg, verboseLog, vqtSettings, removedColliders);
            RemoveExistingLegColliders(rightUpperLeg, verboseLog, vqtSettings, removedColliders);
            RemoveExistingLegColliders(leftLowerLeg, verboseLog, vqtSettings, removedColliders);
            RemoveExistingLegColliders(rightLowerLeg, verboseLog, vqtSettings, removedColliders);

            if (includeUpperLeg)
            {
                AddLegCapsuleCollider(
                    colliders,
                    leftUpperLeg,
                    leftLowerLeg,
                    "YM_VRoidSkirtRefine_LeftUpperLegCollider",
                    true,
                    GeneratedUpperLegColliderRadiusRatio,
                    Quaternion.Euler(6.0f, 0.0f, 4.0f),
                    "upper leg",
                    verboseLog);
                AddLegCapsuleCollider(
                    colliders,
                    rightUpperLeg,
                    rightLowerLeg,
                    "YM_VRoidSkirtRefine_RightUpperLegCollider",
                    false,
                    GeneratedUpperLegColliderRadiusRatio,
                    Quaternion.Euler(6.0f, 0.0f, -4.0f),
                    "upper leg",
                    verboseLog);
            }

            if (includeLowerLeg)
            {
                AddLegCapsuleCollider(
                    colliders,
                    leftLowerLeg,
                    leftFoot,
                    "YM_VRoidSkirtRefine_LeftLowerLegCollider",
                    true,
                    GeneratedLowerLegColliderRadiusRatio,
                    Quaternion.identity,
                    "lower leg",
                    verboseLog);
                AddLegCapsuleCollider(
                    colliders,
                    rightLowerLeg,
                    rightFoot,
                    "YM_VRoidSkirtRefine_RightLowerLegCollider",
                    false,
                    GeneratedLowerLegColliderRadiusRatio,
                    Quaternion.identity,
                    "lower leg",
                    verboseLog);
            }

            return colliders;
        }

        private static void AddFloorCollider(
            Transform armature,
            List<VRCPhysBoneColliderBase> colliders,
            string colliderName,
            string logLabel,
            bool enabled,
            bool verboseLog)
        {
            if (!enabled || armature == null || colliders == null) return;

            var colliderTransform = armature.Find(colliderName);
            if (colliderTransform == null)
            {
                var colliderObject = new GameObject(colliderName);
                colliderTransform = colliderObject.transform;
                colliderTransform.SetParent(armature, false);
            }

            colliderTransform.localPosition = Vector3.zero;
            colliderTransform.localRotation = Quaternion.identity;

            var collider = colliderTransform.GetComponent<VRCPhysBoneCollider>();
            if (collider == null)
            {
                collider = colliderTransform.gameObject.AddComponent<VRCPhysBoneCollider>();
            }

            collider.rootTransform = armature;
            collider.shapeType = VRCPhysBoneColliderBase.ShapeType.Plane;
            collider.insideBounds = false;
            collider.bonesAsSpheres = false;
            collider.position = Vector3.zero;
            collider.rotation = Quaternion.identity;
            colliders.Add(collider);

            if (verboseLog)
            {
                Debug.Log($"[{ToolName}] Added {logLabel} floor PhysBone collider: {GetPath(colliderTransform)}");
            }
        }

        private static void AddLegCapsuleCollider(
            List<VRCPhysBoneColliderBase> colliders,
            Transform leg,
            Transform legEnd,
            string colliderName,
            bool isLeft,
            float radiusRatio,
            Quaternion rotation,
            string logLabel,
            bool verboseLog)
        {
            if (colliders == null || leg == null) return;

            var colliderTransform = leg.Find(colliderName);
            if (colliderTransform == null)
            {
                var colliderObject = new GameObject(colliderName);
                colliderTransform = colliderObject.transform;
                colliderTransform.SetParent(leg, false);
                colliderTransform.localPosition = Vector3.zero;
                colliderTransform.localRotation = Quaternion.identity;
                colliderTransform.localScale = Vector3.one;
            }

            var collider = colliderTransform.GetComponent<VRCPhysBoneCollider>();
            if (collider == null)
            {
                collider = colliderTransform.gameObject.AddComponent<VRCPhysBoneCollider>();
            }

            var sign = isLeft ? 1.0f : -1.0f;
            var legLength = CalculateLegColliderHeight(leg, legEnd);
            var radius = legLength * radiusRatio;
            var colliderHeight = legLength + radius * 2.0f;
            collider.rootTransform = leg;
            collider.shapeType = VRCPhysBoneColliderBase.ShapeType.Capsule;
            collider.insideBounds = false;
            collider.bonesAsSpheres = false;
            collider.radius = radius;
            collider.height = colliderHeight;
            collider.position = new Vector3(0.01f * sign, -legLength * 0.5f, -0.01f);
            collider.rotation = rotation;
            colliders.Add(collider);

            if (verboseLog)
            {
                Debug.Log($"[{ToolName}] Added {logLabel} PhysBone collider: {GetPath(colliderTransform)}");
            }
        }

        private static float CalculateLegColliderHeight(Transform leg, Transform legEnd)
        {
            if (leg == null || legEnd == null) return GeneratedFallbackLegColliderHeight;

            var localEnd = leg.InverseTransformPoint(legEnd.position);
            var height = localEnd.magnitude;
            return height > 1e-4f ? height : GeneratedFallbackLegColliderHeight;
        }

        private static void RemoveExistingLegColliders(
            Transform leg,
            bool verboseLog,
            Component vqtSettings,
            List<VRCPhysBoneColliderBase> removedColliders)
        {
            if (leg == null) return;

            var colliders = leg.GetComponentsInChildren<VRCPhysBoneCollider>(true)
                .Where(collider => collider != null
                    && !collider.name.StartsWith("YM_VRoidSkirtRefine_", StringComparison.Ordinal))
                .ToList();
            if (colliders.Count == 0) return;

            RemoveObjectsFromArrayField(
                vqtSettings,
                "physBoneCollidersToKeep",
                colliders.Cast<Component>().ToList());
            if (removedColliders != null) removedColliders.AddRange(colliders);

            foreach (var collider in colliders)
            {
                if (verboseLog) Debug.Log($"[{ToolName}] Removed existing leg PhysBone collider: {GetPath(collider.transform)}");
                if (collider.transform == leg)
                {
                    Object.DestroyImmediate(collider);
                }
                else
                {
                    Object.DestroyImmediate(collider.gameObject);
                }
            }
        }

        private static Component GetVqtSettingsForKeepList(GameObject avatarRoot, YMVRoidSkirtRefine component)
        {
            if (avatarRoot == null || component == null || !component.addGeneratedDynamicsToVqtKeepList) return null;
            return TryFindVqtAvatarConverterSettings(avatarRoot, out var settings) ? settings : null;
        }

        private static Vector3 EstimateAverageEndpointPosition(List<LongCoatProcessedChain> processedChains)
        {
            if (processedChains == null || processedChains.Count == 0) return Vector3.zero;

            var sum = Vector3.zero;
            var count = 0;
            foreach (var processed in processedChains)
            {
                if (processed == null || processed.FinalBones == null || processed.FinalBones.Count == 0) continue;

                var endpoint = EstimateEndpointPosition(processed.FinalBones);
                if (endpoint == Vector3.zero) continue;

                sum += endpoint;
                count++;
            }

            return count > 0 ? sum / count : Vector3.zero;
        }

        private static Vector3 EstimateEndpointPosition(IReadOnlyList<Transform> bones)
        {
            if (bones == null || bones.Count < 2) return Vector3.zero;

            var last = bones[bones.Count - 1];
            var previous = bones[bones.Count - 2];
            if (last == null || previous == null) return Vector3.zero;

            var step = last.position - previous.position;
            if (step.sqrMagnitude <= 1e-8f) return Vector3.zero;

            return last.InverseTransformVector(step);
        }

        private static void ApplyPhysBoneSettings(
            VRCPhysBone physBone,
            SkirtRefinePhysBoneSettings settings,
            List<VRCPhysBoneColliderBase> colliders,
            Vector3 endpointPosition,
            SkirtRefinePhysBoneMultiChildType multiChildType,
            List<Transform> ignoreTransforms = null)
        {
            if (physBone == null) return;
            settings = settings ?? new SkirtRefinePhysBoneSettings();

            physBone.version = (VRCPhysBoneBase.Version)settings.version;
            physBone.integrationType = VRCPhysBoneBase.IntegrationType.Simplified;
            physBone.ignoreTransforms = ignoreTransforms != null
                ? ignoreTransforms.Where(t => t != null).Distinct().ToList()
                : new List<Transform>();
            physBone.ignoreOtherPhysBones = true;
            physBone.endpointPosition = endpointPosition;
            physBone.multiChildType = ToVrcMultiChildType(multiChildType);
            physBone.pull = Mathf.Clamp01(settings.pull);
            physBone.pullCurve = CloneOrDefaultCurve(settings.pullCurve, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
            physBone.spring = Mathf.Clamp01(settings.spring);
            physBone.springCurve = CloneOrDefaultCurve(settings.springCurve, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
            physBone.gravity = Mathf.Clamp(settings.gravity, -1.0f, 1.0f);
            physBone.gravityCurve = CloneOrDefaultCurve(settings.gravityCurve, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
            physBone.gravityFalloff = Mathf.Clamp01(settings.gravityFalloff);
            physBone.gravityFalloffCurve = CloneOrDefaultCurve(settings.gravityFalloffCurve, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
            physBone.immobileType = ToVrcImmobileType(settings.immobileType);
            physBone.immobile = Mathf.Clamp01(settings.immobile);
            physBone.immobileCurve = CloneOrDefaultCurve(settings.immobileCurve, AnimationCurve.Linear(0.0f, 1.0f, 1.0f, Mathf.Clamp01(settings.immobileTipMultiplier)));
            physBone.radius = Mathf.Max(0.0f, settings.radius);
            physBone.radiusCurve = CloneOrDefaultCurve(settings.radiusCurve, AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f));
            physBone.limitType = ToVrcLimitType(settings.limitType);
            physBone.maxAngleX = Mathf.Clamp(settings.maxAngle, 0.0f, 180.0f);
            physBone.maxAngleXCurve = CloneOrDefaultCurve(settings.maxAngleCurve, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
            physBone.maxAngleZ = settings.limitType == SkirtRefinePhysBoneLimitType.Polar
                ? Mathf.Clamp(settings.maxYaw, 0.0f, 90.0f)
                : 0.0f;
            physBone.maxAngleZCurve = CloneOrDefaultCurve(settings.maxYawCurve, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
            physBone.limitRotation = settings.limitRotation;
            physBone.allowCollision = ToVrcPermission(settings.allowCollision);
            physBone.collisionFilter.contentTypes = settings.collisionContentTypes;
            physBone.collisionFilter.allowSelf = settings.collisionAllowSelf;
            physBone.collisionFilter.allowOthers = settings.collisionAllowOthers;
            physBone.allowGrabbing = ToVrcPermission(settings.allowGrabbing);
            physBone.grabFilter.allowSelf = settings.grabAllowSelf;
            physBone.grabFilter.allowOthers = settings.grabAllowOthers;
            physBone.allowPosing = ToVrcPermission(settings.allowPosing);
            physBone.poseFilter.allowSelf = settings.poseAllowSelf;
            physBone.poseFilter.allowOthers = settings.poseAllowOthers;
            physBone.snapToHand = settings.snapToHand;
            physBone.grabMovement = Mathf.Clamp01(settings.grabMovement);
            physBone.maxStretch = Mathf.Clamp01(settings.maxStretch);
            physBone.maxStretchCurve = CloneOrDefaultCurve(settings.maxStretchCurve, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
            physBone.maxSquish = Mathf.Clamp01(settings.maxSquish);
            physBone.maxSquishCurve = CloneOrDefaultCurve(settings.maxSquishCurve, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
            physBone.stretchMotion = Mathf.Clamp01(settings.stretchMotion);
            physBone.stretchMotionCurve = CloneOrDefaultCurve(settings.stretchMotionCurve, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
            physBone.isAnimated = false;
            physBone.resetWhenDisabled = true;
            physBone.parameter = string.Empty;
            physBone.showGizmos = true;
            physBone.boneOpacity = 0.2f;
            physBone.limitOpacity = 0.2f;

            physBone.colliders = MergePhysBoneColliders(settings.colliders, colliders);
        }

        private static List<VRCPhysBoneColliderBase> MergePhysBoneColliders(
            List<VRCPhysBoneColliderBase> configuredColliders,
            List<VRCPhysBoneColliderBase> generatedColliders)
        {
            var result = new List<VRCPhysBoneColliderBase>();
            if (generatedColliders != null)
            {
                result.AddRange(generatedColliders.Where(c => c != null));
            }
            if (configuredColliders != null)
            {
                result.AddRange(configuredColliders.Where(c => c != null));
            }

            return result.Distinct().ToList();
        }

        private static AnimationCurve CloneOrDefaultCurve(AnimationCurve source, AnimationCurve fallback)
        {
            if (source == null || source.length == 0)
            {
                return new AnimationCurve(fallback != null ? fallback.keys : Array.Empty<Keyframe>());
            }

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
        }

        private static VRCPhysBoneBase.MultiChildType ToVrcMultiChildType(SkirtRefinePhysBoneMultiChildType value)
        {
            switch (value)
            {
                case SkirtRefinePhysBoneMultiChildType.First:
                    return VRCPhysBoneBase.MultiChildType.First;
                case SkirtRefinePhysBoneMultiChildType.Average:
                    return VRCPhysBoneBase.MultiChildType.Average;
                case SkirtRefinePhysBoneMultiChildType.Ignore:
                default:
                    return VRCPhysBoneBase.MultiChildType.Ignore;
            }
        }

        private static VRCPhysBoneBase.ImmobileType ToVrcImmobileType(SkirtRefinePhysBoneImmobileType value)
        {
            switch (value)
            {
                case SkirtRefinePhysBoneImmobileType.AllMotion:
                    return VRCPhysBoneBase.ImmobileType.AllMotion;
                case SkirtRefinePhysBoneImmobileType.World:
                default:
                    return VRCPhysBoneBase.ImmobileType.World;
            }
        }

        private static List<Transform> GetFrontRootIgnoreTransforms(List<LongCoatProcessedChain> processedChains)
        {
            if (processedChains == null) return new List<Transform>();
            return processedChains
                .Where(processed => IsFrontChain(processed?.Chain))
                .Select(processed => processed.FinalBones != null && processed.FinalBones.Count > 0 ? processed.FinalBones[0] : null)
                .Where(root => root != null)
                .Distinct()
                .ToList();
        }

        private static List<VRCPhysBone> BuildFrontRootRotationConstraintMode(
            Animator animator,
            List<LongCoatProcessedChain> processedChains,
            SkirtRefinePhysBoneSettings physBoneSettings,
            List<VRCPhysBoneColliderBase> colliders,
            float constraintWeight,
            ConstraintImplementationMode constraintMode,
            bool aimFrontLimitsForward,
            bool verboseLog)
        {
            var generated = new List<VRCPhysBone>();
            if (animator == null || processedChains == null) return generated;

            foreach (var processed in processedChains)
            {
                if (processed == null || !IsFrontChain(processed.Chain) || processed.FinalBones == null || processed.FinalBones.Count < 2) continue;

                var source = ResolveUpperLegForChain(animator, processed.Chain);
                AddOrUpdateRotationConstraint(processed.FinalBones[0], source, constraintWeight, constraintMode, verboseLog);

                var physBoneRoot = processed.FinalBones[1];
                if (physBoneRoot == null) continue;

                var physBone = physBoneRoot.GetComponent<VRCPhysBone>();
                if (physBone == null)
                {
                    physBone = physBoneRoot.gameObject.AddComponent<VRCPhysBone>();
                }

                physBone.rootTransform = physBoneRoot;
                ApplyPhysBoneSettings(
                    physBone,
                    physBoneSettings,
                    colliders,
                    Vector3.zero,
                    SkirtRefinePhysBoneMultiChildType.First);
                ApplyFrontLimitRotationOverride(physBone, processed.Chain, aimFrontLimitsForward);
                generated.Add(physBone);
            }

            return generated;
        }

        private static List<VRCPhysBone> BuildLongCoatRotationConstraintMode(
            Animator animator,
            List<LongCoatProcessedChain> processedChains,
            SkirtRefinePhysBoneSettings physBoneSettings,
            List<VRCPhysBoneColliderBase> colliders,
            ConstraintImplementationMode constraintMode,
            float frontConstraintWeight,
            float sideConstraintWeight,
            float backConstraintWeight,
            bool aimFrontLimitsForward,
            bool verboseLog)
        {
            var generated = new List<VRCPhysBone>();
            if (animator == null || processedChains == null) return generated;

            foreach (var processed in processedChains)
            {
                if (processed == null || processed.FinalBones == null || processed.FinalBones.Count == 0) continue;

                var source = ResolveUpperLegForChain(animator, processed.Chain);
                var constraintWeight = GetLongCoatRotationConstraintWeight(
                    processed.Chain,
                    frontConstraintWeight,
                    sideConstraintWeight,
                    backConstraintWeight);
                if (processed.FinalBones.Count > 0)
                {
                    AddOrUpdateRotationConstraint(processed.FinalBones[0], source, constraintWeight, constraintMode, verboseLog);
                }

                if (processed.FinalBones.Count > 2)
                {
                    var lowerLegSource = ResolveLowerLegForChain(animator, processed.Chain);
                    var lowerLegConstraintWeight = GetLongCoatLowerLegRotationConstraintWeight(processed.Chain);
                    AddOrUpdateRotationConstraint(processed.FinalBones[2], lowerLegSource, lowerLegConstraintWeight, constraintMode, verboseLog);
                }

                if (processed.FinalBones.Count > 3)
                {
                    var lowerRoot = processed.FinalBones[3];
                    if (lowerRoot != null)
                    {
                        var physBone = lowerRoot.GetComponent<VRCPhysBone>();
                        if (physBone == null)
                        {
                            physBone = lowerRoot.gameObject.AddComponent<VRCPhysBone>();
                        }

                        physBone.rootTransform = lowerRoot;
                        ApplyPhysBoneSettings(
                            physBone,
                            physBoneSettings,
                            colliders,
                            Vector3.zero,
                            SkirtRefinePhysBoneMultiChildType.First);
                        ApplyFrontLimitRotationOverride(physBone, processed.Chain, aimFrontLimitsForward);
                        generated.Add(physBone);
                    }
                }
            }

            return generated;
        }

        private static void ApplyFrontLimitRotationOverride(
            VRCPhysBone physBone,
            OnePieceChain chain,
            bool aimFrontLimitsForward)
        {
            if (!aimFrontLimitsForward || physBone == null || !IsFrontChain(chain)) return;

            var rotation = physBone.limitRotation;
            rotation.y = IsRightChain(chain) ? 35.0f : -35.0f;
            physBone.limitRotation = rotation;
        }

        private static Transform ResolveUpperLegForChain(Animator animator, OnePieceChain chain)
        {
            if (animator == null || chain == null || string.IsNullOrEmpty(chain.Label)) return null;

            return chain.Label.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0
                ? animator.GetBoneTransform(HumanBodyBones.RightUpperLeg)
                : animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        }

        private static Transform ResolveLowerLegForChain(Animator animator, OnePieceChain chain)
        {
            if (animator == null || chain == null || string.IsNullOrEmpty(chain.Label)) return null;

            return chain.Label.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0
                ? animator.GetBoneTransform(HumanBodyBones.RightLowerLeg)
                : animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        }

        private static float GetLongCoatRotationConstraintWeight(
            OnePieceChain chain,
            float frontConstraintWeight,
            float sideConstraintWeight,
            float backConstraintWeight)
        {
            if (chain == null || string.IsNullOrEmpty(chain.Label)) return sideConstraintWeight;
            if (IsFrontChain(chain)) return frontConstraintWeight;
            if (chain.Label.IndexOf("Side", StringComparison.OrdinalIgnoreCase) >= 0) return sideConstraintWeight;
            if (chain.Label.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0) return backConstraintWeight;
            return frontConstraintWeight;
        }

        private static float GetLongCoatLowerLegRotationConstraintWeight(OnePieceChain chain)
        {
            if (chain == null || string.IsNullOrEmpty(chain.Label)) return 0.7f;
            if (IsFrontChain(chain)) return 0.4f;
            if (chain.Label.IndexOf("Side", StringComparison.OrdinalIgnoreCase) >= 0) return 0.7f;
            if (chain.Label.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0) return 0.9f;
            return 0.4f;
        }

        private static bool IsFrontChain(OnePieceChain chain)
        {
            return chain != null
                && !string.IsNullOrEmpty(chain.Label)
                && chain.Label.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsRightChain(OnePieceChain chain)
        {
            return chain != null
                && !string.IsNullOrEmpty(chain.Label)
                && chain.Label.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<Transform> GetLongCoatSharedLegSourceBones(Animator animator)
        {
            var result = new List<Transform>();
            if (animator == null) return result;

            AddIfNotNull(result, animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg));
            AddIfNotNull(result, animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg));
            AddIfNotNull(result, animator.GetBoneTransform(HumanBodyBones.LeftFoot));
            AddIfNotNull(result, animator.GetBoneTransform(HumanBodyBones.RightUpperLeg));
            AddIfNotNull(result, animator.GetBoneTransform(HumanBodyBones.RightLowerLeg));
            AddIfNotNull(result, animator.GetBoneTransform(HumanBodyBones.RightFoot));
            return result;
        }

        private static void AddIfNotNull(List<Transform> list, Transform value)
        {
            if (list == null || value == null || list.Contains(value)) return;
            list.Add(value);
        }

        private static void AddOrUpdateRotationConstraint(
            Transform target,
            Transform source,
            float sourceWeight,
            ConstraintImplementationMode constraintMode,
            bool verboseLog)
        {
            if (target == null || source == null) return;

            var localPosition = target.localPosition;
            var localRotation = target.localRotation;
            var localScale = target.localScale;
            var rotationOffset = (Quaternion.Inverse(source.rotation) * target.rotation).eulerAngles;
            ConstraintUtility.AddRotationConstraintAllAxes(target, source, sourceWeight, rotationOffset, constraintMode);
            target.localPosition = localPosition;
            target.localRotation = localRotation;
            target.localScale = localScale;

            if (verboseLog)
            {
                Debug.Log($"[{ToolName}] Added long coat rotation constraint: {GetPath(target)} -> {GetPath(source)}, weight={sourceWeight:0.00}");
            }
        }

        private static ConstraintImplementationMode ToConstraintImplementationMode(SkirtRefineConstraintMode mode)
        {
            return mode == SkirtRefineConstraintMode.UnityConstraints
                ? ConstraintImplementationMode.UnityConstraints
                : ConstraintImplementationMode.VRChatConstraints;
        }

        private static VRCPhysBoneBase.LimitType ToVrcLimitType(SkirtRefinePhysBoneLimitType value)
        {
            switch (value)
            {
                case SkirtRefinePhysBoneLimitType.Angle:
                    return VRCPhysBoneBase.LimitType.Angle;
                case SkirtRefinePhysBoneLimitType.Hinge:
                    return VRCPhysBoneBase.LimitType.Hinge;
                case SkirtRefinePhysBoneLimitType.Polar:
                    return VRCPhysBoneBase.LimitType.Polar;
                case SkirtRefinePhysBoneLimitType.None:
                default:
                    return VRCPhysBoneBase.LimitType.None;
            }
        }

        private static VRCPhysBoneBase.AdvancedBool ToVrcPermission(SkirtRefinePhysBonePermission value)
        {
            if (Enum.TryParse(value.ToString(), out VRCPhysBoneBase.AdvancedBool parsed))
            {
                return parsed;
            }

            return VRCPhysBoneBase.AdvancedBool.Other;
        }

        private static void RebindDeletedManagementBones(
            GameObject avatarRoot,
            Dictionary<Transform, Transform> oldToNewBoneMap,
            bool verboseLog)
        {
            if (avatarRoot == null || oldToNewBoneMap == null || oldToNewBoneMap.Count == 0) return;

            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.bones == null || smr.bones.Length == 0) continue;

                var bones = smr.bones;
                var changed = false;
                for (var i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null && oldToNewBoneMap.TryGetValue(bones[i], out var replacement) && replacement != null)
                    {
                        bones[i] = replacement;
                        changed = true;
                    }
                }

                if (!changed) continue;
                smr.bones = bones;
                if (verboseLog) Debug.Log($"[{ToolName}] Rebound deleted management bone references: {GetPath(smr.transform)}");
            }
        }

        private static void AddDistanceReweightInfos(
            List<ChainReweightInfo> infos,
            string label,
            List<Transform> originalBones,
            IEnumerable<List<Transform>> finalBoneChains,
            List<Transform> sharedOriginalBones = null)
        {
            if (infos == null || finalBoneChains == null) return;

            var originals = originalBones != null
                ? originalBones.Where(bone => bone != null).Distinct().ToList()
                : new List<Transform>();
            if (originals.Count == 0) return;

            var shared = sharedOriginalBones != null
                ? sharedOriginalBones.Where(bone => bone != null).Distinct().ToList()
                : null;

            foreach (var finalBones in finalBoneChains)
            {
                var finals = finalBones != null
                    ? finalBones.Where(bone => bone != null).Distinct().ToList()
                    : new List<Transform>();
                if (finals.Count == 0) continue;

                infos.Add(new ChainReweightInfo(label, originals, finals, shared));
            }
        }

        private static void ReweightSkirtVertices(
            GameObject avatarRoot,
            List<ChainReweightInfo> chainInfos,
            Transform hipBone,
            float hipWeightReduction,
            Transform spineBone,
            float spineWeightReduction,
            YMVRoidSkirtRefine component,
            bool allowCoverageAboveFirstBone,
            BuildContext context)
        {
            if (component != null && component.useGeometricSkirtWeights)
            {
                ReweightSkirtVerticesGeometric(
                    avatarRoot,
                    chainInfos,
                    hipBone,
                    hipWeightReduction,
                    spineBone,
                    spineWeightReduction,
                    component,
                    allowCoverageAboveFirstBone,
                    context);
                return;
            }

            ReweightSkirtVerticesLegacy(
                avatarRoot,
                chainInfos,
                hipBone,
                hipWeightReduction,
                spineBone,
                spineWeightReduction,
                component != null && component.verboseLog,
                context);
        }

        private static void ReweightSkirtVerticesLegacy(
            GameObject avatarRoot,
            List<ChainReweightInfo> chainInfos,
            Transform hipBone,
            float hipWeightReduction,
            Transform spineBone,
            float spineWeightReduction,
            bool verboseLog,
            BuildContext context)
        {
            if (avatarRoot == null || chainInfos == null || chainInfos.Count == 0) return;
            hipWeightReduction = Mathf.Clamp01(hipWeightReduction);
            spineWeightReduction = Mathf.Clamp01(spineWeightReduction);

            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null || smr.bones == null || smr.bones.Length == 0) continue;

                var mesh = smr.sharedMesh;
                var rendererBones = smr.bones.ToList();
                var bindposes = mesh.bindposes.ToList();
                var rendererChainInfos = BuildRendererChainInfos(smr, rendererBones, bindposes, chainInfos);
                if (rendererChainInfos.Count == 0) continue;
                var replacedSourceIndices = BuildReplacedSourceIndexSet(rendererChainInfos);
                var replacedSourceIndexList = replacedSourceIndices.ToList();
                var judgmentPoints = BuildBoneJudgmentPoints(rendererChainInfos);
                if (judgmentPoints.Count == 0) continue;
                var generatedFinalIndices = BuildGeneratedFinalIndexSet(rendererChainInfos);
                var hipIndex = hipBone != null ? rendererBones.IndexOf(hipBone) : -1;
                var spineIndex = spineBone != null ? rendererBones.IndexOf(spineBone) : -1;

                var weights = mesh.boneWeights;
                if (weights == null || weights.Length == 0) continue;

                var vertices = mesh.vertices;
                var verticesInCoatWeightedSubmesh = BuildVerticesInCoatWeightedSubmeshes(mesh, weights, rendererChainInfos);
                var changedWeights = false;
                for (var vi = 0; vi < weights.Length && vi < vertices.Length; vi++)
                {
                    var bw = weights[vi];
                    var pairs = ExtractPairs(bw);
                    var replacedTargetWeight = pairs
                        .Where(p => p.w > 1e-6f && replacedSourceIndices.Contains(p.idx))
                        .Sum(p => p.w);
                    if (replacedTargetWeight <= 1e-6f) continue;
                    if (vi >= verticesInCoatWeightedSubmesh.Length || !verticesInCoatWeightedSubmesh[vi]) continue;

                    var world = smr.transform.TransformPoint(vertices[vi]);
                    RemoveBones(pairs, replacedSourceIndexList);
                    var targetWeight = replacedTargetWeight + ExtractTransferWeight(pairs, hipIndex, hipWeightReduction);
                    if (spineIndex != hipIndex)
                    {
                        targetWeight += ExtractTransferWeight(pairs, spineIndex, spineWeightReduction);
                    }
                    AddNearestJudgmentPointDistribution(pairs, judgmentPoints, world, targetWeight);
                    weights[vi] = ToBoneWeight(pairs);
                    changedWeights = true;
                }

                if (!changedWeights) continue;
                SmoothGeneratedWeightsByTopology(mesh, weights, verticesInCoatWeightedSubmesh, generatedFinalIndices);

                var newMesh = NdmfObjectRegistry.Clone(mesh);
                context?.AssetSaver.SaveAsset(newMesh);
                newMesh.name = mesh.name + "_YMVRoidSkirtRefine";
                newMesh.bindposes = bindposes.ToArray();
                newMesh.boneWeights = weights;
                smr.sharedMesh = newMesh;
                smr.bones = rendererBones.ToArray();

                if (verboseLog)
                {
                    Debug.Log($"[{ToolName}] Reweighted skirt vertices: {GetPath(smr.transform)}");
                }
            }
        }

        private static void ReweightSkirtVerticesGeometric(
            GameObject avatarRoot,
            List<ChainReweightInfo> chainInfos,
            Transform hipBone,
            float hipWeightReduction,
            Transform spineBone,
            float spineWeightReduction,
            YMVRoidSkirtRefine component,
            bool allowCoverageAboveFirstBone,
            BuildContext context)
        {
            if (avatarRoot == null || chainInfos == null || chainInfos.Count == 0 || component == null) return;

            var settings = new GeometricSkirtWeightSettings(component);
            hipWeightReduction = Mathf.Clamp01(hipWeightReduction);
            spineWeightReduction = Mathf.Clamp01(spineWeightReduction);

            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null || smr.bones == null || smr.bones.Length == 0) continue;

                var mesh = smr.sharedMesh;
                var rendererBones = smr.bones.ToList();
                var bindposes = mesh.bindposes.ToList();
                var rendererChainInfos = BuildRendererChainInfos(smr, rendererBones, bindposes, chainInfos);
                if (rendererChainInfos.Count == 0) continue;

                var replacedSourceIndices = BuildReplacedSourceIndexSet(rendererChainInfos);
                AddSkirtNamedBoneIndices(rendererBones, replacedSourceIndices);
                AddRootFallbackSourceBoneIndices(rendererBones, replacedSourceIndices);
                var hipIndex = hipBone != null ? rendererBones.IndexOf(hipBone) : -1;
                var spineIndex = spineBone != null ? rendererBones.IndexOf(spineBone) : -1;
                var hipWeightIndices = BuildGeometricHipWeightIndexSet(rendererBones, hipIndex);
                var spineWeightIndices = BuildGeometricSpineWeightIndexSet(rendererBones, spineIndex);
                var bodyWeightIndices = BuildGeometricBodyWeightIndexSet(hipWeightIndices, spineWeightIndices);
                var earlyLegWeightIndices = BuildGeometricLegWeightIndexSet(rendererBones);
                var protectedHoleWeightIndices = BuildGeometricHoleProtectedWeightIndexSet(rendererBones, bodyWeightIndices, earlyLegWeightIndices);
                var originalWeights = ReadAllBoneWeightsByVertex(mesh);
                if (originalWeights.Length == 0) continue;

                var vertices = mesh.vertices;
                if (vertices == null || vertices.Length == 0) continue;

                var verticesInCoatWeightedSubmesh = BuildVerticesInCoatWeightedSubmeshes(mesh, originalWeights, rendererChainInfos);
                var targetVertices = BuildGeometricTargetVertices(
                    mesh,
                    vertices,
                    originalWeights,
                    verticesInCoatWeightedSubmesh,
                    replacedSourceIndices,
                    hipIndex,
                    spineIndex,
                    protectedHoleWeightIndices,
                    settings,
                    out var sourceWeightedVertices);
                if (!targetVertices.Any(v => v)) continue;
                RestrictTargetVerticesToGeneratedBoneVerticalRange(targetVertices, sourceWeightedVertices, vertices, rendererChainInfos, smr.transform);
                FillGeometricTargetHolesByMesh(mesh, verticesInCoatWeightedSubmesh, targetVertices, originalWeights, protectedHoleWeightIndices);
                if (!targetVertices.Any(v => v)) continue;

                if (!TryBuildSkirtCoordinateFrame(vertices, targetVertices, out var centerXZ, out var maxY, out var yRange)) continue;
                var boneChains = BuildGeometricBoneChains(smr, rendererChainInfos, centerXZ, maxY, yRange, settings);
                if (boneChains.Count == 0) continue;
                var generatedWeights = GenerateGeometricSkirtWeights(
                    vertices,
                    targetVertices,
                    boneChains,
                    centerXZ,
                    maxY,
                    yRange,
                    settings,
                    hipWeightReduction,
                    spineWeightReduction,
                    allowCoverageAboveFirstBone,
                    out var hipCoverages,
                    out var spineCoverages);
                if (!generatedWeights.Any(w => w != null && w.Count > 0)) continue;
                SmoothSkirtCoveragesByMesh(mesh, targetVertices, hipCoverages);
                SmoothSkirtCoveragesByMesh(mesh, targetVertices, spineCoverages);
                if (settings.EnableRingSmoothing)
                {
                    SmoothGeometricWeightsByRings(generatedWeights, vertices, targetVertices, centerXZ, maxY, yRange, settings);
                }

                if (settings.EnableMeshSmoothing)
                {
                    SmoothGeometricWeightsByMesh(mesh, generatedWeights, targetVertices, settings);
                }

                var outputWeights = new List<(int idx, float w)>[originalWeights.Length];
                var changedWeights = false;
                for (var vi = 0; vi < outputWeights.Length; vi++)
                {
                    var generated = vi < generatedWeights.Length ? generatedWeights[vi] : null;
                    if (!IsAffectedVertex(targetVertices, vi) || generated == null || generated.Count == 0)
                    {
                        outputWeights[vi] = originalWeights[vi] != null
                            ? new List<(int idx, float w)>(originalWeights[vi])
                            : new List<(int idx, float w)>();
                        continue;
                    }

                    PruneAndNormalizeDictionary(generated, settings.MaxInfluencesAfterPrune, settings.MinimumWeight);
                    if (generated.Count == 0)
                    {
                        outputWeights[vi] = originalWeights[vi] != null
                            ? new List<(int idx, float w)>(originalWeights[vi])
                            : new List<(int idx, float w)>();
                        continue;
                    }

                    outputWeights[vi] = BuildGeometricOutputWeightsForVertex(
                        originalWeights[vi],
                        generated,
                        replacedSourceIndices,
                        hipWeightIndices,
                        spineWeightIndices,
                        earlyLegWeightIndices,
                        GetSkirtCoverage(hipCoverages, vi),
                        GetSkirtCoverage(spineCoverages, vi),
                        settings);
                    changedWeights = true;
                }

                if (!changedWeights) continue;

                var newMesh = NdmfObjectRegistry.Clone(mesh);
                context?.AssetSaver.SaveAsset(newMesh);
                newMesh.name = mesh.name + "_YMVRoidSkirtRefine";
                newMesh.bindposes = bindposes.ToArray();
                ApplyAllBoneWeights(newMesh, outputWeights);
                smr.sharedMesh = newMesh;
                smr.bones = rendererBones.ToArray();

                if (component.verboseLog)
                {
                    Debug.Log($"[{ToolName}] Geometrically reweighted skirt vertices: {GetPath(smr.transform)}");
                }
            }
        }

        private static List<RendererChainInfo> BuildRendererChainInfos(
            SkinnedMeshRenderer smr,
            List<Transform> rendererBones,
            List<Matrix4x4> bindposes,
            List<ChainReweightInfo> chainInfos)
        {
            var result = new List<RendererChainInfo>();

            foreach (var chain in chainInfos)
            {
                var chainOriginalIndices = chain.OriginalBones
                    .Select(b => rendererBones.IndexOf(b))
                    .Where(i => i >= 0)
                    .Distinct()
                    .ToList();
                if (chainOriginalIndices.Count == 0) continue;

                var sharedOriginalIndices = chain.SharedOriginalBones
                    .Select(b => rendererBones.IndexOf(b))
                    .Where(i => i >= 0)
                    .Distinct()
                    .ToList();
                AddSharedOriginalIndicesByName(rendererBones, chain.SharedOriginalBones, sharedOriginalIndices);

                var finalIndices = new List<int>();
                foreach (var bone in chain.FinalBones)
                {
                    if (bone == null) continue;
                    var index = rendererBones.IndexOf(bone);
                    if (index < 0)
                    {
                        rendererBones.Add(bone);
                        bindposes.Add(bone.worldToLocalMatrix * smr.transform.localToWorldMatrix);
                        index = rendererBones.Count - 1;
                    }
                    else if (index < bindposes.Count)
                    {
                        bindposes[index] = bone.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                    }
                    finalIndices.Add(index);
                }

                if (finalIndices.Count == 0) continue;
                result.Add(new RendererChainInfo(chain.Label, chain.FinalBones, chainOriginalIndices, sharedOriginalIndices, finalIndices));
            }

            return result;
        }

        private static void AddSharedOriginalIndicesByName(
            List<Transform> rendererBones,
            List<Transform> sharedOriginalBones,
            List<int> sharedOriginalIndices)
        {
            if (rendererBones == null || sharedOriginalBones == null || sharedOriginalIndices == null) return;

            var sharedNames = new HashSet<string>(
                sharedOriginalBones
                    .Where(b => b != null && !string.IsNullOrEmpty(b.name))
                    .Select(b => b.name));
            if (sharedNames.Count == 0) return;

            for (var i = 0; i < rendererBones.Count; i++)
            {
                var bone = rendererBones[i];
                if (bone == null || string.IsNullOrEmpty(bone.name)) continue;
                if (!sharedNames.Contains(bone.name)) continue;
                if (!sharedOriginalIndices.Contains(i)) sharedOriginalIndices.Add(i);
            }
        }

        private static HashSet<int> BuildGeneratedFinalIndexSet(List<RendererChainInfo> rendererChainInfos)
        {
            var result = new HashSet<int>();
            if (rendererChainInfos == null) return result;

            foreach (var info in rendererChainInfos)
            {
                if (info == null || info.FinalIndices == null) continue;
                foreach (var index in info.FinalIndices)
                {
                    if (index >= 0) result.Add(index);
                }
            }

            return result;
        }

        private static HashSet<int> BuildReplacedSourceIndexSet(List<RendererChainInfo> rendererChainInfos)
        {
            var result = new HashSet<int>();
            if (rendererChainInfos == null) return result;

            foreach (var info in rendererChainInfos)
            {
                if (info == null) continue;

                foreach (var index in info.ChainOriginalIndices)
                {
                    if (index >= 0) result.Add(index);
                }

                foreach (var index in info.SharedOriginalIndices)
                {
                    if (index >= 0) result.Add(index);
                }
            }

            return result;
        }

        private static void AddSkirtNamedBoneIndices(List<Transform> rendererBones, HashSet<int> indices)
        {
            if (rendererBones == null || indices == null) return;

            for (var i = 0; i < rendererBones.Count; i++)
            {
                var bone = rendererBones[i];
                if (bone == null || string.IsNullOrEmpty(bone.name)) continue;
                if (bone.name.IndexOf("Skirt", StringComparison.OrdinalIgnoreCase) < 0
                    && bone.name.IndexOf("Coat", StringComparison.OrdinalIgnoreCase) < 0) continue;

                indices.Add(i);
            }
        }

        private static void AddRootFallbackSourceBoneIndices(List<Transform> rendererBones, HashSet<int> indices)
        {
            if (rendererBones == null || indices == null) return;

            for (var i = 0; i < rendererBones.Count; i++)
            {
                var bone = rendererBones[i];
                if (bone == null || string.IsNullOrEmpty(bone.name)) continue;
                if (!string.Equals(bone.name, "Root", StringComparison.OrdinalIgnoreCase)) continue;
                indices.Add(i);
            }
        }

        private static HashSet<int> BuildGeometricHipWeightIndexSet(List<Transform> rendererBones, int hipIndex)
        {
            var result = new HashSet<int>();
            if (hipIndex >= 0) result.Add(hipIndex);
            if (rendererBones == null) return result;

            for (var i = 0; i < rendererBones.Count; i++)
            {
                var bone = rendererBones[i];
                if (bone == null || string.IsNullOrEmpty(bone.name)) continue;
                var name = bone.name;
                if (name.IndexOf("Hips", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(i);
                }
            }

            return result;
        }

        private static HashSet<int> BuildGeometricSpineWeightIndexSet(List<Transform> rendererBones, int spineIndex)
        {
            var result = new HashSet<int>();
            if (spineIndex >= 0) result.Add(spineIndex);
            if (rendererBones == null) return result;

            for (var i = 0; i < rendererBones.Count; i++)
            {
                var bone = rendererBones[i];
                if (bone == null || string.IsNullOrEmpty(bone.name)) continue;
                var name = bone.name;
                if (name.IndexOf("Spine", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(i);
                }
            }

            return result;
        }

        private static HashSet<int> BuildGeometricBodyWeightIndexSet(
            HashSet<int> hipWeightIndices,
            HashSet<int> spineWeightIndices)
        {
            var result = new HashSet<int>();
            if (hipWeightIndices != null)
            {
                foreach (var index in hipWeightIndices) result.Add(index);
            }

            if (spineWeightIndices != null)
            {
                foreach (var index in spineWeightIndices) result.Add(index);
            }

            return result;
        }

        private static HashSet<int> BuildGeometricLegWeightIndexSet(List<Transform> rendererBones)
        {
            var result = new HashSet<int>();
            if (rendererBones == null) return result;

            for (var i = 0; i < rendererBones.Count; i++)
            {
                var bone = rendererBones[i];
                if (bone == null || string.IsNullOrEmpty(bone.name)) continue;
                var name = bone.name;
                if (name.IndexOf("UpperLeg", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("LowerLeg", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(i);
                }
            }

            return result;
        }

        private static HashSet<int> BuildGeometricHoleProtectedWeightIndexSet(
            List<Transform> rendererBones,
            HashSet<int> bodyWeightIndices,
            HashSet<int> legWeightIndices)
        {
            var result = new HashSet<int>();
            if (bodyWeightIndices != null)
            {
                foreach (var index in bodyWeightIndices) result.Add(index);
            }

            if (legWeightIndices != null)
            {
                foreach (var index in legWeightIndices) result.Add(index);
            }

            if (rendererBones == null) return result;
            for (var i = 0; i < rendererBones.Count; i++)
            {
                var bone = rendererBones[i];
                if (bone == null || string.IsNullOrEmpty(bone.name)) continue;
                var name = bone.name;
                if (name.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Bust", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Shoulder", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Neck", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Toe", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(i);
                }
            }

            return result;
        }

        private static List<BoneJudgmentPoint> BuildBoneJudgmentPoints(List<RendererChainInfo> rendererChainInfos)
        {
            var result = new List<BoneJudgmentPoint>();
            if (rendererChainInfos == null) return result;

            foreach (var info in rendererChainInfos)
            {
                if (info == null || info.FinalBones == null || info.FinalIndices == null) continue;

                var count = Mathf.Min(info.FinalBones.Count, info.FinalIndices.Count);
                if (count == 0) continue;

                var positions = new Vector3[count];
                for (var i = 0; i < count; i++)
                {
                    positions[i] = info.FinalBones[i] != null ? info.FinalBones[i].position : Vector3.zero;
                }

                var points = new Vector3[count];
                if (count == 1)
                {
                    points[0] = positions[0];
                }
                else
                {
                    for (var i = 0; i < count - 1; i++)
                    {
                        points[i] = (positions[i] + positions[i + 1]) * 0.5f;
                    }

                    points[count - 1] = positions[count - 1] + (positions[count - 1] - points[count - 2]);
                }

                for (var i = 0; i < count; i++)
                {
                    if (info.FinalBones[i] == null) continue;
                    result.Add(new BoneJudgmentPoint(info.FinalIndices[i], points[i]));
                }
            }

            return result;
        }

        private static void AddNearestJudgmentPointDistribution(
            List<(int idx, float w)> pairs,
            List<BoneJudgmentPoint> judgmentPoints,
            Vector3 worldPosition,
            float targetWeight)
        {
            if (pairs == null || judgmentPoints == null || judgmentPoints.Count == 0 || targetWeight <= 1e-6f) return;

            var nearest = FindNearestJudgmentPoints(judgmentPoints, worldPosition, 4);
            if (nearest.Count == 0) return;

            var firstDistance = nearest[0].Distance;
            var selected = nearest
                .Where(p => ShouldUseJudgmentPoint(firstDistance, p.Distance))
                .ToList();
            if (selected.Count == 0)
            {
                selected.Add(nearest[0]);
            }

            var availableSlots = Mathf.Max(1, 4 - pairs.Count);
            if (selected.Count > availableSlots)
            {
                selected.RemoveRange(availableSlots, selected.Count - availableSlots);
            }

            var distribution = CalculateNearestJudgmentWeights(firstDistance, selected);
            for (var i = 0; i < selected.Count; i++)
            {
                AddOrAccumulate(pairs, selected[i].BoneIndex, targetWeight * distribution[i]);
            }
        }

        private static List<BoneJudgmentPoint> FindNearestJudgmentPoints(
            List<BoneJudgmentPoint> judgmentPoints,
            Vector3 worldPosition,
            int maxCount)
        {
            var result = new List<BoneJudgmentPoint>();
            if (judgmentPoints == null || maxCount <= 0) return result;

            for (var i = 0; i < judgmentPoints.Count; i++)
            {
                var point = judgmentPoints[i];
                if (point == null) continue;

                point.Distance = Vector3.Distance(worldPosition, point.Position);
                var insertIndex = result.FindIndex(p => point.Distance < p.Distance);
                if (insertIndex < 0)
                {
                    result.Add(point);
                }
                else
                {
                    result.Insert(insertIndex, point);
                }

                if (result.Count > maxCount)
                {
                    result.RemoveAt(result.Count - 1);
                }
            }

            return result;
        }

        private static bool ShouldUseJudgmentPoint(float firstDistance, float distance)
        {
            if (firstDistance <= 1e-6f) return distance <= 1e-6f;
            return distance < firstDistance * 2.0f;
        }

        private static List<float> CalculateNearestJudgmentWeights(float firstDistance, List<BoneJudgmentPoint> selected)
        {
            var result = new List<float>();
            if (selected == null || selected.Count == 0) return result;
            if (selected.Count == 1)
            {
                result.Add(1.0f);
                return result;
            }

            if (firstDistance <= 1e-6f)
            {
                var exactCount = selected.Count(p => p.Distance <= 1e-6f);
                if (exactCount == 0) exactCount = 1;
                for (var i = 0; i < selected.Count; i++)
                {
                    result.Add(i < exactCount ? 1.0f / exactCount : 0.0f);
                }
                return result;
            }

            var total = 0.0f;
            for (var i = 0; i < selected.Count; i++)
            {
                var ratio = Mathf.Clamp(selected[i].Distance / firstDistance, 1.0f, 2.0f);
                var score = Mathf.Max(0.0f, 2.0f - ratio);
                score = Mathf.Pow(score, NearestJudgmentWeightFalloffPower);
                result.Add(score);
                total += score;
            }

            if (total <= 1e-6f)
            {
                result.Clear();
                result.Add(1.0f);
                for (var i = 1; i < selected.Count; i++) result.Add(0.0f);
                return result;
            }

            for (var i = 0; i < result.Count; i++)
            {
                result[i] /= total;
            }
            return result;
        }

        private static void SmoothGeneratedWeightsByTopology(
            Mesh mesh,
            BoneWeight[] weights,
            bool[] affectedVertices,
            HashSet<int> generatedIndices)
        {
            if (mesh == null || weights == null || affectedVertices == null || generatedIndices == null || generatedIndices.Count == 0) return;

            var adjacency = BuildMeshAdjacency(mesh, affectedVertices);
            if (adjacency.Count == 0) return;
            var vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0) return;
            var affectedYs = Enumerable.Range(0, Mathf.Min(vertices.Length, affectedVertices.Length))
                .Where(index => affectedVertices[index])
                .Select(index => vertices[index].y)
                .ToList();
            if (affectedYs.Count == 0) return;
            var verticalEpsilon = Mathf.Max(1e-5f, (affectedYs.Max() - affectedYs.Min()) * 0.002f);
            var current = new Dictionary<int, float>[weights.Length];
            var generatedSums = new float[weights.Length];
            for (var vi = 0; vi < weights.Length && vi < affectedVertices.Length; vi++)
            {
                if (!affectedVertices[vi]) continue;

                var pairs = ExtractPairs(weights[vi]);
                var generated = ExtractGeneratedWeights(pairs, generatedIndices);
                if (generated.Count == 0) continue;

                var sum = generated.Values.Sum();
                if (sum <= 1e-6f) continue;

                NormalizeDictionary(generated, sum);
                current[vi] = generated;
                generatedSums[vi] = sum;
            }

            for (var iteration = 0; iteration < TopologyWeightJaggednessCorrectionIterations; iteration++)
            {
                var next = CloneGeneratedWeights(current);
                for (var vi = 0; vi < current.Length && vi < vertices.Length; vi++)
                {
                    var generated = current[vi];
                    if (generated == null || !adjacency.TryGetValue(vi, out var neighbors)) continue;

                    foreach (var index in generated.Keys.ToList())
                    {
                        if (!TryAverageDirectionalGeneratedWeight(current, adjacency, vertices, neighbors, vi, index, true, verticalEpsilon, out var lowerAverage)) continue;
                        if (!TryAverageDirectionalGeneratedWeight(current, adjacency, vertices, neighbors, vi, index, false, verticalEpsilon, out var upperAverage)) continue;

                        var value = generated[index];
                        var isPeak = value > lowerAverage + TopologyWeightJaggednessThreshold
                            && value > upperAverage + TopologyWeightJaggednessThreshold;
                        var isValley = value + TopologyWeightJaggednessThreshold < lowerAverage
                            && value + TopologyWeightJaggednessThreshold < upperAverage;
                        if (!isPeak && !isValley) continue;

                        var target = (lowerAverage + upperAverage) * 0.5f;
                        next[vi][index] = Mathf.Lerp(value, target, TopologyWeightJaggednessCorrectionStrength);
                    }

                    NormalizeDictionary(next[vi], next[vi].Values.Sum());
                }

                current = next;
            }

            for (var vi = 0; vi < weights.Length && vi < current.Length; vi++)
            {
                if (current[vi] == null || generatedSums[vi] <= 1e-6f) continue;

                var pairs = ExtractPairs(weights[vi]);
                var preservedIndices = pairs
                    .Where(p => !generatedIndices.Contains(p.idx))
                    .Select(p => p.idx)
                    .Distinct()
                    .ToList();
                pairs.RemoveAll(p => generatedIndices.Contains(p.idx));
                foreach (var pair in current[vi])
                {
                    AddOrAccumulate(pairs, pair.Key, pair.Value * generatedSums[vi]);
                }
                TrimBoneWeights(pairs, preservedIndices);
                weights[vi] = ToBoneWeight(pairs);
            }
        }

        private static Dictionary<int, float>[] CloneGeneratedWeights(Dictionary<int, float>[] source)
        {
            if (source == null) return Array.Empty<Dictionary<int, float>>();

            var result = new Dictionary<int, float>[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] == null) continue;
                result[i] = new Dictionary<int, float>(source[i]);
            }

            return result;
        }

        private static bool TryAverageDirectionalGeneratedWeight(
            Dictionary<int, float>[] generatedWeightsByVertex,
            Dictionary<int, HashSet<int>> adjacency,
            Vector3[] vertices,
            HashSet<int> neighbors,
            int currentIndex,
            int generatedIndex,
            bool lower,
            float verticalEpsilon,
            out float average)
        {
            average = 0.0f;
            if (generatedWeightsByVertex == null || adjacency == null || vertices == null || neighbors == null) return false;
            if (currentIndex < 0 || currentIndex >= vertices.Length) return false;

            var sum = 0.0f;
            var weightSum = 0.0f;
            foreach (var neighbor in SelectDirectionalFlowVertices(adjacency, vertices, neighbors, currentIndex, lower, verticalEpsilon))
            {
                var vertexIndex = neighbor.Index;
                if (vertexIndex < 0 || vertexIndex >= generatedWeightsByVertex.Length) continue;
                if (generatedWeightsByVertex[vertexIndex] == null) continue;
                if (!generatedWeightsByVertex[vertexIndex].TryGetValue(generatedIndex, out var value)) value = 0.0f;

                var sampleWeight = 1.0f / neighbor.Depth;
                sum += value * sampleWeight;
                weightSum += sampleWeight;
            }

            if (weightSum <= 1e-6f) return false;
            average = sum / weightSum;
            return true;
        }

        private static List<DirectionalVertexSample> SelectDirectionalFlowVertices(
            Dictionary<int, HashSet<int>> adjacency,
            Vector3[] vertices,
            HashSet<int> neighbors,
            int currentIndex,
            bool lower,
            float verticalEpsilon)
        {
            var result = new List<DirectionalVertexSample>();
            if (adjacency == null || vertices == null || neighbors == null || currentIndex < 0 || currentIndex >= vertices.Length) return result;

            var current = vertices[currentIndex];
            var bestRatio = float.PositiveInfinity;
            foreach (var neighbor in neighbors)
            {
                if (!TryGetVerticalNeighborRatio(vertices, current, neighbor, lower, verticalEpsilon, out var ratio)) continue;
                if (ratio < bestRatio) bestRatio = ratio;
            }
            if (float.IsPositiveInfinity(bestRatio)) return result;

            var maxRatio = Mathf.Min(1.0f, bestRatio + 0.25f);
            var queue = new Queue<DirectionalVertexSample>();
            var visited = new HashSet<int> { currentIndex };
            foreach (var neighbor in neighbors)
            {
                if (!TryGetVerticalNeighborRatio(vertices, current, neighbor, lower, verticalEpsilon, out var ratio)) continue;
                if (ratio > maxRatio) continue;
                if (!visited.Add(neighbor)) continue;
                var sample = new DirectionalVertexSample(neighbor, 1);
                result.Add(sample);
                queue.Enqueue(sample);
            }

            while (queue.Count > 0)
            {
                var sample = queue.Dequeue();
                if (sample.Depth >= TopologyWeightDirectionalSearchDepth) continue;
                if (sample.Index < 0 || sample.Index >= vertices.Length) continue;

                var samplePosition = vertices[sample.Index];
                if (!adjacency.TryGetValue(sample.Index, out var sampleNeighbors)) continue;
                foreach (var next in sampleNeighbors)
                {
                    if (!TryGetVerticalNeighborRatio(vertices, samplePosition, next, lower, verticalEpsilon, out var ratio)) continue;
                    if (ratio > maxRatio) continue;
                    if (!visited.Add(next)) continue;

                    var nextSample = new DirectionalVertexSample(next, sample.Depth + 1);
                    result.Add(nextSample);
                    queue.Enqueue(nextSample);
                }
            }

            return result;
        }

        private static bool TryGetVerticalNeighborRatio(
            Vector3[] vertices,
            Vector3 current,
            int neighbor,
            bool lower,
            float verticalEpsilon,
            out float ratio)
        {
            ratio = 0.0f;
            if (vertices == null || neighbor < 0 || neighbor >= vertices.Length) return false;

            var delta = vertices[neighbor] - current;
            var dy = delta.y;
            if (lower && dy >= -verticalEpsilon) return false;
            if (!lower && dy <= verticalEpsilon) return false;

            var absY = Mathf.Abs(dy);
            var horizontal = new Vector2(delta.x, delta.z).magnitude;
            ratio = horizontal / Mathf.Max(absY, 1e-6f);
            return ratio <= 1.25f;
        }

        private static Dictionary<int, HashSet<int>> BuildMeshAdjacency(Mesh mesh, bool[] affectedVertices)
        {
            var adjacency = new Dictionary<int, HashSet<int>>();
            if (mesh == null || affectedVertices == null) return adjacency;

            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var indices = mesh.GetIndices(subMesh);
                var topology = mesh.GetTopology(subMesh);
                if (topology == MeshTopology.Triangles)
                {
                    for (var i = 0; i + 2 < indices.Length; i += 3)
                    {
                        AddAdjacencyTriangle(adjacency, affectedVertices, indices[i], indices[i + 1], indices[i + 2]);
                    }
                }
                else if (topology == MeshTopology.Quads)
                {
                    for (var i = 0; i + 3 < indices.Length; i += 4)
                    {
                        AddAdjacencyTriangle(adjacency, affectedVertices, indices[i], indices[i + 1], indices[i + 2]);
                        AddAdjacencyTriangle(adjacency, affectedVertices, indices[i], indices[i + 2], indices[i + 3]);
                    }
                }
            }

            return adjacency;
        }

        private static void AddAdjacencyTriangle(Dictionary<int, HashSet<int>> adjacency, bool[] affectedVertices, int a, int b, int c)
        {
            AddAdjacencyEdge(adjacency, affectedVertices, a, b);
            AddAdjacencyEdge(adjacency, affectedVertices, b, c);
            AddAdjacencyEdge(adjacency, affectedVertices, c, a);
        }

        private static void AddAdjacencyEdge(Dictionary<int, HashSet<int>> adjacency, bool[] affectedVertices, int a, int b)
        {
            if (!IsAffectedVertex(affectedVertices, a) || !IsAffectedVertex(affectedVertices, b)) return;
            if (!adjacency.TryGetValue(a, out var aSet))
            {
                aSet = new HashSet<int>();
                adjacency[a] = aSet;
            }
            if (!adjacency.TryGetValue(b, out var bSet))
            {
                bSet = new HashSet<int>();
                adjacency[b] = bSet;
            }

            aSet.Add(b);
            bSet.Add(a);
        }

        private static bool IsAffectedVertex(bool[] affectedVertices, int index)
        {
            return index >= 0 && index < affectedVertices.Length && affectedVertices[index];
        }

        private static Dictionary<int, float> ExtractGeneratedWeights(List<(int idx, float w)> pairs, HashSet<int> generatedIndices)
        {
            var result = new Dictionary<int, float>();
            if (pairs == null || generatedIndices == null) return result;

            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                if (pair.w <= 1e-6f || !generatedIndices.Contains(pair.idx)) continue;
                result[pair.idx] = result.TryGetValue(pair.idx, out var existing)
                    ? existing + pair.w
                    : pair.w;
            }

            return result;
        }

        private static void NormalizeDictionary(Dictionary<int, float> weights, float sum)
        {
            if (weights == null || sum <= 1e-6f) return;

            var keys = weights.Keys.ToList();
            foreach (var key in keys)
            {
                weights[key] /= sum;
            }
        }

        private static List<(int idx, float w)>[] ReadAllBoneWeightsByVertex(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount <= 0) return Array.Empty<List<(int idx, float w)>>();

            var result = new List<(int idx, float w)>[mesh.vertexCount];
            var bonesPerVertex = mesh.GetBonesPerVertex();
            var allWeights = mesh.GetAllBoneWeights();
            if (bonesPerVertex.Length == mesh.vertexCount && allWeights.Length > 0)
            {
                var weightIndex = 0;
                for (var vi = 0; vi < bonesPerVertex.Length; vi++)
                {
                    var pairs = new List<(int idx, float w)>();
                    var count = bonesPerVertex[vi];
                    for (var i = 0; i < count && weightIndex < allWeights.Length; i++, weightIndex++)
                    {
                        var weight = allWeights[weightIndex];
                        AddOrAccumulate(pairs, weight.boneIndex, weight.weight);
                    }
                    Normalize(pairs);
                    result[vi] = pairs;
                }
                return result;
            }

            var legacyWeights = mesh.boneWeights;
            for (var vi = 0; vi < result.Length; vi++)
            {
                result[vi] = legacyWeights != null && vi < legacyWeights.Length
                    ? ExtractPairs(legacyWeights[vi])
                    : new List<(int idx, float w)>();
            }
            return result;
        }

        private static void ApplyAllBoneWeights(Mesh mesh, List<(int idx, float w)>[] weightsByVertex)
        {
            if (mesh == null || weightsByVertex == null) return;

            var bonesPerVertex = new NativeArray<byte>(weightsByVertex.Length, Allocator.Temp);
            var allWeights = new List<BoneWeight1>();
            for (var vi = 0; vi < weightsByVertex.Length; vi++)
            {
                var pairs = weightsByVertex[vi] ?? new List<(int idx, float w)>();
                PruneListWeights(pairs, byte.MaxValue, 1e-8f);
                if (pairs.Count == 0)
                {
                    pairs.Add((0, 1.0f));
                }

                bonesPerVertex[vi] = (byte)Mathf.Min(byte.MaxValue, pairs.Count);
                for (var i = 0; i < pairs.Count && i < byte.MaxValue; i++)
                {
                    allWeights.Add(new BoneWeight1
                    {
                        boneIndex = pairs[i].idx,
                        weight = pairs[i].w
                    });
                }
            }

            var allWeightsArray = new NativeArray<BoneWeight1>(allWeights.Count, Allocator.Temp);
            for (var i = 0; i < allWeights.Count; i++)
            {
                allWeightsArray[i] = allWeights[i];
            }

            mesh.SetBoneWeights(bonesPerVertex, allWeightsArray);
            bonesPerVertex.Dispose();
            allWeightsArray.Dispose();
        }

        private static List<(int idx, float w)> BuildGeometricOutputWeightsForVertex(
            List<(int idx, float w)> originalPairs,
            Dictionary<int, float> generatedWeights,
            HashSet<int> replacedSourceIndices,
            HashSet<int> hipWeightIndices,
            HashSet<int> spineWeightIndices,
            HashSet<int> earlyLegWeightIndices,
            float hipCoverage,
            float spineCoverage,
            GeometricSkirtWeightSettings settings)
        {
            hipCoverage = Mathf.Clamp01(hipCoverage);
            spineCoverage = Mathf.Clamp01(spineCoverage);
            var pairs = originalPairs != null
                ? new List<(int idx, float w)>(originalPairs)
                : new List<(int idx, float w)>();
            var replacedTargetWeight = pairs
                .Where(p => p.w > 1e-6f && replacedSourceIndices.Contains(p.idx))
                .Sum(p => p.w);
            RemoveBones(pairs, replacedSourceIndices.ToList());

            var targetWeight = replacedTargetWeight;
            MoveBodyWeightsToSkirtByCoverage(pairs, earlyLegWeightIndices, 1.0f, ref targetWeight);
            MoveBodyWeightsToSkirtByCoverage(pairs, hipWeightIndices, hipCoverage, ref targetWeight);
            MoveBodyWeightsToSkirtByCoverage(pairs, spineWeightIndices, spineCoverage, ref targetWeight);

            var forceCoverage = Mathf.Max(hipCoverage, spineCoverage);
            if (settings.ForceGeneratedWeightsForTargetVertices && targetWeight <= 1e-6f && forceCoverage > 1e-6f)
            {
                targetWeight = forceCoverage;
            }

            if (settings.ForceGeneratedWeightsForTargetVertices && targetWeight >= 1.0f - 1e-6f)
            {
                pairs.Clear();
                targetWeight = 1.0f;
            }

            var generatedToAdd = LimitGeneratedWeightsToAvailableSlots(pairs, generatedWeights, settings.MaxInfluencesAfterPrune, settings.MinimumWeight);
            if (generatedToAdd.Count == 0)
            {
                PruneListWeights(pairs, settings.MaxInfluencesAfterPrune, settings.MinimumWeight);
                return pairs;
            }

            foreach (var pair in generatedToAdd)
            {
                AddOrAccumulate(pairs, pair.Key, pair.Value * targetWeight);
            }

            PruneListWeights(pairs, settings.MaxInfluencesAfterPrune, settings.MinimumWeight);
            return pairs;
        }

        private static void MoveBodyWeightsToSkirtByCoverage(
            List<(int idx, float w)> pairs,
            HashSet<int> boneIndices,
            float coverage,
            ref float targetWeight)
        {
            if (pairs == null || boneIndices == null || boneIndices.Count == 0 || coverage <= 1e-6f) return;

            foreach (var boneIndex in boneIndices.ToList())
            {
                var bodyWeight = RemoveBoneAndReturnWeight(pairs, boneIndex);
                if (bodyWeight <= 1e-6f) continue;

                var transfer = bodyWeight * Mathf.Clamp01(coverage);
                var retained = bodyWeight - transfer;
                if (retained > 1e-6f)
                {
                    AddOrAccumulate(pairs, boneIndex, retained);
                }

                targetWeight += transfer;
            }
        }

        private static Dictionary<int, float> LimitGeneratedWeightsToAvailableSlots(
            List<(int idx, float w)> pairs,
            Dictionary<int, float> generatedWeights,
            int maxInfluences,
            float minimumWeight)
        {
            var result = new Dictionary<int, float>();
            if (pairs == null || generatedWeights == null || generatedWeights.Count == 0) return result;

            var existingIndices = pairs
                .Where(p => p.w >= minimumWeight)
                .Select(p => p.idx)
                .Distinct()
                .ToList();
            var reusable = generatedWeights
                .Where(pair => existingIndices.Contains(pair.Key))
                .OrderByDescending(pair => pair.Value)
                .ToList();
            foreach (var pair in reusable)
            {
                result[pair.Key] = pair.Value;
            }

            var availableSlots = Mathf.Max(0, maxInfluences - existingIndices.Count - result.Count(pair => !existingIndices.Contains(pair.Key)));
            foreach (var pair in generatedWeights
                         .Where(pair => !existingIndices.Contains(pair.Key))
                         .OrderByDescending(pair => pair.Value))
            {
                if (availableSlots <= 0) break;
                result[pair.Key] = pair.Value;
                availableSlots--;
            }

            PruneAndNormalizeDictionary(result, Mathf.Max(1, maxInfluences), 1e-8f);
            return result;
        }

        private static bool[] BuildGeometricTargetVertices(
            Mesh mesh,
            Vector3[] vertices,
            List<(int idx, float w)>[] originalWeights,
            bool[] verticesInCoatWeightedSubmesh,
            HashSet<int> replacedSourceIndices,
            int hipIndex,
            int spineIndex,
            HashSet<int> protectedWeightIndices,
            GeometricSkirtWeightSettings settings,
            out bool[] sourceWeightedVertices)
        {
            var count = originalWeights != null ? originalWeights.Length : 0;
            var result = new bool[count];
            var seed = new bool[count];
            sourceWeightedVertices = seed;
            for (var vi = 0; vi < count; vi++)
            {
                if (!IsAffectedVertex(verticesInCoatWeightedSubmesh, vi)) continue;

                var pairs = originalWeights[vi];
                if (pairs == null || pairs.Count == 0) continue;

                var sourceWeight = pairs
                    .Where(p => p.w > 1e-6f && replacedSourceIndices.Contains(p.idx))
                    .Sum(p => p.w);
                var hasSourceWeight = sourceWeight >= GeometricTargetSourceMinimumWeight;
                var hasHipTransfer = hipIndex >= 0 && pairs.Any(p => p.idx == hipIndex && p.w > 1e-6f);
                var hasSpineTransfer = spineIndex >= 0 && pairs.Any(p => p.idx == spineIndex && p.w > 1e-6f);
                seed[vi] = hasSourceWeight;
                result[vi] = hasSourceWeight || hasHipTransfer || hasSpineTransfer;
            }

            if (settings.ExpandTargetVerticesGeometrically)
            {
                ExpandTargetVerticesFromSeeds(
                    mesh,
                    vertices,
                    verticesInCoatWeightedSubmesh,
                    seed,
                    result,
                    originalWeights,
                    protectedWeightIndices,
                    settings.TargetBoundsPadding);
            }

            return result;
        }

        private static void ExpandTargetVerticesFromSeeds(
            Mesh mesh,
            Vector3[] vertices,
            bool[] verticesInCoatWeightedSubmesh,
            bool[] seedVertices,
            bool[] targetVertices,
            List<(int idx, float w)>[] originalWeights,
            HashSet<int> protectedWeightIndices,
            float padding)
        {
            if (vertices == null || verticesInCoatWeightedSubmesh == null || seedVertices == null || targetVertices == null) return;

            var count = Mathf.Min(vertices.Length, Mathf.Min(seedVertices.Length, targetVertices.Length));
            var seedIndices = Enumerable.Range(0, count)
                .Where(i => seedVertices[i])
                .ToList();
            if (seedIndices.Count == 0) return;

            var minY = seedIndices.Min(i => vertices[i].y);
            var maxY = seedIndices.Max(i => vertices[i].y);
            var yRange = Mathf.Max(1e-5f, maxY - minY);
            var center = Vector2.zero;
            for (var i = 0; i < seedIndices.Count; i++)
            {
                var vertex = vertices[seedIndices[i]];
                center += new Vector2(vertex.x, vertex.z);
            }
            center /= seedIndices.Count;

            var maxRadius = 0.0f;
            for (var i = 0; i < seedIndices.Count; i++)
            {
                var vertex = vertices[seedIndices[i]];
                maxRadius = Mathf.Max(maxRadius, Vector2.Distance(new Vector2(vertex.x, vertex.z), center));
            }

            var yPadding = yRange * Mathf.Max(0.0f, padding);
            var radiusPadding = Mathf.Max(0.02f, maxRadius * Mathf.Max(0.0f, padding));
            var lowerY = minY - yPadding;
            var upperY = maxY + yPadding;
            var upperRadius = maxRadius + radiusPadding;
            var allVertices = Enumerable
                .Range(0, targetVertices.Length)
                .Select(_ => true)
                .ToArray();
            var connectedToSeeds = BuildVerticesConnectedToSeeds(mesh, allVertices, seedVertices);

            for (var vi = 0; vi < count; vi++)
            {
                if (connectedToSeeds != null && !IsAffectedVertex(connectedToSeeds, vi)) continue;
                if (!IsAffectedVertex(verticesInCoatWeightedSubmesh, vi)
                    && HasAnyProtectedWeight(originalWeights, vi, protectedWeightIndices))
                {
                    continue;
                }

                var vertex = vertices[vi];
                if (vertex.y < lowerY || vertex.y > upperY) continue;
                var radius = Vector2.Distance(new Vector2(vertex.x, vertex.z), center);
                if (radius > upperRadius) continue;

                targetVertices[vi] = true;
            }
        }

        private static bool[] BuildVerticesConnectedToSeeds(Mesh mesh, bool[] allowedVertices, bool[] seedVertices)
        {
            if (mesh == null || allowedVertices == null || seedVertices == null) return null;

            var adjacency = BuildMeshAdjacency(mesh, allowedVertices);
            if (adjacency.Count == 0) return null;

            var result = new bool[Mathf.Min(allowedVertices.Length, seedVertices.Length)];
            var queue = new Queue<int>();
            for (var i = 0; i < result.Length; i++)
            {
                if (!seedVertices[i] || !allowedVertices[i]) continue;
                result[i] = true;
                queue.Enqueue(i);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (var neighbor in neighbors)
                {
                    if (neighbor < 0 || neighbor >= result.Length) continue;
                    if (result[neighbor] || !allowedVertices[neighbor]) continue;
                    result[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            return result;
        }

        private static void FillGeometricTargetHolesByMesh(
            Mesh mesh,
            bool[] allowedVertices,
            bool[] targetVertices,
            List<(int idx, float w)>[] originalWeights,
            HashSet<int> protectedWeightIndices)
        {
            if (mesh == null || allowedVertices == null || targetVertices == null) return;

            var allVertices = Enumerable
                .Range(0, targetVertices.Length)
                .Select(_ => true)
                .ToArray();
            var adjacency = BuildMeshAdjacency(mesh, allVertices);
            if (adjacency.Count == 0) return;
            FillTargetHolesWithoutProtectedWeights(adjacency, targetVertices, originalWeights, protectedWeightIndices);

            var changed = true;
            while (changed)
            {
                changed = false;
                var additions = new List<int>();
                for (var vi = 0; vi < targetVertices.Length; vi++)
                {
                    if (targetVertices[vi]) continue;
                    if (HasAnyProtectedWeight(originalWeights, vi, protectedWeightIndices)) continue;
                    if (!adjacency.TryGetValue(vi, out var neighbors)) continue;

                    var targetNeighborCount = neighbors.Count(n => IsAffectedVertex(targetVertices, n));
                    if (targetNeighborCount < 2) continue;
                    additions.Add(vi);
                }

                if (additions.Count == 0) return;
                foreach (var vi in additions)
                {
                    targetVertices[vi] = true;
                }
                changed = true;
            }
        }

        private static void FillTargetHolesWithoutProtectedWeights(
            Dictionary<int, HashSet<int>> adjacency,
            bool[] targetVertices,
            List<(int idx, float w)>[] originalWeights,
            HashSet<int> protectedWeightIndices)
        {
            if (adjacency == null || targetVertices == null || originalWeights == null || protectedWeightIndices == null) return;

            var visited = new bool[targetVertices.Length];
            for (var start = 0; start < targetVertices.Length; start++)
            {
                if (visited[start] || targetVertices[start]) continue;

                var component = new List<int>();
                var queue = new Queue<int>();
                var touchesTarget = false;
                var hasProtectedWeight = false;
                visited[start] = true;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    component.Add(current);
                    if (HasAnyProtectedWeight(originalWeights, current, protectedWeightIndices)) hasProtectedWeight = true;
                    if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                    foreach (var neighbor in neighbors)
                    {
                        if (neighbor < 0 || neighbor >= targetVertices.Length) continue;
                        if (targetVertices[neighbor])
                        {
                            touchesTarget = true;
                            continue;
                        }

                        if (visited[neighbor]) continue;
                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }

                if (hasProtectedWeight || !touchesTarget) continue;
                foreach (var index in component)
                {
                    targetVertices[index] = true;
                }
            }
        }

        private static bool HasAnyProtectedWeight(
            List<(int idx, float w)>[] weights,
            int vertexIndex,
            HashSet<int> protectedWeightIndices)
        {
            if (weights == null || protectedWeightIndices == null || vertexIndex < 0 || vertexIndex >= weights.Length) return false;

            var pairs = weights[vertexIndex];
            return pairs != null && pairs.Any(pair => pair.w > 1e-6f && protectedWeightIndices.Contains(pair.idx));
        }

        private static void RestrictTargetVerticesToGeneratedBoneVerticalRange(
            bool[] targetVertices,
            bool[] protectedVertices,
            Vector3[] vertices,
            List<RendererChainInfo> rendererChainInfos,
            Transform rendererTransform)
        {
            if (targetVertices == null || vertices == null || rendererChainInfos == null || rendererTransform == null) return;

            var boneYs = rendererChainInfos
                .Where(info => info != null && info.FinalBones != null)
                .SelectMany(info => info.FinalBones)
                .Where(bone => bone != null)
                .Select(bone => rendererTransform.InverseTransformPoint(bone.position).y)
                .ToList();
            if (boneYs.Count == 0) return;

            var minY = boneYs.Min();
            var maxY = boneYs.Max();
            var padding = Mathf.Max(0.03f, (maxY - minY) * 0.2f);
            var upperY = maxY + padding;
            var count = Mathf.Min(targetVertices.Length, vertices.Length);
            for (var vi = 0; vi < count; vi++)
            {
                if (!targetVertices[vi]) continue;
                if (IsAffectedVertex(protectedVertices, vi)) continue;
                var y = vertices[vi].y;
                if (y > upperY)
                {
                    targetVertices[vi] = false;
                }
            }
        }

        private static bool TryBuildSkirtCoordinateFrame(Vector3[] vertices, bool[] targetVertices, out Vector2 centerXZ, out float maxY, out float yRange)
        {
            centerXZ = Vector2.zero;
            maxY = 0.0f;
            yRange = 0.0f;
            if (vertices == null || targetVertices == null) return false;

            var minY = float.PositiveInfinity;
            maxY = float.NegativeInfinity;
            var sum = Vector2.zero;
            var count = 0;
            for (var vi = 0; vi < vertices.Length && vi < targetVertices.Length; vi++)
            {
                if (!targetVertices[vi]) continue;

                var vertex = vertices[vi];
                sum += new Vector2(vertex.x, vertex.z);
                minY = Mathf.Min(minY, vertex.y);
                maxY = Mathf.Max(maxY, vertex.y);
                count++;
            }

            if (count == 0 || float.IsInfinity(minY) || float.IsInfinity(maxY) || float.IsNaN(minY) || float.IsNaN(maxY)) return false;

            centerXZ = sum / count;
            yRange = Mathf.Max(1e-5f, maxY - minY);
            return true;
        }

        private static List<GeometricBoneChain> BuildGeometricBoneChains(
            SkinnedMeshRenderer smr,
            List<RendererChainInfo> rendererChainInfos,
            Vector2 centerXZ,
            float maxY,
            float yRange,
            GeometricSkirtWeightSettings settings)
        {
            var result = new List<GeometricBoneChain>();
            if (smr == null || rendererChainInfos == null) return result;

            foreach (var info in rendererChainInfos)
            {
                if (info == null || info.FinalBones == null || info.FinalIndices == null) continue;

                var count = Mathf.Min(info.FinalBones.Count, info.FinalIndices.Count);
                if (count == 0) continue;

                if (!TryCalculateChainU(info.FinalBones, smr.transform, centerXZ, out var chainU)) continue;
                var localPositions = new Vector3[count];
                for (var i = 0; i < count; i++)
                {
                    localPositions[i] = info.FinalBones[i] != null
                        ? smr.transform.InverseTransformPoint(info.FinalBones[i].position)
                        : Vector3.zero;
                }

                var samples = new List<GeometricBoneSample>();
                for (var i = 0; i < count; i++)
                {
                    var bone = info.FinalBones[i];
                    if (bone == null || info.FinalIndices[i] < 0) continue;

                    var local = CalculateVerticalJudgmentPosition(localPositions, i, settings.VerticalJudgmentOffset);
                    var boneT = Mathf.Clamp01((maxY - local.y) / yRange);
                    var positionT = Mathf.Clamp01((maxY - localPositions[i].y) / yRange);
                    samples.Add(new GeometricBoneSample(info.FinalIndices[i], boneT, positionT, i));
                }

                samples = samples
                    .GroupBy(sample => sample.BoneIndex)
                    .Select(group => group.OrderBy(sample => sample.StageIndex).First())
                    .OrderBy(sample => sample.BoneT)
                    .ToList();
                if (samples.Count == 0) continue;

                result.Add(new GeometricBoneChain(chainU, samples));
            }

            return result.OrderBy(chain => chain.ChainU).ToList();
        }

        private static Vector3 CalculateVerticalJudgmentPosition(Vector3[] localPositions, int index, float offset)
        {
            if (localPositions == null || localPositions.Length == 0) return Vector3.zero;
            index = Mathf.Clamp(index, 0, localPositions.Length - 1);
            offset = Mathf.Clamp01(offset);

            var current = localPositions[index];
            if (localPositions.Length == 1 || offset <= 1e-6f) return current;

            if (index + 1 < localPositions.Length)
            {
                return Vector3.Lerp(current, localPositions[index + 1], offset);
            }

            return current + (current - localPositions[index - 1]) * offset;
        }

        private static bool TryCalculateChainU(List<Transform> bones, Transform rendererTransform, Vector2 centerXZ, out float chainU)
        {
            chainU = 0.0f;
            if (bones == null || rendererTransform == null) return false;

            var locals = bones
                .Where(bone => bone != null)
                .Select(bone => rendererTransform.InverseTransformPoint(bone.position))
                .ToList();
            if (locals.Count == 0) return false;

            var minY = locals.Min(p => p.y);
            var maxY = locals.Max(p => p.y);
            var lowerHalfThreshold = Mathf.Lerp(maxY, minY, 0.5f);
            var selected = locals
                .Where(p => p.y <= lowerHalfThreshold)
                .ToList();
            if (selected.Count == 0)
            {
                selected = locals;
            }

            var sum = Vector2.zero;
            for (var i = 0; i < selected.Count; i++)
            {
                sum += new Vector2(selected[i].x, selected[i].z);
            }

            var average = sum / selected.Count;
            chainU = CalculateCircularU(new Vector3(average.x, 0.0f, average.y), centerXZ);
            return true;
        }

        private static Dictionary<int, float>[] GenerateGeometricSkirtWeights(
            Vector3[] vertices,
            bool[] targetVertices,
            List<GeometricBoneChain> boneChains,
            Vector2 centerXZ,
            float maxY,
            float yRange,
            GeometricSkirtWeightSettings settings,
            float hipWeightReduction,
            float spineWeightReduction,
            bool allowCoverageAboveFirstBone,
            out float[] hipCoverages,
            out float[] spineCoverages)
        {
            var result = new Dictionary<int, float>[vertices != null ? vertices.Length : 0];
            hipCoverages = new float[result.Length];
            spineCoverages = new float[result.Length];
            if (vertices == null || targetVertices == null || boneChains == null || boneChains.Count == 0) return result;

            for (var vi = 0; vi < vertices.Length && vi < targetVertices.Length; vi++)
            {
                if (!targetVertices[vi]) continue;

                var vertex = vertices[vi];
                var vertexU = CalculateCircularU(vertex, centerXZ);
                var vertexT = Mathf.Clamp01((maxY - vertex.y) / yRange);
                var weights = CalculateLocalBilinearWeights(vertexU, vertexT, boneChains, settings);
                PruneAndNormalizeDictionary(weights, settings.MaxInfluencesBeforePrune, 1e-8f);
                result[vi] = weights;
                hipCoverages[vi] = CalculateGeometricSkirtCoverage(vertexU, vertexT, boneChains, settings, hipWeightReduction, allowCoverageAboveFirstBone);
                spineCoverages[vi] = CalculateGeometricSkirtCoverage(vertexU, vertexT, boneChains, settings, spineWeightReduction, allowCoverageAboveFirstBone);
            }

            return result;
        }

        private static float CalculateGeometricSkirtCoverage(
            float vertexU,
            float vertexT,
            List<GeometricBoneChain> chains,
            GeometricSkirtWeightSettings settings,
            float hipWeightReduction,
            bool allowCoverageAboveFirstBone)
        {
            if (chains == null || chains.Count == 0) return 0.0f;

            var firstStageT = GetInterpolatedFirstStageT(vertexU, chains, settings);
            var firstPositionT = GetInterpolatedFirstPositionT(vertexU, chains, settings);
            var span = Mathf.Max(1e-5f, firstStageT - firstPositionT);
            var coverageStartT = allowCoverageAboveFirstBone
                ? firstPositionT - span * LongCoatUpperSkirtCoverageVirtualSpanFactor
                : firstPositionT;
            if (vertexT <= coverageStartT) return 0.0f;
            if (vertexT >= firstStageT) return 1.0f;

            var blend = Mathf.Clamp01((vertexT - coverageStartT) / Mathf.Max(1e-5f, firstStageT - coverageStartT));
            var exponent = Mathf.Lerp(1.8f, 0.35f, Mathf.Clamp01(hipWeightReduction));
            return Mathf.Pow(blend, exponent);
        }

        private static float GetInterpolatedFirstStageT(
            float vertexU,
            List<GeometricBoneChain> chains,
            GeometricSkirtWeightSettings settings)
        {
            if (chains == null || chains.Count == 0) return 0.0f;
            if (chains.Count == 1) return GetFirstStageT(chains[0]);

            FindCircularChainPair(chains, vertexU, out var left, out var right, out var angularBlend);
            angularBlend = ApplySmoothAngularBlend(angularBlend, settings.AngularBlendSmoothing);
            return Mathf.Lerp(GetFirstStageT(left), GetFirstStageT(right), angularBlend);
        }

        private static float GetInterpolatedFirstPositionT(
            float vertexU,
            List<GeometricBoneChain> chains,
            GeometricSkirtWeightSettings settings)
        {
            if (chains == null || chains.Count == 0) return 0.0f;
            if (chains.Count == 1) return GetFirstPositionT(chains[0]);

            FindCircularChainPair(chains, vertexU, out var left, out var right, out var angularBlend);
            angularBlend = ApplySmoothAngularBlend(angularBlend, settings.AngularBlendSmoothing);
            return Mathf.Lerp(GetFirstPositionT(left), GetFirstPositionT(right), angularBlend);
        }

        private static float GetFirstStageT(GeometricBoneChain chain)
        {
            if (chain == null || chain.Samples == null || chain.Samples.Count == 0) return 0.0f;
            return chain.Samples[0].BoneT;
        }

        private static float GetFirstPositionT(GeometricBoneChain chain)
        {
            if (chain == null || chain.Samples == null || chain.Samples.Count == 0) return 0.0f;
            return chain.Samples[0].PositionT;
        }

        private static float GetSkirtCoverage(float[] coverages, int index)
        {
            return coverages != null && index >= 0 && index < coverages.Length
                ? Mathf.Clamp01(coverages[index])
                : 0.0f;
        }

        private static void SmoothSkirtCoveragesByMesh(Mesh mesh, bool[] targetVertices, float[] coverages)
        {
            if (mesh == null || targetVertices == null || coverages == null) return;

            var adjacency = BuildMeshAdjacency(mesh, targetVertices);
            if (adjacency.Count == 0) return;

            for (var iteration = 0; iteration < 2; iteration++)
            {
                var next = (float[])coverages.Clone();
                for (var vi = 0; vi < coverages.Length && vi < targetVertices.Length; vi++)
                {
                    if (!targetVertices[vi]) continue;
                    if (coverages[vi] <= 1e-6f) continue;
                    if (!adjacency.TryGetValue(vi, out var neighbors)) continue;
                    if (neighbors.Any(n => n >= 0 && n < coverages.Length && IsAffectedVertex(targetVertices, n) && coverages[n] <= 1e-6f)) continue;

                    var neighborValues = neighbors
                        .Where(n => n >= 0 && n < coverages.Length && IsAffectedVertex(targetVertices, n))
                        .Select(n => coverages[n])
                        .Where(value => value > 1e-6f)
                        .ToList();
                    if (neighborValues.Count < 2) continue;

                    var average = neighborValues.Average();
                    if (coverages[vi] >= average) continue;
                    next[vi] = Mathf.Max(coverages[vi], average);
                }

                Array.Copy(next, coverages, coverages.Length);
            }
        }

        private static Dictionary<int, float> CalculateLocalBilinearWeights(
            float vertexU,
            float vertexT,
            List<GeometricBoneChain> chains,
            GeometricSkirtWeightSettings settings)
        {
            var result = new Dictionary<int, float>();
            if (chains == null || chains.Count == 0) return result;

            if (chains.Count == 1)
            {
                AddVerticalBilinearWeights(result, chains[0], vertexT, 1.0f);
                NormalizeDictionary(result, result.Values.Sum());
                return result;
            }

            FindCircularChainPair(chains, vertexU, out var left, out var right, out var angularBlend);
            angularBlend = ApplySmoothAngularBlend(angularBlend, settings.AngularBlendSmoothing);
            AddVerticalBilinearWeights(result, left, vertexT, 1.0f - angularBlend);
            AddVerticalBilinearWeights(result, right, vertexT, angularBlend);
            NormalizeDictionary(result, result.Values.Sum());
            return result;
        }

        private static float ApplySmoothAngularBlend(float blend, float smoothing)
        {
            blend = Mathf.Clamp01(blend);
            smoothing = Mathf.Clamp01(smoothing);
            if (smoothing <= 1e-6f) return blend;

            var smooth = blend * blend * (3.0f - 2.0f * blend);
            return Mathf.Lerp(blend, smooth, smoothing);
        }

        private static void FindCircularChainPair(
            List<GeometricBoneChain> chains,
            float vertexU,
            out GeometricBoneChain left,
            out GeometricBoneChain right,
            out float blend)
        {
            left = chains[chains.Count - 1];
            right = chains[0];

            for (var i = 0; i < chains.Count; i++)
            {
                var current = chains[i];
                var next = chains[(i + 1) % chains.Count];
                var currentU = current.ChainU;
                var nextU = next.ChainU;
                var unwrappedNextU = nextU <= currentU ? nextU + 1.0f : nextU;
                var unwrappedVertexU = vertexU < currentU ? vertexU + 1.0f : vertexU;
                if (unwrappedVertexU < currentU || unwrappedVertexU > unwrappedNextU) continue;

                left = current;
                right = next;
                var span = Mathf.Max(1e-5f, unwrappedNextU - currentU);
                blend = Mathf.Clamp01((unwrappedVertexU - currentU) / span);
                return;
            }

            var fallbackSpan = Mathf.Max(1e-5f, right.ChainU + 1.0f - left.ChainU);
            var fallbackU = vertexU < left.ChainU ? vertexU + 1.0f : vertexU;
            blend = Mathf.Clamp01((fallbackU - left.ChainU) / fallbackSpan);
        }

        private static void AddVerticalBilinearWeights(
            Dictionary<int, float> result,
            GeometricBoneChain chain,
            float vertexT,
            float chainWeight)
        {
            if (result == null || chain == null || chain.Samples == null || chain.Samples.Count == 0 || chainWeight <= 1e-8f) return;

            if (chain.Samples.Count == 1 || vertexT <= chain.Samples[0].BoneT)
            {
                AddOrAccumulateDictionary(result, chain.Samples[0].BoneIndex, chainWeight);
                return;
            }

            var last = chain.Samples[chain.Samples.Count - 1];
            if (vertexT >= last.BoneT)
            {
                AddOrAccumulateDictionary(result, last.BoneIndex, chainWeight);
                return;
            }

            for (var i = 0; i + 1 < chain.Samples.Count; i++)
            {
                var upper = chain.Samples[i];
                var lower = chain.Samples[i + 1];
                if (vertexT < upper.BoneT || vertexT > lower.BoneT) continue;

                var lowerPositionT = Mathf.Clamp(lower.PositionT, upper.BoneT, lower.BoneT);
                if (lowerPositionT > upper.BoneT + 1e-5f && vertexT <= lowerPositionT)
                {
                    var blend = Mathf.Clamp01((vertexT - upper.BoneT) / (lowerPositionT - upper.BoneT));
                    AddOrAccumulateDictionary(result, upper.BoneIndex, chainWeight * Mathf.Lerp(1.0f, 0.5f, blend));
                    AddOrAccumulateDictionary(result, lower.BoneIndex, chainWeight * Mathf.Lerp(0.0f, 0.5f, blend));
                    return;
                }

                if (lower.BoneT > lowerPositionT + 1e-5f)
                {
                    var blend = Mathf.Clamp01((vertexT - lowerPositionT) / (lower.BoneT - lowerPositionT));
                    AddOrAccumulateDictionary(result, upper.BoneIndex, chainWeight * Mathf.Lerp(0.5f, 0.0f, blend));
                    AddOrAccumulateDictionary(result, lower.BoneIndex, chainWeight * Mathf.Lerp(0.5f, 1.0f, blend));
                    return;
                }

                AddOrAccumulateDictionary(result, upper.BoneIndex, chainWeight * 0.5f);
                AddOrAccumulateDictionary(result, lower.BoneIndex, chainWeight * 0.5f);
                return;
            }

            var nearest = chain.Samples
                .OrderBy(sample => Mathf.Abs(sample.BoneT - vertexT))
                .First();
            AddOrAccumulateDictionary(result, nearest.BoneIndex, chainWeight);
        }

        private static void SmoothGeometricWeightsByRings(
            Dictionary<int, float>[] weights,
            Vector3[] vertices,
            bool[] targetVertices,
            Vector2 centerXZ,
            float maxY,
            float yRange,
            GeometricSkirtWeightSettings settings)
        {
            if (weights == null || vertices == null || targetVertices == null || settings.RingSmoothIterations <= 0) return;

            var ringCount = Mathf.Max(1, settings.RingCount);
            var rings = new List<GeometricRingVertex>[ringCount];
            for (var i = 0; i < ringCount; i++) rings[i] = new List<GeometricRingVertex>();

            for (var vi = 0; vi < vertices.Length && vi < targetVertices.Length; vi++)
            {
                if (!targetVertices[vi] || weights[vi] == null || weights[vi].Count == 0) continue;

                var u = CalculateCircularU(vertices[vi], centerXZ);
                var t = Mathf.Clamp01((maxY - vertices[vi].y) / yRange);
                var ring = Mathf.Clamp(Mathf.FloorToInt(t * ringCount), 0, ringCount - 1);
                rings[ring].Add(new GeometricRingVertex(vi, u));
            }

            foreach (var ring in rings)
            {
                ring.Sort((a, b) => a.U.CompareTo(b.U));
            }

            for (var iteration = 0; iteration < settings.RingSmoothIterations; iteration++)
            {
                var next = CloneGeneratedWeights(weights);
                for (var ringIndex = 0; ringIndex < rings.Length; ringIndex++)
                {
                    var ring = rings[ringIndex];
                    if (ring.Count < 2) continue;

                    for (var i = 0; i < ring.Count; i++)
                    {
                        var current = ring[i].Index;
                        var prev = ring[(i - 1 + ring.Count) % ring.Count].Index;
                        var nextIndex = ring[(i + 1) % ring.Count].Index;
                        next[current] = BlendWeightDictionaries(
                            weights[current],
                            settings.RingSmoothCenterWeight,
                            weights[prev],
                            settings.RingSmoothNeighborWeight,
                            weights[nextIndex],
                            settings.RingSmoothNeighborWeight);
                        PruneAndNormalizeDictionary(next[current], settings.MaxInfluencesBeforePrune, 1e-8f);
                    }
                }

                weights = CopyGeneratedWeights(next, weights);
            }
        }

        private static void SmoothGeometricWeightsByMesh(
            Mesh mesh,
            Dictionary<int, float>[] weights,
            bool[] targetVertices,
            GeometricSkirtWeightSettings settings)
        {
            if (mesh == null || weights == null || targetVertices == null || settings.MeshSmoothIterations <= 0 || settings.MeshSmoothBlend <= 1e-6f) return;

            var adjacency = BuildMeshAdjacency(mesh, targetVertices);
            if (adjacency.Count == 0) return;

            for (var iteration = 0; iteration < settings.MeshSmoothIterations; iteration++)
            {
                var next = CloneGeneratedWeights(weights);
                foreach (var item in adjacency)
                {
                    var vi = item.Key;
                    if (vi < 0 || vi >= weights.Length || weights[vi] == null || weights[vi].Count == 0) continue;

                    var average = new Dictionary<int, float>();
                    var neighborCount = 0;
                    foreach (var neighbor in item.Value)
                    {
                        if (neighbor < 0 || neighbor >= weights.Length || weights[neighbor] == null || weights[neighbor].Count == 0) continue;

                        AddScaledWeights(average, weights[neighbor], 1.0f);
                        neighborCount++;
                    }

                    if (neighborCount == 0) continue;
                    ScaleWeights(average, 1.0f / neighborCount);
                    next[vi] = LerpWeightDictionaries(weights[vi], average, settings.MeshSmoothBlend);
                    PruneAndNormalizeDictionary(next[vi], settings.MaxInfluencesBeforePrune, 1e-8f);
                }

                weights = CopyGeneratedWeights(next, weights);
            }
        }

        private static Dictionary<int, float>[] CopyGeneratedWeights(Dictionary<int, float>[] source, Dictionary<int, float>[] destination)
        {
            if (source == null || destination == null) return destination;

            var count = Mathf.Min(source.Length, destination.Length);
            for (var i = 0; i < count; i++)
            {
                destination[i] = source[i];
            }
            return destination;
        }

        private static Dictionary<int, float> BlendWeightDictionaries(
            Dictionary<int, float> center,
            float centerWeight,
            Dictionary<int, float> prev,
            float prevWeight,
            Dictionary<int, float> next,
            float nextWeight)
        {
            var result = new Dictionary<int, float>();
            AddScaledWeights(result, center, centerWeight);
            AddScaledWeights(result, prev, prevWeight);
            AddScaledWeights(result, next, nextWeight);
            return result;
        }

        private static Dictionary<int, float> LerpWeightDictionaries(Dictionary<int, float> from, Dictionary<int, float> to, float blend)
        {
            var result = new Dictionary<int, float>();
            AddScaledWeights(result, from, 1.0f - Mathf.Clamp01(blend));
            AddScaledWeights(result, to, Mathf.Clamp01(blend));
            return result;
        }

        private static void AddScaledWeights(Dictionary<int, float> destination, Dictionary<int, float> source, float scale)
        {
            if (destination == null || source == null || scale <= 0.0f) return;

            foreach (var pair in source)
            {
                if (pair.Value <= 0.0f) continue;
                destination[pair.Key] = destination.TryGetValue(pair.Key, out var existing)
                    ? existing + pair.Value * scale
                    : pair.Value * scale;
            }
        }

        private static void AddOrAccumulateDictionary(Dictionary<int, float> weights, int index, float weight)
        {
            if (weights == null || index < 0 || weight <= 0.0f) return;

            weights[index] = weights.TryGetValue(index, out var existing)
                ? existing + weight
                : weight;
        }

        private static void ScaleWeights(Dictionary<int, float> weights, float scale)
        {
            if (weights == null) return;

            var keys = weights.Keys.ToList();
            foreach (var key in keys)
            {
                weights[key] *= scale;
            }
        }

        private static void PruneAndNormalizeDictionary(Dictionary<int, float> weights, int maxInfluences, float minimumWeight)
        {
            if (weights == null || weights.Count == 0) return;

            var sorted = weights
                .Where(p => p.Value >= minimumWeight)
                .OrderByDescending(p => p.Value)
                .Take(Mathf.Max(1, maxInfluences))
                .ToList();
            weights.Clear();
            var sum = sorted.Sum(p => p.Value);
            if (sum <= 1e-8f) return;

            foreach (var pair in sorted)
            {
                weights[pair.Key] = pair.Value / sum;
            }
        }

        private static void PruneListWeights(List<(int idx, float w)> pairs, int maxInfluences, float minimumWeight)
        {
            if (pairs == null) return;

            var sorted = pairs
                .Where(p => p.idx >= 0 && p.w >= minimumWeight)
                .GroupBy(p => p.idx)
                .Select(g => (idx: g.Key, w: g.Sum(p => p.w)))
                .OrderByDescending(p => p.w)
                .Take(Mathf.Max(1, maxInfluences))
                .ToList();
            pairs.Clear();
            pairs.AddRange(sorted);
            Normalize(pairs);
            pairs.Sort((x, y) => y.w.CompareTo(x.w));
        }

        private static float CalculateCircularU(Vector3 localPosition, Vector2 centerXZ)
        {
            var angle = Mathf.Atan2(localPosition.z - centerXZ.y, localPosition.x - centerXZ.x);
            var u = angle / (Mathf.PI * 2.0f);
            if (u < 0.0f) u += 1.0f;
            return u;
        }

        private static float CircularDistance01(float a, float b)
        {
            var distance = Mathf.Abs(a - b);
            return Mathf.Min(distance, 1.0f - distance);
        }

        private static float AngularKernel(float vertexU, float chainU, float sigma)
        {
            sigma = Mathf.Max(1e-4f, sigma);
            var distance = CircularDistance01(vertexU, chainU);
            return Mathf.Exp(-(distance * distance) / (2.0f * sigma * sigma));
        }

        private static float VerticalKernel(float vertexT, float boneT, float radius)
        {
            radius = Mathf.Max(1e-4f, radius);
            var x = Mathf.Abs(vertexT - boneT) / radius;
            if (x >= 1.0f) return 0.0f;

            var s = 1.0f - x;
            return s * s * (3.0f - 2.0f * s);
        }

        private static bool[] BuildVerticesInCoatWeightedSubmeshes(
            Mesh mesh,
            List<(int idx, float w)>[] weights,
            List<RendererChainInfo> rendererChainInfos)
        {
            var result = new bool[mesh != null ? mesh.vertexCount : 0];
            if (mesh == null || weights == null || weights.Length == 0 || rendererChainInfos == null || rendererChainInfos.Count == 0) return result;

            var coatBoneIndices = new HashSet<int>(
                rendererChainInfos
                    .SelectMany(info => info.ChainOriginalIndices)
                    .Where(i => i >= 0));
            if (coatBoneIndices.Count == 0) return result;

            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var indices = mesh.GetIndices(subMesh);
                var hasCoatWeight = false;
                for (var i = 0; i < indices.Length; i++)
                {
                    var vertexIndex = indices[i];
                    if (vertexIndex < 0 || vertexIndex >= weights.Length) continue;
                    var pairs = weights[vertexIndex];
                    if (pairs == null || !pairs.Any(p => p.w > 1e-6f && coatBoneIndices.Contains(p.idx))) continue;

                    hasCoatWeight = true;
                    break;
                }

                if (!hasCoatWeight) continue;

                for (var i = 0; i < indices.Length; i++)
                {
                    var vertexIndex = indices[i];
                    if (vertexIndex < 0 || vertexIndex >= result.Length) continue;
                    result[vertexIndex] = true;
                }
            }

            return result;
        }

        private static bool[] BuildVerticesInCoatWeightedSubmeshes(
            Mesh mesh,
            BoneWeight[] weights,
            List<RendererChainInfo> rendererChainInfos)
        {
            var result = new bool[mesh != null ? mesh.vertexCount : 0];
            if (mesh == null || weights == null || weights.Length == 0 || rendererChainInfos == null || rendererChainInfos.Count == 0) return result;

            var coatBoneIndices = new HashSet<int>(
                rendererChainInfos
                    .SelectMany(info => info.ChainOriginalIndices)
                    .Where(i => i >= 0));
            if (coatBoneIndices.Count == 0) return result;

            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var indices = mesh.GetIndices(subMesh);
                var hasCoatWeight = false;
                for (var i = 0; i < indices.Length; i++)
                {
                    var vertexIndex = indices[i];
                    if (vertexIndex < 0 || vertexIndex >= weights.Length) continue;
                    if (!HasWeightForAnyIndex(weights[vertexIndex], coatBoneIndices)) continue;

                    hasCoatWeight = true;
                    break;
                }

                if (!hasCoatWeight) continue;

                for (var i = 0; i < indices.Length; i++)
                {
                    var vertexIndex = indices[i];
                    if (vertexIndex < 0 || vertexIndex >= result.Length) continue;
                    result[vertexIndex] = true;
                }
            }

            return result;
        }

        private static bool HasWeightForAnyIndex(BoneWeight weight, HashSet<int> boneIndices)
        {
            if (boneIndices == null || boneIndices.Count == 0) return false;

            return (weight.weight0 > 1e-6f && boneIndices.Contains(weight.boneIndex0))
                || (weight.weight1 > 1e-6f && boneIndices.Contains(weight.boneIndex1))
                || (weight.weight2 > 1e-6f && boneIndices.Contains(weight.boneIndex2))
                || (weight.weight3 > 1e-6f && boneIndices.Contains(weight.boneIndex3));
        }

        private static Transform FindDescendantByPartialName(Transform root, string partialName)
        {
            if (root == null || string.IsNullOrEmpty(partialName)) return null;

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || string.IsNullOrEmpty(transform.name)) continue;
                if (transform.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return transform;
                }
            }

            return null;
        }

        private static bool IsAncestorOf(Transform ancestor, Transform target)
        {
            var current = target;
            while (current != null)
            {
                if (current == ancestor) return true;
                current = current.parent;
            }

            return false;
        }

        private static float ExtractTransferWeight(List<(int idx, float w)> pairs, int boneIndex, float reduction)
        {
            if (pairs == null || boneIndex < 0 || reduction <= 1e-6f) return 0.0f;

            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                if (pair.idx != boneIndex || pair.w <= 1e-6f) continue;

                var transfer = pair.w * Mathf.Clamp01(reduction);
                var remaining = pair.w - transfer;
                if (remaining <= 1e-6f)
                {
                    pairs.RemoveAt(i);
                }
                else
                {
                    pairs[i] = (pair.idx, remaining);
                }

                return transfer;
            }

            return 0.0f;
        }

        private static float RemoveBoneAndReturnWeight(List<(int idx, float w)> pairs, int boneIndex)
        {
            if (pairs == null || boneIndex < 0) return 0.0f;

            var removed = 0.0f;
            for (var i = pairs.Count - 1; i >= 0; i--)
            {
                if (pairs[i].idx != boneIndex) continue;
                removed += Mathf.Max(0.0f, pairs[i].w);
                pairs.RemoveAt(i);
            }

            return removed;
        }

        private static List<(int idx, float w)> ExtractPairs(BoneWeight bw)
        {
            var pairs = new List<(int idx, float w)>(4);
            AddOrAccumulate(pairs, bw.boneIndex0, bw.weight0);
            AddOrAccumulate(pairs, bw.boneIndex1, bw.weight1);
            AddOrAccumulate(pairs, bw.boneIndex2, bw.weight2);
            AddOrAccumulate(pairs, bw.boneIndex3, bw.weight3);
            return pairs;
        }

        private static float SumWeightForIndices(List<(int idx, float w)> pairs, List<int> indices)
        {
            var sum = 0f;
            for (var i = 0; i < pairs.Count; i++)
            {
                if (indices.Contains(pairs[i].idx)) sum += pairs[i].w;
            }
            return sum;
        }

        private static void RemoveBones(List<(int idx, float w)> pairs, List<int> indices)
        {
            pairs.RemoveAll(p => indices.Contains(p.idx));
        }

        private static void AddOrAccumulate(List<(int idx, float w)> pairs, int idx, float w)
        {
            if (idx < 0 || w <= 0f) return;
            for (var i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].idx != idx) continue;
                pairs[i] = (idx, pairs[i].w + w);
                return;
            }
            pairs.Add((idx, w));
        }

        private static void Normalize(List<(int idx, float w)> pairs)
        {
            var sum = 0f;
            for (var i = 0; i < pairs.Count; i++) sum += pairs[i].w;
            if (sum <= 1e-6f) return;
            for (var i = 0; i < pairs.Count; i++) pairs[i] = (pairs[i].idx, pairs[i].w / sum);
        }

        private static void TrimBoneWeights(List<(int idx, float w)> pairs, List<int> preservedSourceIndices)
        {
            if (pairs == null || pairs.Count <= 4)
            {
                PreserveSourceWeightsAndNormalizeGenerated(pairs, preservedSourceIndices);
                return;
            }

            var preserved = pairs
                .Where(p => p.w > 1e-6f && preservedSourceIndices != null && preservedSourceIndices.Contains(p.idx))
                .OrderByDescending(p => p.w)
                .ToList();
            var generated = pairs
                .Where(p => p.w > 1e-6f && (preservedSourceIndices == null || !preservedSourceIndices.Contains(p.idx)))
                .OrderByDescending(p => p.w)
                .ToList();
            var preservedTargetSum = preserved.Sum(p => p.w);
            var generatedTargetSum = generated.Sum(p => p.w);
            var generatedSlots = ResolveGeneratedWeightSlots(generated, generatedTargetSum, preserved.Count);
            var preservedSlots = Mathf.Max(0, 4 - generatedSlots);

            pairs.Clear();
            foreach (var pair in preserved.Take(preservedSlots))
            {
                pairs.Add(pair);
            }

            foreach (var pair in generated.Take(generatedSlots))
            {
                pairs.Add(pair);
            }

            if (pairs.Count < 4)
            {
                foreach (var pair in preserved.Skip(preservedSlots))
                {
                    if (pairs.Count >= 4) break;
                    pairs.Add(pair);
                }
            }

            if (pairs.Count < 4)
            {
                foreach (var pair in generated.Skip(generatedSlots))
                {
                    if (pairs.Count >= 4) break;
                    pairs.Add(pair);
                }
            }

            PreserveSourceWeightsAndNormalizeGenerated(pairs, preservedSourceIndices, generatedTargetSum, preservedTargetSum);
        }

        private static int ResolveGeneratedWeightSlots(List<(int idx, float w)> generated, float generatedTargetSum, int preservedCount)
        {
            if (generated == null || generated.Count == 0) return 0;
            if (generated.Count == 1) return 1;
            if (preservedCount >= 3 && generatedTargetSum < 0.08f) return 1;

            var secondWeightRatio = generatedTargetSum > 1e-6f ? generated[1].w / generatedTargetSum : 0f;
            return secondWeightRatio >= 0.05f ? 2 : 1;
        }

        private static void PreserveSourceWeightsAndNormalizeGenerated(
            List<(int idx, float w)> pairs,
            List<int> preservedSourceIndices,
            float? generatedTargetSumOverride = null,
            float? preservedTargetSumOverride = null)
        {
            if (pairs == null || pairs.Count == 0) return;

            var generatedIndices = preservedSourceIndices != null
                ? pairs
                    .Where(p => !preservedSourceIndices.Contains(p.idx))
                    .Select(p => p.idx)
                    .Distinct()
                    .ToList()
                : pairs.Select(p => p.idx).Distinct().ToList();

            var generatedSum = SumWeightForIndices(pairs, generatedIndices);
            var generatedTargetSum = generatedTargetSumOverride ?? generatedSum;
            if (generatedSum > 1e-6f && generatedTargetSum > 1e-6f)
            {
                var scale = generatedTargetSum / generatedSum;
                for (var i = 0; i < pairs.Count; i++)
                {
                    if (!generatedIndices.Contains(pairs[i].idx)) continue;
                    pairs[i] = (pairs[i].idx, pairs[i].w * scale);
                }
            }

            if (preservedSourceIndices != null && preservedTargetSumOverride.HasValue)
            {
                var preservedIndices = pairs
                    .Where(p => preservedSourceIndices.Contains(p.idx))
                    .Select(p => p.idx)
                    .Distinct()
                    .ToList();
                var preservedSum = SumWeightForIndices(pairs, preservedIndices);
                var preservedTargetSum = preservedTargetSumOverride.Value;
                if (preservedSum > 1e-6f && preservedTargetSum > 1e-6f)
                {
                    var scale = preservedTargetSum / preservedSum;
                    for (var i = 0; i < pairs.Count; i++)
                    {
                        if (!preservedIndices.Contains(pairs[i].idx)) continue;
                        pairs[i] = (pairs[i].idx, pairs[i].w * scale);
                    }
                }
            }

            pairs.Sort((x, y) => y.w.CompareTo(x.w));
        }

        private static BoneWeight ToBoneWeight(List<(int idx, float w)> pairs)
        {
            var bw = new BoneWeight();
            if (pairs.Count > 0) { bw.boneIndex0 = pairs[0].idx; bw.weight0 = pairs[0].w; }
            if (pairs.Count > 1) { bw.boneIndex1 = pairs[1].idx; bw.weight1 = pairs[1].w; }
            if (pairs.Count > 2) { bw.boneIndex2 = pairs[2].idx; bw.weight2 = pairs[2].w; }
            if (pairs.Count > 3) { bw.boneIndex3 = pairs[3].idx; bw.weight3 = pairs[3].w; }
            return bw;
        }

        private static void RemoveComponents(YMVRoidSkirtRefine[] components)
        {
            if (components == null) return;

            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null) continue;
                Object.DestroyImmediate(component);
            }
        }

        private static int GetDepthFromRoot(Transform target, Transform root)
        {
            if (target == null) return int.MaxValue;
            if (root == null) return 0;

            var depth = 0;
            var current = target;
            while (current != null && current != root)
            {
                depth++;
                current = current.parent;
            }

            return current == root ? depth : int.MaxValue;
        }

        private static string GetPath(Transform transform)
        {
            if (transform == null) return "(null)";

            var names = new List<string>();
            var current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private sealed class OnePieceChain
        {
            public string Label;
            public Transform ManagerRoot;
            public Transform SwingRoot;
            public List<Transform> SwingBones = new List<Transform>();
            public List<VRCPhysBone> SourcePhysBones = new List<VRCPhysBone>();
        }

        private sealed class SourceChainGroup
        {
            public readonly OnePieceChain Primary;
            public readonly List<OnePieceChain> AllChains;

            public SourceChainGroup(OnePieceChain primary, List<OnePieceChain> allChains)
            {
                Primary = primary;
                AllChains = allChains != null
                    ? allChains.Where(c => c != null).ToList()
                    : new List<OnePieceChain>();
            }
        }

        private sealed class ChainReweightInfo
        {
            public readonly string Label;
            public readonly List<Transform> OriginalBones;
            public readonly List<Transform> SharedOriginalBones;
            public readonly List<Transform> FinalBones;

            public ChainReweightInfo(
                string label,
                List<Transform> originalBones,
                List<Transform> finalBones,
                List<Transform> sharedOriginalBones = null)
            {
                Label = label;
                OriginalBones = originalBones != null ? originalBones.Where(b => b != null).ToList() : new List<Transform>();
                SharedOriginalBones = sharedOriginalBones != null ? sharedOriginalBones.Where(b => b != null).ToList() : new List<Transform>();
                FinalBones = finalBones != null ? finalBones.Where(b => b != null).ToList() : new List<Transform>();
            }
        }

        private sealed class LongCoatProcessedChain
        {
            public readonly OnePieceChain Chain;
            public readonly List<Transform> FinalBones;

            public LongCoatProcessedChain(OnePieceChain chain, List<Transform> finalBones)
            {
                Chain = chain;
                FinalBones = finalBones != null ? finalBones.Where(b => b != null).ToList() : new List<Transform>();
            }
        }

        internal sealed class PreviewBuildResult
        {
            public static readonly PreviewBuildResult Empty = new PreviewBuildResult(null, null);

            public readonly VRCPhysBone[] OnePiecePhysBones;
            public readonly VRCPhysBone[] LongCoatPhysBones;

            internal PreviewBuildResult(VRCPhysBone[] onePiecePhysBones, VRCPhysBone[] longCoatPhysBones)
            {
                OnePiecePhysBones = onePiecePhysBones ?? Array.Empty<VRCPhysBone>();
                LongCoatPhysBones = longCoatPhysBones ?? Array.Empty<VRCPhysBone>();
            }
        }

        private enum RefineKind
        {
            Unknown,
            OnePiece,
            LongCoat
        }

        private sealed class RefineResult
        {
            public static readonly RefineResult Empty = new RefineResult(RefineKind.Unknown, null, null, null, null, null);

            private readonly Dictionary<string, List<Transform>> finalBonesByLabel;
            public readonly RefineKind Kind;
            public readonly List<VRCPhysBone> GeneratedPhysBones;
            public readonly List<VRCPhysBoneColliderBase> GeneratedPhysBoneColliders;
            public readonly List<VRCPhysBone> RemovedPhysBones;
            public readonly List<VRCPhysBoneColliderBase> RemovedPhysBoneColliders;

            public bool IsEmpty => finalBonesByLabel.Count == 0;

            public RefineResult(
                RefineKind kind,
                List<LongCoatProcessedChain> chains,
                List<VRCPhysBone> generatedPhysBones,
                List<VRCPhysBoneColliderBase> generatedPhysBoneColliders,
                List<VRCPhysBone> removedPhysBones,
                List<VRCPhysBoneColliderBase> removedPhysBoneColliders)
            {
                finalBonesByLabel = new Dictionary<string, List<Transform>>(StringComparer.OrdinalIgnoreCase);
                Kind = kind;
                GeneratedPhysBones = generatedPhysBones != null
                    ? generatedPhysBones.Where(pb => pb != null).Distinct().ToList()
                    : new List<VRCPhysBone>();
                GeneratedPhysBoneColliders = generatedPhysBoneColliders != null
                    ? generatedPhysBoneColliders.Where(c => c != null).Distinct().ToList()
                    : new List<VRCPhysBoneColliderBase>();
                RemovedPhysBones = removedPhysBones != null
                    ? removedPhysBones.Where(pb => pb != null).Distinct().ToList()
                    : new List<VRCPhysBone>();
                RemovedPhysBoneColliders = removedPhysBoneColliders != null
                    ? removedPhysBoneColliders.Where(c => c != null).Distinct().ToList()
                    : new List<VRCPhysBoneColliderBase>();
                if (chains == null) return;

                foreach (var chain in chains)
                {
                    if (chain == null || chain.Chain == null || string.IsNullOrEmpty(chain.Chain.Label)) continue;
                    var finalBones = chain.FinalBones != null ? chain.FinalBones.Where(b => b != null).ToList() : new List<Transform>();
                    if (finalBones.Count == 0) continue;
                    finalBonesByLabel[chain.Chain.Label] = finalBones;
                }
            }

            public List<List<Transform>> GetFinalBoneChains()
            {
                return finalBonesByLabel.Values
                    .Select(bones => bones != null
                        ? bones.Where(bone => bone != null).Distinct().ToList()
                        : new List<Transform>())
                    .Where(bones => bones.Count > 0)
                    .ToList();
            }
        }

        private sealed class RendererChainInfo
        {
            public readonly string Label;
            public readonly List<Transform> FinalBones;
            public readonly List<int> ChainOriginalIndices;
            public readonly List<int> SharedOriginalIndices;
            public readonly List<int> FinalIndices;

            public RendererChainInfo(
                string label,
                List<Transform> finalBones,
                List<int> chainOriginalIndices,
                List<int> sharedOriginalIndices,
                List<int> finalIndices)
            {
                Label = label;
                FinalBones = finalBones;
                ChainOriginalIndices = chainOriginalIndices ?? new List<int>();
                SharedOriginalIndices = sharedOriginalIndices ?? new List<int>();
                FinalIndices = finalIndices;
            }
        }

        private sealed class GeometricSkirtWeightSettings
        {
            public readonly int MaxInfluencesBeforePrune;
            public readonly int MaxInfluencesAfterPrune;
            public readonly float AngularSigma;
            public readonly float AngularBlendSmoothing;
            public readonly float VerticalRadius;
            public readonly float VerticalJudgmentOffset;
            public readonly int RingCount;
            public readonly int RingSmoothIterations;
            public readonly float RingSmoothCenterWeight;
            public readonly float RingSmoothNeighborWeight;
            public readonly int MeshSmoothIterations;
            public readonly float MeshSmoothBlend;
            public readonly float MinimumWeight;
            public readonly bool EnableRingSmoothing;
            public readonly bool EnableMeshSmoothing;
            public readonly bool ForceGeneratedWeightsForTargetVertices;
            public readonly bool IncludeHipSpineOnlyTargetVertices;
            public readonly bool ExpandTargetVerticesGeometrically;
            public readonly float TargetBoundsPadding;

            public GeometricSkirtWeightSettings(YMVRoidSkirtRefine component)
            {
                MaxInfluencesBeforePrune = Mathf.Clamp(component != null ? component.skirtWeightMaxInfluencesBeforePrune : 4, 1, 4);
                MaxInfluencesAfterPrune = Mathf.Clamp(component != null ? component.skirtWeightMaxInfluencesAfterPrune : 4, 1, 4);
                AngularSigma = Mathf.Max(1e-4f, component != null ? component.skirtWeightAngularSigma : 0.18f);
                AngularBlendSmoothing = Mathf.Clamp01(component != null ? component.skirtWeightAngularBlendSmoothing : 1.0f);
                VerticalRadius = Mathf.Max(1e-4f, component != null ? component.skirtWeightVerticalRadius : 0.32f);
                VerticalJudgmentOffset = Mathf.Clamp01(component != null ? component.skirtWeightVerticalJudgmentOffset : 0.5f);
                RingCount = Mathf.Max(1, component != null ? component.skirtWeightRingCount : 32);
                RingSmoothIterations = 0;
                RingSmoothCenterWeight = Mathf.Max(0.0f, component != null ? component.skirtWeightRingSmoothCenterWeight : 0.5f);
                RingSmoothNeighborWeight = Mathf.Max(0.0f, component != null ? component.skirtWeightRingSmoothNeighborWeight : 0.25f);
                MeshSmoothIterations = 0;
                MeshSmoothBlend = Mathf.Clamp01(component != null ? component.skirtWeightMeshSmoothBlend : 0.2f);
                MinimumWeight = Mathf.Max(0.0f, component != null ? component.skirtWeightMinimumWeight : 0.005f);
                EnableRingSmoothing = component == null || component.skirtWeightEnableRingSmoothing;
                EnableMeshSmoothing = component == null || component.skirtWeightEnableMeshSmoothing;
                ForceGeneratedWeightsForTargetVertices = component == null || component.skirtWeightForceGeneratedWeightsForTargetVertices;
                IncludeHipSpineOnlyTargetVertices = component != null && component.skirtWeightIncludeHipSpineOnlyTargetVertices;
                ExpandTargetVerticesGeometrically = component == null || component.skirtWeightExpandTargetVerticesGeometrically;
                TargetBoundsPadding = Mathf.Max(0.0f, component != null ? component.skirtWeightTargetBoundsPadding : 0.15f);
            }
        }

        private sealed class GeometricBoneChain
        {
            public readonly float ChainU;
            public readonly List<GeometricBoneSample> Samples;

            public GeometricBoneChain(float chainU, List<GeometricBoneSample> samples)
            {
                ChainU = chainU;
                Samples = samples ?? new List<GeometricBoneSample>();
            }
        }

        private readonly struct GeometricBoneSample
        {
            public readonly int BoneIndex;
            public readonly float BoneT;
            public readonly float PositionT;
            public readonly int StageIndex;

            public GeometricBoneSample(int boneIndex, float boneT, float positionT, int stageIndex)
            {
                BoneIndex = boneIndex;
                BoneT = boneT;
                PositionT = positionT;
                StageIndex = stageIndex;
            }
        }

        private readonly struct GeometricRingVertex
        {
            public readonly int Index;
            public readonly float U;

            public GeometricRingVertex(int index, float u)
            {
                Index = index;
                U = u;
            }
        }

        private sealed class BoneJudgmentPoint
        {
            public readonly int BoneIndex;
            public readonly Vector3 Position;
            public float Distance;

            public BoneJudgmentPoint(int boneIndex, Vector3 position)
            {
                BoneIndex = boneIndex;
                Position = position;
            }
        }

        private readonly struct DirectionalVertexSample
        {
            public readonly int Index;
            public readonly int Depth;

            public DirectionalVertexSample(int index, int depth)
            {
                Index = index;
                Depth = Mathf.Max(1, depth);
            }
        }
    }
}
