using UnityEditor;
using UnityEngine;

namespace YoridoriModifiers.Core.Editor
{
    public static class PreviewInspectorGui
    {
        private static readonly Color ActiveButtonColor = new(0.4f, 0.85f, 0.4f);
        private static GUIStyle _statusStyle;

        private static float RowHeight => EditorGUIUtility.singleLineHeight + 2f;

        public static bool DrawPreviewButton(bool active, string label = "Preview", float width = 90f)
        {
            var previous = GUI.backgroundColor;
            if (active) GUI.backgroundColor = ActiveButtonColor;
            var clicked = GUILayout.Button(label, GUILayout.Width(width), GUILayout.Height(RowHeight));
            GUI.backgroundColor = previous;
            return clicked;
        }

        public static void DrawStatus(bool processing, bool failed, string detail = null, float width = 140f)
        {
            var text = string.Empty;
            if (processing)
            {
                text = "Processing...";
            }
            else if (failed)
            {
                text = "Failed";
            }
            else if (!string.IsNullOrEmpty(detail))
            {
                text = detail;
            }

            DrawMiniStatus(text, width);
        }

        public static bool DrawResetPreviewButton(string label = "Reset Preview", float width = 120f)
        {
            return GUILayout.Button(label, GUILayout.Width(width), GUILayout.Height(RowHeight));
        }

        public static void DrawPreviewRecoveryHelp(string message)
        {
            EditorGUILayout.HelpBox(message, MessageType.None);
        }

        private static void DrawMiniStatus(string text, float width)
        {
            _statusStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            var rect = GUILayoutUtility.GetRect(width, RowHeight, GUILayout.Width(width), GUILayout.Height(RowHeight));
            if (!string.IsNullOrEmpty(text))
            {
                rect.y += 2f;
                GUI.Label(rect, text, _statusStyle);
            }
        }
    }
}
