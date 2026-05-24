using UnityEngine;
using VRC.SDKBase;

namespace YoridoriModifiers.EyeFreeze
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Yoridori Modifiers/YM Eye Freeze")]
    public sealed class YMEyeFreeze : MonoBehaviour, IEditorOnly
    {
        [Tooltip("Expression Menu display name.")]
        public string menuName = "Eye Freeze";

        [Tooltip("Internal expression parameter name.")]
        public string parameterName = "YM/EyeFreeze";

        [Tooltip("Save the expression parameter value.")]
        public bool saved = true;

        [Tooltip("Sync the expression parameter value.")]
        public bool synced = true;
    }
}
