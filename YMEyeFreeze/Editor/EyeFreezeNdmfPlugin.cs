using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
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
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("jp.yoridrill.ym-arm-patch")
                .AfterPlugin("jp.yoridrill.ym-mesh-trimmer")
                .AfterPlugin("jp.yoridrill.ym-mtoon-to-liltoon")
                .AfterPlugin("nadena.dev.modular-avatar")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Build YM Eye Freeze", Execute);
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

            var parameterName = string.IsNullOrWhiteSpace(component.parameterName)
                ? "YM/EyeFreeze"
                : component.parameterName.Trim();
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

            var leftConstraint = BuildEyeRotationConstraint(leftEye, CreateFreezeTarget(head, leftEye, LeftEyeTargetName));
            var rightConstraint = BuildEyeRotationConstraint(rightEye, CreateFreezeTarget(head, rightEye, RightEyeTargetName));

            var offClip = CreateEyeFreezeClip(avatarRoot, leftConstraint, rightConstraint, false, "YM Eye Freeze Off");
            var onClip = CreateEyeFreezeClip(avatarRoot, leftConstraint, rightConstraint, true, "YM Eye Freeze On");
            context.AssetSaver.SaveAsset(offClip);
            context.AssetSaver.SaveAsset(onClip);

            var controller = BuildFxController(context, descriptor, parameterName, offClip, onClip);
            if (controller == null) return;

            if (MergeExpressionParameter(context, descriptor, parameterName, component.saved, component.synced))
            {
                MergeExpressionMenu(context, descriptor, menuName, parameterName);
            }
            else
            {
                RemoveExpressionMenuControl(context, descriptor, parameterName);
            }
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

        private static AnimationClip CreateEyeFreezeClip(
            GameObject avatarRoot,
            VRCRotationConstraint leftConstraint,
            VRCRotationConstraint rightConstraint,
            bool isActive,
            string clipName)
        {
            var clip = new AnimationClip
            {
                name = clipName
            };

            AddConstraintActiveCurve(clip, AnimationUtility.CalculateTransformPath(leftConstraint.transform, avatarRoot.transform), isActive);
            AddConstraintActiveCurve(clip, AnimationUtility.CalculateTransformPath(rightConstraint.transform, avatarRoot.transform), isActive);
            return clip;
        }

        private static void AddConstraintActiveCurve(AnimationClip clip, string path, bool isActive)
        {
            clip.SetCurve(path, typeof(VRCRotationConstraint), nameof(VRCConstraintBase.IsActive), OneKeyCurve(isActive ? 1f : 0f));
        }

        private static AnimationCurve OneKeyCurve(float value)
        {
            return new AnimationCurve(new Keyframe(0f, value));
        }

        private static AnimatorController BuildFxController(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            string parameterName,
            AnimationClip offClip,
            AnimationClip onClip)
        {
            var layerIndex = EnsureFxLayer(descriptor);
            if (layerIndex < 0)
            {
                Debug.LogWarning("[YM Eye Freeze] FX layer not found. Skipped.");
                return null;
            }

            var sourceController = ResolveFxSourceController(descriptor.baseAnimationLayers[layerIndex]);
            var controller = sourceController != null
                ? Object.Instantiate(sourceController)
                : new AnimatorController();
            RegisterReplacedObject(sourceController, controller);

            controller.name = "YM Eye Freeze FX";
            context.AssetSaver.SaveAsset(controller);

            EnsureBlendTreeParameters(controller);
            AddBoolParameterIfMissing(controller, parameterName);
            var eyeTrackingActiveParameter = FindExistingParameterName(controller, JerryEyeTrackingActiveParameterCandidates);
            AddEyeFreezeLayer(controller, parameterName, offClip, onClip, eyeTrackingActiveParameter);

            descriptor.customizeAnimationLayers = true;
            var layers = descriptor.baseAnimationLayers;
            layers[layerIndex].isDefault = false;
            layers[layerIndex].animatorController = controller;
            descriptor.baseAnimationLayers = layers;

            return controller;
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

        private static AnimatorController ResolveFxSourceController(VRCAvatarDescriptor.CustomAnimLayer layer)
        {
            if (!layer.isDefault)
            {
                if (layer.animatorController is AnimatorController controller) return controller;
                if (layer.animatorController != null)
                {
                    Debug.LogWarning("[YM Eye Freeze] FX layer controller is not an AnimatorController. A new controller will be created.");
                }
            }

            return null;
        }

        private static void AddBoolParameterIfMissing(AnimatorController controller, string parameterName)
        {
            if (controller.parameters.Any(p => p.name == parameterName))
            {
                return;
            }

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = parameterName,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false
            });
        }

        private static string FindExistingParameterName(AnimatorController controller, string[] candidates)
        {
            if (controller == null || candidates == null) return null;

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var parameterName = candidate.Trim();
                if (controller.parameters.Any(parameter => parameter.name == parameterName))
                {
                    return parameterName;
                }
            }

            return null;
        }

        private static void EnsureBlendTreeParameters(AnimatorController controller)
        {
            if (controller == null) return;

            var existing = new HashSet<string>(controller.parameters.Select(parameter => parameter.name), StringComparer.Ordinal);
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var layer in controller.layers)
            {
                CollectBlendTreeParameters(layer?.stateMachine, referenced);
            }

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

        private static void AddEyeFreezeLayer(
            AnimatorController controller,
            string parameterName,
            AnimationClip offClip,
            AnimationClip onClip,
            string eyeTrackingActiveParameter)
        {
            var existingLayers = controller.layers
                .Where(layer => layer != null && layer.name != ToolName)
                .ToArray();
            controller.layers = existingLayers;

            controller.AddLayer(ToolName);
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;

            var stateMachine = layer.stateMachine;
            stateMachine.name = ToolName;

            var offState = stateMachine.AddState("Off", new Vector3(240f, 80f, 0f));
            offState.motion = offClip;
            offState.writeDefaultValues = false;

            var onState = stateMachine.AddState("On", new Vector3(240f, 180f, 0f));
            onState.motion = onClip;
            onState.writeDefaultValues = false;

            stateMachine.defaultState = offState;

            var toOn = offState.AddTransition(onState);
            ConfigureTransition(toOn);
            toOn.AddCondition(AnimatorConditionMode.If, 0f, parameterName);
            if (!string.IsNullOrWhiteSpace(eyeTrackingActiveParameter))
            {
                AddFalsyCondition(toOn, controller, eyeTrackingActiveParameter);
            }

            var toOff = onState.AddTransition(offState);
            ConfigureTransition(toOff);
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, parameterName);
            if (!string.IsNullOrWhiteSpace(eyeTrackingActiveParameter))
            {
                var toOffForEyeTracking = onState.AddTransition(offState);
                ConfigureTransition(toOffForEyeTracking);
                AddTruthyCondition(toOffForEyeTracking, controller, eyeTrackingActiveParameter);
            }

            layers[layers.Length - 1] = layer;
            controller.layers = layers;
        }

        private static void AddTruthyCondition(AnimatorStateTransition transition, AnimatorController controller, string parameterName)
        {
            var type = GetParameterType(controller, parameterName);
            if (type == AnimatorControllerParameterType.Float || type == AnimatorControllerParameterType.Int)
            {
                transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, parameterName);
                return;
            }

            transition.AddCondition(AnimatorConditionMode.If, 0f, parameterName);
        }

        private static void AddFalsyCondition(AnimatorStateTransition transition, AnimatorController controller, string parameterName)
        {
            var type = GetParameterType(controller, parameterName);
            if (type == AnimatorControllerParameterType.Float || type == AnimatorControllerParameterType.Int)
            {
                transition.AddCondition(AnimatorConditionMode.Less, 0.5f, parameterName);
                return;
            }

            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, parameterName);
        }

        private static AnimatorControllerParameterType? GetParameterType(AnimatorController controller, string parameterName)
        {
            if (controller == null || string.IsNullOrWhiteSpace(parameterName)) return null;
            return controller.parameters.FirstOrDefault(parameter => parameter.name == parameterName)?.type;
        }

        private static void ConfigureTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.exitTime = 0f;
        }

        private static bool MergeExpressionParameter(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            string parameterName,
            bool saved,
            bool synced)
        {
            var parameters = descriptor.expressionParameters != null
                ? Object.Instantiate(descriptor.expressionParameters)
                : ScriptableObject.CreateInstance<VRCExpressionParameters>();
            RegisterReplacedObject(descriptor.expressionParameters, parameters);

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
            else if (list.Sum(p => p == null ? 0 : VRCExpressionParameters.TypeCost(p.valueType)) + VRCExpressionParameters.TypeCost(parameter.valueType) <= VRCExpressionParameters.MAX_PARAMETER_COST)
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
                ? Object.Instantiate(descriptor.expressionsMenu)
                : ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            RegisterReplacedObject(descriptor.expressionsMenu, menu);

            menu.name = "YM Eye Freeze Menu";
            menu.controls ??= new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            menu.controls = menu.controls
                .Where(control => control == null || control.parameter == null || control.parameter.name != parameterName)
                .ToList();

            if (menu.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
            {
                Debug.LogWarning("[YM Eye Freeze] Expression Menu is full. Toggle was not added.");
            }
            else
            {
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
            }

            context.AssetSaver.SaveAsset(menu);
            descriptor.customExpressions = true;
            descriptor.expressionsMenu = menu;
        }

        private static void RemoveExpressionMenuControl(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            string parameterName)
        {
            if (descriptor == null || descriptor.expressionsMenu == null || string.IsNullOrWhiteSpace(parameterName)) return;

            var menu = Object.Instantiate(descriptor.expressionsMenu);
            RegisterReplacedObject(descriptor.expressionsMenu, menu);

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
    }
}
