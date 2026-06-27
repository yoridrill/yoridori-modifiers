using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using YoridoriModifiers.Core.Editor;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(YoridoriModifiers.FacialMapper.FacialMapperNdmfPlugin))]

namespace YoridoriModifiers.FacialMapper
{
    public sealed class FacialMapperNdmfPlugin : Plugin<FacialMapperNdmfPlugin>
    {
        private const string ToolName = "YM Facial Mapper";
        private const string QualifiedPluginName = "jp.yoridrill.ym-facial-mapper";
        private const string GestureLeft = "GestureLeft";
        private const string GestureRight = "GestureRight";
        private const string JerryDisableFacialExpressions = "FacialExpressionsDisabled";
        private const string JerryInternalFacialExpressionsDisabled = "YM/JerryInternalFacialExpressionsDisabled";
        public override string QualifiedName => QualifiedPluginName;
        public override string DisplayName => ToolName;

        protected override void Configure()
        {
            var sequence = InPhase(BuildPhase.Transforming)
                .AfterPlugin("jp.yoridrill.ym-arm-patch")
                .AfterPlugin("jp.yoridrill.ym-mesh-trimmer")
                .AfterPlugin("jp.yoridrill.ym-mtoon-to-liltoon")
                .AfterPlugin("jp.yoridrill.ym-eye-freeze")
                .AfterPlugin("nadena.dev.modular-avatar")
                .BeforePlugin("com.anatawa12.avatar-optimizer");

            sequence.Run("Prepare YM Facial Mapper FX layer", PrepareFxLayer);
            sequence.WithRequiredExtension(typeof(AnimatorServicesContext), scoped =>
            {
                scoped.Run("Build YM Facial Mapper", Execute);
            });
        }

        private static void PrepareFxLayer(BuildContext context)
        {
            if (context?.AvatarRootObject == null) return;
            if (!context.AvatarRootObject.GetComponentsInChildren<YMFacialMapper>(true).Any()) return;

            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null) EnsureFxLayer(descriptor);
        }

        private static void Execute(BuildContext context)
        {
            if (context == null || context.AvatarRootObject == null) return;

            var components = context.AvatarRootObject.GetComponentsInChildren<YMFacialMapper>(true);
            if (components == null || components.Length == 0) return;

            var component = SelectPreferredComponent(components, context.AvatarRootObject);
            if (component == null) return;

            try
            {
                ErrorReport.WithContextObject(component, () => Build(context, component));
            }
            finally
            {
                foreach (var c in components)
                {
                    if (c != null) Object.DestroyImmediate(c);
                }
            }
        }

        private static void Build(BuildContext context, YMFacialMapper component)
        {
            var avatarRoot = context.AvatarRootObject;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                LogUtility.Warning(ToolName, "Build", "VRCAvatarDescriptor not found. Skipped.", component);
                return;
            }

            EnsureHandSignSettings(component);
            var candidates = BuildCandidates(component);
            if (candidates.Count == 0)
            {
                LogUtility.Verbose(ToolName, component.verboseLog, "Build", "No expression candidates configured.");
                return;
            }

            var shapeNames = candidates.SelectMany(c => c.ShapeKeys).Select(s => s.Name).Distinct(StringComparer.Ordinal).ToArray();
            var renderers = BuildRendererMap(avatarRoot, descriptor, shapeNames, component.verboseLog);
            if (shapeNames.Length > 0 && renderers.Count == 0)
            {
                LogUtility.Verbose(
                    ToolName,
                    component.verboseLog,
                    "Build",
                    "No SkinnedMeshRenderer with configured shape keys was found. Expression states may be empty.",
                    component);
            }

            var animatorServices = context.Extension<AnimatorServicesContext>();
            StripOriginalGestureLayerFaceCurves(animatorServices, component);
            if (!BuildFxController(animatorServices, component, candidates, renderers)) return;

            LogUtility.Verbose(ToolName, component.verboseLog, "Build", $"Built {candidates.Count} expression candidates.");
        }

        private static bool BuildFxController(
            AnimatorServicesContext animatorServices,
            YMFacialMapper component,
            List<Candidate> candidates,
            Dictionary<string, SkinnedMeshRenderer> rendererMap)
        {
            if (!animatorServices.ControllerContext.Controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.FX,
                    out var controller))
            {
                LogUtility.Warning(ToolName, "Build", "FX layer not found. Skipped.", component);
                return false;
            }

            controller.Name = "YM Facial Mapper FX";

            RemoveExistingLayers(controller);
            StripGestureDrivenFxFaceCurves(controller, component.verboseLog);
            EnsureBlendTreeParameters(controller, component.verboseLog);
            RedirectJerryFacialExpressionsDisabledDrivers(controller, component.verboseLog);
            SuppressExistingEyeTrackingRestores(controller, component.verboseLog);
            var externalFaceBlockers = CollectExternalFaceBlockers(controller);
            AddExistingParameterBlocker(
                controller,
                externalFaceBlockers,
                JerryDisableFacialExpressions,
                component.verboseLog,
                "Jerry's Templates Disable Facial Expressions");
            AddIntParameterIfMissing(controller, GestureLeft);
            AddIntParameterIfMissing(controller, GestureRight);
            if (externalFaceBlockers.Count > 0)
            {
                LogUtility.Verbose(ToolName, component.verboseLog, "FX", $"Detected {externalFaceBlockers.Count} external face expression conditions.");
            }

            AddResolverLayer(
                controller,
                animatorServices,
                candidates,
                rendererMap,
                externalFaceBlockers,
                component.writeDefaults);

            return true;
        }

        private static VirtualClip CreateResolvedExpressionClip(
            AnimatorServicesContext animatorServices,
            string clipName,
            IReadOnlyList<Candidate> activeCandidates,
            IReadOnlyCollection<ShapeKeySpec> allShapeKeys,
            Dictionary<string, SkinnedMeshRenderer> rendererMap,
            bool includeActiveValues,
            bool includeResetValues)
        {
            var clip = VirtualClip.Create(clipName);

            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            if (includeResetValues)
            {
                foreach (var shapeKey in allShapeKeys)
                {
                    values[shapeKey.Name] = 0f;
                }
            }

            if (includeActiveValues && activeCandidates != null)
            {
                for (var i = activeCandidates.Count - 1; i >= 0; i--)
                {
                    foreach (var shapeKey in activeCandidates[i].ShapeKeys)
                    {
                        values[shapeKey.Name] = shapeKey.Weight;
                    }
                }
            }

            foreach (var pair in values)
            {
                if (!rendererMap.TryGetValue(pair.Key, out var renderer) || renderer == null) continue;
                var path = animatorServices.ObjectPathRemapper.GetVirtualPathForObject(renderer.gameObject);
                clip.SetFloatCurve(
                    path,
                    typeof(SkinnedMeshRenderer),
                    "blendShape." + pair.Key,
                    OneKeyCurve(pair.Value));
            }

            return clip;
        }

        private static void AddResolverLayer(
            VirtualAnimatorController controller,
            AnimatorServicesContext animatorServices,
            List<Candidate> candidates,
            Dictionary<string, SkinnedMeshRenderer> rendererMap,
            IReadOnlyList<ConditionGroup> externalFaceBlockers,
            bool writeDefaults)
        {
            var allShapeKeys = candidates
                .SelectMany(candidate => candidate.ShapeKeys)
                .GroupBy(shapeKey => shapeKey.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();

            var layerName = $"{ToolName} Resolver";
            var layer = controller.AddLayer(LayerPriority.Default, layerName);
            layer.DefaultWeight = 1f;

            var stateMachine = layer.StateMachine;
            stateMachine.Name = layerName;
            var externalBlendDurations = ResolveExternalBlendDurations(controller, externalFaceBlockers);

            var resetClip = CreateResolvedExpressionClip(
                animatorServices,
                $"{ToolName} Reset",
                Array.Empty<Candidate>(),
                allShapeKeys,
                rendererMap,
                includeActiveValues: false,
                includeResetValues: !writeDefaults);

            var resetState = stateMachine.AddState("Reset", resetClip, new Vector3(220f, 80f, 0f));
            resetState.WriteDefaultValues = writeDefaults;
            AddFaceTrackingControl(resetState, stopEyelids: false, stopViseme: false);
            stateMachine.DefaultState = resetState;

            var hasExternalFaceBlockers = externalFaceBlockers is { Count: > 0 };
            if (hasExternalFaceBlockers)
            {
                AddLayerWeightControl(resetState, layer, 1f, externalBlendDurations.restore);
                AddExternalFaceOverrideState(
                    stateMachine,
                    resetState,
                    externalFaceBlockers,
                    new Vector3(20f, 80f, 0f));
            }

            foreach (var leftSign in Enum.GetValues(typeof(YMFacialMapper.HandSign)).Cast<YMFacialMapper.HandSign>())
            {
                foreach (var rightSign in Enum.GetValues(typeof(YMFacialMapper.HandSign)).Cast<YMFacialMapper.HandSign>())
                {
                    var activeCandidates = ResolveActiveCandidates(candidates, leftSign, rightSign);
                    var stateName = $"L{(int)leftSign}_R{(int)rightSign}";
                    var clip = CreateResolvedExpressionClip(
                        animatorServices,
                        $"{ToolName} {stateName}",
                        activeCandidates,
                        allShapeKeys,
                        rendererMap,
                        includeActiveValues: true,
                        includeResetValues: !writeDefaults);

                    var state = stateMachine.AddState(
                        stateName,
                        clip,
                        new Vector3(220f + (int)rightSign * 180f, 180f + (int)leftSign * 60f, 0f));
                    state.WriteDefaultValues = writeDefaults;
                    AddFaceTrackingControl(state, activeCandidates);
                    if (hasExternalFaceBlockers)
                    {
                        AddLayerWeightControl(state, layer, 1f, externalBlendDurations.restore);
                        AddExternalFaceOverrideState(
                            stateMachine,
                            state,
                            externalFaceBlockers,
                            new Vector3(20f + (int)rightSign * 180f, 180f + (int)leftSign * 60f, 0f));
                    }

                    var conditions = ImmutableList.Create(
                        new AnimatorCondition
                        {
                            mode = AnimatorConditionMode.Equals,
                            parameter = GestureLeft,
                            threshold = (float)leftSign
                        },
                        new AnimatorCondition
                        {
                            mode = AnimatorConditionMode.Equals,
                            parameter = GestureRight,
                            threshold = (float)rightSign
                        });
                    conditions = AddSingleConditionExternalFaceGuards(conditions, externalFaceBlockers);
                    var transition = CreateTransition(state, conditions);
                    transition.CanTransitionToSelf = false;
                    stateMachine.AnyStateTransitions = stateMachine.AnyStateTransitions.Add(transition);
                }
            }

            foreach (var externalState in (externalFaceBlockers ?? Array.Empty<ConditionGroup>())
                         .Where(blocker => blocker != null)
                         .SelectMany(blocker => blocker.DestinationStates)
                         .Where(state => state != null)
                         .Distinct())
            {
                AddLayerWeightControl(externalState, layer, 0f, externalBlendDurations.disable);
            }
        }

        private static (float disable, float restore) ResolveExternalBlendDurations(
            VirtualAnimatorController controller,
            IReadOnlyList<ConditionGroup> externalFaceBlockers)
        {
            const float defaultDisableDuration = 0.1f;
            const float defaultRestoreDuration = 0.2f;
            if (controller == null || externalFaceBlockers == null)
            {
                return (defaultDisableDuration, defaultRestoreDuration);
            }

            var externalStates = externalFaceBlockers
                .Where(blocker => blocker != null)
                .SelectMany(blocker => blocker.DestinationStates)
                .Where(state => state != null)
                .Distinct()
                .ToArray();
            var disableControls = externalStates
                .SelectMany(state => state.Behaviours.OfType<VRCAnimatorLayerControl>())
                .Where(control =>
                    control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX &&
                    control.goalWeight <= 0.001f)
                .ToArray();
            var controlledLayers = disableControls.Select(control => control.layer).ToHashSet();

            var externalParameters = externalFaceBlockers
                .Where(blocker => blocker?.Conditions != null)
                .SelectMany(blocker => blocker.Conditions)
                .Select(condition => condition.Parameter)
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter))
                .ToHashSet(StringComparer.Ordinal);
            var parameterTransitionDurations = controller.AllReachableNodes()
                .OfType<VirtualStateTransition>()
                .Where(transition =>
                    transition.HasFixedDuration &&
                    transition.Duration > 0f &&
                    transition.Conditions.Any(condition => externalParameters.Contains(condition.parameter)))
                .Select(transition => transition.Duration)
                .ToArray();

            var disableDuration = new[]
                {
                    disableControls.Length > 0
                        ? disableControls.Max(control => Mathf.Max(0f, control.blendDuration))
                        : 0f,
                    parameterTransitionDurations.Length > 0
                        ? parameterTransitionDurations.Max()
                        : 0f
                }
                .Max();
            if (disableDuration <= 0f) disableDuration = defaultDisableDuration;
            var restoreDurations = controller.AllReachableNodes()
                .OfType<VirtualState>()
                .SelectMany(state => state.Behaviours.OfType<VRCAnimatorLayerControl>())
                .Where(control =>
                    control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX &&
                    control.goalWeight >= 0.999f &&
                    controlledLayers.Contains(control.layer))
                .Select(control => Mathf.Max(0f, control.blendDuration))
                .ToArray();
            var restoreDuration = new[]
                {
                    restoreDurations.Length > 0 ? restoreDurations.Max() : 0f,
                    parameterTransitionDurations.Length > 0 ? parameterTransitionDurations.Max() : 0f
                }
                .Max();
            if (restoreDuration <= 0f) restoreDuration = defaultRestoreDuration;

            return (disableDuration, restoreDuration);
        }

        private static void AddLayerWeightControl(
            VirtualState state,
            VirtualLayer layer,
            float goalWeight,
            float blendDuration)
        {
            if (state == null || layer == null) return;

            var control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            control.layer = layer.VirtualLayerIndex;
            control.goalWeight = goalWeight;
            control.blendDuration = Mathf.Max(0f, blendDuration);
            control.debugString = $"{ToolName}: {layer.Name} weight {goalWeight:0}";
            state.Behaviours = state.Behaviours.Add(control);
        }

        private static void AddFaceTrackingControl(VirtualState state, IReadOnlyList<Candidate> candidates)
        {
            var stopEyelids = candidates != null && candidates.Any(candidate => candidate.StopEyelidLeft || candidate.StopEyelidRight);
            var stopViseme = candidates != null && candidates.Any(candidate => candidate.StopViseme);
            AddFaceTrackingControl(state, stopEyelids, stopViseme);
        }

        private static void AddFaceTrackingControl(VirtualState state, bool stopEyelids, bool stopViseme)
        {
            if (state == null) return;

            var control = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
            var noChange = VRC_AnimatorTrackingControl.TrackingType.NoChange;
            var tracking = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            var animation = VRC_AnimatorTrackingControl.TrackingType.Animation;

            control.trackingHead = noChange;
            control.trackingLeftHand = noChange;
            control.trackingRightHand = noChange;
            control.trackingHip = noChange;
            control.trackingLeftFoot = noChange;
            control.trackingRightFoot = noChange;
            control.trackingLeftFingers = noChange;
            control.trackingRightFingers = noChange;
            control.trackingEyes = stopEyelids ? animation : tracking;
            control.trackingMouth = stopViseme ? animation : tracking;
            state.Behaviours = state.Behaviours.Add(control);
        }

        private static void AddExternalFaceOverrideState(
            VirtualStateMachine stateMachine,
            VirtualState sourceState,
            IReadOnlyList<ConditionGroup> externalFaceBlockers,
            Vector3 position)
        {
            if (stateMachine == null || sourceState == null || externalFaceBlockers == null) return;

            // Keep the exact source motion while the resolver layer fades out. Transitioning to an empty motion
            // makes Unity blend through the avatar setup pose when Write Defaults is enabled, which can produce a
            // one-frame eyebrow/face twitch before an external expression takes over.
            var externalOverrideState = stateMachine.AddState(
                $"External Override {sourceState.Name}",
                sourceState.Motion,
                position);
            externalOverrideState.WriteDefaultValues = sourceState.WriteDefaultValues;

            foreach (var blocker in externalFaceBlockers)
            {
                if (blocker?.Conditions == null || blocker.Conditions.Length == 0) continue;
                var conditions = blocker.Conditions.Select(ToAnimatorCondition).ToImmutableList();
                var transition = CreateTransition(externalOverrideState, conditions);
                sourceState.Transitions = sourceState.Transitions.Add(transition);
            }
        }

        private static ImmutableList<AnimatorCondition> AddSingleConditionExternalFaceGuards(
            ImmutableList<AnimatorCondition> conditions,
            IReadOnlyList<ConditionGroup> externalFaceBlockers)
        {
            if (externalFaceBlockers == null) return conditions;

            foreach (var blocker in externalFaceBlockers)
            {
                if (blocker?.Conditions == null || blocker.Conditions.Length != 1) continue;
                var inverse = InvertCondition(blocker.Conditions[0]);
                conditions = conditions.Add(ToAnimatorCondition(inverse));
            }

            return conditions;
        }

        private static AnimatorCondition ToAnimatorCondition(ConditionSpec condition)
        {
            return new AnimatorCondition
            {
                mode = condition.Mode,
                parameter = condition.Parameter,
                threshold = condition.Threshold
            };
        }

        private static VirtualStateTransition CreateTransition(
            VirtualState destination,
            ImmutableList<AnimatorCondition> conditions)
        {
            var transition = VirtualStateTransition.Create();
            transition.SetDestination(destination);
            transition.ExitTime = null;
            transition.HasFixedDuration = true;
            transition.Duration = 0f;
            transition.Conditions = conditions;
            return transition;
        }

        private static bool Conflicts(Candidate candidate, Candidate other)
        {
            if (candidate.IsNeutral != other.IsNeutral) return false;
            if (!candidate.OccupiesEyelidLeft && !candidate.OccupiesEyelidRight && !candidate.OccupiesViseme) return false;
            if (!other.OccupiesEyelidLeft && !other.OccupiesEyelidRight && !other.OccupiesViseme) return false;
            return (candidate.OccupiesEyelidLeft && other.OccupiesEyelidLeft) ||
                   (candidate.OccupiesEyelidRight && other.OccupiesEyelidRight) ||
                   (candidate.OccupiesViseme && other.OccupiesViseme);
        }

        private static List<Candidate> ResolveActiveCandidates(
            List<Candidate> candidates,
            YMFacialMapper.HandSign leftSign,
            YMFacialMapper.HandSign rightSign)
        {
            var activeCandidates = new List<Candidate>();
            foreach (var candidate in candidates)
            {
                if (!IsCandidateActive(candidate, leftSign, rightSign)) continue;
                if (activeCandidates.Any(active => Conflicts(candidate, active))) continue;
                activeCandidates.Add(candidate);
            }

            return activeCandidates;
        }

        private static bool IsCandidateActive(
            Candidate candidate,
            YMFacialMapper.HandSign leftSign,
            YMFacialMapper.HandSign rightSign)
        {
            if (candidate.IsNeutral)
            {
                return leftSign == YMFacialMapper.HandSign.Neutral &&
                       rightSign == YMFacialMapper.HandSign.Neutral;
            }

            if (candidate.Side == YMFacialMapper.HandSide.Left)
            {
                return leftSign == candidate.Sign;
            }

            if (candidate.Side == YMFacialMapper.HandSide.Right)
            {
                return rightSign == candidate.Sign;
            }

            return false;
        }

        private static List<Candidate> BuildCandidates(YMFacialMapper component)
        {
            var candidates = new List<Candidate>();
            AddCandidate(candidates, component.neutral, YMFacialMapper.HandSign.Neutral, null);

            var sideOrder = component.conflictPriority == YMFacialMapper.ConflictPriority.Right
                ? new[] { YMFacialMapper.HandSide.Right, YMFacialMapper.HandSide.Left }
                : new[] { YMFacialMapper.HandSide.Left, YMFacialMapper.HandSide.Right };

            foreach (var side in sideOrder)
            {
                foreach (var sign in YMFacialMapper.HandSignOrder)
                {
                    var setting = component.handSigns.FirstOrDefault(s => s != null && s.sign == sign);
                    if (setting == null) continue;
                    AddCandidate(candidates, side == YMFacialMapper.HandSide.Left ? setting.left : setting.right, sign, side);
                }
            }

            return candidates;
        }

        private static void AddCandidate(
            List<Candidate> candidates,
            YMFacialMapper.ExpressionSlot slot,
            YMFacialMapper.HandSign sign,
            YMFacialMapper.HandSide? side)
        {
            if (slot == null) return;
            var shapeKeys = ParseShapeKeys(slot.shapeKeys);
            if (shapeKeys.Count == 0 && !slot.stopEyelidLeft && !slot.stopEyelidRight && !slot.stopViseme) return;

            candidates.Add(new Candidate(sign, side, slot.stopEyelidLeft, slot.stopEyelidRight, slot.stopViseme, shapeKeys));
        }

        private static List<ShapeKeySpec> ParseShapeKeys(IEnumerable<string> rawShapeKeys)
        {
            var result = new List<ShapeKeySpec>();
            if (rawShapeKeys == null) return result;

            foreach (var raw in rawShapeKeys)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var text = raw.Trim();
                var splitIndex = text.LastIndexOf('=');
                if (splitIndex < 0) splitIndex = text.LastIndexOf(':');

                var name = text;
                var weight = 100f;
                if (splitIndex > 0 && splitIndex < text.Length - 1)
                {
                    name = text.Substring(0, splitIndex).Trim();
                    var valueText = text.Substring(splitIndex + 1).Trim();
                    if (float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    {
                        weight = parsed;
                    }
                }

                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Add(new ShapeKeySpec(name, Mathf.Clamp(weight, 0f, 100f)));
            }

            return result;
        }

        private static Dictionary<string, SkinnedMeshRenderer> BuildRendererMap(
            GameObject avatarRoot,
            VRCAvatarDescriptor descriptor,
            string[] shapeNames,
            bool verbose)
        {
            var result = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            if (avatarRoot == null || shapeNames == null || shapeNames.Length == 0) return result;

            var renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r != null && r.sharedMesh != null)
                .ToArray();

            var preferred = descriptor != null && descriptor.VisemeSkinnedMesh != null
                ? descriptor.VisemeSkinnedMesh
                : renderers
                    .OrderByDescending(r => CountMatchingBlendShapes(r, shapeNames))
                    .ThenBy(r => RendererNameScore(r))
                    .FirstOrDefault(r => CountMatchingBlendShapes(r, shapeNames) > 0);

            foreach (var shapeName in shapeNames)
            {
                SkinnedMeshRenderer renderer = null;
                if (preferred != null && HasBlendShape(preferred, shapeName))
                {
                    renderer = preferred;
                }
                else
                {
                    renderer = renderers.FirstOrDefault(r => HasBlendShape(r, shapeName));
                }

                if (renderer != null)
                {
                    result[shapeName] = renderer;
                }
                else
                {
                    LogUtility.Verbose(ToolName, verbose, "ShapeKey", $"Shape key not found: {shapeName}");
                }
            }

            LogUtility.Verbose(ToolName, verbose, "Renderer", $"Mapped {result.Count}/{shapeNames.Length} shape keys.");
            return result;
        }

        private static int CountMatchingBlendShapes(SkinnedMeshRenderer renderer, IEnumerable<string> shapeNames)
        {
            if (renderer == null || renderer.sharedMesh == null || shapeNames == null) return 0;
            return shapeNames.Count(name => HasBlendShape(renderer, name));
        }

        private static int RendererNameScore(SkinnedMeshRenderer renderer)
        {
            var name = renderer != null ? renderer.name.ToLowerInvariant() : string.Empty;
            if (name.Contains("face")) return 0;
            if (name.Contains("body")) return 1;
            return 2;
        }

        private static bool HasBlendShape(SkinnedMeshRenderer renderer, string shapeName)
        {
            return renderer != null &&
                   renderer.sharedMesh != null &&
                   renderer.sharedMesh.GetBlendShapeIndex(shapeName) >= 0;
        }

        private static AnimationCurve OneKeyCurve(float value)
        {
            return new AnimationCurve(new Keyframe(0f, value));
        }

        private static void AddIntParameterIfMissing(VirtualAnimatorController controller, string parameterName)
        {
            if (controller.Parameters.ContainsKey(parameterName)) return;
            controller.Parameters = controller.Parameters.Add(parameterName, new AnimatorControllerParameter
            {
                name = parameterName,
                type = AnimatorControllerParameterType.Int,
                defaultInt = 0
            });
        }

        private static void AddBoolParameterIfMissing(VirtualAnimatorController controller, string parameterName)
        {
            if (controller.Parameters.ContainsKey(parameterName)) return;
            controller.Parameters = controller.Parameters.Add(parameterName, new AnimatorControllerParameter
            {
                name = parameterName,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false
            });
        }

        private static void EnsureBlendTreeParameters(VirtualAnimatorController controller, bool verbose)
        {
            if (controller == null) return;

            var parameters = controller.Parameters;
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var blendTree in controller.AllReachableNodes().OfType<VirtualBlendTree>())
            {
                AddParameterName(referenced, blendTree.BlendParameter);
                AddParameterName(referenced, blendTree.BlendParameterY);
                foreach (var child in blendTree.Children)
                {
                    AddParameterName(referenced, child.DirectBlendParameter);
                }
            }

            var added = 0;
            foreach (var parameterName in referenced.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(parameterName) || parameters.ContainsKey(parameterName)) continue;
                parameters = parameters.Add(parameterName, new AnimatorControllerParameter
                {
                    name = parameterName,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f
                });
                added++;
            }
            controller.Parameters = parameters;

            if (added > 0)
            {
                LogUtility.Verbose(ToolName, verbose, "FX", $"Added {added} missing BlendTree parameters to the virtualized FX controller.");
            }
        }

        private static void AddParameterName(HashSet<string> parameters, string parameterName)
        {
            if (parameters == null || string.IsNullOrWhiteSpace(parameterName)) return;
            parameters.Add(parameterName.Trim());
        }

        private static void RedirectJerryFacialExpressionsDisabledDrivers(VirtualAnimatorController controller, bool verbose)
        {
            if (controller == null) return;

            var redirected = RedirectParameterDrivers(
                controller.AllReachableNodes().OfType<VirtualState>(),
                JerryDisableFacialExpressions,
                JerryInternalFacialExpressionsDisabled);

            if (redirected <= 0) return;

            AddBoolParameterIfMissing(controller, JerryInternalFacialExpressionsDisabled);
            LogUtility.Verbose(
                ToolName,
                verbose,
                "FX",
                $"Redirected {redirected} Jerry internal writes from {JerryDisableFacialExpressions} to {JerryInternalFacialExpressionsDisabled}.");
        }

        private static int RedirectParameterDrivers(
            IEnumerable<VirtualState> states,
            string sourceParameter,
            string destinationParameter)
        {
            if (states == null ||
                string.IsNullOrWhiteSpace(sourceParameter) ||
                string.IsNullOrWhiteSpace(destinationParameter))
            {
                return 0;
            }

            var redirected = 0;
            foreach (var state in states)
            {
                if (state == null) continue;
                foreach (var driver in state.Behaviours.OfType<VRCAvatarParameterDriver>())
                {
                    if (driver.parameters == null) continue;
                    foreach (var parameter in driver.parameters)
                    {
                        if (parameter == null || parameter.name != sourceParameter) continue;
                        parameter.name = destinationParameter;
                        redirected++;
                    }
                }
            }

            return redirected;
        }

        private static void SuppressExistingEyeTrackingRestores(VirtualAnimatorController controller, bool verbose)
        {
            if (controller == null) return;

            var changed = 0;
            var tracking = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            var noChange = VRC_AnimatorTrackingControl.TrackingType.NoChange;
            changed += SuppressExistingEyeTrackingRestores(
                controller.AllReachableNodes().OfType<VirtualState>(),
                tracking,
                noChange);

            if (changed > 0)
            {
                LogUtility.Verbose(
                    ToolName,
                    verbose,
                    "FX",
                    $"Changed {changed} existing eye tracking restore behaviours to NoChange so YM Facial Mapper can control blink/eye-look exclusion.");
            }
        }

        private static int SuppressExistingEyeTrackingRestores(
            IEnumerable<VirtualState> states,
            VRC_AnimatorTrackingControl.TrackingType tracking,
            VRC_AnimatorTrackingControl.TrackingType noChange)
        {
            if (states == null) return 0;

            var changed = 0;
            foreach (var state in states)
            {
                if (state == null) continue;
                foreach (var control in state.Behaviours.OfType<VRCAnimatorTrackingControl>())
                {
                    if (control.trackingEyes != tracking) continue;
                    control.trackingEyes = noChange;
                    changed++;
                }
            }

            return changed;
        }

        private static void StripOriginalGestureLayerFaceCurves(
            AnimatorServicesContext animatorServices,
            YMFacialMapper component)
        {
            if (!animatorServices.ControllerContext.Controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.Gesture,
                    out var controller)) return;

            var stripped = RewriteControllerFaceMotions(controller);
            controller.Name = $"{controller.Name} YM Facial Mapper Gesture Face Stripped";
            if (stripped > 0)
            {
                LogUtility.Verbose(ToolName, component.verboseLog, "Gesture", $"Stripped {stripped} blend shape curves from Gesture animations.");
            }
        }

        private static void StripGestureDrivenFxFaceCurves(
            VirtualAnimatorController controller,
            bool verbose)
        {
            if (controller == null) return;
            var stripped = 0;
            var clipMap = new Dictionary<VirtualClip, VirtualClip>();

            foreach (var layer in controller.Layers)
            {
                if (layer?.StateMachine == null || !StateMachineUsesGestureParameters(layer.StateMachine)) continue;
                stripped += RewriteStateMachineFaceMotions(layer.StateMachine, clipMap);
            }

            if (stripped > 0)
            {
                LogUtility.Verbose(ToolName, verbose, "FX", $"Stripped {stripped} blend shape curves from gesture-driven FX animations.");
            }
        }

        private static List<ConditionGroup> CollectExternalFaceBlockers(VirtualAnimatorController controller)
        {
            var groups = new List<ConditionGroup>();
            var groupsByKey = new Dictionary<string, ConditionGroup>(StringComparer.Ordinal);
            if (controller == null) return groups;

            foreach (var transition in controller.AllReachableNodes().OfType<VirtualTransitionBase>())
            {
                TryAddExternalFaceBlocker(controller, transition, groups, groupsByKey);
            }

            return groups;
        }

        private static void AddExistingParameterBlocker(
            VirtualAnimatorController controller,
            List<ConditionGroup> groups,
            string parameterName,
            bool verbose,
            string label)
        {
            if (controller == null ||
                groups == null ||
                string.IsNullOrWhiteSpace(parameterName) ||
                !TryCreateTruthyCondition(controller, parameterName.Trim(), out var condition))
            {
                return;
            }

            if (groups.Any(group =>
                    group?.Conditions != null &&
                    group.Conditions.Length == 1 &&
                    group.Conditions[0].Parameter == condition.Parameter))
            {
                return;
            }

            groups.Add(new ConditionGroup(new[] { condition }));
            LogUtility.Verbose(ToolName, verbose, "FX", $"Detected {label} parameter: {condition.Parameter}");
        }

        private static void TryAddExternalFaceBlocker(
            VirtualAnimatorController controller,
            VirtualTransitionBase transition,
            List<ConditionGroup> groups,
            Dictionary<string, ConditionGroup> groupsByKey)
        {
            if (transition?.DestinationState == null) return;
            if (IsFaceTrackingState(transition.DestinationState)) return;
            if (!MotionHasBlendShapeCurves(transition.DestinationState.Motion)) return;

            var conditions = transition.Conditions
                .Where(condition => IsExternalFaceBlockerCondition(condition.parameter))
                .Select(condition => NormalizeCondition(controller, condition.parameter, condition.mode, condition.threshold))
                .ToArray();
            if (conditions.Length == 0) return;

            var key = string.Join("|", conditions
                .OrderBy(condition => condition.Parameter, StringComparer.Ordinal)
                .ThenBy(condition => condition.Mode)
                .ThenBy(condition => condition.Threshold)
                .Select(condition => $"{condition.Parameter}:{condition.Mode}:{condition.Threshold.ToString(CultureInfo.InvariantCulture)}"));
            if (groupsByKey.TryGetValue(key, out var existing))
            {
                existing.DestinationStates.Add(transition.DestinationState);
                return;
            }

            var group = new ConditionGroup(conditions, transition.DestinationState);
            groupsByKey[key] = group;
            groups.Add(group);
        }

        private static bool IsFaceTrackingState(VirtualState state)
        {
            if (state == null) return false;
            return IsFaceTrackingName(state.Name) || IsFaceTrackingMotion(state.Motion);
        }

        private static bool IsFaceTrackingMotion(VirtualMotion motion)
        {
            switch (motion)
            {
                case null:
                    return false;
                case VirtualBlendTree blendTree:
                    return IsFaceTrackingName(blendTree.Name) ||
                           IsFaceTrackingParameter(blendTree.BlendParameter) ||
                           IsFaceTrackingParameter(blendTree.BlendParameterY) ||
                           blendTree.Children.Any(child =>
                               IsFaceTrackingParameter(child.DirectBlendParameter) ||
                               IsFaceTrackingMotion(child.Motion));
                default:
                    return IsFaceTrackingName(motion.Name);
            }
        }

        private static bool IsFaceTrackingName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var lower = name.ToLowerInvariant();
            return lower.Contains("face tracking") ||
                   lower.Contains("do not edit") ||
                   lower.StartsWith("ft ", StringComparison.Ordinal) ||
                   lower.StartsWith("ft/", StringComparison.Ordinal);
        }

        private static bool IsFaceTrackingParameter(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) return false;
            var lower = parameterName.ToLowerInvariant();
            return lower.StartsWith("ft/", StringComparison.Ordinal) ||
                   lower.StartsWith("state/", StringComparison.Ordinal) ||
                   lower.StartsWith("oscm/", StringComparison.Ordinal) ||
                   lower.StartsWith("smoothing/", StringComparison.Ordinal) ||
                   lower.Contains("trackingactive") ||
                   lower.Contains("facetracking") ||
                   lower.Contains("visemesenable") ||
                   lower.Contains("eyedilationenable");
        }

        private static bool MotionHasBlendShapeCurves(VirtualMotion motion)
        {
            switch (motion)
            {
                case null:
                    return false;
                case VirtualClip clip:
                    return clip.GetFloatCurveBindings().Any(IsBlendShapeBinding) ||
                           clip.GetObjectCurveBindings().Any(IsBlendShapeBinding);
                case VirtualBlendTree blendTree:
                    return blendTree.Children.Any(child => MotionHasBlendShapeCurves(child.Motion));
                default:
                    return false;
            }
        }

        private static bool IsExternalFaceBlockerCondition(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) return false;
            if (IsGestureParameter(parameterName)) return false;

            var lower = parameterName.ToLowerInvariant();
            if (lower == "islocal" ||
                lower == "vrmode" ||
                lower == "afk" ||
                lower.Contains("facialexpressionsdisabled") ||
                lower.Contains("face tracking") ||
                lower.Contains("facetracking") ||
                lower.Contains("eyetrackingactive") ||
                lower.Contains("liptrackingactive") ||
                lower.Contains("eyedilationenable") ||
                lower.Contains("visemesenable") ||
                lower.Contains("facetrackingemulation") ||
                lower.Contains("facetrackinglimits") ||
                lower.Contains("trackingactive") ||
                lower.Contains("disable hand gestures") ||
                lower.Contains("disablehandgestures") ||
                lower.Contains("blink") ||
                lower.Contains("viseme") ||
                lower.StartsWith("ft/", StringComparison.Ordinal) ||
                lower.StartsWith("state/", StringComparison.Ordinal) ||
                lower.StartsWith("oscm/", StringComparison.Ordinal) ||
                lower.StartsWith("smoothing/", StringComparison.Ordinal) ||
                lower.StartsWith("vrcfaceblend", StringComparison.Ordinal) ||
                lower.StartsWith("vrcl", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static ConditionSpec NormalizeCondition(
            VirtualAnimatorController controller,
            string parameterName,
            AnimatorConditionMode mode,
            float threshold)
        {
            var type = GetParameterType(controller, parameterName);
            if ((type == AnimatorControllerParameterType.Float || type == AnimatorControllerParameterType.Int) &&
                (mode == AnimatorConditionMode.If || mode == AnimatorConditionMode.IfNot))
            {
                return mode == AnimatorConditionMode.If
                    ? new ConditionSpec(parameterName, AnimatorConditionMode.Greater, 0.5f)
                    : new ConditionSpec(parameterName, AnimatorConditionMode.Less, 0.5f);
            }

            return new ConditionSpec(parameterName, mode, threshold);
        }

        private static bool TryCreateTruthyCondition(
            VirtualAnimatorController controller,
            string parameterName,
            out ConditionSpec condition)
        {
            var type = GetParameterType(controller, parameterName);
            switch (type)
            {
                case AnimatorControllerParameterType.Bool:
                    condition = new ConditionSpec(parameterName, AnimatorConditionMode.If, 0f);
                    return true;
                case AnimatorControllerParameterType.Float:
                case AnimatorControllerParameterType.Int:
                    condition = new ConditionSpec(parameterName, AnimatorConditionMode.Greater, 0.5f);
                    return true;
                default:
                    condition = default;
                    return false;
            }
        }

        private static AnimatorControllerParameterType? GetParameterType(VirtualAnimatorController controller, string parameterName)
        {
            if (controller == null || string.IsNullOrWhiteSpace(parameterName)) return null;
            return controller.Parameters.TryGetValue(parameterName, out var parameter) ? parameter.type : null;
        }

        private static ConditionSpec InvertCondition(ConditionSpec condition)
        {
            const float epsilon = 0.0001f;
            return condition.Mode switch
            {
                AnimatorConditionMode.If => new ConditionSpec(condition.Parameter, AnimatorConditionMode.IfNot, condition.Threshold),
                AnimatorConditionMode.IfNot => new ConditionSpec(condition.Parameter, AnimatorConditionMode.If, condition.Threshold),
                AnimatorConditionMode.Equals => new ConditionSpec(condition.Parameter, AnimatorConditionMode.NotEqual, condition.Threshold),
                AnimatorConditionMode.NotEqual => new ConditionSpec(condition.Parameter, AnimatorConditionMode.Equals, condition.Threshold),
                AnimatorConditionMode.Greater => new ConditionSpec(condition.Parameter, AnimatorConditionMode.Less, condition.Threshold + epsilon),
                AnimatorConditionMode.Less => new ConditionSpec(condition.Parameter, AnimatorConditionMode.Greater, condition.Threshold - epsilon),
                _ => condition
            };
        }

        private static int RewriteControllerFaceMotions(VirtualAnimatorController controller)
        {
            if (controller == null) return 0;
            var stripped = 0;
            var clipMap = new Dictionary<VirtualClip, VirtualClip>();
            foreach (var layer in controller.Layers)
            {
                stripped += RewriteStateMachineFaceMotions(layer?.StateMachine, clipMap);
            }

            return stripped;
        }

        private static int RewriteStateMachineFaceMotions(
            VirtualStateMachine stateMachine,
            Dictionary<VirtualClip, VirtualClip> clipMap)
        {
            if (stateMachine == null) return 0;
            var stripped = 0;
            foreach (var state in stateMachine.AllStates())
            {
                var result = RewriteFaceMotion(state.Motion, clipMap);
                if (!ReferenceEquals(result.motion, state.Motion)) state.Motion = result.motion;
                stripped += result.strippedCurves;
            }

            return stripped;
        }

        private static (VirtualMotion motion, int strippedCurves) RewriteFaceMotion(
            VirtualMotion motion,
            Dictionary<VirtualClip, VirtualClip> clipMap)
        {
            switch (motion)
            {
                case null:
                    return (null, 0);
                case VirtualClip clip:
                    return RewriteFaceClip(clip, clipMap);
                case VirtualBlendTree blendTree:
                    return RewriteFaceBlendTree(blendTree, clipMap);
                default:
                    return (motion, 0);
            }
        }

        private static (VirtualMotion motion, int strippedCurves) RewriteFaceClip(
            VirtualClip sourceClip,
            Dictionary<VirtualClip, VirtualClip> clipMap)
        {
            if (sourceClip == null || sourceClip.IsMarkerClip) return (sourceClip, 0);
            if (clipMap.TryGetValue(sourceClip, out var existing)) return (existing, 0);

            var floatBindings = sourceClip.GetFloatCurveBindings().Where(IsBlendShapeBinding).ToArray();
            var objectBindings = sourceClip.GetObjectCurveBindings().Where(IsBlendShapeBinding).ToArray();
            var stripped = floatBindings.Length + objectBindings.Length;
            if (stripped == 0) return (sourceClip, 0);

            var clip = sourceClip.Clone();
            clip.Name = $"{sourceClip.Name} YM Facial Mapper Face Stripped";
            foreach (var binding in floatBindings) clip.SetFloatCurve(binding, null);
            foreach (var binding in objectBindings) clip.SetObjectCurve(binding, null);
            clipMap[sourceClip] = clip;
            return (clip, stripped);
        }

        private static (VirtualMotion motion, int strippedCurves) RewriteFaceBlendTree(
            VirtualBlendTree sourceTree,
            Dictionary<VirtualClip, VirtualClip> clipMap)
        {
            if (sourceTree == null) return (null, 0);
            var stripped = 0;
            var changed = false;
            var children = sourceTree.Children.Select(child =>
            {
                var result = RewriteFaceMotion(child.Motion, clipMap);
                stripped += result.strippedCurves;
                changed |= !ReferenceEquals(result.motion, child.Motion);
                return new VirtualBlendTree.VirtualChildMotion
                {
                    Motion = result.motion,
                    CycleOffset = child.CycleOffset,
                    DirectBlendParameter = child.DirectBlendParameter,
                    Mirror = child.Mirror,
                    Threshold = child.Threshold,
                    Position = child.Position,
                    TimeScale = child.TimeScale
                };
            }).ToImmutableList();

            if (!changed) return (sourceTree, stripped);

            var tree = VirtualBlendTree.Create($"{sourceTree.Name} YM Facial Mapper Face Stripped");
            tree.BlendParameter = sourceTree.BlendParameter;
            tree.BlendParameterY = sourceTree.BlendParameterY;
            tree.BlendType = sourceTree.BlendType;
            tree.MaxThreshold = sourceTree.MaxThreshold;
            tree.MinThreshold = sourceTree.MinThreshold;
            tree.UseAutomaticThresholds = sourceTree.UseAutomaticThresholds;
            tree.NormalizedBlendValues = sourceTree.NormalizedBlendValues;
            tree.Children = children;
            return (tree, stripped);
        }

        private static bool StateMachineUsesGestureParameters(VirtualStateMachine stateMachine)
        {
            if (stateMachine == null) return false;

            if (stateMachine.AllReachableNodes()
                .OfType<VirtualTransitionBase>()
                .Any(transition => transition.Conditions.Any(condition => IsGestureParameter(condition.parameter))))
            {
                return true;
            }

            return stateMachine.AllReachableNodes()
                .OfType<VirtualBlendTree>()
                .Any(blendTree =>
                    IsGestureParameter(blendTree.BlendParameter) ||
                    IsGestureParameter(blendTree.BlendParameterY) ||
                    blendTree.Children.Any(child => IsGestureParameter(child.DirectBlendParameter)));
        }

        private static bool IsGestureParameter(string parameterName)
        {
            return parameterName == GestureLeft ||
                   parameterName == GestureRight ||
                   parameterName == "GestureLeftWeight" ||
                   parameterName == "GestureRightWeight";
        }

        private static bool IsBlendShapeBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(SkinnedMeshRenderer) &&
                   binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);
        }

        private static void RemoveExistingLayers(VirtualAnimatorController controller)
        {
            controller.RemoveLayers(layer =>
                layer?.Name != null && layer.Name.StartsWith(ToolName, StringComparison.Ordinal));
        }

        private static void EnsureFxLayer(VRCAvatarDescriptor descriptor)
        {
            var layers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            for (var i = 0; i < layers.Length; i++)
            {
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX) return;
            }

            Array.Resize(ref layers, layers.Length + 1);
            var index = layers.Length - 1;
            layers[index] = new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.FX,
                isDefault = false
            };
            descriptor.baseAnimationLayers = layers;
        }

        private static YMFacialMapper SelectPreferredComponent(YMFacialMapper[] components, GameObject avatarRoot)
        {
            var rootTransform = avatarRoot != null ? avatarRoot.transform : null;
            return components
                .Where(c => c != null)
                .OrderByDescending(c => c.transform == rootTransform)
                .ThenBy(c => PreviewCoordinator.GetDepthFromRoot(c.transform, rootTransform))
                .FirstOrDefault();
        }

        private static void EnsureHandSignSettings(YMFacialMapper component)
        {
            YMFacialMapperDefaults.EnsureHandSigns(component);
        }

        private readonly struct ConditionSpec
        {
            public readonly string Parameter;
            public readonly AnimatorConditionMode Mode;
            public readonly float Threshold;

            public ConditionSpec(string parameter, AnimatorConditionMode mode, float threshold)
            {
                Parameter = parameter;
                Mode = mode;
                Threshold = threshold;
            }
        }

        private sealed class ConditionGroup
        {
            public readonly ConditionSpec[] Conditions;
            public readonly HashSet<VirtualState> DestinationStates = new();

            public ConditionGroup(ConditionSpec[] conditions, VirtualState destinationState = null)
            {
                Conditions = conditions ?? Array.Empty<ConditionSpec>();
                if (destinationState != null) DestinationStates.Add(destinationState);
            }
        }

        private sealed class Candidate
        {
            public readonly YMFacialMapper.HandSign Sign;
            public readonly YMFacialMapper.HandSide? Side;
            public readonly bool StopEyelidLeft;
            public readonly bool StopEyelidRight;
            public readonly bool StopViseme;
            public readonly List<ShapeKeySpec> ShapeKeys;

            public Candidate(
                YMFacialMapper.HandSign sign,
                YMFacialMapper.HandSide? side,
                bool stopEyelidLeft,
                bool stopEyelidRight,
                bool stopViseme,
                List<ShapeKeySpec> shapeKeys)
            {
                Sign = sign;
                Side = side;
                StopEyelidLeft = stopEyelidLeft;
                StopEyelidRight = stopEyelidRight;
                StopViseme = stopViseme;
                ShapeKeys = shapeKeys ?? new List<ShapeKeySpec>();
            }

            public bool IsNeutral => Sign == YMFacialMapper.HandSign.Neutral;
            public bool OccupiesEyelidLeft => StopEyelidLeft;
            public bool OccupiesEyelidRight => StopEyelidRight;
            public bool OccupiesViseme => StopViseme;

            public string DisplayName => IsNeutral
                ? "Neutral"
                : $"{Side}{Sign}";
        }

        private readonly struct ShapeKeySpec
        {
            public readonly string Name;
            public readonly float Weight;

            public ShapeKeySpec(string name, float weight)
            {
                Name = name;
                Weight = weight;
            }
        }

    }
}
