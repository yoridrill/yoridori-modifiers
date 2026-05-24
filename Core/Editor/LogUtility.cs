using UnityEngine;

namespace YoridoriModifiers.Core.Editor
{
    public static class LogUtility
    {
        public static void Info(string toolName, string message, Object context = null)
        {
            Debug.Log(Format(toolName, null, message), context);
        }

        public static void Info(string toolName, string category, string message, Object context = null)
        {
            Debug.Log(Format(toolName, category, message), context);
        }

        public static void Verbose(string toolName, bool verboseLog, string message, Object context = null)
        {
            if (!verboseLog) return;
            Info(toolName, message, context);
        }

        public static void Verbose(string toolName, bool verboseLog, string category, string message, Object context = null)
        {
            if (!verboseLog) return;
            Info(toolName, category, message, context);
        }

        public static void Warning(string toolName, string message, Object context = null)
        {
            Debug.LogWarning(Format(toolName, null, message), context);
        }

        public static void Warning(string toolName, string category, string message, Object context = null)
        {
            Debug.LogWarning(Format(toolName, category, message), context);
        }

        public static void Error(string toolName, string message, Object context = null)
        {
            Debug.LogError(Format(toolName, null, message), context);
        }

        public static void Error(string toolName, string category, string message, Object context = null)
        {
            Debug.LogError(Format(toolName, category, message), context);
        }

        public static void PreviewSkipped(string toolName, string reason, Object context = null)
        {
            Warning(toolName, "Preview skipped. " + reason, context);
        }

        private static string Format(string toolName, string category, string message)
        {
            var prefix = string.IsNullOrEmpty(category)
                ? $"[{toolName}]"
                : $"[{toolName}][{category}]";
            return $"{prefix} {message}";
        }
    }
}
