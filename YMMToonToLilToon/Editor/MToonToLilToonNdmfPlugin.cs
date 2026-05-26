using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

[assembly: ExportsPlugin(typeof(YoridoriModifiers.MToonToLilToon.MToonToLilToonNdmfPlugin))]

namespace YoridoriModifiers.MToonToLilToon
{
    public sealed class MToonToLilToonNdmfPlugin : Plugin<MToonToLilToonNdmfPlugin>
    {
        public override string QualifiedName => "jp.yoridrill.ym-mtoon-to-liltoon";
        public override string DisplayName => "YM MToon to lilToon";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("jp.yoridrill.ym-arm-patch")
                .AfterPlugin("jp.yoridrill.ym-mesh-trimmer")
                .BeforePlugin("com.github.kurotu.vrc-quest-tools")
                .Run("Convert MToon10 Materials to lilToon", Execute);
        }

        private static void Execute(BuildContext context)
        {
            var root = ResolveAvatarRoot(context);
            if (root == null) return;

            var components = root.GetComponentsInChildren<MToonToLilToonComponent>(true);
            try
            {
                if (components.Any(c => c != null && c.isPreviewing))
                {
                    MToonToLilToonPreviewUtility.StopPreview();
                    foreach (var component in components.Where(c => c != null))
                    {
                        component.isPreviewing = false;
                    }
                }

                var selected = SelectPreferredComponent(components, root);
                foreach (var component in components)
                {
                    if (component != selected) continue;
                    ApplyOnBuild(component);
                }
            }
            finally
            {
                RemoveComponents(components);
            }
        }

        private static GameObject ResolveAvatarRoot(BuildContext context)
        {
            var contextType = context.GetType();

            var avatarRootObject = contextType.GetProperty("AvatarRootObject")?.GetValue(context) as GameObject;
            if (avatarRootObject != null) return avatarRootObject;

            var avatarRootTransform = contextType.GetProperty("AvatarRootTransform")?.GetValue(context) as Transform;
            return avatarRootTransform != null ? avatarRootTransform.gameObject : null;
        }

        private static void ApplyOnBuild(MToonToLilToonComponent component)
        {
            MToonToLilToonProcessor.ApplyOnBuild(component);
        }

        private static MToonToLilToonComponent SelectPreferredComponent(
            MToonToLilToonComponent[] components,
            GameObject avatarRoot)
        {
            if (components == null || components.Length == 0) return null;

            MToonToLilToonComponent best = null;
            var bestScore = int.MinValue;
            var rootTransform = avatarRoot != null ? avatarRoot.transform : null;
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null) continue;
                var depth = PreviewCoordinator.GetDepthFromRoot(component.transform, rootTransform);
                var score = -depth * 10000 - i;
                if (score <= bestScore) continue;
                best = component;
                bestScore = score;
            }

            return best;
        }

        private static void RemoveComponents(MToonToLilToonComponent[] components)
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
