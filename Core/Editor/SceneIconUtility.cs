using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YoridoriModifiers.Core.Editor
{
    public static class SceneIconUtility
    {
        private const int MonoBehaviourClassId = 114;

        private static readonly HashSet<Type> PendingTypes = new HashSet<Type>();
        private static MethodInfo _setIconEnabled;
        private static MethodInfo _setGizmoEnabled;
        private static MethodInfo _getAnnotations;
        private static FieldInfo _annotationClassId;
        private static FieldInfo _annotationScriptClass;

        public static void HideComponentIcon<T>() where T : MonoBehaviour
        {
            HideComponentIcon(typeof(T));
        }

        public static void HideComponentIcon(Type componentType)
        {
            if (componentType == null || !typeof(MonoBehaviour).IsAssignableFrom(componentType))
            {
                return;
            }

            if (PendingTypes.Add(componentType))
            {
                EditorApplication.update -= ApplyPendingIconSettings;
                EditorApplication.update += ApplyPendingIconSettings;
            }
        }

        private static void ApplyPendingIconSettings()
        {
            foreach (var componentType in new List<Type>(PendingTypes))
            {
                if (!HasAnnotation(componentType))
                {
                    continue;
                }

                SetIconEnabled(componentType, false);
                SetGizmoEnabled(componentType, false);
                PendingTypes.Remove(componentType);
            }

            if (PendingTypes.Count == 0)
            {
                EditorApplication.update -= ApplyPendingIconSettings;
            }
        }

        private static bool HasAnnotation(Type componentType)
        {
            var annotationType = Assembly.GetAssembly(typeof(UnityEditor.Editor))?.GetType("UnityEditor.Annotation");
            _getAnnotations ??= Assembly.GetAssembly(typeof(UnityEditor.Editor))
                ?.GetType("UnityEditor.AnnotationUtility")
                ?.GetMethod("GetAnnotations", BindingFlags.Static | BindingFlags.NonPublic);
            _annotationClassId ??= annotationType?.GetField("classID", BindingFlags.Instance | BindingFlags.Public);
            _annotationScriptClass ??= annotationType?.GetField("scriptClass", BindingFlags.Instance | BindingFlags.Public);

            if (_getAnnotations == null || _annotationClassId == null || _annotationScriptClass == null)
            {
                return true;
            }

            var annotations = (Array)_getAnnotations.Invoke(null, Array.Empty<object>());
            foreach (var annotation in annotations)
            {
                var classId = (int)_annotationClassId.GetValue(annotation);
                var scriptClass = (string)_annotationScriptClass.GetValue(annotation);
                if (classId == MonoBehaviourClassId && scriptClass == componentType.Name)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetIconEnabled(Type componentType, bool enabled)
        {
#if UNITY_2022_1_OR_NEWER
            GizmoUtility.SetIconEnabled(componentType, enabled);
#else
            SetIconEnabledByAnnotationUtility(componentType, enabled);
#endif
        }

        private static void SetGizmoEnabled(Type componentType, bool enabled)
        {
#if UNITY_2022_1_OR_NEWER
            GizmoUtility.SetGizmoEnabled(componentType, enabled);
#else
            SetGizmoEnabledByAnnotationUtility(componentType, enabled);
#endif
        }

        private static void SetIconEnabledByAnnotationUtility(Type componentType, bool enabled)
        {
            _setIconEnabled ??= Assembly.GetAssembly(typeof(UnityEditor.Editor))
                ?.GetType("UnityEditor.AnnotationUtility")
                ?.GetMethod("SetIconEnabled", BindingFlags.Static | BindingFlags.NonPublic);

            _setIconEnabled?.Invoke(null, new object[] { MonoBehaviourClassId, componentType.Name, enabled ? 1 : 0 });
        }

        private static void SetGizmoEnabledByAnnotationUtility(Type componentType, bool enabled)
        {
            _setGizmoEnabled ??= Assembly.GetAssembly(typeof(UnityEditor.Editor))
                ?.GetType("UnityEditor.AnnotationUtility")
                ?.GetMethod("SetGizmoEnabled", BindingFlags.Static | BindingFlags.NonPublic);

            _setGizmoEnabled?.Invoke(null, new object[] { MonoBehaviourClassId, componentType.Name, enabled ? 1 : 0 });
        }
    }
}
