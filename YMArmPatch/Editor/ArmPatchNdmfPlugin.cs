using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using YoridoriModifiers.Core.Editor;

[assembly: ExportsPlugin(typeof(YoridoriModifiers.ArmPatch.ArmPatchNdmfPlugin))]

namespace YoridoriModifiers.ArmPatch
{
    public sealed class ArmPatchNdmfPlugin : Plugin<ArmPatchNdmfPlugin>
    {
        private const string ToolName = "YM Arm Patch";

        public override string QualifiedName => "jp.yoridrill.ym-arm-patch";
        public override string DisplayName => "YM Arm Patch";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Build YM Arm Patch rig (Before MA)", ctx =>
                {
                    ApplyFix(ctx, PatchBuildOrder.BeforeModularAvatar);
                });

            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Build YM Arm Patch rig (After MA)", ctx =>
                {
                    ApplyFix(ctx, PatchBuildOrder.AfterModularAvatar);
                });

            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Reset preview and remove patch components", ctx =>
                {
                    ArmPatchPreviewUtility.ResetAllPreviewArtifacts();
                    RemovePatchComponentsBeforeAO(ctx);
                });
        }

        private static void ApplyFix(BuildContext ctx, PatchBuildOrder currentPassOrder)
        {
            if (ctx == null || ctx.AvatarRootObject == null) return;

            var components = ctx.AvatarRootObject.GetComponentsInChildren<ArmPatchComponent>(true);
            if (components == null || components.Length == 0) return;

            var settings = Aggregate(components, ctx.AvatarRootObject);
            if (settings.buildOrder != currentPassOrder) return;

            var animator = ctx.AvatarRootObject.GetComponentInChildren<Animator>(true);
            if (!IsValidHumanoid(animator))
            {
                Debug.LogWarning("[YM Arm Patch] Humanoid Animator not found. Skipped.");
                return;
            }

            var replaceMap = new Dictionary<Transform, Transform>();
            if (settings.verboseLog)
            {
                Debug.Log("[YM Arm Patch] Build pass started.");
                Debug.Log($"[YM Arm Patch] Aggregated settings: forearmType={settings.forearmTwistBoneType}, forearmCount={settings.forearmTwistBoneCount}, forearmFix={settings.enableForearmFix}");
            }

            if (settings.enableShoulderFix)
            {
                BuildShoulderFix(animator, settings, replaceMap);
            }

            if (settings.enableForearmFix)
            {
                BuildForearmFix(ctx.AvatarRootObject, animator, settings, replaceMap);
            }

            if (settings.enableThumbFix)
            {
                BuildThumbFix(animator, settings, replaceMap);
            }

            RebindRenderers(ctx.AvatarRootObject, replaceMap, settings.verboseLog);
            RemoveComponents(components);

            if (settings.verboseLog)
            {
                Debug.Log(
                    $"[YM Arm Patch] Finished. replaceMapCount={replaceMap.Count}, " +
                    $"mode={settings.constraintMode}, order={settings.buildOrder}");
            }
        }

        private static void RemovePatchComponentsBeforeAO(BuildContext ctx)
        {
            if (ctx == null || ctx.AvatarRootObject == null) return;

            var components = ctx.AvatarRootObject.GetComponentsInChildren<ArmPatchComponent>(true);
            RemoveComponents(components);
        }

        public static void BuildPatchRig(GameObject avatarRoot, ArmPatchComponent component, bool verboseLog = false)
        {
            if (avatarRoot == null || component == null) return;
            component.MigrateSerializedValuesIfNeeded();

            var animator = avatarRoot.GetComponentInChildren<Animator>(true);
            if (!IsValidHumanoid(animator))
            {
                LogUtility.PreviewSkipped(ToolName, "Humanoid Animator not found.");
                return;
            }

            var settings = new AggregatedSettings
            {
                enableShoulderFix = component.enableShoulderFix,
                shoulderPositionOffset = component.shoulderPositionOffset,
                shoulderEulerOffset = component.shoulderEulerOffset,
                upperArmTwistAxis = component.upperArmRollAxis,
                upperArmTwistWeight = component.upperArmRollWeight,
                enableForearmFix = component.enableForearmFix,
                forearmElbowScale = component.forearmElbowScale,
                forearmWristScale = component.forearmWristScale,
                forearmElbowRollOffset = component.forearmElbowRollOffset,
                forearmTwistAxis = component.forearmRollAxis,
                forearmPitchAxis = component.forearmPitchAxis,
                forearmTwistWeight = component.forearmRollWeight,
                forearmTwistBoneType = component.forearmTwistBoneType,
                forearmTwistBoneCount = component.forearmTwistBoneCount,
                forearmSkinMaterialName = component.forearmSkinMaterialName,
                forearmPreferElbowShape = component.forearmPreferElbowShape,
                enableThumbFix = component.enableThumbFix,
                thumbEulerOffset = component.thumbEulerOffset,
                constraintMode = component.constraintMode,
                buildOrder = component.buildOrder,
                verboseLog = verboseLog || component.verboseLog
            };

            var replaceMap = new Dictionary<Transform, Transform>();

            if (settings.enableShoulderFix)
            {
                BuildShoulderFix(animator, settings, replaceMap);
            }

            if (settings.enableForearmFix)
            {
                BuildForearmFix(avatarRoot, animator, settings, replaceMap);
            }

            if (settings.enableThumbFix)
            {
                BuildThumbFix(animator, settings, replaceMap);
            }

            RebindRenderers(avatarRoot, replaceMap, settings.verboseLog);
        }

        private static bool IsValidHumanoid(Animator animator)
        {
            return animator != null && animator.avatar != null && animator.avatar.isHuman;
        }

        private static void BuildShoulderFix(
            Animator animator,
            AggregatedSettings settings,
            Dictionary<Transform, Transform> replaceMap)
        {
            BuildShoulderSide(
                "L",
                animator.GetBoneTransform(HumanBodyBones.LeftShoulder),
                animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                settings.shoulderPositionOffset,
                settings.shoulderEulerOffset,
                settings.upperArmTwistAxis,
                settings.upperArmTwistWeight,
                settings.constraintMode,
                settings.verboseLog,
                replaceMap
            );

            BuildShoulderSide(
                "R",
                animator.GetBoneTransform(HumanBodyBones.RightShoulder),
                animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                settings.shoulderPositionOffset,
                MirrorOffsetForRight(settings.shoulderEulerOffset),
                settings.upperArmTwistAxis,
                settings.upperArmTwistWeight,
                settings.constraintMode,
                settings.verboseLog,
                replaceMap
            );
        }

        private static void BuildShoulderSide(
            string sideLabel,
            Transform originalShoulder,
            Transform originalUpperArm,
            Transform originalLowerArm,
            Vector3 shoulderPositionOffset,
            Vector3 shoulderEulerOffset,
            TwistAxis twistAxis,
            float twistWeight,
            ConstraintMode constraintMode,
            bool verboseLog,
            Dictionary<Transform, Transform> replaceMap)
        {
            if (originalShoulder == null || originalUpperArm == null || originalLowerArm == null)
            {
                Debug.LogWarning($"[YM Arm Patch] [{sideLabel}] Shoulder fix skipped. Required bones not found.");
                return;
            }

            var shoulderLocalOffset = ConvertParentSpaceOffsetToChildLocal(originalShoulder, shoulderPositionOffset);

            var shoulderDef = CreateChildOffsetBone(
                originalShoulder.name + "_Def",
                originalShoulder,
                shoulderLocalOffset,
                shoulderEulerOffset
            );

            var upperArmAim = CreateChildCopiedLocalBone(
                originalUpperArm.name + "_Aim",
                shoulderDef,
                originalUpperArm
            );

            var upperArmDef = CreateChildAlignedBone(
                originalUpperArm.name + "_Def",
                upperArmAim
            );

            if (constraintMode == ConstraintMode.VRChatConstraints)
            {
                AddVRCUpperArmAimConstraint(upperArmAim, originalLowerArm, sideLabel);
                AddVRCUpperArmTwistConstraint(upperArmDef, originalUpperArm, twistAxis, twistWeight);
            }
            else
            {
                AddUnityUpperArmAimConstraint(upperArmAim, originalLowerArm, sideLabel);
                AddUnityUpperArmTwistConstraint(upperArmDef, originalUpperArm, twistAxis, twistWeight);
            }

            replaceMap[originalShoulder] = shoulderDef;
            replaceMap[originalUpperArm] = upperArmDef;

            if (verboseLog)
            {
                Debug.Log(
                    $"[YM Arm Patch] [{sideLabel}] Shoulder fix created. " +
                    $"pos={shoulderPositionOffset}, rot={shoulderEulerOffset}, twistAxis={twistAxis}, twistWeight={twistWeight:F2}");
            }
        }

        private static void BuildForearmFix(
            GameObject avatarRoot,
            Animator animator,
            AggregatedSettings settings,
            Dictionary<Transform, Transform> replaceMap)
        {
            BuildForearmSide(
                "L",
                animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                animator.GetBoneTransform(HumanBodyBones.LeftHand),
                animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal),
                animator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate),
                animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal),
                settings.forearmElbowScale,
                settings.forearmWristScale,
                settings.forearmElbowRollOffset,
                settings.forearmTwistAxis,
                settings.forearmPitchAxis,
                settings.forearmTwistWeight,
                settings.forearmTwistBoneType,
                settings.forearmTwistBoneCount,
                settings.forearmSkinMaterialName,
                settings.forearmPreferElbowShape,
                settings.constraintMode,
                settings.verboseLog,
                replaceMap,
                avatarRoot
            );

            BuildForearmSide(
                "R",
                animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                animator.GetBoneTransform(HumanBodyBones.RightHand),
                animator.GetBoneTransform(HumanBodyBones.RightThumbProximal),
                animator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate),
                animator.GetBoneTransform(HumanBodyBones.RightLittleProximal),
                settings.forearmElbowScale,
                settings.forearmWristScale,
                settings.forearmElbowRollOffset,
                settings.forearmTwistAxis,
                settings.forearmPitchAxis,
                settings.forearmTwistWeight,
                settings.forearmTwistBoneType,
                settings.forearmTwistBoneCount,
                settings.forearmSkinMaterialName,
                settings.forearmPreferElbowShape,
                settings.constraintMode,
                settings.verboseLog,
                replaceMap,
                avatarRoot
            );
        }

        private static void BuildForearmSide(
            string sideLabel,
            Transform originalLowerArm,
            Transform originalHand,
            Transform originalThumbProximal,
            Transform originalThumbIntermediate,
            Transform originalLittleProximal,
            Vector3 elbowScale,
            Vector3 wristScale,
            float elbowRollOffset,
            TwistAxis forearmTwistAxis,
            TwistAxis forearmPitchAxis,
            float forearmTwistWeight,
            ForearmTwistBoneType twistBoneType,
            ForearmTwistBoneCount twistBoneCount,
            string skinMaterialName,
            bool preferElbowShape,
            ConstraintMode constraintMode,
            bool verboseLog,
            Dictionary<Transform, Transform> replaceMap,
            GameObject avatarRoot)
        {
            if (twistBoneCount == ForearmTwistBoneCount.Count0) twistBoneType = ForearmTwistBoneType.None;
            else if (string.IsNullOrEmpty(skinMaterialName) || skinMaterialName == "Auto") twistBoneType = ForearmTwistBoneType.AllTwist;
            else twistBoneType = ForearmTwistBoneType.SkinOnly;

            if (originalLowerArm == null)
            {
                Debug.LogWarning($"[YM Arm Patch] [{sideLabel}] Forearm fix skipped. LowerArm not found.");
                return;
            }

            Transform forearmTwistExtractor = null;
            if (originalHand != null)
            {
                forearmTwistExtractor = CreateChildAlignedBone(originalHand.name + "_ForearmTwistExtractor", originalHand);
                if (constraintMode == ConstraintMode.VRChatConstraints)
                {
                    AddVRCForearmTwistExtractorAimConstraint(forearmTwistExtractor, originalLowerArm, originalHand, sideLabel, forearmTwistAxis, forearmPitchAxis);
                }
                else
                {
                    AddUnityForearmTwistExtractorAimConstraint(forearmTwistExtractor, originalLowerArm, originalHand, sideLabel, forearmTwistAxis, forearmPitchAxis);
                }
            }
            var twistSource = forearmTwistExtractor;

            if (twistBoneType == ForearmTwistBoneType.None)
            {
                var forearmDef = CreateChildAlignedBone(
                    originalLowerArm.name + "_Forearm_Def",
                    originalLowerArm
                );

                forearmDef.localScale = (elbowScale + wristScale) * 0.5f;

                if (originalHand == null)
                {
                    Debug.LogWarning($"[YM Arm Patch] [{sideLabel}] Forearm rotate part skipped. Hand not found.");
                }
                else if (constraintMode == ConstraintMode.VRChatConstraints)
                {
                    AddVRCForearmRotateConstraint(forearmDef, twistSource, forearmTwistAxis, forearmTwistWeight);
                }
                else
                {
                    AddUnityForearmRotateConstraint(forearmDef, twistSource, forearmTwistAxis, forearmTwistWeight);
                }

                replaceMap[originalLowerArm] = forearmDef;

                if (verboseLog && originalHand != null)
                {
                    LogForearmDebug(
                        sideLabel,
                        "ForearmDef",
                        originalLowerArm,
                        originalHand,
                        forearmDef,
                        forearmTwistAxis,
                        forearmTwistWeight
                    );
                }
            }
            else if (originalHand != null)
            {
                int twistCount = (int)twistBoneCount;
                Transform twistParent;
                if (preferElbowShape)
                {
                    var twistAimUpAxis = GetNonRollAxis(forearmTwistAxis, forearmPitchAxis);
                    twistParent = CreateSiblingBone(originalLowerArm.name + "_TwistAim", originalLowerArm.parent, originalLowerArm);
                    if (constraintMode == ConstraintMode.VRChatConstraints) AddVRCAimConstraint(twistParent, originalHand, sideLabel, twistAimUpAxis);
                    else AddUnityAimConstraint(twistParent, originalHand, sideLabel, twistAimUpAxis);
                }
                else
                {
                    twistParent = originalLowerArm;
                }

                if (verboseLog)
                {
                    Vector3 axis = (originalHand.position - originalLowerArm.position).normalized;
                    Debug.Log(
                        $"[YM Arm Patch] [{sideLabel}] Twist parent selected. " +
                        $"name={twistParent.name}, parent={GetPath(twistParent.parent)}, " +
                        $"lowerArm={originalLowerArm.name}, hand={originalHand.name}, " +
                        $"worldAxis=({axis.x:F4},{axis.y:F4},{axis.z:F4}), " +
                        $"distance={Vector3.Distance(originalLowerArm.position, originalHand.position):F6}, " +
                        $"twistAxis={forearmTwistAxis}, count={twistCount}, preferElbowShape={preferElbowShape}");
                }

                var twistBones = new List<Transform>(twistCount);
                Vector3 handLocalFromParent = twistParent.InverseTransformPoint(originalHand.position);
                Vector3 localDir = handLocalFromParent.sqrMagnitude > 1e-8f ? handLocalFromParent.normalized : Vector3.right;
                float localDist = handLocalFromParent.magnitude;
                for (int i = 0; i < twistCount; i++)
                {
                    float t = twistCount <= 1 ? 1f : (float)i / (twistCount - 1);
                    var b = CreateChildAlignedBone($"{originalLowerArm.name}_Twist_{i:D2}", twistParent);
                    b.localPosition = localDir * (localDist * t);
                    b.localRotation = Quaternion.identity;
                    twistBones.Add(b);
                    if (constraintMode == ConstraintMode.VRChatConstraints) AddVRCForearmRotateConstraint(b, twistSource, forearmTwistAxis, t);
                    else AddUnityForearmRotateConstraint(b, twistSource, forearmTwistAxis, t);

                    if (verboseLog)
                    {
                        LogForearmDebug(
                            sideLabel,
                            $"Twist[{i}]",
                            originalLowerArm,
                            originalHand,
                            b,
                            forearmTwistAxis,
                            t
                        );
                    }
                }
                replaceMap[originalLowerArm] = twistBones[0];
                ReweightForearmVerticesToTwistBones(
                    avatarRoot,
                    originalLowerArm,
                    originalHand,
                    originalThumbProximal,
                    originalThumbIntermediate,
                    originalLittleProximal,
                    twistBones,
                    twistBoneType,
                    skinMaterialName,
                    verboseLog);
                ApplyForearmTwistBoneScales(
                    twistBones,
                    elbowScale,
                    wristScale);
                if (preferElbowShape)
                {
                    ApplyElbowRollOffsetToTwistAim(twistParent, constraintMode, forearmTwistAxis, elbowRollOffset);
                }
                else
                {
                    ApplyElbowRollOffsetToTwistBones(twistBones, forearmTwistAxis, elbowRollOffset);
                }

                if (verboseLog)
                {
                    Debug.Log($"[YM Arm Patch] [{sideLabel}] Twist bones generated. count={twistCount}, type={twistBoneType}");
                }
            }

            if (verboseLog)
            {
                if (twistBoneType == ForearmTwistBoneType.None)
                {
                    Debug.Log($"[YM Arm Patch] [{sideLabel}] Forearm_Def mode active.");
                }
                else
                {
                    Debug.Log($"[YM Arm Patch] [{sideLabel}] Twist mode active. type={twistBoneType}, preferElbowShape={preferElbowShape}");
                }
            }
        }

        private static void BuildThumbFix(
            Animator animator,
            AggregatedSettings settings,
            Dictionary<Transform, Transform> replaceMap)
        {
            BuildThumbSide(
                "L",
                animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal),
                animator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate),
                animator.GetBoneTransform(HumanBodyBones.LeftThumbDistal),
                settings.thumbEulerOffset,
                settings.constraintMode,
                settings.verboseLog,
                replaceMap
            );

            BuildThumbSide(
                "R",
                animator.GetBoneTransform(HumanBodyBones.RightThumbProximal),
                animator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate),
                animator.GetBoneTransform(HumanBodyBones.RightThumbDistal),
                MirrorOffsetForRight(settings.thumbEulerOffset),
                settings.constraintMode,
                settings.verboseLog,
                replaceMap
            );
        }

        private static void BuildThumbSide(
            string sideLabel,
            Transform originalProximal,
            Transform originalIntermediate,
            Transform originalDistal,
            Vector3 eulerOffset,
            ConstraintMode constraintMode,
            bool verboseLog,
            Dictionary<Transform, Transform> replaceMap)
        {
            if (originalProximal == null || originalIntermediate == null || originalDistal == null)
            {
                Debug.LogWarning($"[YM Arm Patch] [{sideLabel}] Thumb fix skipped. Required thumb bones not found.");
                return;
            }

            var proximalParent = originalProximal.parent;
            if (proximalParent == null)
            {
                Debug.LogWarning($"[YM Arm Patch] [{sideLabel}] Thumb fix skipped. Thumb parent not found.");
                return;
            }

            var proximalDef = CreateSiblingBone(originalProximal.name + "_Def", proximalParent, originalProximal);
            var intermediateDef = CreateSiblingBone(originalIntermediate.name + "_Def", proximalDef, originalIntermediate);
            var distalDef = CreateSiblingBone(originalDistal.name + "_Def", intermediateDef, originalDistal);

            if (constraintMode == ConstraintMode.VRChatConstraints)
            {
                AddVRCRotationConstraintAllAxes(proximalDef, originalProximal, eulerOffset);
                AddVRCRotationConstraintAllAxes(intermediateDef, originalIntermediate, eulerOffset);
                AddVRCRotationConstraintAllAxes(distalDef, originalDistal, eulerOffset);
            }
            else
            {
                AddUnityRotationConstraintAllAxes(proximalDef, originalProximal, eulerOffset);
                AddUnityRotationConstraintAllAxes(intermediateDef, originalIntermediate, eulerOffset);
                AddUnityRotationConstraintAllAxes(distalDef, originalDistal, eulerOffset);
            }

            replaceMap[originalProximal] = proximalDef;
            replaceMap[originalIntermediate] = intermediateDef;
            replaceMap[originalDistal] = distalDef;

            if (verboseLog)
            {
                Debug.Log($"[YM Arm Patch] [{sideLabel}] Thumb constraints created. mode={constraintMode}, rot={eulerOffset}");
            }
        }

        // Bone creation helpers

        private static Vector3 ConvertParentSpaceOffsetToChildLocal(Transform childParent, Vector3 parentSpaceOffset)
        {
            if (childParent == null) return parentSpaceOffset;

            var parent = childParent.parent;
            if (parent == null) return parentSpaceOffset;

            Vector3 worldOffset = parent.TransformVector(parentSpaceOffset);
            return childParent.InverseTransformVector(worldOffset);
        }

        private static Transform CreateBoneWithLocal(
            string name,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = localRotation;
            t.localScale = localScale;
            return t;
        }

        private static Transform CreateSiblingBone(string name, Transform parent, Transform source)
        {
            return CreateBoneWithLocal(
                name,
                parent,
                source.localPosition,
                source.localRotation,
                source.localScale
            );
        }

        private static Transform CreateChildAlignedBone(string name, Transform parent)
        {
            return CreateBoneWithLocal(
                name,
                parent,
                Vector3.zero,
                Quaternion.identity,
                Vector3.one
            );
        }

        private static Transform CreateChildCopiedLocalBone(string name, Transform parent, Transform source)
        {
            return CreateBoneWithLocal(
                name,
                parent,
                source.localPosition,
                source.localRotation,
                source.localScale
            );
        }

        private static Transform CreateChildOffsetBone(string name, Transform parent, Vector3 localPositionOffset, Vector3 localEulerOffset)
        {
            return CreateBoneWithLocal(
                name,
                parent,
                localPositionOffset,
                Quaternion.Euler(localEulerOffset),
                Vector3.one
            );
        }

        // VRChat constraints

        private static void AddVRCUpperArmAimConstraint(
            Transform target,
            Transform lowerArm,
            string sideLabel)
        {
            AddVRCAimConstraint(target, lowerArm, sideLabel, null);
        }

        private static void AddVRCAimConstraint(
            Transform target,
            Transform lowerArm,
            string sideLabel,
            TwistAxis? upAxis)
        {
            var constraint = target.gameObject.AddComponent<VRCAimConstraint>();

            var localAim = lowerArm.localPosition;
            if (localAim.sqrMagnitude < 1e-8f)
            {
                localAim = sideLabel == "L" ? Vector3.right : Vector3.left;
            }

            constraint.IsActive = true;
            constraint.GlobalWeight = 1f;
            constraint.Locked = true;
            constraint.SolveInLocalSpace = false;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;

            constraint.AffectsRotationX = true;
            constraint.AffectsRotationY = true;
            constraint.AffectsRotationZ = true;

            constraint.AimAxis = localAim.normalized;
            constraint.UpAxis = upAxis.HasValue ? ToAxisVector(upAxis.Value) : Vector3.up;
            constraint.WorldUp = VRCConstraintBase.WorldUpType.SceneUp;
            constraint.WorldUpVector = constraint.UpAxis;

            constraint.Sources.Clear();
            constraint.Sources.Add(new VRCConstraintSource(lowerArm, 1f));

            constraint.ApplyConfigurationChanges();
        }

        private static void AddVRCForearmTwistExtractorAimConstraint(Transform target, Transform lowerArm, Transform hand, string sideLabel, TwistAxis rollAxis, TwistAxis pitchAxis)
        {
            pitchAxis = GetNonRollAxis(rollAxis, pitchAxis);
            var constraint = target.gameObject.AddComponent<VRCAimConstraint>();
            constraint.IsActive = true;
            constraint.GlobalWeight = 1f;
            constraint.Locked = true;
            constraint.SolveInLocalSpace = false;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;
            constraint.AffectsRotationX = true;
            constraint.AffectsRotationY = true;
            constraint.AffectsRotationZ = true;
            constraint.AimAxis = GetSignedAxisToward(target, lowerArm, rollAxis, sideLabel);
            constraint.UpAxis = ToAxisVector(pitchAxis);
            constraint.WorldUp = VRCConstraintBase.WorldUpType.ObjectRotationUp;
            constraint.WorldUpTransform = hand;
            constraint.WorldUpVector = ToAxisVector(pitchAxis);
            constraint.Sources.Clear();
            constraint.Sources.Add(new VRCConstraintSource(lowerArm, 1f));
            constraint.ApplyConfigurationChanges();
        }

        private static void AddVRCUpperArmTwistConstraint(
            Transform target,
            Transform source,
            TwistAxis twistAxis,
            float twistWeight)
        {
            var constraint = target.gameObject.AddComponent<VRCRotationConstraint>();

            constraint.IsActive = true;
            constraint.GlobalWeight = twistWeight;
            constraint.Locked = true;
            constraint.SolveInLocalSpace = false;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;

            constraint.RotationAtRest = target.localEulerAngles;
            constraint.RotationOffset = Vector3.zero;

            SetVRCRotationAxis(constraint, twistAxis);

            constraint.Sources.Clear();
            constraint.Sources.Add(new VRCConstraintSource(source, 1f));

            constraint.ApplyConfigurationChanges();
        }

        private static void AddVRCForearmRotateConstraint(
            Transform target,
            Transform handSource,
            TwistAxis twistAxis,
            float twistWeight)
        {
            var constraint = target.gameObject.AddComponent<VRCRotationConstraint>();

            constraint.IsActive = true;
            constraint.GlobalWeight = twistWeight;
            constraint.Locked = true;
            constraint.SolveInLocalSpace = false;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;

            constraint.RotationAtRest = target.localEulerAngles;
            constraint.RotationOffset = Vector3.zero;

            constraint.AffectsRotationX = true;
            constraint.AffectsRotationY = true;
            constraint.AffectsRotationZ = true;

            constraint.Sources.Clear();
            constraint.Sources.Add(new VRCConstraintSource(handSource, 1f));

            constraint.ApplyConfigurationChanges();
        }

        private static void AddVRCRotationConstraintAllAxes(Transform target, Transform source, Vector3 eulerOffset)
        {
            var constraint = target.gameObject.AddComponent<VRCRotationConstraint>();

            constraint.IsActive = true;
            constraint.GlobalWeight = 1f;
            constraint.Locked = true;
            constraint.SolveInLocalSpace = false;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;

            constraint.RotationAtRest = target.localEulerAngles;
            constraint.RotationOffset = eulerOffset;

            constraint.AffectsRotationX = true;
            constraint.AffectsRotationY = true;
            constraint.AffectsRotationZ = true;

            constraint.Sources.Clear();
            constraint.Sources.Add(new VRCConstraintSource(source, 1f));

            constraint.ApplyConfigurationChanges();
        }

        private static void SetVRCRotationAxis(VRCRotationConstraint constraint, TwistAxis axis)
        {
            constraint.AffectsRotationX = axis == TwistAxis.X;
            constraint.AffectsRotationY = axis == TwistAxis.Y;
            constraint.AffectsRotationZ = axis == TwistAxis.Z;
        }

        // Unity constraints

        private static void AddUnityUpperArmAimConstraint(
            Transform target,
            Transform lowerArm,
            string sideLabel)
        {
            AddUnityAimConstraint(target, lowerArm, sideLabel, null);
        }

        private static void AddUnityAimConstraint(
            Transform target,
            Transform lowerArm,
            string sideLabel,
            TwistAxis? upAxis)
        {
            var constraint = target.gameObject.AddComponent<AimConstraint>();
            constraint.constraintActive = false;
            constraint.locked = false;
            constraint.weight = 1f;
            constraint.rotationAxis = Axis.X | Axis.Y | Axis.Z;

            var src = new ConstraintSource
            {
                sourceTransform = lowerArm,
                weight = 1f
            };
            constraint.AddSource(src);

            var localAim = lowerArm.localPosition;
            if (localAim.sqrMagnitude < 1e-8f)
            {
                localAim = sideLabel == "L" ? Vector3.right : Vector3.left;
            }

            constraint.aimVector = localAim.normalized;
            constraint.upVector = upAxis.HasValue ? ToAxisVector(upAxis.Value) : Vector3.up;
            constraint.worldUpType = AimConstraint.WorldUpType.SceneUp;
            constraint.constraintActive = true;
            constraint.locked = true;
        }

        private static void AddUnityForearmTwistExtractorAimConstraint(Transform target, Transform lowerArm, Transform hand, string sideLabel, TwistAxis rollAxis, TwistAxis pitchAxis)
        {
            pitchAxis = GetNonRollAxis(rollAxis, pitchAxis);
            var constraint = target.gameObject.AddComponent<AimConstraint>();
            constraint.constraintActive = false;
            constraint.locked = false;
            constraint.weight = 1f;
            constraint.rotationAxis = Axis.X | Axis.Y | Axis.Z;
            constraint.AddSource(new ConstraintSource { sourceTransform = lowerArm, weight = 1f });
            constraint.aimVector = GetSignedAxisToward(target, lowerArm, rollAxis, sideLabel);
            constraint.upVector = ToAxisVector(pitchAxis);
            constraint.worldUpType = AimConstraint.WorldUpType.ObjectRotationUp;
            constraint.worldUpObject = hand;
            constraint.worldUpVector = ToAxisVector(pitchAxis);
            constraint.constraintActive = true;
            constraint.locked = true;
        }

        private static void AddUnityUpperArmTwistConstraint(
            Transform target,
            Transform source,
            TwistAxis twistAxis,
            float twistWeight)
        {
            var constraint = target.gameObject.AddComponent<RotationConstraint>();
            constraint.constraintActive = false;
            constraint.locked = false;
            constraint.weight = twistWeight;
            constraint.rotationAxis = GetUnityRotationAxis(twistAxis);

            var src = new ConstraintSource
            {
                sourceTransform = source,
                weight = 1f
            };

            constraint.AddSource(src);
            constraint.rotationOffset = Vector3.zero;
            constraint.constraintActive = true;
            constraint.locked = true;
        }

        private static void AddUnityForearmRotateConstraint(
            Transform target,
            Transform handSource,
            TwistAxis twistAxis,
            float twistWeight)
        {
            var constraint = target.gameObject.AddComponent<RotationConstraint>();
            constraint.constraintActive = false;
            constraint.locked = false;
            constraint.weight = twistWeight;
            constraint.rotationAxis = Axis.X | Axis.Y | Axis.Z;

            var src = new ConstraintSource
            {
                sourceTransform = handSource,
                weight = 1f
            };

            constraint.AddSource(src);
            constraint.rotationOffset = Vector3.zero;
            constraint.constraintActive = true;
            constraint.locked = true;
        }

        private static void AddUnityRotationConstraintAllAxes(Transform target, Transform source, Vector3 eulerOffset)
        {
            var constraint = target.gameObject.AddComponent<RotationConstraint>();
            constraint.constraintActive = false;
            constraint.locked = false;
            constraint.rotationAxis = Axis.X | Axis.Y | Axis.Z;
            constraint.weight = 1f;

            var src = new ConstraintSource
            {
                sourceTransform = source,
                weight = 1f
            };

            constraint.AddSource(src);
            constraint.rotationOffset = eulerOffset;
            constraint.constraintActive = true;
            constraint.locked = true;
        }

        private static Axis GetUnityRotationAxis(TwistAxis axis)
        {
            switch (axis)
            {
                case TwistAxis.X: return Axis.X;
                case TwistAxis.Y: return Axis.Y;
                case TwistAxis.Z: return Axis.Z;
                default: return Axis.X;
            }
        }

        private static Vector3 ToAxisVector(TwistAxis axis)
        {
            switch (axis)
            {
                case TwistAxis.X: return Vector3.right;
                case TwistAxis.Y: return Vector3.up;
                case TwistAxis.Z: return Vector3.forward;
                default: return Vector3.right;
            }
        }

        private static TwistAxis GetNonRollAxis(TwistAxis rollAxis, TwistAxis pitchAxis)
        {
            if (pitchAxis != rollAxis) return pitchAxis;
            return rollAxis == TwistAxis.X ? TwistAxis.Y : TwistAxis.X;
        }

        private static Vector3 GetSignedAxisToward(Transform target, Transform source, TwistAxis axis, string sideLabel)
        {
            var axisVector = ToAxisVector(axis);
            if (target == null || source == null) return sideLabel == "R" ? -axisVector : axisVector;

            Vector3 localDirection = target.InverseTransformDirection(source.position - target.position);
            if (localDirection.sqrMagnitude < 1e-8f) return sideLabel == "R" ? -axisVector : axisVector;

            return Vector3.Dot(localDirection.normalized, axisVector) < 0f ? -axisVector : axisVector;
        }

        // Misc helpers

        private static Vector3 MirrorOffsetForRight(Vector3 leftLikeOffset)
        {
            return new Vector3(leftLikeOffset.x, -leftLikeOffset.y, -leftLikeOffset.z);
        }

        private static void RebindRenderers(GameObject avatarRoot, Dictionary<Transform, Transform> replaceMap, bool verboseLog)
        {
            var renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int changedRendererCount = 0;

            foreach (var smr in renderers)
            {
                if (smr == null || smr.bones == null || smr.bones.Length == 0) continue;

                bool changed = false;
                var bones = smr.bones;

                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    if (bone == null) continue;

                    if (replaceMap.TryGetValue(bone, out var replacement) && replacement != null)
                    {
                        bones[i] = replacement;
                        changed = true;
                    }
                }

                if (changed)
                {
                    smr.bones = bones;
                    changedRendererCount++;

                    if (verboseLog)
                    {
                        Debug.Log($"[YM Arm Patch] Rebound renderer: {GetPath(smr.transform)}");
                    }
                }
            }

            if (verboseLog)
            {
                Debug.Log($"[YM Arm Patch] Renderer rebinding finished. changedRendererCount={changedRendererCount}");
            }
        }

        private static void ReweightForearmVerticesToTwistBones(
            GameObject avatarRoot,
            Transform lowerArm,
            Transform hand,
            Transform thumbProximal,
            Transform thumbIntermediate,
            Transform littleProximal,
            List<Transform> twistBones,
            ForearmTwistBoneType twistBoneType,
            string skinMaterialName,
            bool verboseLog)
        {
            var renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in renderers)
            {
                if (smr.sharedMesh == null) continue;
                int lowerIdx = Array.IndexOf(smr.bones, lowerArm);
                int handIdx = Array.IndexOf(smr.bones, hand);
                int thumbProximalIdx = Array.IndexOf(smr.bones, thumbProximal);
                int thumbIntermediateIdx = Array.IndexOf(smr.bones, thumbIntermediate);
                int littleProximalIdx = Array.IndexOf(smr.bones, littleProximal);
                if (lowerIdx < 0 && handIdx < 0) continue;

                bool hasArmWeights = false;
                var originalWeights = smr.sharedMesh.boneWeights;
                for (int i = 0; i < originalWeights.Length; i++)
                {
                    if (GetWeightForBoneIndex(originalWeights[i], lowerIdx) + GetWeightForBoneIndex(originalWeights[i], handIdx) > 1e-6f)
                    {
                        hasArmWeights = true;
                        break;
                    }
                }
                if (!hasArmWeights)
                {
                    if (verboseLog) Debug.Log($"[YM Arm Patch] Twist reweight skipped (no forearm/hand weights): {GetPath(smr.transform)}");
                    continue;
                }

                var mesh = UnityEngine.Object.Instantiate(smr.sharedMesh);
                mesh.name = smr.sharedMesh.name + "_Twist";
                var bones = smr.bones.ToList();
                var bindposes = mesh.bindposes.ToList();
                Matrix4x4 lowerBindpose = lowerIdx >= 0 && lowerIdx < bindposes.Count
                    ? bindposes[lowerIdx]
                    : lowerArm.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                Matrix4x4 currentLowerBindpose = lowerArm.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                var twistBoneIndices = new int[twistBones.Count];
                for (int i = 0; i < twistBones.Count; i++)
                {
                    bones.Add(twistBones[i]);
                    bindposes.Add(BuildTwistBindposeFromLowerArmBindpose(
                        twistBones[i],
                        smr,
                        currentLowerBindpose,
                        lowerBindpose));
                    twistBoneIndices[i] = bones.Count - 1;
                }

                var vertices = mesh.vertices;
                var weights = mesh.boneWeights;
                Vector3 a = lowerArm.position;
                Vector3 b = hand.position;
                Vector3 axis = (b - a).normalized;
                float dist = Vector3.Distance(a, b);
                if (dist < 1e-6f) continue;
                int n = twistBones.Count;

                for (int vi = 0; vi < weights.Length; vi++)
                {
                    var bw = weights[vi];
                    float lowerW = GetWeightForBoneIndex(bw, lowerIdx);
                    float handW = GetWeightForBoneIndex(bw, handIdx);
                    float thumbProximalW = GetWeightForBoneIndex(bw, thumbProximalIdx);
                    float thumbIntermediateW = GetWeightForBoneIndex(bw, thumbIntermediateIdx);
                    float littleProximalW = GetWeightForBoneIndex(bw, littleProximalIdx);
                    float armW = lowerW + handW;
                    if (armW <= 1e-6f) continue;

                    bool isSkinVertex = twistBoneType != ForearmTwistBoneType.SkinOnly || IsSkinVertex(smr, vi, skinMaterialName);
                    if (twistBoneType == ForearmTwistBoneType.SkinOnly && !isSkinVertex)
                    {
                        var nonSkinPairs = new List<(int idx, float w)>(6);
                        float nonSkinToRootTwistW = armW + thumbProximalW + thumbIntermediateW + littleProximalW;
                        AddOrAccumulate(nonSkinPairs, twistBoneIndices[0], nonSkinToRootTwistW);
                        AddOrAccumulate(nonSkinPairs, bw.boneIndex0, bw.weight0);
                        AddOrAccumulate(nonSkinPairs, bw.boneIndex1, bw.weight1);
                        AddOrAccumulate(nonSkinPairs, bw.boneIndex2, bw.weight2);
                        AddOrAccumulate(nonSkinPairs, bw.boneIndex3, bw.weight3);
                        RemoveBone(nonSkinPairs, lowerIdx);
                        RemoveBone(nonSkinPairs, handIdx);
                        RemoveBone(nonSkinPairs, thumbProximalIdx);
                        RemoveBone(nonSkinPairs, thumbIntermediateIdx);
                        RemoveBone(nonSkinPairs, littleProximalIdx);
                        nonSkinPairs.Sort((x, y) => y.w.CompareTo(x.w));
                        if (nonSkinPairs.Count > 4) nonSkinPairs.RemoveRange(4, nonSkinPairs.Count - 4);
                        Normalize(nonSkinPairs);
                        weights[vi] = ToBoneWeight(nonSkinPairs);
                        continue;
                    }

                    Vector3 world = smr.transform.TransformPoint(vertices[vi]);
                    float t = Mathf.Clamp01(Vector3.Dot(world - a, axis) / dist);
                    float f = t * (n - 1);
                    int i0 = Mathf.FloorToInt(f);
                    int i1 = Mathf.Min(i0 + 1, n - 1);
                    float u = f - i0;
                    float s = u * u * (3f - 2f * u);
                    float w0 = i0 == i1 ? 1f : 1f - s;
                    float w1 = i0 == i1 ? 0f : s;

                    var pairs = new List<(int idx, float w)>(6);
                    if (lowerW <= 1e-6f) continue;
                    AddOrAccumulate(pairs, twistBoneIndices[i0], lowerW * w0);
                    if (w1 > 0f) AddOrAccumulate(pairs, twistBoneIndices[i1], lowerW * w1);
                    AddOrAccumulate(pairs, bw.boneIndex0, bw.weight0);
                    AddOrAccumulate(pairs, bw.boneIndex1, bw.weight1);
                    AddOrAccumulate(pairs, bw.boneIndex2, bw.weight2);
                    AddOrAccumulate(pairs, bw.boneIndex3, bw.weight3);
                    RemoveBone(pairs, lowerIdx);
                    pairs.Sort((x,y)=>y.w.CompareTo(x.w));
                    if (pairs.Count > 4) pairs.RemoveRange(4, pairs.Count - 4);
                    Normalize(pairs);
                    weights[vi] = ToBoneWeight(pairs);
                }

                mesh.bindposes = bindposes.ToArray();
                mesh.boneWeights = weights;
                smr.sharedMesh = mesh;
                smr.bones = bones.ToArray();
                if (verboseLog) Debug.Log($"[YM Arm Patch] Twist reweight: {GetPath(smr.transform)}");
            }
        }

        private static Matrix4x4 BuildTwistBindposeFromLowerArmBindpose(
            Transform twistBone,
            SkinnedMeshRenderer smr,
            Matrix4x4 currentLowerBindpose,
            Matrix4x4 lowerBindpose)
        {
            Matrix4x4 currentTwistBindpose = twistBone.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            Matrix4x4 lowerToTwist = currentTwistBindpose * currentLowerBindpose.inverse;
            return lowerToTwist * lowerBindpose;
        }

        private static bool IsSkinVertex(SkinnedMeshRenderer smr, int vertexIndex, string skinMaterialName)
        {
            if (smr == null || smr.sharedMesh == null || string.IsNullOrEmpty(skinMaterialName)) return false;
            var mesh = smr.sharedMesh;
            var materials = smr.sharedMaterials;
            int subMeshCount = mesh.subMeshCount;
            int count = Mathf.Min(subMeshCount, materials != null ? materials.Length : 0);
            for (int sub = 0; sub < count; sub++)
            {
                var mat = materials[sub];
                if (mat == null || !string.Equals(mat.name, skinMaterialName, StringComparison.Ordinal)) continue;
                var triangles = mesh.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i++)
                {
                    if (triangles[i] == vertexIndex) return true;
                }
            }
            return false;
        }

        private static void ApplyForearmTwistBoneScales(
            List<Transform> twistBones,
            Vector3 elbowScale,
            Vector3 wristScale)
        {
            if (twistBones == null || twistBones.Count == 0) return;
            int n = twistBones.Count;
            for (int i = 0; i < n; i++)
            {
                float t = n <= 1 ? 1f : (float)i / (n - 1);
                if (twistBones[i] != null)
                {
                    twistBones[i].localScale = Vector3.Lerp(elbowScale, wristScale, t);
                }
            }
        }

        private static void ApplyElbowRollOffsetToTwistAim(Transform twistAim, ConstraintMode constraintMode, TwistAxis rollAxis, float offset)
        {
            if (twistAim == null) return;
            var euler = BuildElbowRollOffsetEuler(rollAxis, offset);

            if (constraintMode == ConstraintMode.VRChatConstraints)
            {
                var c = twistAim.GetComponent<VRCAimConstraint>();
                if (c != null)
                {
                    c.RotationOffset = euler;
                    c.ApplyConfigurationChanges();
                }
            }
            else
            {
                var c = twistAim.GetComponent<AimConstraint>();
                if (c != null) c.rotationOffset = euler;
            }
        }

        private static Vector3 BuildElbowRollOffsetEuler(TwistAxis rollAxis, float offset)
        {
            var euler = Vector3.zero;
            if (rollAxis == TwistAxis.X) euler.x = offset;
            else if (rollAxis == TwistAxis.Y) euler.y = offset;
            else euler.z = offset;
            return euler;
        }

        private static void ApplyElbowRollOffsetToTwistBones(List<Transform> twistBones, TwistAxis rollAxis, float offset)
        {
            if (twistBones == null || twistBones.Count == 0) return;
            var rotation = Quaternion.Euler(BuildElbowRollOffsetEuler(rollAxis, offset));
            foreach (var twistBone in twistBones)
            {
                if (twistBone == null) continue;
                twistBone.localRotation = rotation;
                SyncRotationConstraintRestToCurrentLocalRotation(twistBone);
            }
        }

        private static void SyncRotationConstraintRestToCurrentLocalRotation(Transform target)
        {
            if (target == null) return;

            var vrcConstraint = target.GetComponent<VRCRotationConstraint>();
            if (vrcConstraint != null)
            {
                vrcConstraint.RotationAtRest = target.localEulerAngles;
                vrcConstraint.ApplyConfigurationChanges();
            }

            var unityConstraint = target.GetComponent<RotationConstraint>();
            if (unityConstraint != null)
            {
                unityConstraint.rotationAtRest = target.localEulerAngles;
            }
        }

        private static float GetWeightForBoneIndex(BoneWeight bw, int boneIndex)
        {
            if (boneIndex < 0) return 0f;
            float w = 0f;
            if (bw.boneIndex0 == boneIndex) w += bw.weight0;
            if (bw.boneIndex1 == boneIndex) w += bw.weight1;
            if (bw.boneIndex2 == boneIndex) w += bw.weight2;
            if (bw.boneIndex3 == boneIndex) w += bw.weight3;
            return w;
        }

        private static void AddOrAccumulate(List<(int idx, float w)> pairs, int idx, float w)
        {
            if (idx < 0 || w <= 0f) return;
            for (int i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].idx == idx) { pairs[i] = (idx, pairs[i].w + w); return; }
            }
            pairs.Add((idx, w));
        }

        private static void RemoveBone(List<(int idx, float w)> pairs, int idx)
        {
            if (idx < 0) return;
            pairs.RemoveAll(p => p.idx == idx);
        }

        private static void Normalize(List<(int idx, float w)> pairs)
        {
            float sum = 0f;
            for (int i = 0; i < pairs.Count; i++) sum += pairs[i].w;
            if (sum <= 1e-6f) return;
            for (int i = 0; i < pairs.Count; i++) pairs[i] = (pairs[i].idx, pairs[i].w / sum);
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

        private static void LogForearmDebug(
            string sideLabel,
            string label,
            Transform lowerArm,
            Transform hand,
            Transform target,
            TwistAxis axis,
            float weight)
        {
            Vector3 laToHand = hand.position - lowerArm.position;
            Vector3 laToTarget = target.position - lowerArm.position;
            float dist = laToHand.magnitude;
            float t = dist > 1e-6f ? Vector3.Dot(laToTarget, laToHand.normalized) / dist : 0f;

            Debug.Log(
                $"[YM Arm Patch] [{sideLabel}] {label} debug. " +
                $"target={target.name}, parent={GetPath(target.parent)}, " +
                $"worldPos=({target.position.x:F4},{target.position.y:F4},{target.position.z:F4}), " +
                $"localPos=({target.localPosition.x:F4},{target.localPosition.y:F4},{target.localPosition.z:F4}), " +
                $"localEuler=({target.localEulerAngles.x:F2},{target.localEulerAngles.y:F2},{target.localEulerAngles.z:F2}), " +
                $"axis={axis}, weight={weight:F4}, projectedT={t:F4}");
        }

        private static AggregatedSettings Aggregate(ArmPatchComponent[] components, GameObject avatarRoot)
        {
            if (components.Length > 1)
            {
                Debug.LogWarning("[YM Arm Patch] Multiple components found. Preferred component will be used.");
            }
            var c = SelectPreferredComponent(components, avatarRoot);
            c.MigrateSerializedValuesIfNeeded();

            return new AggregatedSettings
            {
                enableShoulderFix = c.enableShoulderFix,
                shoulderPositionOffset = c.shoulderPositionOffset,
                shoulderEulerOffset = c.shoulderEulerOffset,
                upperArmTwistAxis = c.upperArmRollAxis,
                upperArmTwistWeight = c.upperArmRollWeight,
                enableForearmFix = c.enableForearmFix,
                forearmElbowScale = c.forearmElbowScale,
                forearmWristScale = c.forearmWristScale,
                forearmElbowRollOffset = c.forearmElbowRollOffset,
                forearmTwistAxis = c.forearmRollAxis,
                forearmPitchAxis = c.forearmPitchAxis,
                forearmTwistWeight = c.forearmRollWeight,
                forearmTwistBoneType = c.forearmTwistBoneType,
                forearmTwistBoneCount = c.forearmTwistBoneCount,
                forearmSkinMaterialName = c.forearmSkinMaterialName,
                forearmPreferElbowShape = c.forearmPreferElbowShape,
                enableThumbFix = c.enableThumbFix,
                thumbEulerOffset = c.thumbEulerOffset,
                constraintMode = c.constraintMode,
                buildOrder = c.buildOrder,
                verboseLog = c.verboseLog
            };
        }

        private static ArmPatchComponent SelectPreferredComponent(ArmPatchComponent[] components, GameObject avatarRoot)
        {
            ArmPatchComponent best = components[0];
            int bestScore = int.MinValue;
            Transform root = avatarRoot != null ? avatarRoot.transform : null;

            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                int depth = PreviewCoordinator.GetDepthFromRoot(c.transform, root);
                int score = -depth * 10000 - i;
                if (score > bestScore)
                {
                    best = c;
                    bestScore = score;
                }
            }

            return best;
        }
        private static void RemoveComponents(ArmPatchComponent[] components)
        {
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(components[i]);
                }
            }
        }

        private static string GetPath(Transform t)
        {
            var stack = new Stack<string>();
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", stack);
        }

        private struct AggregatedSettings
        {
            public bool enableShoulderFix;
            public Vector3 shoulderPositionOffset;
            public Vector3 shoulderEulerOffset;
            public TwistAxis upperArmTwistAxis;
            public float upperArmTwistWeight;
            public bool enableForearmFix;
            public Vector3 forearmElbowScale;
            public Vector3 forearmWristScale;
            public float forearmElbowRollOffset;
            public TwistAxis forearmTwistAxis;
            public TwistAxis forearmPitchAxis;
            public float forearmTwistWeight;
            public ForearmTwistBoneType forearmTwistBoneType;
            public ForearmTwistBoneCount forearmTwistBoneCount;
            public string forearmSkinMaterialName;
            public bool forearmPreferElbowShape;
            public bool enableThumbFix;
            public Vector3 thumbEulerOffset;
            public ConstraintMode constraintMode;
            public PatchBuildOrder buildOrder;
            public bool verboseLog;
        }
    }
}
