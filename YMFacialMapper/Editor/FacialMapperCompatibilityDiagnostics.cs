using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace YoridoriModifiers.FacialMapper
{
    internal sealed class FacialMapperLinkWarning : IError
    {
        private readonly string message;
        private readonly List<ObjectReference> references = new List<ObjectReference>();

        public FacialMapperLinkWarning(string message)
        {
            this.message = message;
        }

        public ErrorSeverity Severity => ErrorSeverity.NonFatal;
        public VisualElement CreateVisualElement(ErrorReport report)
        {
            var label = new Label(message);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
        public string ToMessage() => message;
        public void AddReference(ObjectReference reference)
        {
            if (reference != null) references.Add(reference);
        }
    }

    // Read-only preview of the descriptor FX. MA's merged result is checked separately at build time.
    internal static class FacialMapperCompatibilityDiagnostics
    {
        internal static string[] FindUnlinkedControls(AnimatorController controller)
        {
            if (controller == null) return System.Array.Empty<string>();
            var eligible = new HashSet<int>();
            var layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                var machines = Machines(layers[i].stateMachine).ToArray();
                var states = machines.SelectMany(m => m.states).Select(s => s.state).ToArray();
                var motions = states.SelectMany(s => Motions(s.motion)).Distinct().ToArray();
                bool gesture = machines.SelectMany(m => m.anyStateTransitions.Cast<AnimatorTransitionBase>()
                        .Concat(m.entryTransitions)).Concat(states.SelectMany(s => s.transitions))
                    .Any(t => t.conditions.Any(c => FacialMapperNdmfPlugin.IsGestureParameter(c.parameter))) ||
                    machines.SelectMany(m => m.stateMachines.SelectMany(child => m.GetStateMachineTransitions(child.stateMachine)))
                        .Any(t => t.conditions.Any(c => FacialMapperNdmfPlugin.IsGestureParameter(c.parameter))) ||
                    motions.OfType<BlendTree>().Any(UsesGesture);
                bool nonzero = motions.OfType<AnimationClip>().Any(clip => AnimationUtility.GetCurveBindings(clip)
                    .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape."))
                    .Any(b => FacialMapperNdmfPlugin.HasNonZeroValues(AnimationUtility.GetEditorCurve(clip, b))));
                if (gesture && nonzero) eligible.Add(i);
            }
            return layers.SelectMany(l => Machines(l.stateMachine)).SelectMany(m => m.states).Select(s => s.state)
                .Where(s =>
                {
                    var controls = s.behaviours.OfType<VRCAnimatorLayerControl>()
                        .Where(FacialMapperNdmfPlugin.IsEndpointControl).ToArray();
                    return controls.Length > 0 && !FacialMapperNdmfPlugin.CompatibleControls(
                        controls.Where(c => eligible.Contains(c.layer)).ToArray());
                }).Select(s => s.name).Distinct().ToArray();
        }

        private static IEnumerable<AnimatorStateMachine> Machines(AnimatorStateMachine machine)
        {
            if (machine == null) yield break;
            yield return machine;
            foreach (var child in machine.stateMachines)
                foreach (var nested in Machines(child.stateMachine)) yield return nested;
        }

        private static IEnumerable<Motion> Motions(Motion root)
        {
            var visited = new HashSet<Motion>();
            var pending = new Stack<Motion>();
            if (root != null) pending.Push(root);
            while (pending.Count > 0)
            {
                var motion = pending.Pop();
                if (!visited.Add(motion)) continue;
                yield return motion;
                if (motion is BlendTree tree)
                    foreach (var child in tree.children) if (child.motion != null) pending.Push(child.motion);
            }
        }

        private static bool UsesGesture(BlendTree tree)
        {
            if (tree.blendType == BlendTreeType.Direct)
                return tree.children.Any(c => FacialMapperNdmfPlugin.IsGestureParameter(c.directBlendParameter));
            return FacialMapperNdmfPlugin.IsGestureParameter(tree.blendParameter) ||
                (tree.blendType != BlendTreeType.Simple1D && FacialMapperNdmfPlugin.IsGestureParameter(tree.blendParameterY));
        }
    }
}
