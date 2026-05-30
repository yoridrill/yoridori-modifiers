using System;
using UnityEngine;

namespace YoridoriModifiers.Core.Editor
{
    public static class TextureReadUtility
    {
        public static Texture2D ToReadableTexture(Texture texture, bool linear = false)
        {
            if (texture == null) return null;
            if (linear && texture is Texture2D readableSource && readableSource.isReadable)
            {
                try
                {
                    var copy = new Texture2D(readableSource.width, readableSource.height, TextureFormat.RGBA32, false, true);
                    CopyTextureSettings(readableSource, copy);
                    copy.SetPixels(readableSource.GetPixels());
                    copy.Apply(false, false);
                    return copy;
                }
                catch (Exception ex) when (ex is UnityException || ex is ArgumentException)
                {
                    // Fall through to a GPU copy for unreadable or platform-restricted textures.
                }
            }

            var width = texture.width;
            var height = texture.height;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var current = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
                CopyTextureSettings(texture, readable);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply();
                return readable;
            }
            finally
            {
                RenderTexture.active = current;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        public static Texture2D ToReadableTextureWithTransform(Texture texture, Vector2 scale, Vector2 offset, bool linear = false)
        {
            var readable = ToReadableTexture(texture, linear);
            if (readable == null) return null;
            if ((scale - Vector2.one).sqrMagnitude < 0.000001f && offset.sqrMagnitude < 0.000001f) return readable;

            var width = readable.width;
            var height = readable.height;
            var transformed = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            CopyTextureSettings(readable, transformed);
            var colors = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                var v = (y + 0.5f) / height;
                for (var x = 0; x < width; x++)
                {
                    var u = (x + 0.5f) / width;
                    var tu = Mathf.Repeat(u * scale.x + offset.x, 1f);
                    var tv = Mathf.Repeat(v * scale.y + offset.y, 1f);
                    colors[y * width + x] = SampleRepeatPoint(readable, tu, tv);
                }
            }

            transformed.SetPixels(colors);
            transformed.Apply();
            if (readable != texture)
            {
                UnityEngine.Object.DestroyImmediate(readable);
            }
            return transformed;
        }

        private static void CopyTextureSettings(Texture source, Texture target)
        {
            target.name = source.name;
            target.wrapMode = source.wrapMode;
            target.filterMode = source.filterMode;
            target.anisoLevel = source.anisoLevel;
        }

        private static Color SampleRepeatPoint(Texture2D texture, float u, float v)
        {
            var width = texture.width;
            var height = texture.height;
            var x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(u, 1f) * width), 0, Mathf.Max(0, width - 1));
            var y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(v, 1f) * height), 0, Mathf.Max(0, height - 1));
            return texture.GetPixel(x, y);
        }
    }
}
