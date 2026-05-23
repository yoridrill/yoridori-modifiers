using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

[assembly: ExportsPlugin(typeof(NDMFVRoidArmPatch.Editor.NDMFVRoidArmPatchPlugin))]

namespace NDMFVRoidArmPatch.Editor
{
    public sealed class NDMFVRoidArmPatchPlugin : Plugin<NDMFVRoidArmPatchPlugin>
    {
        public override string QualifiedName => "jp.yoridrill.ndmf-vroid-arm-patch";
        public override string DisplayName => "NDMF VRoid Arm Patch";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Build NDMF VRoid Arm Patch rig (Before MA)", ctx =>
                {
                    ApplyFix(ctx, PatchBuildOrder.BeforeModularAvatar);
                });

            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Build NDMF VRoid Arm Patch rig (After MA)", ctx =>
                {
                    ApplyFix(ctx, PatchBuildOrder.AfterModularAvatar);
                });

            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Reset preview and remove patch components", ctx =>
                {
                    NDMFVRoidArmPatchPreviewUtility.ResetAllPreviewArtifacts();
                    RemovePatchComponentsBeforeAO(ctx);
                });
        }

        private static void ApplyFix(BuildContext ctx, PatchBuildOrder currentPassOrder)
        {
            if (ctx == null || ctx.AvatarRootObject == null) return;

            var components = ctx.AvatarRootObject.GetComponentsInChildren<NDMFVRoidArmPatchComponent>(true);
            if (components == null || components.Length == 0) return;

            var settings = Aggregate(components, ctx.AvatarRootObject);
            if (settings.buildOrder != currentPassOrder) return;

            var animator = ctx.AvatarRootObject.GetComponentInChildren<Animator>(true);
            if (!IsValidHumanoid(animator))
            {
                Debug.LogWarning("[NDMF VRoid Arm Patch] Humanoid Animator not found. Skipped.");
                return;
            }

            var replaceMap = new Dictionary<Transform, Transform>();
            if (settings.verboseLog)
            {
                Debug.Log("[NDMF VRoid Arm Patch] Build pass started.");
                Debug.Log($"[NDMF VRoid Arm Patch] Aggregated settings: forearmType={settings.forearmTwistBoneType}, forearmCount={settings.forearmTwistBoneCount}, forearmFix={settings.enableForearmFix}");
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
                    $"[NDMF VRoid Arm Patch] Finished. replaceMapCount={replaceMap.Count}, " +
                    $"mode={settings.constraintMode}, order={settings.buildOrder}");
            }
        }

        private static void RemovePatchComponentsBeforeAO(BuildContext ctx)
        {
            if (ctx == null || ctx.AvatarRootObject == null) return;

            var components = ctx.AvatarRootObject.GetComponentsInChildren<NDMFVRoidArmPatchComponent>(true);
            RemoveComponents(components);
        }

        public static void BuildPatchRig(GameObject avatarRoot, NDMFVRoidArmPatchComponent component, bool verboseLog = false)
        {
            if (avatarRoot == null || component == null) return;

            var animator = avatarRoot.GetComponentInChildren<Animator>(true);
            if (!IsValidHumanoid(animator))
            {
                Debug.LogWarning("[NDMF VRoid Arm Patch] Preview skipped. Humanoid Animator not found.");
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
                forearmThicknessRootScale = component.forearmThicknessRootScale,
                forearmThicknessTipScale = component.forearmThicknessTipScale,
                forearmWidthRootScale = component.forearmWidthRootScale,
                forearmWidthTipScale = component.forearmWidthTipScale,
                forearmRootRollOffset = component.forearmRootRollOffset,
                forearmTwistAxis = component.forearmRollAxis,
                forearmPitchAxis = component.forearmPitchAxis,
                forearmTwistWeight = component.forearmRollWeight,
                forearmTwistBoneType = component.forearmTwistBoneType,
                forearmTwistBoneCount = component.forearmTwistBoneCount,
                forearmSkinMaterialName = component.forearmSkinMaterialName,
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
                Debug.LogWarning($"[NDMF VRoid Arm Patch] [{sideLabel}] Shoulder fix skipped. Required bones not found.");
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
                    $"[NDMF VRoid Arm Patch] [{sideLabel}] Shoulder fix created. " +
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
                settings.forearmThicknessRootScale,
                settings.forearmThicknessTipScale,
                settings.forearmWidthRootScale,
                settings.forearmWidthTipScale,
                settings.forearmRootRollOffset,
                settings.forearmTwistAxis,
                settings.forearmPitchAxis,
                settings.forearmTwistWeight,
                settings.forearmTwistBoneType,
                settings.forearmTwistBoneCount,
                settings.forearmSkinMaterialName,
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
                settings.forearmThicknessRootScale,
                settings.forearmThicknessTipScale,
                settings.forearmWidthRootScale,
                settings.forearmWidthTipScale,
                settings.forearmRootRollOffset,
                settings.forearmTwistAxis,
                settings.forearmPitchAxis,
                settings.forearmTwistWeight,
                settings.forearmTwistBoneType,
                settings.forearmTwistBoneCount,
                settings.forearmSkinMaterialName,
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
            float thicknessRootScale,
            float thicknessTipScale,
            float widthRootScale,
            float widthTipScale,
            float rootRollOffset,
            TwistAxis forearmTwistAxis,
            TwistAxis forearmPitchAxis,
            float forearmTwistWeight,
            ForearmTwistBoneType twistBoneType,
            ForearmTwistBoneCount twistBoneCount,
            string skinMaterialName,
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
                Debug.LogWarning($"[NDMF VRoid Arm Patch] [{sideLabel}] Forearm fix skipped. LowerArm not found.");
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

            if (twistBoneType == ForearmTwistBoneType.None)
            {
                var forearmDef = CreateChildAlignedBone(
                    originalLowerArm.name + "_Forearm_Def",
                    originalLowerArm
                );

                float avgThickness = (thicknessRootScale + thicknessTipScale) * 0.5f;
                float avgWidth = (widthRootScale + widthTipScale) * 0.5f;
                forearmDef.localScale = BuildForearmScaleVector(forearmTwistAxis, avgThickness, avgWidth);

                if (originalHand == null)
                {
                    Debug.LogWarning($"[NDMF VRoid Arm Patch] [{sideLabel}] Forearm rotate part skipped. Hand not found.");
                }
                else if (constraintMode == ConstraintMode.VRChatConstraints)
                {
                    AddVRCForearmRotateConstraint(forearmDef, forearmTwistExtractor, forearmTwistAxis, forearmTwistWeight);
                }
                else
                {
                    AddUnityForearmRotateConstraint(forearmDef, forearmTwistExtractor, forearmTwistAxis, forearmTwistWeight);
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
                var twistAim = CreateSiblingBone(originalLowerArm.name + "_TwistAim", originalLowerArm.parent, originalLowerArm);
                if (constraintMode == ConstraintMode.VRChatConstraints) AddVRCUpperArmAimConstraint(twistAim, originalHand, sideLabel);
                else AddUnityUpperArmAimConstraint(twistAim, originalHand, sideLabel);

                if (verboseLog)
                {
                    Vector3 axis = (originalHand.position - originalLowerArm.position).normalized;
                    Debug.Log(
                        $"[NDMF VRoid Arm Patch] [{sideLabel}] TwistAim created. " +
                        $"name={twistAim.name}, parent={GetPath(twistAim.parent)}, " +
                        $"lowerArm={originalLowerArm.name}, hand={originalHand.name}, " +
                        $"worldAxis=({axis.x:F4},{axis.y:F4},{axis.z:F4}), " +
                        $"distance={Vector3.Distance(originalLowerArm.position, originalHand.position):F6}, " +
                        $"twistAxis={forearmTwistAxis}, count={twistCount}");
                }

                var twistBones = new List<Transform>(twistCount);
                Vector3 handLocalFromAim = twistAim.InverseTransformPoint(originalHand.position);
                Vector3 localDir = handLocalFromAim.sqrMagnitude > 1e-8f ? handLocalFromAim.normalized : Vector3.right;
                float localDist = handLocalFromAim.magnitude;
                for (int i = 0; i < twistCount; i++)
                {
                    float t = twistCount <= 1 ? 1f : (float)i / (twistCount - 1);
                    var b = CreateChildAlignedBone($"{originalLowerArm.name}_Twist_{i:D2}", twistAim);
                    b.localPosition = localDir * (localDist * t);
                    b.localRotation = Quaternion.identity;
                    twistBones.Add(b);
                    if (constraintMode == ConstraintMode.VRChatConstraints) AddVRCForearmRotateConstraintAllAxes(b, forearmTwistExtractor, t);
                    else AddUnityForearmRotateConstraintAllAxes(b, forearmTwistExtractor, t);

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
                    forearmTwistAxis,
                    thicknessRootScale,
                    thicknessTipScale,
                    widthRootScale,
                    widthTipScale);
                ApplyRootRollOffsetToTwistAim(twistAim, constraintMode, forearmTwistAxis, rootRollOffset);

                if (verboseLog)
                {
                    Debug.Log($"[NDMF VRoid Arm Patch] [{sideLabel}] Twist bones generated. count={twistCount}, type={twistBoneType}");
                }
            }

            if (verboseLog)
            {
                if (twistBoneType == ForearmTwistBoneType.None)
                {
                    Debug.Log($"[NDMF VRoid Arm Patch] [{sideLabel}] Forearm_Def mode active.");
                }
                else
                {
                    Debug.Log($"[NDMF VRoid Arm Patch] [{sideLabel}] Twist mode active. type={twistBoneType}");
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
                Debug.LogWarning($"[NDMF VRoid Arm Patch] [{sideLabel}] Thumb fix skipped. Required thumb bones not found.");
                return;
            }

            var proximalParent = originalProximal.parent;
            if (proximalParent == null)
            {
                Debug.LogWarning($"[NDMF VRoid Arm Patch] [{sideLabel}] Thumb fix skipped. Thumb parent not found.");
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
                Debug.Log($"[NDMF VRoid Arm Patch] [{sideLabel}] Thumb constraints created. mode={constraintMode}, rot={eulerOffset}");
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
            constraint.UpAxis = Vector3.up;
            constraint.WorldUp = VRCConstraintBase.WorldUpType.SceneUp;
            constraint.WorldUpVector = Vector3.up;

            constraint.Sources.Clear();
            constraint.Sources.Add(new VRCConstraintSource(lowerArm, 1f));

            constraint.ApplyConfigurationChanges();
        }

        private static void AddVRCForearmTwistExtractorAimConstraint(Transform target, Transform lowerArm, Transform hand, string sideLabel, TwistAxis rollAxis, TwistAxis pitchAxis)
        {
            var constraint = target.gameObject.AddComponent<VRCAimConstraint>();
            var localAim = lowerArm.localPosition;
            if (localAim.sqrMagnitude < 1e-8f) localAim = sideLabel == "L" ? Vector3.right : Vector3.left;
            constraint.IsActive = true;
            constraint.GlobalWeight = 1f;
            constraint.Locked = true;
            constraint.SolveInLocalSpace = false;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;
            constraint.AffectsRotationX = true;
            constraint.AffectsRotationY = true;
            constraint.AffectsRotationZ = true;
            // Mirrored right-hand rigs often require flipped aim sign for extractor stability.
            constraint.AimAxis = sideLabel == "R" ? -ToAxisVector(rollAxis) : ToAxisVector(rollAxis);
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

        private static void AddVRCForearmRotateConstraintAllAxes(Transform target, Transform source, float weight)
        {
            var constraint = target.gameObject.AddComponent<VRCRotationConstraint>();
            constraint.IsActive = true;
            constraint.GlobalWeight = weight;
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
            constraint.Sources.Add(new VRCConstraintSource(source, 1f));
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
            constraint.upVector = Vector3.up;
            constraint.worldUpType = AimConstraint.WorldUpType.SceneUp;
            constraint.constraintActive = true;
            constraint.locked = true;
        }

        private static void AddUnityForearmTwistExtractorAimConstraint(Transform target, Transform lowerArm, Transform hand, string sideLabel, TwistAxis rollAxis, TwistAxis pitchAxis)
        {
            var constraint = target.gameObject.AddComponent<AimConstraint>();
            constraint.constraintActive = false;
            constraint.locked = false;
            constraint.weight = 1f;
            constraint.rotationAxis = Axis.X | Axis.Y | Axis.Z;
            constraint.AddSource(new ConstraintSource { sourceTransform = lowerArm, weight = 1f });
            var localAim = lowerArm.localPosition;
            if (localAim.sqrMagnitude < 1e-8f) localAim = sideLabel == "L" ? Vector3.right : Vector3.left;
            // Mirrored right-hand rigs often require flipped aim sign for extractor stability.
            constraint.aimVector = sideLabel == "R" ? -ToAxisVector(rollAxis) : ToAxisVector(rollAxis);
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

        private static void AddUnityForearmRotateConstraintAllAxes(Transform target, Transform source, float weight)
        {
            var constraint = target.gameObject.AddComponent<RotationConstraint>();
            constraint.constraintActive = false;
            constraint.locked = false;
            constraint.weight = weight;
            constraint.rotationAxis = Axis.X | Axis.Y | Axis.Z;
            constraint.AddSource(new ConstraintSource { sourceTransform = source, weight = 1f });
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

        // Misc helpers

        private static Vector3 BuildForearmScaleVector(TwistAxis twistAxis, float thicknessScale, float widthScale)
        {
            switch (twistAxis)
            {
                case TwistAxis.X:
                    return new Vector3(1f, thicknessScale, widthScale);

                case TwistAxis.Y:
                    return new Vector3(widthScale, 1f, thicknessScale);

                case TwistAxis.Z:
                    return new Vector3(widthScale, thicknessScale, 1f);

                default:
                    return new Vector3(1f, thicknessScale, widthScale);
            }
        }

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
                        Debug.Log($"[NDMF VRoid Arm Patch] Rebound renderer: {GetPath(smr.transform)}");
                    }
                }
            }

            if (verboseLog)
            {
                Debug.Log($"[NDMF VRoid Arm Patch] Renderer rebinding finished. changedRendererCount={changedRendererCount}");
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
                    if (verboseLog) Debug.Log($"[NDMF VRoid Arm Patch] Twist reweight skipped (no forearm/hand weights): {GetPath(smr.transform)}");
                    continue;
                }

                var mesh = UnityEngine.Object.Instantiate(smr.sharedMesh);
                mesh.name = smr.sharedMesh.name + "_Twist";
                var bones = smr.bones.ToList();
                var bindposes = mesh.bindposes.ToList();
                var twistBoneIndices = new int[twistBones.Count];
                for (int i = 0; i < twistBones.Count; i++)
                {
                    bones.Add(twistBones[i]);
                    bindposes.Add(twistBones[i].worldToLocalMatrix * smr.transform.localToWorldMatrix);
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
                if (verboseLog) Debug.Log($"[NDMF VRoid Arm Patch] Twist reweight: {GetPath(smr.transform)}");
            }
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
            TwistAxis forearmTwistAxis,
            float thicknessRootScale,
            float thicknessTipScale,
            float widthRootScale,
            float widthTipScale)
        {
            if (twistBones == null || twistBones.Count == 0) return;
            int n = twistBones.Count;
            for (int i = 0; i < n; i++)
            {
                float t = n <= 1 ? 1f : (float)i / (n - 1);
                float thickness = Mathf.Lerp(thicknessRootScale, thicknessTipScale, t);
                float width = Mathf.Lerp(widthRootScale, widthTipScale, t);
                if (twistBones[i] != null)
                {
                    twistBones[i].localScale = BuildForearmScaleVector(forearmTwistAxis, thickness, width);
                }
            }
        }

        private static void ApplyRootRollOffsetToTwistAim(Transform twistAim, ConstraintMode constraintMode, TwistAxis rollAxis, float offset)
        {
            if (twistAim == null) return;
            var euler = Vector3.zero;
            if (rollAxis == TwistAxis.X) euler.x = offset;
            else if (rollAxis == TwistAxis.Y) euler.y = offset;
            else euler.z = offset;

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
                $"[NDMF VRoid Arm Patch] [{sideLabel}] {label} debug. " +
                $"target={target.name}, parent={GetPath(target.parent)}, " +
                $"worldPos=({target.position.x:F4},{target.position.y:F4},{target.position.z:F4}), " +
                $"localPos=({target.localPosition.x:F4},{target.localPosition.y:F4},{target.localPosition.z:F4}), " +
                $"localEuler=({target.localEulerAngles.x:F2},{target.localEulerAngles.y:F2},{target.localEulerAngles.z:F2}), " +
                $"axis={axis}, weight={weight:F4}, projectedT={t:F4}");
        }

        private static AggregatedSettings Aggregate(NDMFVRoidArmPatchComponent[] components, GameObject avatarRoot)
        {
            if (components.Length > 1)
            {
                Debug.LogWarning("[NDMF VRoid Arm Patch] Multiple components found. Last one will be used.");
            }
            var c = SelectPreferredComponent(components, avatarRoot);

            return new AggregatedSettings
            {
                enableShoulderFix = c.enableShoulderFix,
                shoulderPositionOffset = c.shoulderPositionOffset,
                shoulderEulerOffset = c.shoulderEulerOffset,
                upperArmTwistAxis = c.upperArmRollAxis,
                upperArmTwistWeight = c.upperArmRollWeight,
                enableForearmFix = c.enableForearmFix,
                forearmThicknessRootScale = c.forearmThicknessRootScale,
                forearmThicknessTipScale = c.forearmThicknessTipScale,
                forearmWidthRootScale = c.forearmWidthRootScale,
                forearmWidthTipScale = c.forearmWidthTipScale,
                forearmRootRollOffset = c.forearmRootRollOffset,
                forearmTwistAxis = c.forearmRollAxis,
                forearmPitchAxis = c.forearmPitchAxis,
                forearmTwistWeight = c.forearmRollWeight,
                forearmTwistBoneType = c.forearmTwistBoneType,
                forearmTwistBoneCount = c.forearmTwistBoneCount,
                forearmSkinMaterialName = c.forearmSkinMaterialName,
                enableThumbFix = c.enableThumbFix,
                thumbEulerOffset = c.thumbEulerOffset,
                constraintMode = c.constraintMode,
                buildOrder = c.buildOrder,
                verboseLog = c.verboseLog
            };
        }

        private static NDMFVRoidArmPatchComponent SelectPreferredComponent(NDMFVRoidArmPatchComponent[] components, GameObject avatarRoot)
        {
            NDMFVRoidArmPatchComponent best = components[0];
            int bestScore = int.MinValue;
            Transform root = avatarRoot != null ? avatarRoot.transform : null;

            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                int depth = GetDepthFromRoot(c.transform, root);
                int score = depth;
                if (c.forearmTwistBoneType != ForearmTwistBoneType.None) score += 1000;
                if (score > bestScore)
                {
                    best = c;
                    bestScore = score;
                }
            }

            return best;
        }

        private static int GetDepthFromRoot(Transform t, Transform root)
        {
            int depth = 0;
            while (t != null && t != root)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }

        private static void RemoveComponents(NDMFVRoidArmPatchComponent[] components)
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
            public float forearmThicknessRootScale;
            public float forearmThicknessTipScale;
            public float forearmWidthRootScale;
            public float forearmWidthTipScale;
            public float forearmRootRollOffset;
            public TwistAxis forearmTwistAxis;
            public TwistAxis forearmPitchAxis;
            public float forearmTwistWeight;
            public ForearmTwistBoneType forearmTwistBoneType;
            public ForearmTwistBoneCount forearmTwistBoneCount;
            public string forearmSkinMaterialName;
            public bool enableThumbFix;
            public Vector3 thumbEulerOffset;
            public ConstraintMode constraintMode;
            public PatchBuildOrder buildOrder;
            public bool verboseLog;
        }
    }
}
