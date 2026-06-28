using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YoridoriModifiers.MToonToLilToon
{
    internal static class MToonToLilToonBuildTargetUtility
    {
        internal static bool IsPcBuildTarget(GameObject avatarRoot)
        {
            switch (ResolveCurrentBuildTarget(avatarRoot))
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return true;
                default:
                    return false;
            }
        }

        private static BuildTarget ResolveCurrentBuildTarget(GameObject avatarRoot)
        {
            return ResolveVrcQuestToolsBuildTarget(avatarRoot)
                ?? EditorUserBuildSettings.activeBuildTarget;
        }

        private static BuildTarget? ResolveVrcQuestToolsBuildTarget(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;

            foreach (var component in avatarRoot.GetComponents<Component>())
            {
                if (component == null
                    || component.GetType().FullName != "KRT.VRCQuestTools.Components.PlatformTargetSettings")
                {
                    continue;
                }

                var type = component.GetType();
                var value = type.GetField("buildTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(component)
                    ?? type.GetProperty("buildTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(component);
                switch (value?.ToString())
                {
                    case "PC":
                        return BuildTarget.StandaloneWindows64;
                    case "Android":
                        return BuildTarget.Android;
                    default:
                        return null;
                }
            }

            return null;
        }
    }
}
