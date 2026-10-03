using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;
using YoridoriModifiers.MToonToLilToon;

namespace YoridoriModifiers.OutlineExtender
{
    public sealed partial class YMOutlineExtenderComponentEditor
    {
        private string dependencyKey;
        private Object[] dependencies = System.Array.Empty<Object>();
        private bool rescanPending;
        private double nextDependencyCheck;
        private double lastGuiTime;
        private string polygonEstimateKey;
        private double polygonEstimateDue;
        private bool polygonEstimatePending;
        private bool polygonEstimateReady;
        private (int before, int after) polygonCounts;
        private readonly OutlineBoundaryPaddingCache polygonPaddingCache = new();

        private void EnableInspectorUpdates()
        {
            EditorApplication.update += UpdatePolygonEstimate;
            EditorApplication.projectChanged += InvalidatePolygonEstimate;
            EditorApplication.hierarchyChanged += InvalidatePolygonEstimate;
            Undo.undoRedoPerformed += InvalidatePolygonEstimate;
        }

        private void DisableInspectorUpdates()
        {
            EditorApplication.update -= UpdatePolygonEstimate;
            EditorApplication.projectChanged -= InvalidatePolygonEstimate;
            EditorApplication.hierarchyChanged -= InvalidatePolygonEstimate;
            Undo.undoRedoPerformed -= InvalidatePolygonEstimate;
            polygonPaddingCache.DestroyTextures();
        }

        private void InvalidatePolygonEstimate()
        {
            rescanPending = true;
            Repaint();
        }

        // Poll inexpensive dirty counters outside IMGUI. Layout, repaint and scrolling
        // use the cached summary and never walk renderers or validate shaders.
        private void UpdatePolygonEstimate()
        {
            if (target == null || EditorApplication.timeSinceStartup - lastGuiTime > 1.0) return;
            var now = EditorApplication.timeSinceStartup;
            if (rescanPending || now >= nextDependencyCheck)
            {
                nextDependencyCheck = now + 0.5;
                var state = BuildPreviewStateKey((YMOutlineExtenderComponent)target);
                var key = BuildDependencyKey();
                if (rescanPending || state != previewStateKey || key != dependencyKey)
                    RefreshInspectorState(true);
            }
            if (!polygonEstimatePending || now < polygonEstimateDue) return;
            polygonEstimatePending = false;
            var component = (YMOutlineExtenderComponent)target;
            var root = PreviewCoordinator.FindAvatarRoot(component.gameObject) ?? component.gameObject;
            polygonCounts = OutlineBoundaryFoldProcessor.EstimatePolygons(root, component, polygonPaddingCache);
            polygonEstimateReady = true;
            OutlinePolygonEstimateCache.Store(polygonEstimateKey, polygonCounts);
            Repaint();
        }

        private string BuildDependencyKey()
        {
            var key = new StringBuilder();
            foreach (var obj in dependencies)
                key.Append(obj != null ? obj.GetInstanceID() : 0).Append(':')
                    .Append(obj != null ? EditorUtility.GetDirtyCount(obj) : 0).Append('|');
            return key.ToString();
        }

        private void RefreshDependencies(GameObject root)
        {
            var watched = new HashSet<Object>(materials);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                watched.Add(renderer);
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null) watched.Add(filter);
                var mesh = OutlineRendererUtility.GetMesh(renderer);
                if (mesh != null) watched.Add(mesh);
            }
            foreach (var material in materials)
                foreach (var property in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(property);
                    if (texture != null) watched.Add(texture);
                }
            foreach (var hair in root.GetComponentsInChildren<YoridoriModifiers.HairLookKit.YMHairLookKitComponent>(true)) watched.Add(hair);
            foreach (var converter in root.GetComponentsInChildren<MToonToLilToonComponent>(true)) watched.Add(converter);
            dependencies = watched.OrderBy(obj => obj.GetInstanceID()).ToArray();
            dependencyKey = BuildDependencyKey();
        }

        private void RequestPolygonEstimate(GameObject root, YMOutlineExtenderComponent component, bool hasFoldTargets)
        {
            // Invalidate only when the effective Fold inputs change.
            var estimateKey = OutlineBoundaryFoldProcessor.BuildEstimateSignature(root, component, hairControls);
            if (estimateKey != polygonEstimateKey)
            {
                polygonEstimateKey = estimateKey;
                polygonEstimatePending = component.enableOutlineExtender && hasFoldTargets;
                polygonEstimateReady = !polygonEstimatePending;
                if (!polygonEstimatePending) polygonCounts = default;
                else if (OutlinePolygonEstimateCache.TryGet(estimateKey, out var cached))
                {
                    polygonCounts = cached;
                    polygonEstimatePending = false;
                    polygonEstimateReady = true;
                }
                polygonEstimateDue = EditorApplication.timeSinceStartup + 0.15;
            }
        }
    }
}
