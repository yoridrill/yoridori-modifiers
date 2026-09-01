using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using YoridoriModifiers.Core.Editor;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(YoridoriModifiers.EyeFreeze.EyeFreezeNdmfPlugin))]

namespace YoridoriModifiers.EyeFreeze
{
    public sealed class EyeFreezeNdmfPlugin : Plugin<EyeFreezeNdmfPlugin>
    {
        private const string ToolName = "YM Eye Freeze";
        private const string QualifiedPluginName = "jp.yoridrill.ym-eye-freeze";
        private const string IconPath = "Packages/jp.yoridrill.yoridori-modifiers/icon.png";
        private const string LeftEyeTargetName = "YM_LeftEye_FreezeRotationTarget";
        private const string RightEyeTargetName = "YM_RightEye_FreezeRotationTarget";
        private static readonly string[] JerryEyeTrackingActiveParameterCandidates =
        {
            "EyeTrackingActive",
            "FT/EyeTrackingActive"
        };

        public override string QualifiedName => QualifiedPluginName;
        public override string DisplayName => ToolName;

        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Resolve YM Eye Freeze parameter remapping", ResolveParameterRemapping);

            var sequence = InPhase(BuildPhase.Transforming)
                .AfterPlugin("jp.yoridrill.ym-arm-patch")
                .AfterPlugin("jp.yoridrill.ym-mesh-trimmer")
                .AfterPlugin("jp.yoridrill.ym-mtoon-to-liltoon")
                .AfterPlugin("nadena.dev.modular-avatar")
                .BeforePlugin("com.anatawa12.avatar-optimizer");

            sequence.Run("Prepare YM Eye Freeze FX layer", PrepareFxLayer);
            sequence.WithRequiredExtension(typeof(AnimatorServicesContext), scoped =>
            {
                scoped.Run("Build YM Eye Freeze", Execute);
            });
        }

        private static void ResolveParameterRemapping(BuildContext context)
        {
            if (context?.AvatarRootObject == null) return;

            var parameterInfo = ParameterInfo.ForContext(context);
            foreach (var component in context.AvatarRootObject.GetComponentsInChildren<YMEyeFreeze>(true))
            {
                if (component == null) continue;

                var parameterName = EyeFreezeParameterProvider.NormalizeParameterName(component.parameterName);
                var remappings = parameterInfo.GetParameterRemappingsAt(component, false);
                if (remappings.TryGetValue(
                        (ParameterNamespace.Animator, parameterName),
                        out var remapping) &&
                    !string.IsNullOrWhiteSpace(remapping.ParameterName))
                {
                    component.parameterName = remapping.ParameterName;
                }
                else
                {
                    component.parameterName = parameterName;
                }
            }
        }

        private static void PrepareFxLayer(BuildContext context)
        {
            if (context?.AvatarRootObject == null) return;
            if (!context.AvatarRootObject.GetComponentsInChildren<YMEyeFreeze>(true).Any()) return;

            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null) EnsureFxLayer(descriptor);
        }

        private static void Execute(BuildContext context)
        {
            if (context == null || context.AvatarRootObject == null) return;

            var components = context.AvatarRootObject.GetComponentsInChildren<YMEyeFreeze>(true);
            if (components == null || components.Length == 0) return;

            var component = SelectPreferredComponent(components, context.AvatarRootObject);
            if (component == null) return;

            try
            {
                ErrorReport.WithContextObject(component, () => Build(context, component));
            }
            finally
            {
                RemoveComponents(components);
            }
        }

        private static YMEyeFreeze SelectPreferredComponent(YMEyeFreeze[] components, GameObject avatarRoot)
        {
            if (components == null || components.Length == 0) return null;
            var rootTransform = avatarRoot != null ? avatarRoot.transform : null;
            return components
                .Where(c => c != null)
                .OrderByDescending(c => c.transform == rootTransform)
                .ThenBy(c => PreviewCoordinator.GetDepthFromRoot(c.transform, rootTransform))
                .FirstOrDefault();
        }

        private static void Build(BuildContext context, YMEyeFreeze component)
        {
            var avatarRoot = context.AvatarRootObject;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                Debug.LogWarning("[YM Eye Freeze] VRCAvatarDescriptor not found. Skipped.");
                return;
            }

            var leftEye = descriptor.enableEyeLook ? descriptor.customEyeLookSettings.leftEye : null;
            var rightEye = descriptor.enableEyeLook ? descriptor.customEyeLookSettings.rightEye : null;
            if (leftEye == null || rightEye == null)
            {
                Debug.LogWarning("[YM Eye Freeze] Eye Look or left/right eye bones are not configured. Skipped.");
                return;
            }

            var parameterName = EyeFreezeParameterProvider.NormalizeParameterName(component.parameterName);
            var menuName = string.IsNullOrWhiteSpace(component.menuName)
                ? "Eye Freeze"
                : component.menuName.Trim();

            var animator = avatarRoot.GetComponentInChildren<Animator>(true);
            var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null)
            {
                Debug.LogWarning("[YM Eye Freeze] Humanoid head bone not found. Skipped.");
                return;
            }

            var animatorServices = context.Extension<AnimatorServicesContext>();
            if (!ValidateParameterTypes(animatorServices, descriptor, parameterName)) return;

            var leftConstraint = BuildEyeRotationConstraint(leftEye, CreateFreezeTarget(head, leftEye, LeftEyeTargetName));
            var rightConstraint = BuildEyeRotationConstraint(rightEye, CreateFreezeTarget(head, rightEye, RightEyeTargetName));

            var offClip = CreateEyeFreezeClip(animatorServices, leftConstraint, rightConstraint, false, "YM Eye Freeze Off");
            var onClip = CreateEyeFreezeClip(animatorServices, leftConstraint, rightConstraint, true, "YM Eye Freeze On");

            if (!BuildFxController(animatorServices, parameterName, offClip, onClip)) return;

            if (MergeExpressionParameter(context, descriptor, parameterName, component.saved, component.synced))
            {
                MergeExpressionMenu(context, descriptor, menuName, parameterName);
            }
            else
            {
                RemoveExpressionMenuControl(context, descriptor, parameterName);
            }
        }

        private static bool ValidateParameterTypes(
            AnimatorServicesContext animatorServices,
            VRCAvatarDescriptor descriptor,
            string parameterName)
        {
            if (descriptor?.expressionParameters?.parameters != null)
            {
                var expressionParameter = descriptor.expressionParameters.parameters
                    .FirstOrDefault(parameter => parameter != null && parameter.name == parameterName);
                if (expressionParameter != null &&
                    expressionParameter.valueType != VRCExpressionParameters.ValueType.Bool)
                {
                    ReportParameterTypeError(
                        parameterName,
                        "Expression Parameters",
                        expressionParameter.valueType.ToString(),
                        "Bool");
                    return false;
                }
            }

            if (animatorServices.ControllerContext.Controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.FX,
                    out var controller) &&
                controller.Parameters.TryGetValue(parameterName, out var animatorParameter) &&
                animatorParameter.type != AnimatorControllerParameterType.Bool)
            {
                ReportParameterTypeError(
                    parameterName,
                    "FX Animator",
                    animatorParameter.type.ToString(),
                    AnimatorControllerParameterType.Bool.ToString());
                return false;
            }

            return true;
        }

        private static void ReportParameterTypeError(
            string parameterName,
            string location,
            string actualType,
            string requiredType)
        {
            ErrorReport.ReportError(new NdmfBuildError(
                $"[YM Eye Freeze] Parameter `{parameterName}` is {actualType} in {location}, but {requiredType} is required. " +
                "Change the conflicting parameter name or type."));
        }

        private static Transform CreateFreezeTarget(Transform head, Transform eye, string targetName)
        {
            var target = new GameObject(targetName).transform;
            target.SetParent(head, false);
            target.position = eye.position;
            target.rotation = eye.rotation;
            target.localScale = Vector3.one;
            return target;
        }

        private static VRCRotationConstraint BuildEyeRotationConstraint(Transform eye, Transform source)
        {
            var constraint = eye.gameObject.AddComponent<VRCRotationConstraint>();

            constraint.IsActive = false;
            constraint.GlobalWeight = 1f;
            constraint.Locked = true;
            constraint.SolveInLocalSpace = false;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;

            constraint.RotationAtRest = eye.localEulerAngles;
            constraint.RotationOffset = Vector3.zero;

            constraint.AffectsRotationX = true;
            constraint.AffectsRotationY = true;
            constraint.AffectsRotationZ = true;

            constraint.Sources.Clear();
            constraint.Sources.Add(new VRCConstraintSource(source, 1f));

            constraint.ApplyConfigurationChanges();
            return constraint;
        }

        private static VirtualClip CreateEyeFreezeClip(
            AnimatorServicesContext animatorServices,
            VRCRotationConstraint leftConstraint,
            VRCRotationConstraint rightConstraint,
            bool isActive,
            string clipName)
        {
            var clip = VirtualClip.Create(clipName);

            AddConstraintActiveCurve(
                clip,
                animatorServices.ObjectPathRemapper.GetVirtualPathForObject(leftConstraint.gameObject),
                isActive);
            AddConstraintActiveCurve(
                clip,
                animatorServices.ObjectPathRemapper.GetVirtualPathForObject(rightConstraint.gameObject),
                isActive);
            return clip;
        }

        private static void AddConstraintActiveCurve(VirtualClip clip, string path, bool isActive)
        {
            clip.SetFloatCurve(
                path,
                typeof(VRCRotationConstraint),
                nameof(VRCConstraintBase.IsActive),
                OneKeyCurve(isActive ? 1f : 0f));
        }

        private static AnimationCurve OneKeyCurve(float value)
        {
            return new AnimationCurve(new Keyframe(0f, value));
        }

        private static bool BuildFxController(
            AnimatorServicesContext animatorServices,
            string parameterName,
            VirtualClip offClip,
            VirtualClip onClip)
        {
            if (!animatorServices.ControllerContext.Controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.FX,
                    out var controller))
            {
                Debug.LogWarning("[YM Eye Freeze] FX layer not found. Skipped.");
                return false;
            }

            controller.Name = "YM Eye Freeze FX";

            EnsureBlendTreeParameters(controller);
            AddBoolParameterIfMissing(controller, parameterName);
            var eyeTrackingActiveParameter = FindExistingParameterName(controller, JerryEyeTrackingActiveParameterCandidates);
            AddEyeFreezeLayer(controller, parameterName, offClip, onClip, eyeTrackingActiveParameter);
            return true;
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

        private static string FindExistingParameterName(VirtualAnimatorController controller, string[] candidates)
        {
            if (controller == null || candidates == null) return null;

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var parameterName = candidate.Trim();
                if (controller.Parameters.ContainsKey(parameterName))
                {
                    return parameterName;
                }
            }

            return null;
        }

        private static void EnsureBlendTreeParameters(VirtualAnimatorController controller)
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

            foreach (var parameterName in referenced.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(parameterName) || parameters.ContainsKey(parameterName)) continue;
                parameters = parameters.Add(parameterName, new AnimatorControllerParameter
                {
                    name = parameterName,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f
                });
            }
            controller.Parameters = parameters;
        }

        private static void AddParameterName(HashSet<string> parameters, string parameterName)
        {
            if (parameters == null || string.IsNullOrWhiteSpace(parameterName)) return;
            parameters.Add(parameterName.Trim());
        }

        private static void AddEyeFreezeLayer(
            VirtualAnimatorController controller,
            string parameterName,
            VirtualClip offClip,
            VirtualClip onClip,
            string eyeTrackingActiveParameter)
        {
            controller.RemoveLayers(layer => layer.Name == ToolName);

            var layer = controller.AddLayer(LayerPriority.Default, ToolName);
            layer.DefaultWeight = 1f;

            var stateMachine = layer.StateMachine;
            stateMachine.Name = ToolName;

            var offState = stateMachine.AddState("Off", offClip, new Vector3(240f, 80f, 0f));
            offState.WriteDefaultValues = false;

            var onState = stateMachine.AddState("On", onClip, new Vector3(240f, 180f, 0f));
            onState.WriteDefaultValues = false;

            stateMachine.DefaultState = offState;

            var toOnConditions = ImmutableList.Create(new AnimatorCondition
            {
                mode = AnimatorConditionMode.If,
                parameter = parameterName,
                threshold = 0f
            });
            if (!string.IsNullOrWhiteSpace(eyeTrackingActiveParameter))
            {
                toOnConditions = toOnConditions.Add(CreateFalsyCondition(controller, eyeTrackingActiveParameter));
            }
            offState.Transitions = ImmutableList.Create(CreateTransition(onState, toOnConditions));

            var toOff = CreateTransition(offState, ImmutableList.Create(new AnimatorCondition
            {
                mode = AnimatorConditionMode.IfNot,
                parameter = parameterName,
                threshold = 0f
            }));
            var onTransitions = ImmutableList.Create(toOff);
            if (!string.IsNullOrWhiteSpace(eyeTrackingActiveParameter))
            {
                onTransitions = onTransitions.Add(CreateTransition(
                    offState,
                    ImmutableList.Create(CreateTruthyCondition(controller, eyeTrackingActiveParameter))));
            }
            onState.Transitions = onTransitions;
        }

        private static AnimatorCondition CreateTruthyCondition(VirtualAnimatorController controller, string parameterName)
        {
            var type = GetParameterType(controller, parameterName);
            return new AnimatorCondition
            {
                mode = type is AnimatorControllerParameterType.Float or AnimatorControllerParameterType.Int
                    ? AnimatorConditionMode.Greater
                    : AnimatorConditionMode.If,
                parameter = parameterName,
                threshold = type is AnimatorControllerParameterType.Float or AnimatorControllerParameterType.Int ? 0.5f : 0f
            };
        }

        private static AnimatorCondition CreateFalsyCondition(VirtualAnimatorController controller, string parameterName)
        {
            var type = GetParameterType(controller, parameterName);
            return new AnimatorCondition
            {
                mode = type is AnimatorControllerParameterType.Float or AnimatorControllerParameterType.Int
                    ? AnimatorConditionMode.Less
                    : AnimatorConditionMode.IfNot,
                parameter = parameterName,
                threshold = type is AnimatorControllerParameterType.Float or AnimatorControllerParameterType.Int ? 0.5f : 0f
            };
        }

        private static AnimatorControllerParameterType? GetParameterType(VirtualAnimatorController controller, string parameterName)
        {
            if (controller == null || string.IsNullOrWhiteSpace(parameterName)) return null;
            return controller.Parameters.TryGetValue(parameterName, out var parameter) ? parameter.type : null;
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

        private static bool MergeExpressionParameter(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            string parameterName,
            bool saved,
            bool synced)
        {
            var parameters = descriptor.expressionParameters != null
                ? NdmfObjectRegistry.Clone(descriptor.expressionParameters)
                : ScriptableObject.CreateInstance<VRCExpressionParameters>();

            parameters.name = "YM Eye Freeze Parameters";
            var list = parameters.parameters != null
                ? parameters.parameters.ToList()
                : new System.Collections.Generic.List<VRCExpressionParameters.Parameter>();

            var existingIndex = list.FindIndex(p => p != null && p.name == parameterName);
            var parameter = new VRCExpressionParameters.Parameter
            {
                name = parameterName,
                valueType = VRCExpressionParameters.ValueType.Bool,
                defaultValue = 0f,
                saved = saved,
                networkSynced = synced
            };

            if (existingIndex >= 0)
            {
                list[existingIndex] = parameter;
            }
            else if (list.Sum(p => p == null || !p.networkSynced ? 0 : VRCExpressionParameters.TypeCost(p.valueType)) +
                     (parameter.networkSynced ? VRCExpressionParameters.TypeCost(parameter.valueType) : 0) <=
                     VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                list.Add(parameter);
            }
            else
            {
                Debug.LogWarning("[YM Eye Freeze] Expression Parameters are full. Parameter was not added.");
                return false;
            }

            parameters.parameters = list.ToArray();
            context.AssetSaver.SaveAsset(parameters);
            descriptor.customExpressions = true;
            descriptor.expressionParameters = parameters;
            return true;
        }

        private static void MergeExpressionMenu(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            string menuName,
            string parameterName)
        {
            var menu = descriptor.expressionsMenu != null
                ? NdmfObjectRegistry.Clone(descriptor.expressionsMenu)
                : ScriptableObject.CreateInstance<VRCExpressionsMenu>();

            menu.name = "YM Eye Freeze Menu";
            menu.controls ??= new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            menu.controls = menu.controls
                .Where(control => control == null || control.parameter == null || control.parameter.name != parameterName)
                .ToList();

            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                name = menuName,
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath),
                parameter = new VRCExpressionsMenu.Control.Parameter
                {
                    name = parameterName
                },
                value = 1f
            });

            SplitOverflowMenus(context, menu);

            context.AssetSaver.SaveAsset(menu);
            descriptor.customExpressions = true;
            descriptor.expressionsMenu = menu;
        }

        private static void SplitOverflowMenus(BuildContext context, VRCExpressionsMenu menu)
        {
            var targetMenu = menu;
            var overflowIndex = 1;
            while (targetMenu.controls.Count > VRCExpressionsMenu.MAX_CONTROLS)
            {
                var overflowMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                overflowMenu.name = $"YM Eye Freeze Menu More {overflowIndex++}";
                const int keepCount = VRCExpressionsMenu.MAX_CONTROLS - 1;
                overflowMenu.controls.AddRange(targetMenu.controls.Skip(keepCount));
                targetMenu.controls.RemoveRange(keepCount, targetMenu.controls.Count - keepCount);
                targetMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    name = "More",
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = string.Empty },
                    subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                    subMenu = overflowMenu
                });

                context.AssetSaver.SaveAsset(overflowMenu);
                targetMenu = overflowMenu;
            }
        }

        private static void RemoveExpressionMenuControl(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            string parameterName)
        {
            if (descriptor == null || descriptor.expressionsMenu == null || string.IsNullOrWhiteSpace(parameterName)) return;

            var menu = NdmfObjectRegistry.Clone(descriptor.expressionsMenu);

            menu.name = "YM Eye Freeze Menu";
            menu.controls ??= new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            var beforeCount = menu.controls.Count;
            menu.controls = menu.controls
                .Where(control => control == null || control.parameter == null || control.parameter.name != parameterName)
                .ToList();

            if (menu.controls.Count == beforeCount) return;

            context.AssetSaver.SaveAsset(menu);
            descriptor.customExpressions = true;
            descriptor.expressionsMenu = menu;
        }

        private static void RemoveComponents(YMEyeFreeze[] components)
        {
            if (components == null) return;
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] == null) continue;
                Object.DestroyImmediate(components[i]);
            }
        }

    }
}
