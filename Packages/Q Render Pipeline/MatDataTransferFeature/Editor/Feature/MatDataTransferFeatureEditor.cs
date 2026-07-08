using UnityEditor;
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

        private const int RegistryIdMinDigits = 3;
        private const float RegistryColumnGap = 8f;
        private const float RegistryNameColumnMaxWidth = 180f;
        private static bool s_ShowInstances = true;
        private static bool s_ShowActiveInstances;
        private static bool s_ShowRequestProviders = true;
        private static bool s_ShowLogging;

        private SerializedProperty m_Catalogs;
        private SerializedProperty m_GenericProviderSettings;
        private SerializedProperty m_LoggingSettings;
        private SerializedProperty m_MaxInstanceCount;
        private readonly List<InstanceRegisterEntry> m_InstanceEntries =
            new List<InstanceRegisterEntry>();

        private void OnEnable()
        {
            EnsureFixedFeatureName();
            m_Catalogs = serializedObject.FindProperty(CatalogsPropertyName);
            m_GenericProviderSettings = serializedObject.FindProperty(GenericProviderSettingsPropertyName);
            m_LoggingSettings = serializedObject.FindProperty(LoggingSettingsPropertyName);
            m_MaxInstanceCount = serializedObject.FindProperty(MaxInstanceCountPropertyName);
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
            EditorGUILayout.PropertyField(m_Catalogs, true);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open Binding Editor", GUILayout.Width(160f)))
                    MatDataTransferBindingEditor.Open((MatDataTransferFeature)target);
            }
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
            for (int i = 0; i < m_InstanceEntries.Count; i++)
                width = Mathf.Max(width, style.CalcSize(new GUIContent(FormatRegistryId(m_InstanceEntries[i].Id))).x);

            return Mathf.Ceil(width);
        }

        private static void DrawRegistryEntry(InstanceRegisterEntry entry, float idColumnWidth)
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
            float desiredNameWidth = Mathf.Ceil(style.CalcSize(new GUIContent(displayName)).x);
            float nameWidth = Mathf.Min(RegistryNameColumnMaxWidth, desiredNameWidth, labelWidth);
            float instanceIdWidth = Mathf.Max(0f, labelWidth - nameWidth);
            Rect nameRect = new Rect(contentX, row.y, nameWidth, row.height);
            Rect instanceIdRect = new Rect(nameRect.xMax + contentGap, row.y, instanceIdWidth, row.height);

            GUI.Label(idRect, new GUIContent(idText, idText), style);
            InspectorStyleLibrary.DrawCopyableTailLabel(nameRect, displayName, style, false);
            InspectorStyleLibrary.DrawCopyableTailLabel(instanceIdRect, instanceId, style, false);
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
}
