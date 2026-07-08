using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Rendering.MatDataTransfer.Runtime;

namespace Rendering.MatDataTransfer.Editor
{
    public sealed class MatDataTransferBindingEditor : EditorWindow
    {
        private const float SidebarWidth = 360f;
        private const float RowHeight = 24f;
        private const float DeleteButtonWidth = 24f;
        private const string DefaultConfigFolderName = "Configs";
        private const string CatalogsPropertyName = "m_Catalogs";

        private MatDataTransferFeature m_Feature;
        private ShaderPropertyCatalog m_Catalog;
        private Shader m_Shader;
        private SerializedObject m_CatalogObject;
        private Vector2 m_ListScroll;
        private Vector2 m_DetailScroll;
        private int m_SelectedIndex = -1;
        private GUIStyle m_RowLabelStyle;

        [MenuItem("TA/角色模型工具/材质传输系统/MatDataTransfer Binding Editor")]
        private static void Open()
        {
            MatDataTransferBindingEditor window = GetWindow<MatDataTransferBindingEditor>("Shader Property Catalog");
            window.InitializeWindow();
        }

        public static void Open(MatDataTransferFeature feature)
        {
            MatDataTransferBindingEditor window = GetWindow<MatDataTransferBindingEditor>("Shader Property Catalog");
            window.InitializeWindow();
            window.SetFeature(feature);
        }

        private void InitializeWindow()
        {
            minSize = new Vector2(760f, 420f);
            if (m_Feature == null)
                SetFeature(Selection.activeObject as MatDataTransferFeature);
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawActions();
            EditorGUILayout.Space(8);

            RefreshSerializedObject();
            DrawCatalogEditor();
            ApplySerializedObject();
        }

        private void DrawHeader()
        {
            InspectorStyleLibrary.DrawTitle("Shader Property Catalog Editor");
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Feature", InspectorStyleLibrary.ParameterName, GUILayout.Width(60));
                MatDataTransferFeature selectedFeature = (MatDataTransferFeature)EditorGUILayout.ObjectField(
                    m_Feature,
                    typeof(MatDataTransferFeature),
                    false);
                SetFeature(selectedFeature);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Shader", InspectorStyleLibrary.ParameterName, GUILayout.Width(60));
                m_Shader = (Shader)EditorGUILayout.ObjectField(
                    m_Shader,
                    typeof(Shader),
                    false);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Catalog", InspectorStyleLibrary.ParameterName, GUILayout.Width(60));
                ShaderPropertyCatalog selectedCatalog = (ShaderPropertyCatalog)EditorGUILayout.ObjectField(
                    m_Catalog,
                    typeof(ShaderPropertyCatalog),
                    false);
                SetCatalog(selectedCatalog);

                if (m_Catalog != null && GUILayout.Button("Create New", GUILayout.Width(100)))
                {
                    AssignCreatedCatalog();
                }
                else if (m_Catalog == null && GUILayout.Button("Create Catalog", GUILayout.Width(120)))
                {
                    AssignCreatedCatalog();
                }
            }
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(m_Catalog == null || m_Shader == null))
                {
                    if (GUILayout.Button("1. Sync from Shader", GUILayout.Height(28)))
                        SelectAndSyncShader();
                }

                using (new EditorGUI.DisabledScope(m_Catalog == null))
                {
                    if (GUILayout.Button("2. Export to Material Config", GUILayout.Height(28)))
                        ExportToConfig();
                }
            }
        }

        private void DrawCatalogEditor()
        {
            if (m_CatalogObject == null)
            {
                EditorGUILayout.HelpBox("Create or assign a Shader Property Catalog to begin.", MessageType.Info);
                return;
            }

            SerializedProperty properties = m_CatalogObject.FindProperty("properties");
            if (properties == null)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawListPane(properties);
                DrawDetailPane(properties);
            }
        }

        private void DrawListPane(SerializedProperty list)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(SidebarWidth)))
            {
                DrawListHeader(list);
                m_ListScroll = EditorGUILayout.BeginScrollView(m_ListScroll);
                for (int i = 0; i < list.arraySize; i++)
                    DrawListRow(list, i);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawListHeader(SerializedProperty list)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField($"Properties ({list.arraySize})", InspectorStyleLibrary.Title);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear Missing", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                    ClearMissingProperties(list);
            }
        }

        private void DrawListRow(SerializedProperty list, int index)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(index);
            Rect rowRect = EditorGUILayout.GetControlRect(false, RowHeight);
            bool selected = m_SelectedIndex == index;

            if (Event.current.type == EventType.Repaint)
                DrawRowBackground(rowRect, selected, element);

            Rect labelRect = new Rect(rowRect.x + 6f, rowRect.y, rowRect.width - DeleteButtonWidth - 8f, rowRect.height);
            GUI.Label(labelRect, BuildRowLabel(element), GetRowLabelStyle(selected));

            Rect deleteRect = new Rect(rowRect.xMax - DeleteButtonWidth, rowRect.y + 2f, DeleteButtonWidth - 4f, RowHeight - 4f);
            if (GUI.Button(deleteRect, "×", EditorStyles.miniButton))
            {
                list.DeleteArrayElementAtIndex(index);
                if (m_SelectedIndex == index)
                    m_SelectedIndex = -1;
                ApplySerializedObject();
                GUIUtility.ExitGUI();
            }

            Rect selectRect = new Rect(rowRect.x, rowRect.y, rowRect.width - DeleteButtonWidth, rowRect.height);
            HandleRowSelection(selectRect, index);
        }

        private void DrawRowBackground(Rect rect, bool selected, SerializedProperty element)
        {
            Color color = selected
                ? new Color(0.24f, 0.48f, 0.9f, 0.75f)
                : GetRowBackgroundColor(element);
            EditorGUI.DrawRect(rect, color);
        }

        private Color GetRowBackgroundColor(SerializedProperty element)
        {
            SerializedProperty status = element.FindPropertyRelative("Status");
            if (status != null)
            {
                CatalogPropertyStatus statusValue = (CatalogPropertyStatus)status.enumValueIndex;
                switch (statusValue)
                {
                    case CatalogPropertyStatus.Missing:
                        return new Color(0.72f, 0.18f, 0.16f, 0.4f);
                    case CatalogPropertyStatus.New:
                        return new Color(0.95f, 0.62f, 0.12f, 0.35f);
                    case CatalogPropertyStatus.Ok:
                        return new Color(0.13f, 0.52f, 0.28f, 0.23f);
                }
            }

            return new Color(1f, 1f, 1f, 0.03f);
        }

        private GUIStyle GetRowLabelStyle(bool selected)
        {
            if (m_RowLabelStyle == null)
            {
                m_RowLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip
                };
            }

            m_RowLabelStyle.normal.textColor = selected
                ? Color.white
                : EditorStyles.label.normal.textColor;
            return m_RowLabelStyle;
        }

        private string BuildRowLabel(SerializedProperty element)
        {
            SerializedProperty propertyInfo = element.FindPropertyRelative("PropertyInfo");
            SerializedProperty semanticKey = element.FindPropertyRelative("SuggestedSemanticKey");
            SerializedProperty status = element.FindPropertyRelative("Status");

            string displayName = GetString(propertyInfo, "InspectorDisplayName");
            string key = semanticKey != null ? semanticKey.stringValue : "";
            string statusStr = status != null ? GetStatusIcon(status.enumValueIndex) : "";

            return $"{statusStr} {displayName}  →  {key}";
        }

        private string GetStatusIcon(int statusIndex)
        {
            switch ((CatalogPropertyStatus)statusIndex)
            {
                case CatalogPropertyStatus.Ok: return "✓";
                case CatalogPropertyStatus.New: return "★";
                case CatalogPropertyStatus.Missing: return "✗";
                default: return "";
            }
        }

        private void HandleRowSelection(Rect rowRect, int index)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !rowRect.Contains(current.mousePosition))
                return;

            m_SelectedIndex = index;
            current.Use();
        }

        private void DrawDetailPane(SerializedProperty list)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DrawDetailHeader();
                if (m_SelectedIndex < 0 || m_SelectedIndex >= list.arraySize)
                {
                    EditorGUILayout.HelpBox("Select a property to edit its semantic key.", MessageType.Info);
                    return;
                }

                m_DetailScroll = EditorGUILayout.BeginScrollView(m_DetailScroll);
                DrawPropertyDetail(list.GetArrayElementAtIndex(m_SelectedIndex));
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawDetailHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("Property Details", InspectorStyleLibrary.Title);
            }
        }

        private void DrawPropertyDetail(SerializedProperty property)
        {
            SerializedProperty propertyInfo = property.FindPropertyRelative("PropertyInfo");
            SerializedProperty semanticKey = property.FindPropertyRelative("SuggestedSemanticKey");
            SerializedProperty status = property.FindPropertyRelative("Status");

            EditorGUILayout.Space(8);
            InspectorStyleLibrary.DrawCopyableParameterValue(
                "Display Name",
                GetString(propertyInfo, "InspectorDisplayName"));
            InspectorStyleLibrary.DrawCopyableParameterValue("Property Name", GetString(propertyInfo, "PropertyName"));
            DrawReadOnlyEnum(propertyInfo, "ValueType", "Value Type");
            DrawReadOnlyEnum(property, "Status", "Status");

            EditorGUILayout.Space(12);
            EditorGUILayout.PropertyField(semanticKey, new GUIContent("Semantic Key"));
            EditorGUILayout.HelpBox(
                "Semantic Key is the unique identifier used by business code to reference this property.",
                MessageType.None);
        }

        private void ClearMissingProperties(SerializedProperty list)
        {
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                SerializedProperty status = element.FindPropertyRelative("Status");
                if (status != null && status.enumValueIndex == (int)CatalogPropertyStatus.Missing)
                {
                    list.DeleteArrayElementAtIndex(i);
                    if (m_SelectedIndex == i)
                        m_SelectedIndex = -1;
                }
            }

            ApplySerializedObject();
        }

        private void SelectAndSyncShader()
        {
            if (m_Catalog == null)
            {
                return;
            }

            if (m_Shader == null)
            {
                return;
            }

            ShaderPropertyCatalogBuilder.SyncCatalog(m_Catalog, m_Shader);
            m_CatalogObject = null;
            EditorUtility.SetDirty(m_Catalog);
            AssetDatabase.SaveAssets();
        }

        private void ExportToConfig()
        {
            if (m_Catalog == null)
            {
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Export Material Param Config",
                BuildDefaultConfigAssetName(),
                "asset",
                "Select save path for Material Param Config",
                EnsureDefaultConfigFolder());

            if (string.IsNullOrEmpty(path))
                return;

            MaterialParamConfig config = ScriptableObject.CreateInstance<MaterialParamConfig>();
            List<MaterialParameter> parameters = BuildMaterialParameters();
            config.SetDefaultShaderName(GetCatalogShaderName());
            config.SetParameters(parameters);

            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(config);
        }

        private List<MaterialParameter> BuildMaterialParameters()
        {
            List<MaterialParameter> parameters = new List<MaterialParameter>();
            if (m_Catalog == null || m_Catalog.Properties == null)
                return parameters;

            foreach (var catalogProp in m_Catalog.Properties)
            {
                if (catalogProp == null || catalogProp.Status != CatalogPropertyStatus.Ok)
                    continue;
                if (catalogProp.PropertyInfo == null)
                    continue;

                MaterialParameter param = new MaterialParameter(
                    catalogProp.SuggestedSemanticKey,
                    catalogProp.PropertyInfo.ValueType,
                    catalogProp.PropertyInfo.DefaultValue);

                parameters.Add(param);
            }

            return parameters;
        }

        private string GetCatalogShaderName()
        {
            if (m_Catalog == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(m_Catalog.ShaderName))
                return m_Catalog.ShaderName;

            return m_Catalog.Shader != null ? m_Catalog.Shader.name : string.Empty;
        }

        private void RefreshSerializedObject()
        {
            if (m_Catalog == null)
            {
                m_CatalogObject = null;
                return;
            }

            if (m_CatalogObject == null || m_CatalogObject.targetObject != m_Catalog)
                m_CatalogObject = new SerializedObject(m_Catalog);

            m_CatalogObject.Update();
        }

        private void SetCatalog(ShaderPropertyCatalog catalog)
        {
            if (m_Catalog == catalog)
                return;

            m_Catalog = catalog;
            ResetCatalogEditorState();
            m_Shader = m_Catalog != null ? m_Catalog.Shader : null;
        }

        private void SetFeature(MatDataTransferFeature feature)
        {
            if (m_Feature == feature)
                return;

            m_Feature = feature;
        }

        private string BuildDefaultConfigAssetName()
        {
            string shaderFileName = SanitizeAssetFileName(GetSelectedShaderName());
            return string.IsNullOrEmpty(shaderFileName)
                ? "MaterialParamConfig"
                : shaderFileName + "_MaterialParamConfig";
        }

        private void AssignCreatedCatalog()
        {
            ShaderPropertyCatalog createdCatalog = CreateCatalog(GetSelectedShaderName());
            if (createdCatalog == null)
                return;

            m_Catalog = createdCatalog;
            AddCatalogToCurrentFeature(createdCatalog);
            ResetCatalogEditorState();
        }

        private void ResetCatalogEditorState()
        {
            m_CatalogObject = null;
            m_SelectedIndex = -1;
            m_ListScroll = Vector2.zero;
            m_DetailScroll = Vector2.zero;
        }

        private void ApplySerializedObject()
        {
            if (m_CatalogObject != null && m_CatalogObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(m_CatalogObject.targetObject);
            }
        }

        private static void DrawReadOnlyEnum(SerializedProperty property, string relativeName, string label)
        {
            SerializedProperty child = property.FindPropertyRelative(relativeName);
            string value = child != null && child.enumValueIndex >= 0 && child.enumValueIndex < child.enumDisplayNames.Length
                ? child.enumDisplayNames[child.enumValueIndex]
                : string.Empty;
            InspectorStyleLibrary.DrawParameterValue(label, value);
        }

        private static string GetString(SerializedProperty property, string name)
        {
            SerializedProperty child = property?.FindPropertyRelative(name);
            return child != null ? child.stringValue : string.Empty;
        }

        private string GetSelectedShaderName()
        {
            if (m_Shader != null)
                return m_Shader.name;

            return GetCatalogShaderName();
        }

        private ShaderPropertyCatalog CreateCatalog(string shaderName)
        {
            string defaultAssetName = BuildDefaultCatalogAssetName(shaderName);
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Shader Property Catalog",
                defaultAssetName,
                "asset",
                "Select save path",
                EnsureDefaultConfigFolder());

            if (string.IsNullOrEmpty(path))
                return null;

            ShaderPropertyCatalog catalog = ScriptableObject.CreateInstance<ShaderPropertyCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private void AddCatalogToCurrentFeature(ShaderPropertyCatalog catalog)
        {
            if (m_Feature == null || catalog == null)
                return;

            SerializedObject featureObject = new SerializedObject(m_Feature);
            SerializedProperty catalogs = featureObject.FindProperty(CatalogsPropertyName);
            if (catalogs == null || ContainsCatalog(catalogs, catalog))
                return;

            int index = catalogs.arraySize;
            catalogs.InsertArrayElementAtIndex(index);
            catalogs.GetArrayElementAtIndex(index).objectReferenceValue = catalog;

            featureObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(m_Feature);
            AssetDatabase.SaveAssets();
        }

        private static bool ContainsCatalog(SerializedProperty catalogs, ShaderPropertyCatalog catalog)
        {
            if (catalogs == null || catalog == null)
                return false;

            for (int i = 0; i < catalogs.arraySize; i++)
            {
                SerializedProperty item = catalogs.GetArrayElementAtIndex(i);
                if (item != null && item.objectReferenceValue == catalog)
                    return true;
            }

            return false;
        }

        private string EnsureDefaultConfigFolder()
        {
            string rootPath = FindFeatureRootPath();
            if (string.IsNullOrEmpty(rootPath) || !AssetDatabase.IsValidFolder(rootPath))
                return "Assets";

            string folderPath = rootPath + "/" + DefaultConfigFolderName;
            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder(rootPath, DefaultConfigFolderName);

            return AssetDatabase.IsValidFolder(folderPath)
                ? folderPath
                : "Assets";
        }

        private string FindFeatureRootPath()
        {
            MonoScript script = MonoScript.FromScriptableObject(this);
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

        private static string BuildDefaultCatalogAssetName(string shaderName)
        {
            string shaderFileName = SanitizeAssetFileName(shaderName);
            return string.IsNullOrEmpty(shaderFileName)
                ? "ShaderPropertyCatalog"
                : shaderFileName + "_ShaderPropertyCatalog";
        }

        private static string SanitizeAssetFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
            string fileName = value.Trim();
            for (int i = 0; i < invalidChars.Length; i++)
                fileName = fileName.Replace(invalidChars[i], '_');

            return fileName
                .Replace('/', '_')
                .Replace('\\', '_');
        }
    }
}
