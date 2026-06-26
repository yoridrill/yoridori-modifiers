using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

[assembly: ExportsPlugin(typeof(YoridoriModifiers.HairLookKit.YMHairLookKitNdmfPlugin))]

namespace YoridoriModifiers.HairLookKit
{
    public sealed class YMHairLookKitNdmfPlugin : Plugin<YMHairLookKitNdmfPlugin>
    {
        public override string QualifiedName => "jp.yoridrill.ym-hair-look-kit";
        public override string DisplayName => "YM Hair Look Kit";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("jp.yoridrill.ym-mtoon-to-liltoon")
                .BeforePlugin("com.github.kurotu.vrc-quest-tools")
                .Run("Apply Hair Look Kit", Execute);
        }

        private static void Execute(BuildContext context)
        {
            var root = ResolveAvatarRoot(context);
            if (root == null) return;

            var components = root.GetComponentsInChildren<YMHairLookKitComponent>(true);
            try
            {
                if (components.Any(c => c != null && c.isPreviewing))
                {
                    YMHairLookKitPreviewUtility.StopPreview();
                    foreach (var component in components.Where(c => c != null))
                    {
                        component.isPreviewing = false;
                    }
                }

                var selected = SelectPreferredComponent(components, root);
                foreach (var component in components)
                {
                    if (component != selected) continue;
                    ErrorReport.WithContextObject(component, () => YMHairLookKitProcessor.ApplyOnBuild(component, context));
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

        private static YMHairLookKitComponent SelectPreferredComponent(YMHairLookKitComponent[] components, GameObject avatarRoot)
        {
            if (components == null || components.Length == 0) return null;
            var rootTransform = avatarRoot != null ? avatarRoot.transform : null;
            return components
                .Where(c => c != null)
                .OrderBy(c => PreviewCoordinator.GetDepthFromRoot(c.transform, rootTransform))
                .FirstOrDefault();
        }

        private static void RemoveComponents(YMHairLookKitComponent[] components)
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
