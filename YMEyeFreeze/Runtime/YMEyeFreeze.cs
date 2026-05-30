using UnityEngine;
using VRC.SDKBase;

namespace YoridoriModifiers.EyeFreeze
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Yoridori Modifiers/YM Eye Freeze")]
    public sealed class YMEyeFreeze : MonoBehaviour, IEditorOnly
    {
        public string menuName = "Eye Freeze";

        public string parameterName = "YM/EyeFreeze";

        public bool saved = true;

        public bool synced = true;
    }
}
