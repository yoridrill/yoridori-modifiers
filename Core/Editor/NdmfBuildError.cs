using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine.UIElements;

namespace YoridoriModifiers.Core.Editor
{
    public sealed class NdmfBuildError : IError
    {
        private readonly string message;
        private readonly List<ObjectReference> references = new List<ObjectReference>();

        public NdmfBuildError(string message)
        {
            this.message = message;
        }

        public ErrorSeverity Severity => ErrorSeverity.Error;

        public VisualElement CreateVisualElement(ErrorReport report)
        {
            var label = new Label(message);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        public string ToMessage() => message;

        public void AddReference(ObjectReference obj)
        {
            if (obj != null) references.Add(obj);
        }
    }
}
