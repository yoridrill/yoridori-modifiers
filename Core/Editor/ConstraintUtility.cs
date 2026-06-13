using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace YoridoriModifiers.Core.Editor
{
    public enum ConstraintImplementationMode
    {
        VRChatConstraints,
        UnityConstraints
    }

    public static class ConstraintUtility
    {
        public static void AddRotationConstraintAllAxes(
            Transform target,
            Transform source,
            float weight,
            Vector3 eulerOffset,
            ConstraintImplementationMode mode)
        {
            if (target == null || source == null) return;

            if (mode == ConstraintImplementationMode.VRChatConstraints)
            {
                AddVRCRotationConstraintAllAxes(target, source, weight, eulerOffset);
            }
            else
            {
                AddUnityRotationConstraintAllAxes(target, source, weight, eulerOffset);
            }
        }

        private static void AddVRCRotationConstraintAllAxes(
            Transform target,
            Transform source,
            float weight,
            Vector3 eulerOffset)
        {
            var constraint = target.gameObject.GetComponent<VRCRotationConstraint>();
            if (constraint == null)
            {
                constraint = target.gameObject.AddComponent<VRCRotationConstraint>();
            }

            constraint.IsActive = true;
            constraint.GlobalWeight = Mathf.Clamp01(weight);
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

        private static void AddUnityRotationConstraintAllAxes(
            Transform target,
            Transform source,
            float weight,
            Vector3 eulerOffset)
        {
            var constraint = target.gameObject.GetComponent<RotationConstraint>();
            if (constraint == null)
            {
                constraint = target.gameObject.AddComponent<RotationConstraint>();
            }

            constraint.constraintActive = false;
            constraint.locked = false;
            for (var i = constraint.sourceCount - 1; i >= 0; i--)
            {
                constraint.RemoveSource(i);
            }

            constraint.rotationAxis = Axis.X | Axis.Y | Axis.Z;
            constraint.weight = Mathf.Clamp01(weight);
            constraint.AddSource(new ConstraintSource
            {
                sourceTransform = source,
                weight = 1f
            });
            constraint.rotationAtRest = target.localEulerAngles;
            constraint.rotationOffset = eulerOffset;
            constraint.constraintActive = true;
            constraint.locked = true;
        }
    }
}
