using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;

namespace YoridoriModifiers.EyeFreeze
{
    [ParameterProviderFor(typeof(YMEyeFreeze))]
    internal sealed class EyeFreezeParameterProvider : IParameterProvider
    {
        private readonly YMEyeFreeze component;

        public EyeFreezeParameterProvider(YMEyeFreeze component)
        {
            this.component = component;
        }

        public IEnumerable<ProvidedParameter> GetSuppliedParameters(BuildContext context = null)
        {
            if (component == null) yield break;

            yield return new ProvidedParameter(
                NormalizeParameterName(component.parameterName),
                ParameterNamespace.Animator,
                component,
                EyeFreezeNdmfPlugin.Instance,
                AnimatorControllerParameterType.Bool)
            {
                WantSynced = component.synced,
                DefaultValue = 0f
            };
        }

        internal static string NormalizeParameterName(string parameterName)
        {
            return string.IsNullOrWhiteSpace(parameterName) ? "YM/EyeFreeze" : parameterName.Trim();
        }
    }
}
