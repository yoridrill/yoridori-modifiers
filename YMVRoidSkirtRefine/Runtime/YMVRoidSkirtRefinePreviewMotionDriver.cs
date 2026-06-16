using UnityEngine;

namespace YoridoriModifiers.VRoidSkirtRefine
{
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-10000)]
    public sealed class YMVRoidSkirtRefinePreviewMotionDriver : MonoBehaviour
    {
        private GameObject avatarRoot;
        private AnimationClip[] clips;
        private int[] loopCounts;
        private float startTime;

        public void Initialize(GameObject root, AnimationClip[] previewClips, int[] previewLoopCounts)
        {
            avatarRoot = root;
            clips = previewClips;
            loopCounts = previewLoopCounts;
            startTime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (avatarRoot == null || clips == null || clips.Length == 0) return;

            var totalLength = 0.0f;
            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) continue;
                totalLength += GetClipLength(clips[i]) * GetLoopCount(i);
            }
            if (totalLength <= 0.0f) return;

            var time = (Time.realtimeSinceStartup - startTime) % totalLength;
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip == null) continue;

                var clipLength = GetClipLength(clip);
                var entryLength = clipLength * GetLoopCount(i);
                if (time > entryLength)
                {
                    time -= entryLength;
                    continue;
                }

                clip.SampleAnimation(avatarRoot, time % clipLength);
                return;
            }
        }

        private int GetLoopCount(int index)
        {
            if (loopCounts == null || index < 0 || index >= loopCounts.Length) return 1;
            return Mathf.Max(1, loopCounts[index]);
        }

        private static float GetClipLength(AnimationClip clip)
        {
            return Mathf.Max(0.001f, clip != null ? clip.length : 0.0f);
        }
    }
}
