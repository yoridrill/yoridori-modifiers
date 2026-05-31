using System;
using System.Collections.Generic;
using UnityEngine;

namespace YoridoriModifiers.Core.Editor
{
    public static class PreviewRecoveryUtility
    {
        private static readonly Dictionary<string, Action<GameObject>> ResetHandlers = new();

        public static void RegisterResetHandler(string key, Action<GameObject> resetHandler)
        {
            if (string.IsNullOrEmpty(key) || resetHandler == null) return;
            ResetHandlers[key] = resetHandler;
        }

        public static void ResetAllPreviewArtifacts(GameObject avatarRoot = null)
        {
            foreach (var handler in new List<Action<GameObject>>(ResetHandlers.Values))
            {
                try
                {
                    handler.Invoke(avatarRoot);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
