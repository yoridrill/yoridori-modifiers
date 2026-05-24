using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YoridoriModifiers.Core.Editor
{
    public sealed class PreviewRendererVisibilityScope
    {
        private readonly List<RendererState> _states = new();

        private struct RendererState
        {
            public Renderer renderer;
            public bool wasEnabled;
        }

        public void Hide(GameObject avatarRoot)
        {
            Restore();
            if (avatarRoot == null) return;

            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                _states.Add(new RendererState
                {
                    renderer = renderer,
                    wasEnabled = renderer.enabled
                });
                renderer.enabled = false;
            }
        }

        public void Restore()
        {
            foreach (var state in _states)
            {
                if (state.renderer != null)
                {
                    state.renderer.enabled = state.wasEnabled;
                }
            }

            _states.Clear();
        }
    }

    public static class PreviewCoordinator
    {
        private sealed class Session
        {
            public string ownerKey;
            public string displayName;
            public GameObject avatarRoot;
            public bool usesAnimationMode;
        }

        private static readonly List<Session> Sessions = new();

        public static bool TryBegin(
            string ownerKey,
            string displayName,
            GameObject avatarRoot,
            bool usesAnimationMode,
            out string failureMessage)
        {
            CleanupDeadSessions();
            failureMessage = string.Empty;

            if (string.IsNullOrEmpty(ownerKey) || avatarRoot == null)
            {
                failureMessage = "Preview target is missing.";
                return false;
            }

            var existing = Sessions.FirstOrDefault(s => s.ownerKey == ownerKey);
            if (existing != null)
            {
                existing.displayName = displayName;
                existing.avatarRoot = avatarRoot;
                existing.usesAnimationMode = usesAnimationMode;
                return true;
            }

            foreach (var session in Sessions)
            {
                if (session.usesAnimationMode || usesAnimationMode)
                {
                    failureMessage = $"{session.displayName} is already previewing.";
                    return false;
                }

                if (session.avatarRoot == avatarRoot)
                {
                    failureMessage = $"{session.displayName} is already previewing on this avatar.";
                    return false;
                }
            }

            Sessions.Add(new Session
            {
                ownerKey = ownerKey,
                displayName = displayName,
                avatarRoot = avatarRoot,
                usesAnimationMode = usesAnimationMode
            });
            return true;
        }

        public static void End(string ownerKey)
        {
            if (string.IsNullOrEmpty(ownerKey)) return;
            Sessions.RemoveAll(s => s.ownerKey == ownerKey);
        }

        public static GameObject FindAvatarRoot(GameObject from)
        {
            if (from == null) return null;

            var animator = from.GetComponentsInParent<Animator>(true)
                .FirstOrDefault(a => a.avatar != null && a.avatar.isHuman);
            if (animator != null) return animator.gameObject;

            var transform = from.transform;
            while (transform.parent != null)
            {
                transform = transform.parent;
            }

            return transform.gameObject;
        }

        public static string BuildRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || root == target) return string.Empty;

            var segments = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            if (current != root) return string.Empty;
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static void CleanupDeadSessions()
        {
            Sessions.RemoveAll(s => s == null || s.avatarRoot == null);
        }
    }
}
