using System;
using System.Reflection;
using Anatawa12.AvatarOptimizer.API;
using UnityEditor;
using UnityEngine;

namespace YoridoriModifiers.OutlineExtender.Integrations.AAO
{
    [InitializeOnLoad]
    internal static class YMOutlineExtenderShaderInformation
    {
        private sealed class LilToonInformation : ShaderInformation
        {
            private readonly ShaderInformation source;

            public LilToonInformation(ShaderInformation source) => this.source = source;

            public override ShaderInformationKind SupportedInformationKind => source.SupportedInformationKind;

            public override void GetMaterialInformation(MaterialInformationCallback info) =>
                source.GetMaterialInformation(info);
        }

        static YMOutlineExtenderShaderInformation()
        {
            Register();
            EditorApplication.delayCall += Register;
        }

        private static void Register()
        {
            var shader = Shader.Find("Hidden/yoridrill/lilToonOutlineExtender/CutoutOutline");
            var source = CreateLilToonOutlineInformation();
            if (shader == null || source == null) return;

            try { ShaderInformationRegistry.RegisterShaderInformation(shader, new LilToonInformation(source)); }
            catch (InvalidOperationException) { }
        }

        private static ShaderInformation CreateLilToonOutlineInformation()
        {
            // Reuse AAO's complete, version-matched lilToon definition. A local partial
            // definition can omit Outline, Emission, MatCap or vertex-index usage.
            var type = Type.GetType(
                "Anatawa12.AvatarOptimizer.APIInternal.LiltoonShaderInformation, com.anatawa12.avatar-optimizer.editor");
            var constructor = type?.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            return constructor?.Invoke(new object[] { "CutoutOutline" }) as ShaderInformation;
        }
    }
}
