using System;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
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
        private const string DefaultGestureControllerPath =
            "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/Controllers/vrc_AvatarV3HandsLayer.controller";

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
                Build(context, component);
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
                .ThenBy(c => GetTransformDepth(c.transform))
                .FirstOrDefault();
        }

        private static int GetTransformDepth(Transform transform)
        {
            var depth = 0;
            while (transform != null)
            {
                depth++;
                transform = transform.parent;
            }

            return depth;
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

            var clip = CreateEyeFreezeClip(avatarRoot, leftEye, rightEye);
            context.AssetSaver.SaveAsset(clip);

            var controller = BuildGestureController(context, descriptor, parameterName, clip);
            if (controller == null) return;

            MergeExpressionParameter(context, descriptor, parameterName, component.saved, component.synced);
            MergeExpressionMenu(context, descriptor, menuName, parameterName);
        }

        private static AnimationClip CreateEyeFreezeClip(GameObject avatarRoot, Transform leftEye, Transform rightEye)
        {
            var clip = new AnimationClip
            {
                name = "YM Eye Freeze"
            };

            AddRotationCurves(clip, AnimationUtility.CalculateTransformPath(leftEye, avatarRoot.transform), leftEye.localRotation);
            AddRotationCurves(clip, AnimationUtility.CalculateTransformPath(rightEye, avatarRoot.transform), rightEye.localRotation);
            return clip;
        }

        private static void AddRotationCurves(AnimationClip clip, string path, Quaternion rotation)
        {
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.x", OneKeyCurve(rotation.x));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.y", OneKeyCurve(rotation.y));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.z", OneKeyCurve(rotation.z));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.w", OneKeyCurve(rotation.w));
        }

        private static AnimationCurve OneKeyCurve(float value)
        {
            return new AnimationCurve(new Keyframe(0f, value));
        }

        private static AnimatorController BuildGestureController(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            string parameterName,
            AnimationClip clip)
        {
            var layerIndex = EnsureGestureLayer(descriptor);
            if (layerIndex < 0)
            {
                Debug.LogWarning("[YM Eye Freeze] Gesture layer not found. Skipped.");
                return null;
            }

            var sourceController = ResolveGestureSourceController(descriptor.baseAnimationLayers[layerIndex]);
            var controller = sourceController != null
                ? Object.Instantiate(sourceController)
                : new AnimatorController();

            controller.name = "YM Eye Freeze Gesture";
            context.AssetSaver.SaveAsset(controller);

            AddBoolParameterIfMissing(controller, parameterName);
            AddEyeFreezeLayer(controller, parameterName, clip);

            descriptor.customizeAnimationLayers = true;
            var layers = descriptor.baseAnimationLayers;
            layers[layerIndex].isDefault = false;
            layers[layerIndex].animatorController = controller;
            descriptor.baseAnimationLayers = layers;

            return controller;
        }

        private static int EnsureGestureLayer(VRCAvatarDescriptor descriptor)
        {
            var layers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            for (var i = 0; i < layers.Length; i++)
            {
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.Gesture) return i;
            }

            Array.Resize(ref layers, layers.Length + 1);
            var index = layers.Length - 1;
            layers[index] = new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.Gesture,
                isDefault = true
            };
            descriptor.baseAnimationLayers = layers;
            return index;
        }

        private static AnimatorController ResolveGestureSourceController(VRCAvatarDescriptor.CustomAnimLayer layer)
        {
            if (!layer.isDefault)
            {
                if (layer.animatorController is AnimatorController controller) return controller;
                if (layer.animatorController != null)
                {
                    Debug.LogWarning("[YM Eye Freeze] Gesture layer controller is not an AnimatorController. A new controller will be created.");
                }
            }

            return AssetDatabase.LoadAssetAtPath<AnimatorController>(DefaultGestureControllerPath);
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

        private static void AddEyeFreezeLayer(AnimatorController controller, string parameterName, AnimationClip clip)
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
            offState.writeDefaultValues = false;
            AddTrackingControl(offState, VRCAnimatorTrackingControl.TrackingType.Tracking);

            var onState = stateMachine.AddState("On", new Vector3(240f, 180f, 0f));
            onState.motion = clip;
            onState.writeDefaultValues = false;
            AddTrackingControl(onState, VRCAnimatorTrackingControl.TrackingType.Animation);

            stateMachine.defaultState = offState;

            var toOn = offState.AddTransition(onState);
            ConfigureTransition(toOn);
            toOn.AddCondition(AnimatorConditionMode.If, 0f, parameterName);

            var toOff = onState.AddTransition(offState);
            ConfigureTransition(toOff);
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, parameterName);

            layers[layers.Length - 1] = layer;
            controller.layers = layers;
        }

        private static void AddTrackingControl(AnimatorState state, VRCAnimatorTrackingControl.TrackingType eyesTracking)
        {
            var tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            tracking.trackingHead = VRCAnimatorTrackingControl.TrackingType.NoChange;
            tracking.trackingLeftHand = VRCAnimatorTrackingControl.TrackingType.NoChange;
            tracking.trackingRightHand = VRCAnimatorTrackingControl.TrackingType.NoChange;
            tracking.trackingHip = VRCAnimatorTrackingControl.TrackingType.NoChange;
            tracking.trackingLeftFoot = VRCAnimatorTrackingControl.TrackingType.NoChange;
            tracking.trackingRightFoot = VRCAnimatorTrackingControl.TrackingType.NoChange;
            tracking.trackingLeftFingers = VRCAnimatorTrackingControl.TrackingType.NoChange;
            tracking.trackingRightFingers = VRCAnimatorTrackingControl.TrackingType.NoChange;
            tracking.trackingEyes = eyesTracking;
            tracking.trackingMouth = VRCAnimatorTrackingControl.TrackingType.NoChange;
        }

        private static void ConfigureTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.exitTime = 0f;
        }

        private static void MergeExpressionParameter(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            string parameterName,
            bool saved,
            bool synced)
        {
            var parameters = descriptor.expressionParameters != null
                ? Object.Instantiate(descriptor.expressionParameters)
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
            else if (list.Sum(p => p == null ? 0 : VRCExpressionParameters.TypeCost(p.valueType)) + VRCExpressionParameters.TypeCost(parameter.valueType) <= VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                list.Add(parameter);
            }
            else
            {
                Debug.LogWarning("[YM Eye Freeze] Expression Parameters are full. Parameter was not added.");
            }

            parameters.parameters = list.ToArray();
            context.AssetSaver.SaveAsset(parameters);
            descriptor.customExpressions = true;
            descriptor.expressionParameters = parameters;
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
