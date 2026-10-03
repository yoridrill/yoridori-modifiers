using System.Collections.Generic;
using UnityEngine;

namespace YoridoriModifiers.OutlineExtender
{
    internal static class OutlineRendererUtility
    {
        internal static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        internal static void SetMesh(Renderer renderer, Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh = mesh;
            else
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null) filter.sharedMesh = mesh;
            }
        }

        internal static void ReplaceMaterials(GameObject root, IReadOnlyDictionary<Material, Material> replacements)
        {
            if (replacements.Count == 0) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                    if (materials[i] != null && replacements.TryGetValue(materials[i], out var replacement))
                    {
                        materials[i] = replacement;
                        changed = true;
                    }
                if (changed) renderer.sharedMaterials = materials;
            }
        }
    }
}
