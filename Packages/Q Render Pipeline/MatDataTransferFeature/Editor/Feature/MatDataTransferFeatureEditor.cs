using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using System.Collections.Generic;
using Rendering.MatDataTransfer.Runtime;

namespace Rendering.MatDataTransfer.Editor
{
    [CustomEditor(typeof(MatDataTransferFeature))]
    public sealed class MatDataTransferFeatureEditor : UnityEditor.Editor
    {
        private const string CatalogsPropertyName = "m_Catalogs";
        private const string GenericProviderSettingsPropertyName = "m_GenericProviderSettings";
        private const string LoggingSettingsPropertyName = "m_LoggingSettings";
        private const string MaxInstanceCountPropertyName = "m_MaxInstanceCount";

        private const int RegistryIdMinDigits = 2;
        private const float RegistryColumnGap = 8f;
        private const float RegistryIdColumnPadding = 2f;
        private const float RegistryNameWidthRatio = 0.4f;
        private const float RegistryInstanceIdWidthRatio = 0.6f;
        private const float RegistryDividerWidth = 1f;
        private static bool s_ShowCatalogs = true;
        private static bool s_ShowInstances = true;
        private static bool s_ShowActiveInstances;
        private static bool s_ShowRequestProviders = true;
        private static bool s_ShowLogging = true;

        private SerializedProperty m_Catalogs;
        private SerializedProperty m_GenericProviderSettings;
        private SerializedProperty m_LoggingSettings;
        private SerializedProperty m_MaxInstanceCount;
        private ReorderableList m_CatalogList;
        private readonly List<MatDataTransferInstanceRegisterEntry> m_InstanceEntries =
            new List<MatDataTransferInstanceRegisterEntry>();

        private void OnEnable()
        {
            EnsureFixedFeatureName();
            m_Catalogs = serializedObject.FindProperty(CatalogsPropertyName);
            m_GenericProviderSettings = serializedObject.FindProperty(GenericProviderSettingsPropertyName);
            m_LoggingSettings = serializedObject.FindProperty(LoggingSettingsPropertyName);
            m_MaxInstanceCount = serializedObject.FindProperty(MaxInstanceCountPropertyName);
            InitializeCatalogList();
        }

        public override void OnInspectorGUI()
        {
            EnsureFixedFeatureName();
            serializedObject.Update();

            DrawCatalogs();
            EditorGUILayout.Space(6f);
            DrawInstances();
            EditorGUILayout.Space(6f);
            DrawRequestProviders();
            EditorGUILayout.Space(6f);
            DrawLoggingSettings();

            if (serializedObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(target);
            }
        }

        protected override void OnHeaderGUI()
        {
            EnsureFixedFeatureName();
            InspectorStyleLibrary.DrawTitle(MatDataTransferFeature.FeatureName);
        }

        private void DrawCatalogs()
        {
            bool wasExpanded = s_ShowCatalogs;
            s_ShowCatalogs = InspectorStyleLibrary.DrawFoldoutLayout(
                s_ShowCatalogs,
                "Catalogs",
                BuildCatalogSummary(),
                false);

            if (!wasExpanded && s_ShowCatalogs)
            {
                MatDataTransferCatalogAutoSync.SyncCatalogsFromConfigFolder((MatDataTransferFeature)target);
                serializedObject.Update();
            }

            if (!s_ShowCatalogs)
                return;

            using (InspectorStyleLibrary.BeginPanelLayout())
                DrawCatalogList();
        }

        private string BuildCatalogSummary()
        {
            if (m_Catalogs == null)
                return "0 catalog";

            return m_Catalogs.arraySize == 1
                ? "1 catalog"
                : m_Catalogs.arraySize + " catalogs";
        }

        private void InitializeCatalogList()
        {
            if (m_Catalogs == null)
                return;

            m_CatalogList = new ReorderableList(
                serializedObject,
                m_Catalogs,
                true,
                false,
                true,
                true);
            m_CatalogList.drawElementCallback = DrawCatalogElement;
        }

        private void DrawCatalogList()
        {
            if (m_CatalogList == null)
                InitializeCatalogList();

            if (m_CatalogList == null)
                return;

            m_CatalogList.DoLayoutList();
        }

        private void DrawCatalogElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (m_Catalogs == null || index < 0 || index >= m_Catalogs.arraySize)
                return;

            SerializedProperty element = m_Catalogs.GetArrayElementAtIndex(index);
            rect.y += 1f;
            rect.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(rect, element, new GUIContent("Element " + index));
        }

        private void DrawInstances()
        {
            s_ShowInstances = InspectorStyleLibrary.DrawFoldoutLayout(
                s_ShowInstances,
                "Instances",
                false);
            if (!s_ShowInstances)
                return;

            MatDataTransferFeature feature = (MatDataTransferFeature)target;
            int registeredCount = feature != null ? feature.ActiveInstanceCount : 0;
            int minimumMaxCount = Mathf.Max(MatDataTransferFeature.MinInstanceCount, registeredCount);
            int currentMaxCount = m_MaxInstanceCount != null
                ? m_MaxInstanceCount.intValue
                : MatDataTransferFeature.DefaultMaxInstanceCount;

            if (m_MaxInstanceCount != null && currentMaxCount < minimumMaxCount)
            {
                currentMaxCount = minimumMaxCount;
                if (feature != null && feature.TrySetMaxInstanceCount(minimumMaxCount))
                    m_MaxInstanceCount.intValue = feature.MaxInstanceCount;
                else
                    m_MaxInstanceCount.intValue = minimumMaxCount;
            }

            using (InspectorStyleLibrary.BeginPanelLayout())
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.IntField("Current Instances", registeredCount);

                int nextMaxCount = EditorGUILayout.IntField("Max Instances", Mathf.Max(currentMaxCount, minimumMaxCount));
                nextMaxCount = Mathf.Max(minimumMaxCount, nextMaxCount);

                if (m_MaxInstanceCount != null && nextMaxCount != currentMaxCount)
                {
                    if (feature != null && feature.TrySetMaxInstanceCount(nextMaxCount))
                        m_MaxInstanceCount.intValue = feature.MaxInstanceCount;
                    else
                        m_MaxInstanceCount.intValue = minimumMaxCount;
                }

                DrawActiveInstances(feature);
            }
        }

        private void DrawActiveInstances(MatDataTransferFeature feature)
        {
            if (feature == null)
                return;

            feature.CopyInstanceRegisterEntries(m_InstanceEntries);

            EditorGUILayout.Space(3f);
            s_ShowActiveInstances = InspectorStyleLibrary.DrawFoldoutLayout(
                s_ShowActiveInstances,
                "Active Instances",
                BuildActiveInstancesSummary(m_InstanceEntries.Count),
                false,
                InspectorStyleLibrary.FoldoutPanelLeftPadding);

            if (!s_ShowActiveInstances)
                return;

            EditorGUI.indentLevel++;
            if (m_InstanceEntries.Count == 0)
            {
                InspectorStyleLibrary.DrawDescription("No active instances.");
                EditorGUI.indentLevel--;
                return;
            }

            float idColumnWidth = GetRegistryIdColumnWidth();
            for (int i = 0; i < m_InstanceEntries.Count; i++)
                DrawRegistryEntry(m_InstanceEntries[i], idColumnWidth);

            EditorGUI.indentLevel--;
        }

        private float GetRegistryIdColumnWidth()
        {
            GUIStyle style = InspectorStyleLibrary.Description;
            float width = style.CalcSize(new GUIContent(FormatRegistryIdPlaceholder())).x;
            return Mathf.Ceil(width + RegistryIdColumnPadding);
        }

        private static void DrawRegistryEntry(MatDataTransferInstanceRegisterEntry entry, float idColumnWidth)
        {
            Rect row = InspectorStyleLibrary.GetIndentedControlRectLayout();
            GUIStyle style = InspectorStyleLibrary.Description;
            string idText = FormatRegistryId(entry.Id);
            string displayName = entry.DisplayName ?? string.Empty;
            string instanceId = entry.SourceId ?? string.Empty;

            Rect idRect = new Rect(row.x, row.y, idColumnWidth, row.height);
            float contentX = idRect.xMax + RegistryColumnGap;
            float contentWidth = Mathf.Max(0f, row.xMax - contentX);
            float contentGap = contentWidth > RegistryColumnGap ? RegistryColumnGap : 0f;
            float labelWidth = Mathf.Max(0f, contentWidth - contentGap);
            float totalRatio = RegistryNameWidthRatio + RegistryInstanceIdWidthRatio;
            float nameWidth = totalRatio > 0f
                ? Mathf.Floor(labelWidth * RegistryNameWidthRatio / totalRatio)
                : 0f;
            float instanceIdWidth = Mathf.Max(0f, labelWidth - nameWidth);
            Rect nameRect = new Rect(contentX, row.y, nameWidth, row.height);
            Rect instanceIdRect = new Rect(nameRect.xMax + contentGap, row.y, instanceIdWidth, row.height);

            DrawRegistryColumnDivider(idRect.xMax + RegistryColumnGap * 0.5f, row);
            DrawRegistryColumnDivider(nameRect.xMax + contentGap * 0.5f, row);
            GUI.Label(idRect, new GUIContent(idText, idText), style);
            InspectorStyleLibrary.DrawCopyableTailLabel(nameRect, displayName, style, false);
            InspectorStyleLibrary.DrawCopyableTailLabel(instanceIdRect, instanceId, style, false);
        }

        private static void DrawRegistryColumnDivider(float x, Rect row)
        {
            Color color = EditorGUIUtility.isProSkin
                ? new Color(0.44f, 0.44f, 0.44f, 0.85f)
                : new Color(0.56f, 0.56f, 0.56f, 0.85f);
            Rect dividerRect = new Rect(
                Mathf.Round(x - RegistryDividerWidth * 0.5f),
                row.y + 2f,
                RegistryDividerWidth,
                Mathf.Max(0f, row.height - 4f));
            EditorGUI.DrawRect(dividerRect, color);
        }

        private static string FormatRegistryId(int id)
        {
            return "#" + id;
        }

        private static string FormatRegistryIdPlaceholder()
        {
            return "#" + new string('9', RegistryIdMinDigits);
        }

        private static string BuildActiveInstancesSummary(int count)
        {
            return count + " active";
        }

        private void DrawRequestProviders()
        {
            s_ShowRequestProviders = InspectorStyleLibrary.DrawFoldoutLayout(
                s_ShowRequestProviders,
                "Request Providers",
                false);
            if (!s_ShowRequestProviders)
                return;

            using (InspectorStyleLibrary.BeginPanelLayout())
                DrawGenericProvider();
        }

        private void DrawGenericProvider()
        {
            if (m_GenericProviderSettings == null)
                return;

            InspectorStyleLibrary.DrawTitle(MatDataTransferProviderNames.GenericMaterialParameter);
            InspectorStyleLibrary.DrawCopyableTailLabelLayout(
                "Type: Built-in Generic Material Parameter Provider",
                InspectorStyleLibrary.Description,
                false);
            InspectorStyleLibrary.DrawDescription("Kind: Built-in Request Provider");
            EditorGUILayout.PropertyField(m_GenericProviderSettings.FindPropertyRelative("Enabled"));
            using (new EditorGUI.DisabledScope(true))
                GUILayout.Button("Built-in", EditorStyles.miniButton, GUILayout.Width(72f));
        }

        private void DrawLoggingSettings()
        {
            s_ShowLogging = InspectorStyleLibrary.DrawFoldoutLayout(
                s_ShowLogging,
                "Logging",
                false);
            if (!s_ShowLogging)
                return;

            using (InspectorStyleLibrary.BeginPanelLayout())
            {
                if (m_LoggingSettings != null)
                    DrawLoggingProperties();
            }
        }

        private void DrawLoggingProperties()
        {
            SerializedProperty enableLogging = DrawLoggingProperty("EnableLogging", "Enable Logging");
            bool enabled = enableLogging != null && enableLogging.boolValue;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                DrawMaxTimelineFramesProperty();
                DrawLoggingProperty("AllowReleaseFileLogging", "Allow Release Player File Logging");
            }

            DrawTimelineViewerButton();
        }

        private void DrawMaxTimelineFramesProperty()
        {
            SerializedProperty property = m_LoggingSettings.FindPropertyRelative("MaxTimelineFrames");
            if (property == null)
                return;

            property.intValue = Mathf.Max(
                1,
                EditorGUILayout.IntField("Max Recorded Frames", property.intValue));
        }

        private SerializedProperty DrawLoggingProperty(string propertyName, string label)
        {
            SerializedProperty property = m_LoggingSettings.FindPropertyRelative(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(label));

            return property;
        }

        private void DrawTimelineViewerButton()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                if (GUILayout.Button("Open Timeline Viewer", GUILayout.Width(180f)))
                    MatDataTransferTimelineViewer.OpenWindow();
            }

            MatDataTransferLogging logging = MatDataTransferLogging.Instance;
            if (logging != null && !string.IsNullOrEmpty(logging.CurrentLogFilePath))
                InspectorStyleLibrary.DrawCopyableTailLabelLayout(
                    logging.CurrentLogFilePath,
                    InspectorStyleLibrary.Description,
                    true);
        }

        private void EnsureFixedFeatureName()
        {
            if (target == null || target.name == MatDataTransferFeature.FeatureName)
                return;

            target.name = MatDataTransferFeature.FeatureName;
            EditorUtility.SetDirty(target);
        }
    }

    [InitializeOnLoad]
    internal static class MatDataTransferCatalogAutoSync
    {
        private const string DefaultConfigFolderName = "Configs";
        private static readonly List<ShaderPropertyCatalog> s_Catalogs =
            new List<ShaderPropertyCatalog>();

        static MatDataTransferCatalogAutoSync()
        {
            MatDataTransferFeature.EditorCatalogSyncRequested -= SyncCatalogsFromConfigFolder;
            MatDataTransferFeature.EditorCatalogSyncRequested += SyncCatalogsFromConfigFolder;
        }

        internal static void SyncCatalogsFromConfigFolder(MatDataTransferFeature feature)
        {
            if (feature == null)
                return;

            string folderPath = FindFeatureConfigFolderPath(feature);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return;

            CollectCatalogs(folderPath, s_Catalogs);
            if (feature.MergeCatalogsFromEditor(s_Catalogs))
                EditorUtility.SetDirty(feature);
        }

        private static void CollectCatalogs(string folderPath, List<ShaderPropertyCatalog> catalogs)
        {
            catalogs.Clear();
            string[] catalogGuids = AssetDatabase.FindAssets("t:ShaderPropertyCatalog", new[] { folderPath });
            if (catalogGuids == null || catalogGuids.Length == 0)
                return;

            for (int i = 0; i < catalogGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(catalogGuids[i]);
                ShaderPropertyCatalog catalog = AssetDatabase.LoadAssetAtPath<ShaderPropertyCatalog>(assetPath);
                if (catalog != null)
                    catalogs.Add(catalog);
            }
        }

        private static string FindFeatureConfigFolderPath(MatDataTransferFeature feature)
        {
            string rootPath = FindFeatureRootPath(feature);
            if (string.IsNullOrEmpty(rootPath))
                return string.Empty;

            return rootPath + "/" + DefaultConfigFolderName;
        }

        private static string FindFeatureRootPath(MatDataTransferFeature feature)
        {
            MonoScript script = MonoScript.FromScriptableObject(feature);
            string scriptPath = script != null ? AssetDatabase.GetAssetPath(script) : string.Empty;
            return FindAncestorFolder(scriptPath, nameof(MatDataTransferFeature));
        }

        private static string FindAncestorFolder(string assetPath, string folderName)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(folderName))
                return string.Empty;

            string path = assetPath.Replace('\\', '/');
            int slashIndex = path.LastIndexOf('/');
            if (slashIndex < 0)
                return string.Empty;

            path = path.Substring(0, slashIndex);
            while (!string.IsNullOrEmpty(path))
            {
                if (PathEndsWithFolder(path, folderName))
                    return path;

                slashIndex = path.LastIndexOf('/');
                if (slashIndex < 0)
                    break;

                path = path.Substring(0, slashIndex);
            }

            return string.Empty;
        }

        private static bool PathEndsWithFolder(string path, string folderName)
        {
            int slashIndex = path.LastIndexOf('/');
            string currentFolder = slashIndex >= 0
                ? path.Substring(slashIndex + 1)
                : path;

            return string.Equals(currentFolder, folderName, System.StringComparison.Ordinal);
        }
    }
}
