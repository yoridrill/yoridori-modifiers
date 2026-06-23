using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using nadena.dev.ndmf;
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
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("jp.yoridrill.ym-arm-patch")
                .AfterPlugin("jp.yoridrill.ym-mesh-trimmer")
                .AfterPlugin("jp.yoridrill.ym-mtoon-to-liltoon")
                .AfterPlugin("jp.yoridrill.ym-eye-freeze")
                .AfterPlugin("nadena.dev.modular-avatar")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Build YM Facial Mapper", Execute);
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
                LogUtility.Warning(ToolName, "Build", "No SkinnedMeshRenderer with configured shape keys was found. Expression states may be empty.", component);
            }

            StripOriginalGestureLayerFaceCurves(context, descriptor, component);
            var controller = BuildFxController(context, descriptor, component, candidates, renderers);
            if (controller == null) return;

            LogUtility.Verbose(ToolName, component.verboseLog, "Build", $"Built {candidates.Count} expression candidates.");
        }

        private static AnimatorController BuildFxController(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            YMFacialMapper component,
            List<Candidate> candidates,
            Dictionary<string, SkinnedMeshRenderer> rendererMap)
        {
            var layerIndex = EnsureFxLayer(descriptor);
            if (layerIndex < 0)
            {
                LogUtility.Warning(ToolName, "Build", "FX layer not found. Skipped.", component);
                return null;
            }

            var sourceController = GetFxAnimatorController(descriptor);
            var controller = sourceController != null
                ? Object.Instantiate(sourceController)
                : new AnimatorController();
            RegisterReplacedObject(sourceController, controller);

            controller.name = "YM Facial Mapper FX";
            context.AssetSaver.SaveAsset(controller);

            RemoveExistingLayers(controller);
            StripGestureDrivenFxFaceCurves(controller, context, component.verboseLog);
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

            AddResolverLayer(controller, context, candidates, rendererMap, externalFaceBlockers);

            descriptor.customizeAnimationLayers = true;
            var layers = descriptor.baseAnimationLayers;
            layers[layerIndex].isDefault = false;
            layers[layerIndex].animatorController = controller;
            descriptor.baseAnimationLayers = layers;

            return controller;
        }

        private static AnimationClip CreateResolvedExpressionClip(
            GameObject avatarRoot,
            string clipName,
            IReadOnlyList<Candidate> activeCandidates,
            IReadOnlyCollection<ShapeKeySpec> allShapeKeys,
            Dictionary<string, SkinnedMeshRenderer> rendererMap,
            bool includeActiveValues)
        {
            var clip = new AnimationClip
            {
                name = clipName
            };

            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var shapeKey in allShapeKeys)
            {
                values[shapeKey.Name] = 0f;
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
                var path = AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot.transform);
                clip.SetCurve(
                    path,
                    typeof(SkinnedMeshRenderer),
                    "blendShape." + pair.Key,
                    OneKeyCurve(pair.Value));
            }

            return clip;
        }

        private static void AddResolverLayer(
            AnimatorController controller,
            BuildContext context,
            List<Candidate> candidates,
            Dictionary<string, SkinnedMeshRenderer> rendererMap,
            IReadOnlyList<ConditionGroup> externalFaceBlockers)
        {
            var allShapeKeys = candidates
                .SelectMany(candidate => candidate.ShapeKeys)
                .GroupBy(shapeKey => shapeKey.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();

            controller.AddLayer($"{ToolName} Resolver");
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;

            var stateMachine = layer.stateMachine;
            stateMachine.name = $"{ToolName} Resolver";

            var resetClip = CreateResolvedExpressionClip(
                context.AvatarRootObject,
                $"{ToolName} Reset",
                Array.Empty<Candidate>(),
                allShapeKeys,
                rendererMap,
                includeActiveValues: false);
            context.AssetSaver.SaveAsset(resetClip);

            var resetState = stateMachine.AddState("Reset", new Vector3(220f, 80f, 0f));
            resetState.motion = resetClip;
            resetState.writeDefaultValues = false;
            AddFaceTrackingControl(resetState, stopEyelids: false, stopViseme: false);
            stateMachine.defaultState = resetState;

            foreach (var leftSign in Enum.GetValues(typeof(YMFacialMapper.HandSign)).Cast<YMFacialMapper.HandSign>())
            {
                foreach (var rightSign in Enum.GetValues(typeof(YMFacialMapper.HandSign)).Cast<YMFacialMapper.HandSign>())
                {
                    var activeCandidates = ResolveActiveCandidates(candidates, leftSign, rightSign);
                    var stateName = $"L{(int)leftSign}_R{(int)rightSign}";
                    var clip = CreateResolvedExpressionClip(
                        context.AvatarRootObject,
                        $"{ToolName} {stateName}",
                        activeCandidates,
                        allShapeKeys,
                        rendererMap,
                        includeActiveValues: true);
                    context.AssetSaver.SaveAsset(clip);

                    var state = stateMachine.AddState(stateName, new Vector3(220f + (int)rightSign * 180f, 180f + (int)leftSign * 60f, 0f));
                    state.motion = clip;
                    state.writeDefaultValues = false;
                    AddFaceTrackingControl(state, activeCandidates);
                    AddExternalFaceResetTransitions(state, resetState, externalFaceBlockers);

                    var transition = stateMachine.AddAnyStateTransition(state);
                    ConfigureTransition(transition);
                    transition.canTransitionToSelf = false;
                    transition.AddCondition(AnimatorConditionMode.Equals, (float)leftSign, GestureLeft);
                    transition.AddCondition(AnimatorConditionMode.Equals, (float)rightSign, GestureRight);
                    AddSingleConditionExternalFaceGuards(transition, externalFaceBlockers);
                }
            }

            layers[layers.Length - 1] = layer;
            controller.layers = layers;
        }

        private static void AddFaceTrackingControl(AnimatorState state, IReadOnlyList<Candidate> candidates)
        {
            var stopEyelids = candidates != null && candidates.Any(candidate => candidate.StopEyelidLeft || candidate.StopEyelidRight);
            var stopViseme = candidates != null && candidates.Any(candidate => candidate.StopViseme);
            AddFaceTrackingControl(state, stopEyelids, stopViseme);
        }

        private static void AddFaceTrackingControl(AnimatorState state, bool stopEyelids, bool stopViseme)
        {
            if (state == null) return;

            var control = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
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
        }

        private static void AddExternalFaceResetTransitions(
            AnimatorState state,
            AnimatorState resetState,
            IReadOnlyList<ConditionGroup> externalFaceBlockers)
        {
            if (state == null || resetState == null || externalFaceBlockers == null) return;

            foreach (var blocker in externalFaceBlockers)
            {
                if (blocker?.Conditions == null || blocker.Conditions.Length == 0) continue;
                var transition = state.AddTransition(resetState);
                ConfigureTransition(transition);
                foreach (var condition in blocker.Conditions)
                {
                    transition.AddCondition(condition.Mode, condition.Threshold, condition.Parameter);
                }
            }
        }

        private static void AddSingleConditionExternalFaceGuards(
            AnimatorStateTransition transition,
            IReadOnlyList<ConditionGroup> externalFaceBlockers)
        {
            if (transition == null || externalFaceBlockers == null) return;

            foreach (var blocker in externalFaceBlockers)
            {
                if (blocker?.Conditions == null || blocker.Conditions.Length != 1) continue;
                var inverse = InvertCondition(blocker.Conditions[0]);
                transition.AddCondition(inverse.Mode, inverse.Threshold, inverse.Parameter);
            }
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
                    LogUtility.Warning(ToolName, "ShapeKey", $"Shape key not found: {shapeName}");
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

        private static void ConfigureTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.exitTime = 0f;
        }

        private static void AddIntParameterIfMissing(AnimatorController controller, string parameterName)
        {
            if (controller.parameters.Any(p => p.name == parameterName)) return;
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = parameterName,
                type = AnimatorControllerParameterType.Int,
                defaultInt = 0
            });
        }

        private static void AddBoolParameterIfMissing(AnimatorController controller, string parameterName)
        {
            if (controller.parameters.Any(p => p.name == parameterName)) return;
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = parameterName,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false
            });
        }

        private static void EnsureBlendTreeParameters(AnimatorController controller, bool verbose)
        {
            if (controller == null) return;

            var existing = new HashSet<string>(controller.parameters.Select(parameter => parameter.name), StringComparer.Ordinal);
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var layer in controller.layers)
            {
                CollectBlendTreeParameters(layer?.stateMachine, referenced);
            }

            var added = 0;
            foreach (var parameterName in referenced.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(parameterName) || existing.Contains(parameterName)) continue;
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = parameterName,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f
                });
                existing.Add(parameterName);
                added++;
            }

            if (added > 0)
            {
                LogUtility.Verbose(ToolName, verbose, "FX", $"Added {added} missing BlendTree parameters to the copied FX controller.");
            }
        }

        private static void CollectBlendTreeParameters(AnimatorStateMachine stateMachine, HashSet<string> parameters)
        {
            if (stateMachine == null || parameters == null) return;

            foreach (var childState in stateMachine.states)
            {
                CollectBlendTreeParameters(childState.state?.motion, parameters);
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                CollectBlendTreeParameters(childMachine.stateMachine, parameters);
            }
        }

        private static void CollectBlendTreeParameters(Motion motion, HashSet<string> parameters)
        {
            if (motion is not BlendTree blendTree || parameters == null) return;

            AddParameterName(parameters, blendTree.blendParameter);
            AddParameterName(parameters, blendTree.blendParameterY);
            foreach (var child in blendTree.children)
            {
                AddParameterName(parameters, child.directBlendParameter);
                CollectBlendTreeParameters(child.motion, parameters);
            }
        }

        private static void AddParameterName(HashSet<string> parameters, string parameterName)
        {
            if (parameters == null || string.IsNullOrWhiteSpace(parameterName)) return;
            parameters.Add(parameterName.Trim());
        }

        private static void RedirectJerryFacialExpressionsDisabledDrivers(AnimatorController controller, bool verbose)
        {
            if (controller == null) return;

            var redirected = 0;
            var layers = controller.layers;
            foreach (var layer in layers)
            {
                redirected += RedirectParameterDrivers(layer?.stateMachine, JerryDisableFacialExpressions, JerryInternalFacialExpressionsDisabled);
            }

            if (redirected <= 0) return;

            AddBoolParameterIfMissing(controller, JerryInternalFacialExpressionsDisabled);
            LogUtility.Verbose(
                ToolName,
                verbose,
                "FX",
                $"Redirected {redirected} Jerry internal writes from {JerryDisableFacialExpressions} to {JerryInternalFacialExpressionsDisabled}.");
        }

        private static int RedirectParameterDrivers(
            AnimatorStateMachine stateMachine,
            string sourceParameter,
            string destinationParameter)
        {
            if (stateMachine == null ||
                string.IsNullOrWhiteSpace(sourceParameter) ||
                string.IsNullOrWhiteSpace(destinationParameter))
            {
                return 0;
            }

            var redirected = 0;
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;
                foreach (var driver in state.behaviours.OfType<VRCAvatarParameterDriver>())
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

            foreach (var childMachine in stateMachine.stateMachines)
            {
                redirected += RedirectParameterDrivers(childMachine.stateMachine, sourceParameter, destinationParameter);
            }

            return redirected;
        }

        private static void SuppressExistingEyeTrackingRestores(AnimatorController controller, bool verbose)
        {
            if (controller == null) return;

            var changed = 0;
            var tracking = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            var noChange = VRC_AnimatorTrackingControl.TrackingType.NoChange;
            foreach (var layer in controller.layers)
            {
                changed += SuppressExistingEyeTrackingRestores(layer?.stateMachine, tracking, noChange);
            }

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
            AnimatorStateMachine stateMachine,
            VRC_AnimatorTrackingControl.TrackingType tracking,
            VRC_AnimatorTrackingControl.TrackingType noChange)
        {
            if (stateMachine == null) return 0;

            var changed = 0;
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;
                foreach (var control in state.behaviours.OfType<VRCAnimatorTrackingControl>())
                {
                    if (control.trackingEyes != tracking) continue;
                    control.trackingEyes = noChange;
                    changed++;
                }
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                changed += SuppressExistingEyeTrackingRestores(childMachine.stateMachine, tracking, noChange);
            }

            return changed;
        }

        private static void StripOriginalGestureLayerFaceCurves(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            YMFacialMapper component)
        {
            var layerIndex = FindBaseLayerIndex(descriptor, VRCAvatarDescriptor.AnimLayerType.Gesture);
            if (layerIndex < 0) return;

            var layers = descriptor.baseAnimationLayers;
            if (layers == null || layerIndex >= layers.Length || layers[layerIndex].isDefault) return;
            if (layers[layerIndex].animatorController is not AnimatorController sourceController) return;

            var controller = Object.Instantiate(sourceController);
            RegisterReplacedObject(sourceController, controller);
            controller.name = $"{sourceController.name} YM Facial Mapper Gesture Face Stripped";
            context.AssetSaver.SaveAsset(controller);

            var clipMap = new Dictionary<AnimationClip, AnimationClip>();
            var stripped = RewriteStateMachineFaceMotions(controller.layers, context, clipMap);
            if (stripped > 0)
            {
                LogUtility.Verbose(ToolName, component.verboseLog, "Gesture", $"Stripped {stripped} blend shape curves from copied Gesture animations.");
            }

            layers[layerIndex].animatorController = controller;
            descriptor.baseAnimationLayers = layers;
        }

        private static void StripGestureDrivenFxFaceCurves(
            AnimatorController controller,
            BuildContext context,
            bool verbose)
        {
            if (controller == null) return;
            var clipMap = new Dictionary<AnimationClip, AnimationClip>();
            var stripped = 0;

            var layers = controller.layers;
            for (var i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer == null || !StateMachineUsesGestureParameters(layer.stateMachine)) continue;
                stripped += RewriteStateMachineFaceMotions(layer.stateMachine, context, clipMap);
                layers[i] = layer;
            }

            controller.layers = layers;
            if (stripped > 0)
            {
                LogUtility.Verbose(ToolName, verbose, "FX", $"Stripped {stripped} blend shape curves from copied gesture-driven FX animations.");
            }
        }

        private static List<ConditionGroup> CollectExternalFaceBlockers(AnimatorController controller)
        {
            var groups = new List<ConditionGroup>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (controller == null) return groups;

            foreach (var layer in controller.layers)
            {
                CollectExternalFaceBlockers(controller, layer?.stateMachine, groups, keys);
            }

            return groups;
        }

        private static void AddExistingParameterBlocker(
            AnimatorController controller,
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

        private static void CollectExternalFaceBlockers(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            List<ConditionGroup> groups,
            HashSet<string> keys)
        {
            if (stateMachine == null) return;

            foreach (var transition in stateMachine.anyStateTransitions)
            {
                TryAddExternalFaceBlocker(controller, transition, groups, keys);
            }

            foreach (var transition in stateMachine.entryTransitions)
            {
                TryAddExternalFaceBlocker(controller, transition, groups, keys);
            }

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;
                foreach (var transition in state.transitions)
                {
                    TryAddExternalFaceBlocker(controller, transition, groups, keys);
                }
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                CollectExternalFaceBlockers(controller, childMachine.stateMachine, groups, keys);
            }
        }

        private static void TryAddExternalFaceBlocker(
            AnimatorController controller,
            AnimatorTransitionBase transition,
            List<ConditionGroup> groups,
            HashSet<string> keys)
        {
            if (transition == null || transition.destinationState == null) return;
            if (IsFaceTrackingState(transition.destinationState)) return;
            if (!MotionHasBlendShapeCurves(transition.destinationState.motion)) return;

            var conditions = transition.conditions
                .Where(condition => IsExternalFaceBlockerCondition(condition.parameter))
                .Select(condition => NormalizeCondition(controller, condition.parameter, condition.mode, condition.threshold))
                .ToArray();
            if (conditions.Length == 0) return;

            var key = string.Join("|", conditions
                .OrderBy(condition => condition.Parameter, StringComparer.Ordinal)
                .ThenBy(condition => condition.Mode)
                .ThenBy(condition => condition.Threshold)
                .Select(condition => $"{condition.Parameter}:{condition.Mode}:{condition.Threshold.ToString(CultureInfo.InvariantCulture)}"));
            if (!keys.Add(key)) return;

            groups.Add(new ConditionGroup(conditions));
        }

        private static bool IsFaceTrackingState(AnimatorState state)
        {
            if (state == null) return false;
            return IsFaceTrackingName(state.name) || IsFaceTrackingMotion(state.motion);
        }

        private static bool IsFaceTrackingMotion(Motion motion)
        {
            switch (motion)
            {
                case null:
                    return false;
                case BlendTree blendTree:
                    return IsFaceTrackingName(blendTree.name) ||
                           IsFaceTrackingParameter(blendTree.blendParameter) ||
                           IsFaceTrackingParameter(blendTree.blendParameterY) ||
                           blendTree.children.Any(child =>
                               IsFaceTrackingParameter(child.directBlendParameter) ||
                               IsFaceTrackingMotion(child.motion));
                default:
                    return IsFaceTrackingName(motion.name);
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

        private static bool MotionHasBlendShapeCurves(Motion motion)
        {
            switch (motion)
            {
                case null:
                    return false;
                case AnimationClip clip:
                    return AnimationUtility.GetCurveBindings(clip).Any(IsBlendShapeBinding) ||
                           AnimationUtility.GetObjectReferenceCurveBindings(clip).Any(IsBlendShapeBinding);
                case BlendTree blendTree:
                    return blendTree.children.Any(child => MotionHasBlendShapeCurves(child.motion));
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
            AnimatorController controller,
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
            AnimatorController controller,
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

        private static AnimatorControllerParameterType? GetParameterType(AnimatorController controller, string parameterName)
        {
            if (controller == null || string.IsNullOrWhiteSpace(parameterName)) return null;
            return controller.parameters.FirstOrDefault(parameter => parameter.name == parameterName)?.type;
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

        private static int RewriteStateMachineFaceMotions(
            AnimatorControllerLayer[] layers,
            BuildContext context,
            Dictionary<AnimationClip, AnimationClip> clipMap)
        {
            if (layers == null) return 0;
            var stripped = 0;
            foreach (var layer in layers)
            {
                stripped += RewriteStateMachineFaceMotions(layer?.stateMachine, context, clipMap);
            }

            return stripped;
        }

        private static int RewriteStateMachineFaceMotions(
            AnimatorStateMachine stateMachine,
            BuildContext context,
            Dictionary<AnimationClip, AnimationClip> clipMap)
        {
            if (stateMachine == null) return 0;
            var stripped = 0;

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null || state.motion == null) continue;
                var result = RewriteFaceMotion(state.motion, context, clipMap);
                if (result.motion != state.motion)
                {
                    state.motion = result.motion;
                }

                stripped += result.strippedCurves;
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                stripped += RewriteStateMachineFaceMotions(childMachine.stateMachine, context, clipMap);
            }

            return stripped;
        }

        private static bool StateMachineUsesGestureParameters(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null) return false;

            if (stateMachine.anyStateTransitions.Any(TransitionUsesGestureParameters) ||
                stateMachine.entryTransitions.Any(TransitionUsesGestureParameters))
            {
                return true;
            }

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;
                if (state.transitions.Any(TransitionUsesGestureParameters)) return true;
                if (MotionUsesGestureParameters(state.motion)) return true;
            }

            return stateMachine.stateMachines.Any(child => StateMachineUsesGestureParameters(child.stateMachine));
        }

        private static bool TransitionUsesGestureParameters(AnimatorTransitionBase transition)
        {
            return transition != null && transition.conditions.Any(condition => IsGestureParameter(condition.parameter));
        }

        private static bool MotionUsesGestureParameters(Motion motion)
        {
            if (motion is not BlendTree blendTree) return false;
            if (IsGestureParameter(blendTree.blendParameter) ||
                IsGestureParameter(blendTree.blendParameterY))
            {
                return true;
            }

            return blendTree.children.Any(child => MotionUsesGestureParameters(child.motion));
        }

        private static bool IsGestureParameter(string parameterName)
        {
            return parameterName == GestureLeft ||
                   parameterName == GestureRight ||
                   parameterName == "GestureLeftWeight" ||
                   parameterName == "GestureRightWeight";
        }

        private static (Motion motion, int strippedCurves) RewriteFaceMotion(
            Motion motion,
            BuildContext context,
            Dictionary<AnimationClip, AnimationClip> clipMap)
        {
            switch (motion)
            {
                case null:
                    return (null, 0);
                case AnimationClip clip:
                    var strippedClip = StripBlendShapeCurvesFromClip(clip, context, clipMap, out var strippedCurves);
                    return (strippedClip, strippedCurves);
                case BlendTree blendTree:
                    return RewriteFaceBlendTree(blendTree, context, clipMap);
                default:
                    return (motion, 0);
            }
        }

        private static (Motion motion, int strippedCurves) RewriteFaceBlendTree(
            BlendTree sourceTree,
            BuildContext context,
            Dictionary<AnimationClip, AnimationClip> clipMap)
        {
            if (sourceTree == null) return (null, 0);
            var stripped = 0;
            var children = sourceTree.children;
            var rewrittenChildren = children;
            var changed = false;

            for (var i = 0; i < children.Length; i++)
            {
                var result = RewriteFaceMotion(children[i].motion, context, clipMap);
                stripped += result.strippedCurves;
                if (result.motion == children[i].motion) continue;
                if (!changed)
                {
                    rewrittenChildren = children.ToArray();
                    changed = true;
                }

                rewrittenChildren[i].motion = result.motion;
            }

            if (!changed) return (sourceTree, stripped);

            var tree = Object.Instantiate(sourceTree);
            RegisterReplacedObject(sourceTree, tree);
            tree.name = $"{sourceTree.name} YM Facial Mapper Face Stripped";
            tree.children = rewrittenChildren;
            context.AssetSaver.SaveAsset(tree);
            return (tree, stripped);
        }

        private static AnimationClip StripBlendShapeCurvesFromClip(
            AnimationClip sourceClip,
            BuildContext context,
            Dictionary<AnimationClip, AnimationClip> clipMap,
            out int strippedCurves)
        {
            strippedCurves = 0;
            if (sourceClip == null) return null;
            if (clipMap.TryGetValue(sourceClip, out var existing)) return existing;

            var bindings = AnimationUtility.GetCurveBindings(sourceClip)
                .Where(IsBlendShapeBinding)
                .ToArray();
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(sourceClip)
                .Where(IsBlendShapeBinding)
                .ToArray();
            strippedCurves = bindings.Length + objectBindings.Length;
            if (strippedCurves == 0) return sourceClip;

            var clip = Object.Instantiate(sourceClip);
            RegisterReplacedObject(sourceClip, clip);
            clip.name = $"{sourceClip.name} YM Facial Mapper Face Stripped";

            foreach (var binding in bindings)
            {
                AnimationUtility.SetEditorCurve(clip, binding, null);
            }

            foreach (var binding in objectBindings)
            {
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            }

            context.AssetSaver.SaveAsset(clip);
            clipMap[sourceClip] = clip;
            return clip;
        }

        private static bool IsBlendShapeBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(SkinnedMeshRenderer) &&
                   binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);
        }

        private static void RemoveExistingLayers(AnimatorController controller)
        {
            controller.layers = controller.layers
                .Where(layer => layer != null && !layer.name.StartsWith(ToolName, StringComparison.Ordinal))
                .ToArray();
        }

        internal static AnimatorController GetFxAnimatorController(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) return null;
            var layers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            foreach (var layer in layers)
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX || layer.isDefault) continue;
                return layer.animatorController as AnimatorController;
            }

            return null;
        }

        private static int EnsureFxLayer(VRCAvatarDescriptor descriptor)
        {
            var layers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            for (var i = 0; i < layers.Length; i++)
            {
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX) return i;
            }

            Array.Resize(ref layers, layers.Length + 1);
            var index = layers.Length - 1;
            layers[index] = new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.FX,
                isDefault = false
            };
            descriptor.baseAnimationLayers = layers;
            return index;
        }

        private static int FindBaseLayerIndex(VRCAvatarDescriptor descriptor, VRCAvatarDescriptor.AnimLayerType layerType)
        {
            var layers = descriptor != null
                ? descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()
                : Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            for (var i = 0; i < layers.Length; i++)
            {
                if (layers[i].type == layerType) return i;
            }

            return -1;
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

        private static void RegisterReplacedObject(Object original, Object replacement)
        {
            if (original == null || replacement == null) return;
            try
            {
                ObjectRegistry.RegisterReplacedObject(original, replacement);
            }
            catch (ArgumentException)
            {
                // The replacement may already be known to NDMF if another service touched it first.
            }
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

            public ConditionGroup(ConditionSpec[] conditions)
            {
                Conditions = conditions ?? Array.Empty<ConditionSpec>();
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
