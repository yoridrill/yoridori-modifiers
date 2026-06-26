using System;
using System.Linq;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.MToonToLilToon
{
    public static class MToonToLilToonPreviewBridge
    {
        public static void ApplyForChainedPreview(GameObject avatarRoot, Action<string> onProgress)
        {
            if (avatarRoot == null) return;
            var component = avatarRoot.GetComponentsInChildren<MToonToLilToonComponent>(true)
                .Where(c => c != null)
                .OrderBy(c => PreviewCoordinator.GetDepthFromRoot(c.transform, avatarRoot.transform))
                .FirstOrDefault();
            if (component == null) return;

            MToonToLilToonProcessor.ApplyOnBuild(
                component,
                onProgress,
                MToonToLilToonProcessor.ConversionRoute.Preview);
        }
    }
}
