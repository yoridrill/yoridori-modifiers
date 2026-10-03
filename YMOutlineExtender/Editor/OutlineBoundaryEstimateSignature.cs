using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using static YoridoriModifiers.OutlineExtender.OutlineRendererUtility;
using UnityEngine.Rendering;

namespace YoridoriModifiers.OutlineExtender
{
    internal static partial class OutlineBoundaryFoldProcessor
    {
        // Use the effective Fold inputs rather than the avatar-wide dirty counters.
        // Representative changes, shading edits and other tools' settings can refresh
        // the Inspector without invalidating an unchanged geometry estimate.
        internal static string BuildEstimateSignature(GameObject root, YMOutlineExtenderComponent component,
            OutlineHairMaterialControls controls)
        {
            var key = new StringBuilder();
            key.Append(component.GetInstanceID()).Append('|').Append(component.enabled)
                .Append('|').Append(component.enableOutlineExtender).Append('|').Append(component.enableBoundaryFold)
                .Append('|').Append(Mathf.Clamp(component.outlineWidthMultiplier, 0f, 2f).GetHashCode())
                .Append('|').Append(NormalizeFoldAngle(component.boundaryFoldAngleDegrees))
                .Append('|').Append(NormalizeFoldLength(component.boundaryFoldLengthMultiplier).GetHashCode())
                .Append('|').Append(NormalizeSupportBandWidth(component.boundarySupportBandWidthMultiplier).GetHashCode())
                .Append('|').Append(component.taperBoundaryEndpoints)
                .Append('|').Append(NormalizeFillDistance(component.boundaryFillDistanceMultiplier).GetHashCode());
            var selections = controls.CreateSelectionSet(component.boundaryMaterialSelections);
            var renderers = root.GetComponentsInChildren<Renderer>(true).OrderBy(r => r.GetInstanceID()).ToArray();
            var paddingTextures = new HashSet<Texture2D>();
            foreach (var renderer in renderers)
            {
                var mesh = GetMesh(renderer);
                if (mesh == null || mesh.vertexCount == 0 || !mesh.HasVertexAttribute(VertexAttribute.Normal)) continue;
                var targets = CollectTargets(renderer, mesh, component, selections, false);
                if (targets.Count == 0) continue;
                AppendMesh(key, renderer, mesh);
                var materials = renderer.sharedMaterials;
                foreach (var target in targets.OrderBy(pair => pair.Key))
                {
                    var info = target.Value;
                    var material = materials[target.Key];
                    key.Append("|F:").Append(target.Key).Append(':').Append(material.GetInstanceID())
                        .Append(':').Append(info.Outline.ObjectSpaceWidth.GetHashCode()).Append(':').Append(info.TestAlpha);
                    if (!info.TestAlpha) continue;
                    key.Append(':').Append(info.TextureCutoff.GetHashCode());
                    AppendUv(key, info.TextureScale, info.TextureOffset);
                    var texture = info.MainTexture;
                    key.Append(':').Append(texture.GetInstanceID()).Append(':').Append(UnityEditor.EditorUtility.GetDirtyCount(texture))
                        .Append(':').Append(texture.imageContentsHash).Append(':').Append(texture.width).Append(':').Append(texture.height)
                        .Append(':').Append((int)texture.wrapModeU).Append(':').Append((int)texture.wrapModeV)
                        .Append(':').Append((int)texture.filterMode).Append(':').Append(texture.mipmapCount).Append(':').Append(texture.isDataSRGB);
                    var canPad = CanPadMaterial(material, info);
                    key.Append(':').Append(canPad);
                    if (canPad && component.outlineWidthMultiplier > 0f
                        && NormalizeFillDistance(component.boundaryFillDistanceMultiplier) > 0f) paddingTextures.Add(texture);
                }
            }
            // Padding protects UV islands on any submesh sharing a target texture.
            // These related meshes affect Fold even when their own Fold button is off.
            if (paddingTextures.Count > 0)
                foreach (var renderer in renderers)
                {
                    var mesh = GetMesh(renderer);
                    if (mesh == null || mesh.vertexCount == 0 || !mesh.HasVertexAttribute(VertexAttribute.TexCoord0)) continue;
                    var materials = renderer.sharedMaterials;
                    var appended = false;
                    for (var sub = 0; sub < Mathf.Min(mesh.subMeshCount, materials.Length); sub++)
                    {
                        var material = materials[sub];
                        if (material == null || mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                        var property = OutlineMaterialUtility.ResolveMainTextureProperty(material);
                        if (property == null || !(material.GetTexture(property) is Texture2D texture) || !paddingTextures.Contains(texture)) continue;
                        if (!appended) { key.Append("|UV"); AppendMesh(key, renderer, mesh); appended = true; }
                        key.Append(':').Append(sub).Append(':').Append(material.GetInstanceID()).Append(':').Append(texture.GetInstanceID());
                        AppendUv(key, material.GetTextureScale(property), material.GetTextureOffset(property));
                    }
                }
            return Hash128.Compute(key.ToString()).ToString();
        }

        private static void AppendMesh(StringBuilder key, Renderer renderer, Mesh mesh) => key
            .Append("|M:").Append(renderer.GetInstanceID()).Append(':').Append(mesh.GetInstanceID())
            .Append(':').Append(UnityEditor.EditorUtility.GetDirtyCount(mesh)).Append(':').Append(mesh.vertexCount).Append(':').Append(mesh.subMeshCount);

        private static void AppendUv(StringBuilder key, Vector2 scale, Vector2 offset) => key
            .Append(':').Append(scale.x.GetHashCode()).Append(':').Append(scale.y.GetHashCode())
            .Append(':').Append(offset.x.GetHashCode()).Append(':').Append(offset.y.GetHashCode());
    }
}
