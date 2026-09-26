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
        private const string ExternalEyesAnimation = "YM/ExternalEyesAnimation";
        private const string ExternalGestureSuppressed = "YM/ExternalGestureSuppressed";
        private const string JerryFacialExpressionsDisabled = "FacialExpressionsDisabled";
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

            if (!EnsureParameterType(controller, GestureLeft, AnimatorControllerParameterType.Int, component) ||
                !EnsureParameterType(controller, GestureRight, AnimatorControllerParameterType.Int, component))
            {
                return false;
            }

            RemoveExistingLayers(controller);
            // Capture before stripping: even zero-only gesture layers must not supply external tracking.
            var gestureStates = new HashSet<VirtualState>(controller.Layers
                .Where(l => l.StateMachine != null && StateMachineUsesGestureParameters(l.StateMachine))
                .SelectMany(l => l.StateMachine.AllStates()));
            var strippedLayers = StripGestureDrivenFxFaceCurves(controller, component.verboseLog);
            var unlinked = FindUnlinkedControls(controller, strippedLayers);
            if (unlinked.Length > 0)
            {
                var message = "FX LayerControl states cannot relay suppression to YM: " + string.Join(", ", unlinked) +
                    ". No compatible control of a stripped face layer was found. Controls for unrelated layers do not require linking.";
                ErrorReport.ReportError(new FacialMapperLinkWarning(message));
                LogUtility.Warning(ToolName, "FX", message, component);
            }
            var resolver = AddResolverLayer(
                controller,
                animatorServices,
                candidates,
                rendererMap,
                component.writeDefaults);
            ApplyJerrySuppression(controller, resolver, component.writeDefaults);
            InheritStrippedLayerControls(controller, strippedLayers, resolver, component.verboseLog);
            RestoreExternalEyes(controller, resolver, gestureStates);

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

        private static VirtualLayer AddResolverLayer(
            VirtualAnimatorController controller,
            AnimatorServicesContext animatorServices,
            List<Candidate> candidates,
            Dictionary<string, SkinnedMeshRenderer> rendererMap,
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
                    var transition = CreateTransition(state, conditions);
                    transition.CanTransitionToSelf = false;
                    stateMachine.AnyStateTransitions = stateMachine.AnyStateTransitions.Add(transition);
                }
            }

            return layer;
        }

        private static void ApplyJerrySuppression(
            VirtualAnimatorController controller, VirtualLayer resolver, bool writeDefaults)
        {
            // This public Jerry parameter is the only tool-specific parameter we interpret.
            // Read it as-is: do not add parameters or redirect Jerry's own Parameter Drivers.
            if (!controller.Parameters.TryGetValue(JerryFacialExpressionsDisabled, out var parameter)) return;

            AnimatorConditionMode disabledMode;
            AnimatorConditionMode enabledMode;
            float disabledThreshold = 0f;
            float enabledThreshold = 0f;
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Bool:
                    disabledMode = AnimatorConditionMode.If;
                    enabledMode = AnimatorConditionMode.IfNot;
                    break;
                case AnimatorControllerParameterType.Int:
                    disabledMode = AnimatorConditionMode.NotEqual;
                    enabledMode = AnimatorConditionMode.Equals;
                    break;
                case AnimatorControllerParameterType.Float:
                    disabledMode = AnimatorConditionMode.Greater;
                    enabledMode = AnimatorConditionMode.Less;
                    disabledThreshold = 0.5f;
                    // Next representable float: the two strict comparisons cover 0.5 without overlap.
                    enabledThreshold = 0.50000006f;
                    break;
                default:
                    LogUtility.Warning(ToolName, "FX", "FacialExpressionsDisabled must be Bool, Int or Float. Jerry suppression was skipped.");
                    return;
            }

            var enabled = new AnimatorCondition
            {
                parameter = JerryFacialExpressionsDisabled, mode = enabledMode, threshold = enabledThreshold
            };
            var disabled = new AnimatorCondition
            {
                parameter = JerryFacialExpressionsDisabled, mode = disabledMode, threshold = disabledThreshold
            };
            ApplyResolverSuppression(resolver, writeDefaults, enabled, disabled);
        }

        private static void ApplyResolverSuppression(VirtualLayer resolver, bool writeDefaults,
            AnimatorCondition enabled, AnimatorCondition disabled)
        {
            var stateMachine = resolver.StateMachine;
            var suppressed = stateMachine.AllStates().FirstOrDefault(s => s.Name == "External Face Suppressed");
            if (suppressed == null)
            {
                foreach (var state in stateMachine.AllStates())
                    AddLayerWeightControl(state, resolver, 1f, 0f);
                // Start without TrackingControl so external drivers can establish suppression.
                suppressed = stateMachine.AddState("External Face Suppressed", VirtualClip.Create("YM Facial Mapper Suppressed"));
                suppressed.WriteDefaultValues = writeDefaults;
                AddLayerWeightControl(suppressed, resolver, 0f, 0f);
                stateMachine.DefaultState = suppressed;
                resolver.DefaultWeight = 0f;
            }
            // All enable conditions must pass; each suppression source gets its own OR transition.
            foreach (var transition in stateMachine.AnyStateTransitions)
                if (transition.DestinationState != suppressed)
                    transition.Conditions = transition.Conditions.Add(enabled);
            var suppressTransition = CreateTransition(suppressed, ImmutableList.Create(disabled));
            suppressTransition.CanTransitionToSelf = false;
            stateMachine.AnyStateTransitions = stateMachine.AnyStateTransitions.Insert(0, suppressTransition);
        }

        // FX LayerControl indices are virtualized by NDMF; never compare them with list positions.
        private static void InheritStrippedLayerControls(
            VirtualAnimatorController controller,
            HashSet<int> strippedLayers,
            VirtualLayer resolver,
            bool verbose)
        {
            if (strippedLayers.Count == 0) return;

            var inherited = false;
            foreach (var state in controller.AllReachableNodes().OfType<VirtualState>().ToArray())
            {
                var controls = state.Behaviours.OfType<VRCAnimatorLayerControl>()
                    .Where(control => IsEndpointControl(control) && strippedLayers.Contains(control.layer)).ToArray();
                if (controls.Length == 0) continue;
                if (!CompatibleControls(controls))
                {
                    LogUtility.Verbose(ToolName, verbose, "FX",
                        $"Skipped conflicting stripped-layer controls in state {state.Name}.");
                    continue;
                }

                if (!inherited)
                {
                    EnsureInternalBool(controller, ExternalGestureSuppressed);
                    inherited = true;
                }
                AddBoolDriver(state, ExternalGestureSuppressed, controls[0].goalWeight < 0.5f);
            }
            if (inherited)
                ApplyResolverSuppression(resolver, resolver.StateMachine.DefaultState?.WriteDefaultValues ?? false,
                    new AnimatorCondition { parameter = ExternalGestureSuppressed, mode = AnimatorConditionMode.IfNot },
                    new AnimatorCondition { parameter = ExternalGestureSuppressed, mode = AnimatorConditionMode.If });
        }

        internal static bool CompatibleControls(VRCAnimatorLayerControl[] controls)
        {
            if (controls.Length == 0) return false;
            var duration = controls[0].blendDuration;
            return !float.IsNaN(duration) && !float.IsInfinity(duration) && duration >= 0 &&
                controls.All(c => (c.goalWeight < .5f) == (controls[0].goalWeight < .5f) && c.blendDuration == duration);
        }

        internal static bool IsEndpointControl(VRCAnimatorLayerControl control) =>
            control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX &&
            (Mathf.Abs(control.goalWeight) <= .001f || Mathf.Abs(control.goalWeight - 1) <= .001f);

        private static string[] FindUnlinkedControls(VirtualAnimatorController controller, HashSet<int> strippedLayers) =>
            controller.AllReachableNodes().OfType<VirtualState>().Where(state =>
            {
                var controls = state.Behaviours.OfType<VRCAnimatorLayerControl>().Where(IsEndpointControl).ToArray();
                return controls.Length > 0 && !CompatibleControls(controls.Where(c => strippedLayers.Contains(c.layer)).ToArray());
            }).Select(s => s.Name).Distinct().ToArray();

        private static void RestoreExternalEyes(VirtualAnimatorController controller, VirtualLayer resolver,
            HashSet<VirtualState> gestureStates)
        {
            var sm = resolver.StateMachine;
            var initial = sm.AllStates().FirstOrDefault(s => s.Name == "External Face Suppressed");
            if (initial == null) return;
            EnsureInternalBool(controller, ExternalEyesAnimation);

            var excluded = new HashSet<VirtualState>(gestureStates);
            excluded.UnionWith(sm.AllStates());
            foreach (var state in controller.AllReachableNodes().OfType<VirtualState>().Where(s => !excluded.Contains(s)).ToArray())
            {
                var values = state.Behaviours.OfType<VRCAnimatorTrackingControl>()
                    .Select(c => c.trackingEyes).Where(v => v != VRC_AnimatorTrackingControl.TrackingType.NoChange)
                    .Distinct().ToArray();
                if (values.Length != 1) continue;
                AddBoolDriver(state, ExternalEyesAnimation,
                    values[0] == VRC_AnimatorTrackingControl.TrackingType.Animation);
            }

            // Keep the initial empty state: it must not override providers during initialization.
            var suppressTransitions = sm.AnyStateTransitions.Where(t => t.DestinationState == initial).ToArray();
            sm.AnyStateTransitions = sm.AnyStateTransitions.RemoveRange(suppressTransitions);
            foreach (bool animation in new[] { false, true })
            {
                var state = sm.AddState("External Face Suppressed / Eyes " + (animation ? "Animation" : "Tracking"),
                    VirtualClip.Create("YM Facial Mapper Suppressed Eyes"));
                state.WriteDefaultValues = initial.WriteDefaultValues;
                AddLayerWeightControl(state, resolver, 0, 0);
                AddFaceTrackingControl(state, animation, false);
                var tracking = state.Behaviours.OfType<VRCAnimatorTrackingControl>().Single();
                tracking.trackingMouth = VRC_AnimatorTrackingControl.TrackingType.NoChange;
                foreach (var original in suppressTransitions)
                {
                    var transition = CreateTransition(state, original.Conditions.Add(new AnimatorCondition
                    {
                        parameter = ExternalEyesAnimation,
                        mode = animation ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot
                    }));
                    transition.CanTransitionToSelf = false;
                    sm.AnyStateTransitions = sm.AnyStateTransitions.Insert(0, transition);
                }
            }
        }

        private static void EnsureInternalBool(VirtualAnimatorController controller, string name)
        {
            if (controller.Parameters.TryGetValue(name, out var existing))
            {
                if (existing.type != AnimatorControllerParameterType.Bool)
                    throw new InvalidOperationException(name + " must be an Animator Bool.");
                return;
            }

            controller.Parameters = controller.Parameters.Add(name,
                new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Bool });
        }

        private static void AddBoolDriver(VirtualState state, string parameter, bool value)
        {
            // Append a separate driver to preserve external behaviours and their settings.
            var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            driver.localOnly = false;
            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                name = parameter,
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                value = value ? 1f : 0f
            });
            state.Behaviours = state.Behaviours.Add(driver);
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

        private static bool EnsureParameterType(
            VirtualAnimatorController controller,
            string parameterName,
            AnimatorControllerParameterType requiredType,
            YMFacialMapper component)
        {
            if (!ValidateExistingParameterType(controller, parameterName, requiredType, component)) return false;
            if (controller.Parameters.ContainsKey(parameterName)) return true;

            controller.Parameters = controller.Parameters.Add(parameterName, new AnimatorControllerParameter
            {
                name = parameterName,
                type = requiredType
            });
            return true;
        }

        private static bool ValidateExistingParameterType(
            VirtualAnimatorController controller,
            string parameterName,
            AnimatorControllerParameterType requiredType,
            YMFacialMapper component)
        {
            if (!controller.Parameters.TryGetValue(parameterName, out var parameter) || parameter.type == requiredType)
            {
                return true;
            }

            ErrorReport.ReportError(new NdmfBuildError(
                $"[YM Facial Mapper] Parameter `{parameterName}` is {parameter.type} in the FX Animator, " +
                $"but {requiredType} is required. Change the conflicting parameter name or type."));
            LogUtility.Error(
                ToolName,
                "FX",
                $"Parameter type mismatch for `{parameterName}`: expected {requiredType}, found {parameter.type}.",
                component);
            return false;
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

        private static HashSet<int> StripGestureDrivenFxFaceCurves(
            VirtualAnimatorController controller,
            bool verbose)
        {
            var strippedLayers = new HashSet<int>();
            if (controller == null) return strippedLayers;
            var stripped = 0;
            var clipMap = new Dictionary<VirtualClip, VirtualClip>();

            var targetLayers = controller.Layers.Where(layer => layer?.StateMachine != null &&
                StateMachineUsesGestureParameters(layer.StateMachine) &&
                HasNonZeroFaceCurves(layer.StateMachine)).ToArray();
            foreach (var layer in targetLayers)
            {
                var layerStripped = RewriteStateMachineFaceMotions(layer.StateMachine, clipMap);
                if (layerStripped > 0) strippedLayers.Add(layer.VirtualLayerIndex);
                stripped += layerStripped;
            }

            if (stripped > 0)
            {
                LogUtility.Verbose(ToolName, verbose, "FX", $"Stripped {stripped} blend shape curve usages from {strippedLayers.Count} gesture-driven FX layers.");
            }

            return strippedLayers;
        }

        private static bool HasNonZeroFaceCurves(VirtualStateMachine stateMachine)
        {
            if (stateMachine == null) return false;
            foreach (var clip in stateMachine.AllReachableNodes().OfType<VirtualClip>())
            {
                if (clip.IsMarkerClip) continue;
                foreach (var binding in clip.GetFloatCurveBindings().Where(IsBlendShapeBinding))
                {
                    if (HasNonZeroValues(clip.GetFloatCurve(binding))) return true;
                }
            }
            return false;
        }

        internal static bool HasNonZeroValues(AnimationCurve curve)
        {
            var keys = curve?.keys;
            if (keys == null || keys.Length == 0) return false;
            if (keys.Any(key => key.value != 0f)) return true;
            for (var i = 1; i < keys.Length; i++)
            {
                var left = keys[i - 1];
                var right = keys[i];
                if (right.time <= left.time || float.IsInfinity(left.outTangent) || float.IsInfinity(right.inTangent)) continue;
                if (left.outTangent != 0f || right.inTangent != 0f) return true;
            }
            return false;
        }

        private static int RewriteControllerFaceMotions(VirtualAnimatorController controller)
        {
            if (controller == null) return 0;
            var stripped = 0;
            var clipMap = new Dictionary<VirtualClip, VirtualClip>();
            foreach (var layer in controller.Layers.Where(layer =>
                         HasNonZeroFaceCurves(layer?.StateMachine)).ToArray())
            {
                stripped += RewriteStateMachineFaceMotions(layer.StateMachine, clipMap);
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
            var floatBindings = sourceClip.GetFloatCurveBindings().Where(IsBlendShapeBinding).ToArray();
            var objectBindings = sourceClip.GetObjectCurveBindings().Where(IsBlendShapeBinding).ToArray();
            var stripped = floatBindings.Length + objectBindings.Length;
            if (stripped == 0) return (sourceClip, 0);
            // Count every usage, including shared clips, so all affected source layers are recorded.
            if (clipMap.TryGetValue(sourceClip, out var existing)) return (existing, stripped);

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
                .Any(BlendTreeUsesGestureParameters);
        }

        private static bool BlendTreeUsesGestureParameters(VirtualBlendTree blendTree)
        {
            // Unity retains serialized values in fields that are inactive for the current blend type.
            // In particular, a 1D FT tree can still contain GestureLeftWeight in its unused Direct field.
            switch (blendTree.BlendType)
            {
                case BlendTreeType.Direct:
                    return blendTree.Children.Any(child => IsGestureParameter(child.DirectBlendParameter));
                case BlendTreeType.Simple1D:
                    return IsGestureParameter(blendTree.BlendParameter);
                case BlendTreeType.SimpleDirectional2D:
                case BlendTreeType.FreeformDirectional2D:
                case BlendTreeType.FreeformCartesian2D:
                    return IsGestureParameter(blendTree.BlendParameter) || IsGestureParameter(blendTree.BlendParameterY);
                default:
                    return false;
            }
        }

        internal static bool IsGestureParameter(string parameterName)
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
