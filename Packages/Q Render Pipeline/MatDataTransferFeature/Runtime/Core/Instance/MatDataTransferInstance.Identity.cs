using System.Text;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferInstance
    {
        [SerializeField, HideInInspector] private string sourceGuid;

        public int InstanceId { get; private set; } = -1;
        public bool IsReady => InstanceId >= 0;
        public string SourceId => BuildSourceId();

        internal void SetRegisteredInstanceId(int id)
        {
            InstanceId = id;
        }

        private string BuildSourceId()
        {
            EnsureSourceGuid(false);
            string sceneLabel = GetSceneLabel();
            string transformPath = GetTransformPath(transform);

            return "MDTI."
                + SanitizeSourceIdPart(sceneLabel)
                + "."
                + SanitizeSourceIdPart(transformPath)
                + ".uid_"
                + sourceGuid;
        }

        private void EnsureSourceGuid(bool checkDuplicate)
        {
            if (string.IsNullOrEmpty(sourceGuid) || (checkDuplicate && HasDuplicateSourceGuid()))
                sourceGuid = System.Guid.NewGuid().ToString("N");
        }

        private bool HasDuplicateSourceGuid()
        {
            if (string.IsNullOrEmpty(sourceGuid))
                return false;

            MatDataTransferInstance[] instances = Resources.FindObjectsOfTypeAll<MatDataTransferInstance>();
            for (int i = 0; i < instances.Length; i++)
            {
                MatDataTransferInstance instance = instances[i];
                if (instance == null || ReferenceEquals(instance, this))
                    continue;

                if (string.Equals(instance.sourceGuid, sourceGuid, System.StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private string GetSceneLabel()
        {
            string sceneName = gameObject.scene.name;
            if (!string.IsNullOrEmpty(sceneName))
                return sceneName;

            string scenePath = gameObject.scene.path;
            if (!string.IsNullOrEmpty(scenePath))
                return scenePath;

            return "NoScene";
        }

        private static string GetTransformPath(Transform target)
        {
            if (target == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder(GetTransformSegment(target));
            while (target.parent != null)
            {
                target = target.parent;
                builder.Insert(0, GetTransformSegment(target) + "/");
            }

            return builder.ToString();
        }

        private static string GetTransformSegment(Transform target)
        {
            return target != null
                ? target.name + "[" + target.GetSiblingIndex() + "]"
                : string.Empty;
        }

        private static string SanitizeSourceIdPart(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Empty";

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                builder.Append(IsSourceIdSafeChar(ch) ? ch : '_');
            }

            return builder.ToString();
        }

        private static bool IsSourceIdSafeChar(char ch)
        {
            return char.IsLetterOrDigit(ch)
                || ch == '_'
                || ch == '-'
                || ch == '.'
                || ch == '/'
                || ch == '['
                || ch == ']';
        }
    }
}
