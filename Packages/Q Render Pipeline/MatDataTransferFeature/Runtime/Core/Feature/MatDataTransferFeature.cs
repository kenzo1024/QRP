using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Rendering.MatDataTransfer.Runtime
{
    [CreateAssetMenu(menuName = "Rendering/Features/MatDataTransfer Feature")]
    public partial class MatDataTransferFeature : ScriptableRendererFeature
    {
        #region Constants

        public const int DefaultMaxInstanceCount = 256;
        public const int MinInstanceCount = 0;
        public const string FeatureName = nameof(MatDataTransferFeature);

        #endregion

        #region Static State

        public static MatDataTransferFeature Instance { get; private set; }
#if UNITY_EDITOR
        internal static event Action<MatDataTransferFeature> EditorCatalogSyncRequested;
#endif

        #endregion

        #region Serialized Fields

        [SerializeField] private List<ShaderPropertyCatalog> m_Catalogs =
            new List<ShaderPropertyCatalog>();
        [SerializeField] private MatDataTransferLoggingSettings m_LoggingSettings =
            new MatDataTransferLoggingSettings();
        [SerializeField] private ParamWriteMethod m_EditModeWriteMode =
            ParamWriteMethod.MaterialPropertyBlock;
        [SerializeField] private ParamWriteMethod m_RuntimeWriteMode =
            ParamWriteMethod.MaterialInstance;
        [SerializeField] private int m_MaxInstanceCount = DefaultMaxInstanceCount;

        #endregion

        #region Runtime Fields

        [NonSerialized] private bool m_IsPrimaryInstance;
        [NonSerialized] private readonly Dictionary<string, ShaderPropertyCatalog> m_CatalogByShader =
            new Dictionary<string, ShaderPropertyCatalog>(StringComparer.Ordinal);

        #endregion

        #region Properties

        public IReadOnlyList<ShaderPropertyCatalog> Catalogs => m_Catalogs;
        public MatDataTransferLoggingSettings LoggingSettings => m_LoggingSettings;
        public ParamWriteMethod EditModeWriteMode => m_EditModeWriteMode;
        public ParamWriteMethod RuntimeWriteMode => m_RuntimeWriteMode;
        public int MaxInstanceCount => m_MaxInstanceCount;
        public int ActiveInstanceCount => GetActiveInstanceCount();

        #endregion

        #region Catalog Lookup

        public bool TryGetCatalogForShader(string shaderName, out ShaderPropertyCatalog catalog)
        {
            catalog = null;
            return !string.IsNullOrEmpty(shaderName)
                && m_CatalogByShader.TryGetValue(shaderName, out catalog)
                && catalog != null;
        }

        public bool TryGetProperty(
            string shaderName,
            string semanticKey,
            out ShaderPropertyCatalog catalog,
            out CatalogProperty property)
        {
            property = null;
            if (!TryGetCatalogForShader(shaderName, out catalog)
                || !catalog.TryGetProperty(semanticKey, out property))
                return false;

            return property != null && property.Status == CatalogPropertyStatus.Ok;
        }

        #endregion

        #region ScriptableRendererFeature Lifecycle

        public override void Create()
        {
            if (HasAnotherPrimaryInstance())
                return;

#if UNITY_EDITOR
            RequestEditorCatalogSync();
#endif
            CleanupPrimaryInstance();
            InitializePrimaryInstance();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!IsPrimaryInstance())
                return;

            AddRenderPassTo(renderer);
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsPrimaryInstance())
                return;

            base.Dispose(disposing);
            CleanupPrimaryInstance();
            Instance = null;
        }

        #endregion

        #region Unity Callbacks

        private void OnValidate()
        {
            if (HasAnotherPrimaryInstance())
                return;

            EnsureSerializedSettings();
#if UNITY_EDITOR
            RequestEditorCatalogSync();
#endif
            TrySetMaxInstanceCount(m_MaxInstanceCount);
            ApplyLoggerSettings();
            ApplyWriterSettings();
            RefreshCatalogCaches();
        }

        #endregion

        #region Primary Instance State

        internal bool IsPrimaryInstance()
        {
            return m_IsPrimaryInstance && ReferenceEquals(Instance, this);
        }

        private bool HasAnotherPrimaryInstance()
        {
            return Instance != null && !ReferenceEquals(Instance, this);
        }

        #endregion

        #region Initialization

        private void InitializePrimaryInstance()
        {
            Instance = this;
            m_IsPrimaryInstance = true;

            EnsureSerializedSettings();
            ApplyLoggerSettings();
            RefreshCatalogCaches();

            InitializeInstanceRegister(m_MaxInstanceCount);
            InitializeTransferPipeline();
            InitializeRenderPass();
        }

        private void CleanupPrimaryInstance()
        {
            DisposeRenderPass();
            DisposeTransferPipeline();
            ClearWrittenState();
            DisposeLogger();
            ClearQueuedRequests();
            ClearInstanceRegister();

            m_IsPrimaryInstance = false;
        }

        #endregion

        #region Settings

        private void EnsureSerializedSettings()
        {
            if (m_LoggingSettings == null)
                m_LoggingSettings = new MatDataTransferLoggingSettings();
            if (m_Catalogs == null)
                m_Catalogs = new List<ShaderPropertyCatalog>();

            m_EditModeWriteMode = NormalizeWriteMethod(
                m_EditModeWriteMode,
                ParamWriteMethod.MaterialPropertyBlock);
            m_RuntimeWriteMode = NormalizeWriteMethod(
                m_RuntimeWriteMode,
                ParamWriteMethod.MaterialInstance);
            m_MaxInstanceCount = NormalizeCapacity(m_MaxInstanceCount);
        }

        private static ParamWriteMethod NormalizeWriteMethod(
            ParamWriteMethod writeMethod,
            ParamWriteMethod fallback)
        {
            switch (writeMethod)
            {
                case ParamWriteMethod.MaterialPropertyBlock:
                case ParamWriteMethod.MaterialInstance:
                case ParamWriteMethod.SharedMaterial:
                    return writeMethod;
                default:
                    return fallback;
            }
        }

        private void RefreshCatalogCaches()
        {
            if (m_Catalogs == null)
                m_Catalogs = new List<ShaderPropertyCatalog>();

            m_CatalogByShader.Clear();
            for (int i = 0; i < m_Catalogs.Count; i++)
            {
                ShaderPropertyCatalog catalog = m_Catalogs[i];
                if (catalog == null)
                    continue;

                catalog.RebuildPropertyMap();
                if (!string.IsNullOrEmpty(catalog.ShaderName))
                    m_CatalogByShader[catalog.ShaderName] = catalog;
            }
        }

#if UNITY_EDITOR
        private void RequestEditorCatalogSync()
        {
            EditorCatalogSyncRequested?.Invoke(this);
        }

        internal bool MergeCatalogsFromEditor(IReadOnlyList<ShaderPropertyCatalog> catalogs)
        {
            if (m_Catalogs == null)
                m_Catalogs = new List<ShaderPropertyCatalog>();

            bool changed = RemoveEmptyCatalogSlots();
            if (catalogs != null)
            {
                for (int i = 0; i < catalogs.Count; i++)
                {
                    ShaderPropertyCatalog catalog = catalogs[i];
                    if (catalog == null || m_Catalogs.Contains(catalog))
                        continue;

                    m_Catalogs.Add(catalog);
                    changed = true;
                }
            }

            if (changed)
                RefreshCatalogCaches();

            return changed;
        }

        private bool RemoveEmptyCatalogSlots()
        {
            bool removed = false;
            for (int i = m_Catalogs.Count - 1; i >= 0; i--)
            {
                if (m_Catalogs[i] != null)
                    continue;

                m_Catalogs.RemoveAt(i);
                removed = true;
            }

            return removed;
        }
#endif

        #endregion

        #region Utilities

        internal static string GetTransformPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }

        #endregion
    }
}
