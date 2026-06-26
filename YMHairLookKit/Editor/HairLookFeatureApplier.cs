using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace YoridoriModifiers.HairLookKit
{
    internal static class HairLookFeatureApplier
    {
        internal static void ApplyEyebrow(
            YMHairLookKitComponent component,
            IReadOnlyList<Material> currentMaterials,
            IReadOnlyList<HairMaterialMerger.Result> mergedResults,
            IReadOnlyList<string> errors,
            Action<string> onProgress)
        {
            if (!component.enableEyebrowStencil || HasCategoryError(errors, "Eyebrow")) return;
            var face = HairLookTargetResolver.ResolveCurrentMaterialReference(component.eyebrowFaceMaterial, currentMaterials);
            var eyebrow = HairLookTargetResolver.ResolveCurrentMaterialReference(component.eyebrowMaterial, currentMaterials);
            if (face == null || eyebrow == null) return;

            onProgress?.Invoke("Applying eyebrow stencil...");
            ApplyEyebrowMaterialOverride(eyebrow);
            ApplyStencilSettingsForFace(face);
            ApplyStencilSettingsForEyebrow(eyebrow);
            if (component.eyebrowHairTargetMode == YMHairLookKitComponent.HairTargetMode.MergedHair)
            {
                foreach (var result in mergedResults.Where(r => r?.mergedMaterial != null))
                {
                    ApplyStencilSettingsForFrontHair(result.mergedMaterial);
                }
            }
            else
            {
                ApplyStencilSettingsForFrontHair(HairLookTargetResolver.ResolveCurrentMaterialReference(component.eyebrowHairMaterial, currentMaterials));
            }
        }

        internal static void ApplyFakeShadow(
            YMHairLookKitComponent component,
            GameObject root,
            IReadOnlyList<Material> currentMaterials,
            IReadOnlyList<HairMaterialMerger.Result> mergedResults,
            IReadOnlyList<string> errors,
            List<string> warnings,
            Action<string> onProgress,
            BuildContext buildContext)
        {
            if (!component.enableFakeShadow || HasCategoryError(errors, "FakeShadow")) return;
            var face = HairLookTargetResolver.ResolveCurrentMaterialReference(component.fakeShadowFaceMaterial, currentMaterials);
            if (face == null) return;

            onProgress?.Invoke("Applying FakeShadow...");
            if (component.fakeShadowHairTargetMode == YMHairLookKitComponent.HairTargetMode.MergedHair)
            {
                foreach (var result in mergedResults.Where(r => r?.mergedMaterial != null))
                {
                    var mergedFake = CreateFakeShadowMaterial(result.mergedMaterial, component.fakeShadowDirection, component.fakeShadowOffset, buildContext);
                    if (mergedFake == null)
                    {
                        warnings.Add("FakeShadow enabled but lilToonFakeShadow shader was not found");
                        continue;
                    }
                    AddFakeShadowOverlay(root, result.mergedMaterial, mergedFake, buildContext);
                    ApplyStencilSettingsForFace(face);
                    ApplyStencilSettingsForFrontHair(result.mergedMaterial);
                    ApplyStencilSettingsForFakeShadow(mergedFake);
                    SyncFakeShadowColor(face, mergedFake);
                }
                return;
            }

            var hair = HairLookTargetResolver.ResolveCurrentMaterialReference(component.fakeShadowHairMaterial, currentMaterials);
            if (hair == null) return;
            var fake = CreateFakeShadowMaterial(hair, component.fakeShadowDirection, component.fakeShadowOffset, buildContext);
            if (fake == null)
            {
                warnings.Add("FakeShadow enabled but lilToonFakeShadow shader was not found");
                return;
            }
            AddFakeShadowOverlay(root, hair, fake, buildContext);
            ApplyStencilSettingsForFace(face);
            ApplyStencilSettingsForFrontHair(hair);
            ApplyStencilSettingsForFakeShadow(fake);
            SyncFakeShadowColor(face, fake);
        }

        internal static void ApplyOutline(
            YMHairLookKitComponent component,
            GameObject root,
            IReadOnlyList<Material> currentMaterials,
            IReadOnlyList<string> errors,
            YMHairLookKitProcessor.ProcessRoute route,
            Action<string> onProgress,
            BuildContext buildContext)
        {
            if (!ShouldApplyOutlineCorrection(component, errors, route, root)) return;
            if (component.outlineHairTargetMode == YMHairLookKitComponent.HairTargetMode.MergedHair) return;
            var hair = HairLookTargetResolver.ResolveCurrentMaterialReference(component.outlineHairMaterial, currentMaterials);
            if (hair == null) return;
            onProgress?.Invoke("Baking outline correction...");
            ApplyOutlineCorrectionToMaterial(root, hair, buildContext);
        }

        internal static bool ShouldApplyOutlineCorrection(YMHairLookKitComponent component, IReadOnlyList<string> errors, YMHairLookKitProcessor.ProcessRoute route, GameObject root)
        {
            if (component == null || !component.enableHairOutlineCorrection) return false;
            if (HasCategoryError(errors, "Outline")) return false;
            return !(route == YMHairLookKitProcessor.ProcessRoute.Build && IsMobileBuildTarget(root));
        }

        internal static bool IsMobileBuildTarget(GameObject root)
        {
            switch (ResolveCurrentBuildTarget(root))
            {
                case BuildTarget.Android:
                case BuildTarget.iOS:
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasCategoryError(IEnumerable<string> errors, string category)
        {
            return errors != null && errors.Any(e => e.StartsWith(category + ":", StringComparison.Ordinal));
        }

        private static void AddFakeShadowOverlay(GameObject root, Material hairMaterial, Material fakeShadowMaterial, BuildContext buildContext)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                if (materials == null || !materials.Contains(hairMaterial)) continue;
                var list = materials.ToList();
                list.Add(fakeShadowMaterial);
                renderer.sharedMaterials = list.ToArray();
                buildContext?.AssetSaver.SaveAsset(fakeShadowMaterial);
            }
        }

        private static void ApplyOutlineCorrectionToMaterial(GameObject root, Material hairMaterial, BuildContext buildContext)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var targetSubMeshes = new List<int>();
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == hairMaterial) targetSubMeshes.Add(i);
                }
                if (targetSubMeshes.Count == 0) continue;

                var mesh = HairMaterialMerger.ResolveMesh(renderer);
                if (mesh == null) continue;
                var meshCopy = Object.Instantiate(mesh);
                RegisterReplacedObject(mesh, meshCopy);
                EnsureReferenceTrackableObjectFlags(meshCopy);
                buildContext?.AssetSaver.SaveAsset(meshCopy);
                var outlineAlphaByVertex = Enumerable.Repeat(1f, meshCopy.vertexCount).ToList();
                var bakeIndices = new HashSet<int>();
                foreach (var subMesh in targetSubMeshes)
                {
                    foreach (var index in meshCopy.GetTriangles(subMesh))
                    {
                        if (index < 0 || index >= meshCopy.vertexCount) continue;
                        bakeIndices.Add(index);
                    }
                }
                HairOutlineCorrection.ApplyAverageNormals(meshCopy, bakeIndices.ToArray(), outlineAlphaByVertex);
                SetFloatIfAnyExists(hairMaterial, new[] { "_OutlineVertexR2Width" }, 2f);
                HairMaterialMerger.ApplyMesh(renderer, meshCopy);
            }
        }

        private static Material CreateFakeShadowMaterial(Material source, Vector3 direction, float offset, BuildContext buildContext)
        {
            var shader = ResolveFakeShadowShader();
            if (shader == null || source == null) return null;
            var material = new Material(source)
            {
                name = $"{source.name}_FakeShadow",
                shader = shader,
            };
            SetFloatIfAnyExists(material, new[] { "_UseFakeShadow", "_EnableFakeShadow", "_FakeShadow" }, 1f);
            SetVectorIfAnyExists(material, new[] { "_FakeShadowVector", "_FakeShadowDir", "_FakeShadowDirection" }, new Vector4(direction.x, direction.y, direction.z, offset));
            SetFloatIfAnyExists(material, new[] { "_FakeShadowOffset", "_FakeShadowPositionOffset" }, offset);
            EnsureReferenceTrackableObjectFlags(material);
            buildContext?.AssetSaver.SaveAsset(material);
            return material;
        }

        private static Shader ResolveFakeShadowShader()
        {
            foreach (var shaderName in new[]
                     {
                         "_lil/[Optional] lilToonFakeShadow",
                         "_lil/[Optional]lilToonFakeShadow",
                         "Hidden/lilToonFakeShadow",
                     })
            {
                var shader = Shader.Find(shaderName);
                if (shader != null) return shader;
            }
            var guids = AssetDatabase.FindAssets("lilToonFakeShadow t:Shader");
            return guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Shader>)
                .FirstOrDefault(shader => shader != null);
        }

        private static void ApplyStencilSettingsForFace(Material material)
        {
            if (material == null) return;
            material.renderQueue = 2450;
            ApplyStencilSettings(material, 51f, 63f, 63f, (float)CompareFunction.Always, (float)StencilOp.Replace, (float)StencilOp.Keep, (float)StencilOp.Keep);
        }

        private static void ApplyStencilSettingsForEyebrow(Material material)
        {
            if (material == null) return;
            material.renderQueue = 2451;
            ApplyStencilSettings(material, 128f, 128f, 191f, (float)CompareFunction.Always, (float)StencilOp.Replace, (float)StencilOp.Keep, (float)StencilOp.Keep);
        }

        private static void ApplyStencilSettingsForFrontHair(Material material)
        {
            if (material == null) return;
            material.renderQueue = 2452;
            ApplyStencilSettings(material, 128f, 128f, 63f, (float)CompareFunction.NotEqual, (float)StencilOp.Replace, (float)StencilOp.Keep, (float)StencilOp.Keep);
        }

        private static void ApplyStencilSettingsForFakeShadow(Material material)
        {
            if (material == null) return;
            ApplyStencilSettings(material, 51f, 63f, 0f, (float)CompareFunction.Equal, (float)StencilOp.Keep, (float)StencilOp.Keep, (float)StencilOp.Keep);
        }

        private static void ApplyStencilSettings(Material material, float reference, float readMask, float writeMask, float compare, float pass, float fail, float zFail)
        {
            SetFloatIfAnyExists(material, new[] { "_StencilRef", "_Ref", "_OutlineStencilRef", "_OutlineRef" }, reference);
            SetFloatIfAnyExists(material, new[] { "_StencilReadMask", "_ReadMask", "_OutlineStencilReadMask", "_OutlineReadMask" }, readMask);
            SetFloatIfAnyExists(material, new[] { "_StencilWriteMask", "_WriteMask", "_OutlineStencilWriteMask", "_OutlineWriteMask" }, writeMask);
            SetFloatIfAnyExists(material, new[] { "_StencilComp", "_Comp", "_OutlineStencilComp", "_OutlineComp" }, compare);
            SetFloatIfAnyExists(material, new[] { "_StencilPass", "_Pass", "_OutlineStencilPass", "_OutlinePass" }, pass);
            SetFloatIfAnyExists(material, new[] { "_StencilFail", "_Fail", "_OutlineStencilFail", "_OutlineFail" }, fail);
            SetFloatIfAnyExists(material, new[] { "_StencilZFail", "_ZFail", "_OutlineStencilZFail", "_OutlineZFail" }, zFail);
        }

        private static void ApplyEyebrowMaterialOverride(Material material)
        {
            if (material == null) return;
            if (material.shader != null && material.shader.name.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var hasOutline = (material.HasProperty("_UseOutline") && material.GetFloat("_UseOutline") > 0.5f)
                    || (material.HasProperty("_OutlineEnable") && material.GetFloat("_OutlineEnable") > 0.5f);
                var cutoutShader = Shader.Find(hasOutline ? "Hidden/lilToonCutoutOutline" : "Hidden/lilToonCutout");
                if (cutoutShader != null) material.shader = cutoutShader;
            }
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            SetFloatIfAnyExists(material, new[] { "_UseClipping" }, 1f);
            SetFloatIfAnyExists(material, new[] { "_AlphaMode", "_TransparentMode", "_RenderingMode", "_RenderMode", "_BlendMode", "_Surface" }, 1f);
            SetFloatIfAnyExists(material, new[] { "_Cutoff" }, 0.5f);
            SetFloatIfAnyExists(material, new[] { "_SrcBlend" }, (float)BlendMode.One);
            SetFloatIfAnyExists(material, new[] { "_DstBlend" }, (float)BlendMode.Zero);
            SetFloatIfAnyExists(material, new[] { "_ZWrite" }, 1f);
            material.renderQueue = 2451;
        }

        private static void SyncFakeShadowColor(Material face, Material fakeShadow)
        {
            if (face == null || fakeShadow == null) return;
            if (TryGetColorFromAny(face, new[] { "_ShadowColor", "_Shadow1stColor", "_ShadeColor", "_Color" }, out var color))
            {
                SetColorIfAnyExists(fakeShadow, new[] { "_Color", "_MainColor", "_BaseColor", "_ShadowColor" }, color);
            }
        }

        private static BuildTarget ResolveCurrentBuildTarget(GameObject root)
        {
            var vqtBuildTarget = ResolveVrcQuestToolsBuildTarget(root);
            return vqtBuildTarget ?? EditorUserBuildSettings.activeBuildTarget;
        }

        private static BuildTarget? ResolveVrcQuestToolsBuildTarget(GameObject root)
        {
            if (root == null) return null;

            foreach (var component in root.GetComponents<Component>())
            {
                if (component == null || component.GetType().FullName != "KRT.VRCQuestTools.Components.PlatformTargetSettings") continue;
                var field = component.GetType().GetField("buildTarget");
                var value = field?.GetValue(component);
                if (value == null) return null;

                switch (value.ToString())
                {
                    case "PC":
                        return BuildTarget.StandaloneWindows64;
                    case "Android":
                        return BuildTarget.Android;
                    default:
                        return null;
                }
            }

            return null;
        }

        private static void EnsureReferenceTrackableObjectFlags(Object generatedObject)
        {
            if (generatedObject == null) return;
            var dontSaveFlags = HideFlags.DontSave | HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor | HideFlags.HideAndDontSave;
            generatedObject.hideFlags &= ~dontSaveFlags;
        }

        private static void RegisterReplacedObject(Object original, Object replacement)
        {
            if (original == null || replacement == null) return;
            try
            {
                ObjectRegistry.RegisterReplacedObject(original, replacement);
            }
            catch (ArgumentException)
            {
                // NDMF requires registration before the replacement receives a reference.
            }
        }

        private static bool TryGetColorFromAny(Material material, IReadOnlyList<string> propertyNames, out Color color)
        {
            color = Color.white;
            if (material == null || propertyNames == null) return false;
            foreach (var propertyName in propertyNames)
            {
                if (!material.HasProperty(propertyName)) continue;
                color = material.GetColor(propertyName);
                return true;
            }
            return false;
        }

        private static void SetFloatIfAnyExists(Material material, IReadOnlyList<string> propertyNames, float value)
        {
            if (material == null || propertyNames == null) return;
            foreach (var propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName)) material.SetFloat(propertyName, value);
            }
        }

        private static void SetVectorIfAnyExists(Material material, IReadOnlyList<string> propertyNames, Vector4 value)
        {
            if (material == null || propertyNames == null) return;
            foreach (var propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName)) material.SetVector(propertyName, value);
            }
        }

        private static void SetColorIfAnyExists(Material material, IReadOnlyList<string> propertyNames, Color value)
        {
            if (material == null || propertyNames == null) return;
            foreach (var propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName)) material.SetColor(propertyName, value);
            }
        }
    }
}
