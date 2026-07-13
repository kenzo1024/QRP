using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Rendering.MatDataTransfer.Runtime;

namespace Rendering.MatDataTransfer.Editor
{
    public sealed class MatDataTransferBindingEditor : EditorWindow
    {
        [Serializable]
        private sealed class ShaderWorkspace
        {
            public Shader Shader;
            public ShaderPropertyCatalog Catalog;
        }

        [Serializable]
        private sealed class PersistedWorkspace
        {
            public string ShaderId;
            public string CatalogId;
        }

        [Serializable]
        private sealed class PersistedState
        {
            public List<PersistedWorkspace> Workspaces = new List<PersistedWorkspace>();
            public int SelectedWorkspaceIndex = -1;
            public string SemanticKeyProfileId;
        }

        private const float DefaultShaderPaneWidth = 280f;
        private const float DefaultPropertiesPaneWidth = 420f;
        private const float MinShaderPaneWidth = 240f;
        private const float MinPropertiesPaneWidth = 300f;
        private const float MinDetailPaneWidth = 360f;
        private const float SplitterHitWidth = 8f;
        private const float SplitterLineWidth = 1f;
        private const float LayoutHorizontalPadding = 8f;
        private const float MinWindowHeight = 520f;
        private const float RowHeight = 24f;
        private const float DeleteButtonWidth = 24f;
        private const string DefaultConfigFolderName = "Configs";
        private const string StatePreferenceKeyPrefix =
            "Rendering.MatDataTransfer.BindingEditor.State.v1.";

        [SerializeField] private float m_ShaderPaneWidth = DefaultShaderPaneWidth;
        [SerializeField] private float m_PropertiesPaneWidth = DefaultPropertiesPaneWidth;
        [SerializeField] private List<ShaderWorkspace> m_Workspaces = new List<ShaderWorkspace>();
        [SerializeField] private int m_SelectedWorkspaceIndex = -1;
        [SerializeField] private MaterialSemanticKeyProfile m_SemanticKeyProfile;

        private SerializedObject m_CatalogObject;
        private Vector2 m_ShaderScroll;
        private Vector2 m_PropertyScroll;
        private Vector2 m_DetailScroll;
        private readonly HashSet<int> m_SelectedPropertyIndices = new HashSet<int>();
        private int m_SelectedPropertyAnchorIndex = -1;
        private GUIStyle m_RowLabelStyle;

        private float MinimumWindowWidth =>
            MinShaderPaneWidth +
            MinPropertiesPaneWidth +
            MinDetailPaneWidth +
            SplitterHitWidth * 2f +
            LayoutHorizontalPadding;

        private float AvailableColumnWidth =>
            Mathf.Max(MinimumWindowWidth, position.width) -
            SplitterHitWidth * 2f -
            LayoutHorizontalPadding;

        private ShaderWorkspace ActiveWorkspace =>
            m_SelectedWorkspaceIndex >= 0 && m_SelectedWorkspaceIndex < m_Workspaces.Count
                ? m_Workspaces[m_SelectedWorkspaceIndex]
                : null;

        private Shader ActiveShader => ActiveWorkspace?.Shader;
        private ShaderPropertyCatalog ActiveCatalog => ActiveWorkspace?.Catalog;

        [MenuItem("TA/角色模型工具/材质传输系统/MatDataTransfer Binding Editor")]
        private static void Open()
        {
            MatDataTransferBindingEditor window = GetWindow<MatDataTransferBindingEditor>("Shader Property Catalog");
            window.InitializeWindow(true);
        }

        private void OnEnable()
        {
            RestoreState();
            InitializeWindow(false);
        }

        private void OnDisable()
        {
            ApplySerializedObject();
            SaveState();
        }

        private void InitializeWindow(bool resizeToMinimum)
        {
            EnsureWorkspaceState();
            minSize = new Vector2(MinimumWindowWidth, MinWindowHeight);
            ClampColumnWidths();

            if (!resizeToMinimum)
                return;

            Rect currentPosition = position;
            currentPosition.width = Mathf.Max(currentPosition.width, MinimumWindowWidth);
            currentPosition.height = Mathf.Max(currentPosition.height, MinWindowHeight);
            position = currentPosition;
        }

        private void OnGUI()
        {
            EnsureWorkspaceState();
            DrawHeader();
            EditorGUILayout.Space(6f);

            RefreshSerializedObject();
            DrawThreeColumnEditor();
            ApplySerializedObject();
        }

        private void DrawHeader()
        {
            InspectorStyleLibrary.DrawTitle("Shader Property Catalog Editor");
            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Profile", InspectorStyleLibrary.ParameterName, GUILayout.Width(52f));
                EditorGUI.BeginChangeCheck();
                MaterialSemanticKeyProfile semanticKeyProfile =
                    (MaterialSemanticKeyProfile)EditorGUILayout.ObjectField(
                        m_SemanticKeyProfile,
                        typeof(MaterialSemanticKeyProfile),
                        false);
                if (EditorGUI.EndChangeCheck())
                {
                    m_SemanticKeyProfile = semanticKeyProfile;
                    SaveState();
                }

                if (GUILayout.Button("Create Profile", GUILayout.Width(112f)))
                    AssignCreatedSemanticProfile();
            }
        }

        private void DrawThreeColumnEditor()
        {
            ClampColumnWidths();
            SerializedProperty properties = m_CatalogObject?.FindProperty("properties");
            float detailPaneWidth = AvailableColumnWidth -
                                    m_ShaderPaneWidth -
                                    m_PropertiesPaneWidth;

            using (new EditorGUILayout.HorizontalScope(
                       GUILayout.ExpandWidth(true),
                       GUILayout.ExpandHeight(true)))
            {
                DrawShaderPane();
                DrawSplitter(ColumnSplitter.Shaders);
                DrawPropertiesPane(properties);
                DrawSplitter(ColumnSplitter.Properties);
                DrawDetailPane(properties, detailPaneWidth);
            }
        }

        private void DrawShaderPane()
        {
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox,
                       GUILayout.Width(m_ShaderPaneWidth),
                       GUILayout.ExpandWidth(false),
                       GUILayout.ExpandHeight(true)))
            {
                DrawShaderPaneHeader();
                m_ShaderScroll = EditorGUILayout.BeginScrollView(m_ShaderScroll, GUILayout.ExpandHeight(true));
                for (int i = 0; i < m_Workspaces.Count; i++)
                    DrawShaderRow(i);
                EditorGUILayout.EndScrollView();

                DrawActiveWorkspaceEditor();
            }
        }

        private void DrawShaderPaneHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField($"Shaders ({m_Workspaces.Count})", InspectorStyleLibrary.Title);
                GUILayout.FlexibleSpace();

                GUIContent addContent = new GUIContent(
                    "+",
                    "Add selected Shader assets. Adds an empty workspace when no Shader is selected.");
                if (GUILayout.Button(addContent, EditorStyles.toolbarButton, GUILayout.Width(24f)))
                    AddSelectedShaders();

                using (new EditorGUI.DisabledScope(ActiveWorkspace == null))
                {
                    GUIContent removeContent = new GUIContent("-", "Remove the selected workspace.");
                    if (GUILayout.Button(removeContent, EditorStyles.toolbarButton, GUILayout.Width(24f)))
                        RemoveActiveWorkspace();
                }
            }
        }

        private void DrawShaderRow(int index)
        {
            ShaderWorkspace workspace = m_Workspaces[index];
            Rect rowRect = EditorGUILayout.GetControlRect(false, RowHeight);
            bool selected = m_SelectedWorkspaceIndex == index;

            if (Event.current.type == EventType.Repaint)
            {
                Color background = selected
                    ? new Color(0.24f, 0.48f, 0.9f, 0.75f)
                    : new Color(1f, 1f, 1f, index % 2 == 0 ? 0.035f : 0.065f);
                EditorGUI.DrawRect(rowRect, background);
            }

            Rect iconRect = new Rect(rowRect.x + 4f, rowRect.y + 3f, 18f, 18f);
            Texture icon = EditorGUIUtility.ObjectContent(workspace?.Shader, typeof(Shader)).image;
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

            Rect labelRect = new Rect(rowRect.x + 26f, rowRect.y, rowRect.width - 30f, rowRect.height);
            string label = BuildWorkspaceLabel(workspace, index);
            GUI.Label(labelRect, new GUIContent(label, label), GetRowLabelStyle(selected));

            HandleWorkspaceSelection(rowRect, index);
        }

        private static string BuildWorkspaceLabel(ShaderWorkspace workspace, int index)
        {
            if (workspace?.Shader != null)
                return workspace.Shader.name;
            if (workspace?.Catalog != null && !string.IsNullOrWhiteSpace(workspace.Catalog.ShaderName))
                return workspace.Catalog.ShaderName;
            return $"Shader {index + 1}";
        }

        private void HandleWorkspaceSelection(Rect rowRect, int index)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !rowRect.Contains(current.mousePosition))
                return;

            SelectWorkspace(index);
            current.Use();
        }

        private void DrawActiveWorkspaceEditor()
        {
            EditorGUILayout.Space(4f);
            if (ActiveWorkspace == null)
            {
                EditorGUILayout.HelpBox("Add or select a Shader workspace.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Selected Shader", InspectorStyleLibrary.Title);
            EditorGUI.BeginChangeCheck();
            Shader shader = (Shader)EditorGUILayout.ObjectField(
                "Shader",
                ActiveShader,
                typeof(Shader),
                false);
            if (EditorGUI.EndChangeCheck())
                SetActiveShader(shader);

            EditorGUI.BeginChangeCheck();
            ShaderPropertyCatalog catalog = (ShaderPropertyCatalog)EditorGUILayout.ObjectField(
                "Catalog",
                ActiveCatalog,
                typeof(ShaderPropertyCatalog),
                false);
            if (EditorGUI.EndChangeCheck())
                SetActiveCatalog(catalog);

            DrawWorkspaceMismatchWarning();
            DrawWorkspaceActions();
        }

        private void DrawWorkspaceMismatchWarning()
        {
            if (ActiveShader == null || ActiveCatalog == null || ActiveCatalog.Shader == null)
                return;
            if (ActiveCatalog.Shader == ActiveShader)
                return;

            EditorGUILayout.HelpBox(
                "The Catalog belongs to another Shader. Syncing will rebind it to the selected Shader.",
                MessageType.Warning);
        }

        private void DrawWorkspaceActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Catalog", GUILayout.Height(24f)))
                    AssignCreatedCatalog();

                using (new EditorGUI.DisabledScope(ActiveCatalog == null || ActiveShader == null))
                {
                    if (GUILayout.Button("Sync", GUILayout.Width(64f), GUILayout.Height(24f)))
                        SyncActiveWorkspace();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!HasSyncableWorkspace()))
                {
                    if (GUILayout.Button("Sync All", GUILayout.Height(24f)))
                        SyncAllWorkspaces();
                }

                using (new EditorGUI.DisabledScope(ActiveCatalog == null))
                {
                    if (GUILayout.Button("Export Config", GUILayout.Height(24f)))
                        ExportActiveConfig();
                }
            }
        }

        private void DrawPropertiesPane(SerializedProperty properties)
        {
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox,
                       GUILayout.Width(m_PropertiesPaneWidth),
                       GUILayout.ExpandWidth(false),
                       GUILayout.ExpandHeight(true)))
            {
                DrawPropertiesHeader(properties);
                if (properties == null)
                {
                    EditorGUILayout.HelpBox("Assign or create a Catalog for the selected Shader.", MessageType.Info);
                    return;
                }

                m_PropertyScroll = EditorGUILayout.BeginScrollView(m_PropertyScroll);
                for (int i = 0; i < properties.arraySize; i++)
                    DrawPropertyRow(properties, i);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPropertiesHeader(SerializedProperty properties)
        {
            int count = properties?.arraySize ?? 0;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField($"Properties ({count})", InspectorStyleLibrary.Title);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(properties == null))
                {
                    if (GUILayout.Button("Clear Missing", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                        ClearMissingProperties(properties);
                }
            }
        }

        private void DrawPropertyRow(SerializedProperty list, int index)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(index);
            Rect rowRect = EditorGUILayout.GetControlRect(false, RowHeight);
            bool selected = m_SelectedPropertyIndices.Contains(index);

            if (Event.current.type == EventType.Repaint)
                DrawPropertyRowBackground(rowRect, selected, element);

            Rect labelRect = new Rect(
                rowRect.x + 6f,
                rowRect.y,
                rowRect.width - DeleteButtonWidth - 8f,
                rowRect.height);
            GUI.Label(labelRect, BuildPropertyRowLabel(element), GetRowLabelStyle(selected));

            Rect deleteRect = new Rect(
                rowRect.xMax - DeleteButtonWidth,
                rowRect.y + 2f,
                DeleteButtonWidth - 4f,
                RowHeight - 4f);
            if (GUI.Button(deleteRect, "×", EditorStyles.miniButton))
            {
                list.DeleteArrayElementAtIndex(index);
                RemovePropertySelectionIndex(index);

                ApplySerializedObject();
                GUIUtility.ExitGUI();
            }

            Rect selectRect = new Rect(rowRect.x, rowRect.y, rowRect.width - DeleteButtonWidth, rowRect.height);
            HandlePropertySelection(selectRect, index);
        }

        private static void DrawPropertyRowBackground(
            Rect rect,
            bool selected,
            SerializedProperty element)
        {
            Color color = selected
                ? new Color(0.24f, 0.48f, 0.9f, 0.75f)
                : GetPropertyRowBackgroundColor(element);
            EditorGUI.DrawRect(rect, color);
        }

        private static Color GetPropertyRowBackgroundColor(SerializedProperty element)
        {
            SerializedProperty status = element.FindPropertyRelative("Status");
            if (status == null)
                return new Color(1f, 1f, 1f, 0.03f);

            switch ((CatalogPropertyStatus)status.enumValueIndex)
            {
                case CatalogPropertyStatus.Missing:
                    return new Color(0.72f, 0.18f, 0.16f, 0.4f);
                case CatalogPropertyStatus.New:
                    return new Color(0.95f, 0.62f, 0.12f, 0.35f);
                case CatalogPropertyStatus.Ok:
                    return new Color(0.13f, 0.52f, 0.28f, 0.23f);
                default:
                    return new Color(1f, 1f, 1f, 0.03f);
            }
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

        private static string BuildPropertyRowLabel(SerializedProperty element)
        {
            SerializedProperty propertyInfo = element.FindPropertyRelative("PropertyInfo");
            SerializedProperty semanticKey = element.FindPropertyRelative("SuggestedSemanticKey");
            SerializedProperty status = element.FindPropertyRelative("Status");

            string displayName = GetString(propertyInfo, "InspectorDisplayName");
            string key = semanticKey != null ? semanticKey.stringValue : string.Empty;
            string statusText = status != null ? GetStatusIcon(status.enumValueIndex) : string.Empty;
            return $"{statusText} {displayName}  →  {key}";
        }

        private static string GetStatusIcon(int statusIndex)
        {
            switch ((CatalogPropertyStatus)statusIndex)
            {
                case CatalogPropertyStatus.Ok:
                    return "✓";
                case CatalogPropertyStatus.New:
                    return "★";
                case CatalogPropertyStatus.Missing:
                    return "✗";
                default:
                    return string.Empty;
            }
        }

        private void HandlePropertySelection(Rect rowRect, int index)
        {
            Event current = Event.current;
            if (!rowRect.Contains(current.mousePosition))
                return;

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                SelectProperty(index, current.shift, current.control || current.command);
                current.Use();
                return;
            }

            if (current.type != EventType.ContextClick)
                return;

            if (!m_SelectedPropertyIndices.Contains(index))
                SelectProperty(index, false, false);

            ShowPropertyContextMenu();
            current.Use();
        }

        private void SelectProperty(int index, bool selectRange, bool toggle)
        {
            if (selectRange && m_SelectedPropertyAnchorIndex >= 0)
            {
                if (!toggle)
                    m_SelectedPropertyIndices.Clear();

                int first = Mathf.Min(m_SelectedPropertyAnchorIndex, index);
                int last = Mathf.Max(m_SelectedPropertyAnchorIndex, index);
                for (int i = first; i <= last; i++)
                    m_SelectedPropertyIndices.Add(i);
                return;
            }

            if (toggle)
            {
                if (!m_SelectedPropertyIndices.Add(index))
                    m_SelectedPropertyIndices.Remove(index);
            }
            else
            {
                m_SelectedPropertyIndices.Clear();
                m_SelectedPropertyIndices.Add(index);
            }

            m_SelectedPropertyAnchorIndex = index;
        }

        private void RemovePropertySelectionIndex(int removedIndex)
        {
            List<int> adjustedIndices = new List<int>();
            foreach (int selectedIndex in m_SelectedPropertyIndices)
            {
                if (selectedIndex == removedIndex)
                    continue;

                adjustedIndices.Add(selectedIndex > removedIndex
                    ? selectedIndex - 1
                    : selectedIndex);
            }

            m_SelectedPropertyIndices.Clear();
            for (int i = 0; i < adjustedIndices.Count; i++)
                m_SelectedPropertyIndices.Add(adjustedIndices[i]);

            if (m_SelectedPropertyAnchorIndex == removedIndex)
                m_SelectedPropertyAnchorIndex = -1;
            else if (m_SelectedPropertyAnchorIndex > removedIndex)
                m_SelectedPropertyAnchorIndex--;
        }

        private void PrunePropertySelection(int propertyCount)
        {
            m_SelectedPropertyIndices.RemoveWhere(index => index < 0 || index >= propertyCount);
            if (m_SelectedPropertyAnchorIndex < 0 || m_SelectedPropertyAnchorIndex >= propertyCount)
                m_SelectedPropertyAnchorIndex = -1;
        }

        private List<int> GetSortedPropertySelection()
        {
            List<int> selectedIndices = new List<int>(m_SelectedPropertyIndices);
            selectedIndices.Sort();
            return selectedIndices;
        }

        private void ShowPropertyContextMenu()
        {
            GenericMenu menu = new GenericMenu();
            GUIContent addContent = new GUIContent("Add Selected Properties to Key Profile");
            if (m_SemanticKeyProfile != null && m_SelectedPropertyIndices.Count > 0)
                menu.AddItem(addContent, false, AddSelectedPropertiesToKeyProfile);
            else
                menu.AddDisabledItem(addContent);
            menu.ShowAsContext();
        }

        private void DrawDetailPane(SerializedProperty properties, float width)
        {
            PrunePropertySelection(properties?.arraySize ?? 0);
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox,
                       GUILayout.Width(width),
                       GUILayout.ExpandWidth(false),
                       GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    string title = m_SelectedPropertyIndices.Count > 1
                        ? $"Property Details ({m_SelectedPropertyIndices.Count})"
                        : "Property Details";
                    EditorGUILayout.LabelField(title, InspectorStyleLibrary.Title);
                }

                if (properties == null || m_SelectedPropertyIndices.Count == 0)
                {
                    EditorGUILayout.HelpBox("Select a property to edit its semantic key.", MessageType.Info);
                    return;
                }

                m_DetailScroll = EditorGUILayout.BeginScrollView(m_DetailScroll);
                List<int> selectedIndices = GetSortedPropertySelection();
                for (int i = 0; i < selectedIndices.Count; i++)
                {
                    DrawPropertyDetailElement(
                        properties.GetArrayElementAtIndex(selectedIndices[i]),
                        selectedIndices[i]);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPropertyDetailElement(SerializedProperty property, int index)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SerializedProperty propertyInfo = property.FindPropertyRelative("PropertyInfo");
                string propertyName = GetString(propertyInfo, "PropertyName");
                string displayName = GetString(propertyInfo, "InspectorDisplayName");
                EditorGUILayout.LabelField(
                    $"{index + 1}. {displayName} ({propertyName})",
                    InspectorStyleLibrary.Title);
                DrawPropertyDetail(property);
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawPropertyDetail(SerializedProperty property)
        {
            SerializedProperty propertyInfo = property.FindPropertyRelative("PropertyInfo");
            SerializedProperty semanticKey = property.FindPropertyRelative("SuggestedSemanticKey");

            EditorGUILayout.Space(4f);
            InspectorStyleLibrary.DrawCopyableParameterValue(
                "Display Name",
                GetString(propertyInfo, "InspectorDisplayName"));
            InspectorStyleLibrary.DrawCopyableParameterValue(
                "Property Name",
                GetString(propertyInfo, "PropertyName"));
            DrawReadOnlyEnum(propertyInfo, "ValueType", "Value Type");
            DrawReadOnlyEnum(property, "Status", "Status");

            EditorGUILayout.Space(12f);
            EditorGUILayout.PropertyField(semanticKey, new GUIContent("Semantic Key"));
            EditorGUILayout.HelpBox(
                "Semantic Key is the unique identifier used by business code to reference this property.",
                MessageType.None);

            DrawSemanticProfilePreview(propertyInfo);
        }

        private void AddSelectedPropertiesToKeyProfile()
        {
            if (m_SemanticKeyProfile == null || ActiveCatalog == null)
                return;

            ApplySerializedObject();
            List<int> selectedIndices = GetSortedPropertySelection();
            IReadOnlyList<CatalogProperty> properties = ActiveCatalog.Properties;
            string shaderName = GetSelectedShaderName();

            Undo.RecordObject(m_SemanticKeyProfile, "Add Properties to Semantic Key Profile");
            SerializedObject profileObject = new SerializedObject(m_SemanticKeyProfile);
            profileObject.Update();
            SerializedProperty rules = profileObject.FindProperty("rules");
            if (rules == null)
                return;

            int addedPropertyCount = 0;
            int createdRuleCount = 0;
            int skippedPropertyCount = 0;
            for (int i = 0; i < selectedIndices.Count; i++)
            {
                int selectedIndex = selectedIndices[i];
                if (selectedIndex < 0 || selectedIndex >= properties.Count)
                {
                    skippedPropertyCount++;
                    continue;
                }

                CatalogProperty property = properties[selectedIndex];
                if (!TryAddPropertyToProfileRule(
                        rules,
                        property,
                        shaderName,
                        out bool createdRule))
                {
                    skippedPropertyCount++;
                    continue;
                }

                addedPropertyCount++;
                if (createdRule)
                    createdRuleCount++;
            }

            if (profileObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(m_SemanticKeyProfile);
                AssetDatabase.SaveAssets();
            }

            ShowNotification(new GUIContent(
                $"Profile updated: {addedPropertyCount} added, {createdRuleCount} rules created, {skippedPropertyCount} skipped."));
        }

        private static bool TryAddPropertyToProfileRule(
            SerializedProperty rules,
            CatalogProperty property,
            string shaderName,
            out bool createdRule)
        {
            createdRule = false;
            if (property?.PropertyInfo == null ||
                string.IsNullOrWhiteSpace(property.PropertyInfo.PropertyName) ||
                string.IsNullOrWhiteSpace(property.SuggestedSemanticKey))
            {
                return false;
            }

            string semanticKey = property.SuggestedSemanticKey.Trim();
            string propertyName = property.PropertyInfo.PropertyName.Trim();
            int ruleIndex = FindCompatibleProfileRule(
                rules,
                semanticKey,
                property.PropertyInfo.ValueType,
                shaderName);

            if (ruleIndex < 0)
            {
                ruleIndex = CreateProfileRule(
                    rules,
                    semanticKey,
                    property.PropertyInfo.ValueType,
                    shaderName);
                createdRule = true;
            }

            SerializedProperty propertyNames = rules
                .GetArrayElementAtIndex(ruleIndex)
                .FindPropertyRelative("PropertyNames");
            if (ContainsString(propertyNames, propertyName))
                return false;

            int newIndex = propertyNames.arraySize;
            propertyNames.InsertArrayElementAtIndex(newIndex);
            propertyNames.GetArrayElementAtIndex(newIndex).stringValue = propertyName;
            return true;
        }

        private static int FindCompatibleProfileRule(
            SerializedProperty rules,
            string semanticKey,
            ParamValueType valueType,
            string shaderName)
        {
            for (int i = 0; i < rules.arraySize; i++)
            {
                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                if (!GetBool(rule, "Enabled"))
                    continue;
                if (!string.Equals(GetString(rule, "SemanticKey").Trim(), semanticKey, StringComparison.Ordinal))
                    continue;
                if (GetEnumValue(rule, "ValueType") != (int)valueType)
                    continue;
                if (!RuleIncludesShader(rule, shaderName))
                    continue;

                return i;
            }

            return -1;
        }

        private static bool RuleIncludesShader(SerializedProperty rule, string shaderName)
        {
            int matchMode = GetEnumValue(rule, "ShaderMatchMode");
            SerializedProperty excludedShaders = rule.FindPropertyRelative("ExcludeShaders");
            if (ShaderListMatches(excludedShaders, shaderName, matchMode))
                return false;

            SerializedProperty includedShaders = rule.FindPropertyRelative("IncludeShaders");
            if (!HasNonEmptyString(includedShaders))
                return true;

            return ShaderListMatches(includedShaders, shaderName, matchMode);
        }

        private static bool ShaderListMatches(
            SerializedProperty shaderNames,
            string shaderName,
            int matchMode)
        {
            if (shaderNames == null || string.IsNullOrWhiteSpace(shaderName))
                return false;

            for (int i = 0; i < shaderNames.arraySize; i++)
            {
                string pattern = shaderNames.GetArrayElementAtIndex(i).stringValue;
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                bool matches = matchMode == (int)MaterialSemanticShaderMatchMode.Prefix
                    ? shaderName.StartsWith(pattern, StringComparison.Ordinal)
                    : string.Equals(shaderName, pattern, StringComparison.Ordinal);
                if (matches)
                    return true;
            }

            return false;
        }

        private static int CreateProfileRule(
            SerializedProperty rules,
            string semanticKey,
            ParamValueType valueType,
            string shaderName)
        {
            int index = rules.arraySize;
            rules.InsertArrayElementAtIndex(index);
            SerializedProperty rule = rules.GetArrayElementAtIndex(index);
            rule.FindPropertyRelative("SemanticKey").stringValue = semanticKey;
            rule.FindPropertyRelative("PropertyNames").ClearArray();
            rule.FindPropertyRelative("ValueType").enumValueIndex = (int)valueType;
            SerializedProperty includedShaders = rule.FindPropertyRelative("IncludeShaders");
            includedShaders.ClearArray();
            if (!string.IsNullOrWhiteSpace(shaderName))
            {
                includedShaders.InsertArrayElementAtIndex(0);
                includedShaders.GetArrayElementAtIndex(0).stringValue = shaderName.Trim();
            }
            rule.FindPropertyRelative("ExcludeShaders").ClearArray();
            rule.FindPropertyRelative("ShaderMatchMode").enumValueIndex =
                (int)MaterialSemanticShaderMatchMode.Exact;
            rule.FindPropertyRelative("Priority").intValue = 0;
            rule.FindPropertyRelative("Enabled").boolValue = true;
            rule.FindPropertyRelative("Description").stringValue = string.Empty;
            return index;
        }

        private static bool ContainsString(SerializedProperty list, string expected)
        {
            if (list == null || string.IsNullOrWhiteSpace(expected))
                return false;

            string normalizedExpected = expected.Trim();
            for (int i = 0; i < list.arraySize; i++)
            {
                string value = list.GetArrayElementAtIndex(i).stringValue;
                if (string.Equals(value?.Trim(), normalizedExpected, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool HasNonEmptyString(SerializedProperty list)
        {
            if (list == null)
                return false;

            for (int i = 0; i < list.arraySize; i++)
            {
                if (!string.IsNullOrWhiteSpace(list.GetArrayElementAtIndex(i).stringValue))
                    return true;
            }

            return false;
        }

        private static bool GetBool(SerializedProperty property, string relativeName)
        {
            SerializedProperty child = property?.FindPropertyRelative(relativeName);
            return child != null && child.boolValue;
        }

        private static int GetEnumValue(SerializedProperty property, string relativeName)
        {
            SerializedProperty child = property?.FindPropertyRelative(relativeName);
            return child != null ? child.enumValueIndex : -1;
        }

        private enum ColumnSplitter
        {
            Shaders,
            Properties
        }

        private void DrawSplitter(ColumnSplitter splitter)
        {
            Rect rect = GUILayoutUtility.GetRect(
                SplitterHitWidth,
                SplitterHitWidth,
                GUILayout.ExpandWidth(false),
                GUILayout.ExpandHeight(true));

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event current = Event.current;
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            DrawSplitterLine(rect, GUIUtility.hotControl == controlId);

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && rect.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = controlId;
                        current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        ResizeColumn(splitter, current.delta.x);
                        Repaint();
                        current.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                    }
                    break;
            }
        }

        private void ResizeColumn(ColumnSplitter splitter, float delta)
        {
            if (splitter == ColumnSplitter.Shaders)
                m_ShaderPaneWidth += delta;
            else
                m_PropertiesPaneWidth += delta;

            ClampColumnWidths();
        }

        private static void DrawSplitterLine(Rect rect, bool active)
        {
            Color color = active
                ? new Color(0.92f, 0.42f, 0.22f, 1f)
                : new Color(0.18f, 0.18f, 0.18f, 1f);
            float x = Mathf.Round(rect.center.x - SplitterLineWidth * 0.5f);
            EditorGUI.DrawRect(new Rect(x, rect.y, SplitterLineWidth, rect.height), color);
        }

        private void ClampColumnWidths()
        {
            float maxShaderWidth = AvailableColumnWidth - MinPropertiesPaneWidth - MinDetailPaneWidth;
            m_ShaderPaneWidth = Mathf.Clamp(m_ShaderPaneWidth, MinShaderPaneWidth, maxShaderWidth);

            float maxPropertiesWidth = AvailableColumnWidth - m_ShaderPaneWidth - MinDetailPaneWidth;
            m_PropertiesPaneWidth = Mathf.Clamp(
                m_PropertiesPaneWidth,
                MinPropertiesPaneWidth,
                maxPropertiesWidth);
        }

        private void AddSelectedShaders()
        {
            bool added = false;
            UnityEngine.Object[] selectedObjects = Selection.objects;
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                Shader shader = selectedObjects[i] as Shader;
                if (shader == null)
                    continue;

                int existingIndex = FindWorkspace(shader);
                if (existingIndex >= 0)
                {
                    SelectWorkspace(existingIndex);
                    continue;
                }

                m_Workspaces.Add(new ShaderWorkspace { Shader = shader });
                SelectWorkspace(m_Workspaces.Count - 1);
                added = true;
            }

            if (!added && !HasSelectedShaderAsset())
            {
                m_Workspaces.Add(new ShaderWorkspace());
                SelectWorkspace(m_Workspaces.Count - 1);
            }
        }

        private static bool HasSelectedShaderAsset()
        {
            UnityEngine.Object[] selectedObjects = Selection.objects;
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                if (selectedObjects[i] is Shader)
                    return true;
            }

            return false;
        }

        private int FindWorkspace(Shader shader)
        {
            for (int i = 0; i < m_Workspaces.Count; i++)
            {
                if (m_Workspaces[i]?.Shader == shader)
                    return i;
            }

            return -1;
        }

        private void RemoveActiveWorkspace()
        {
            if (ActiveWorkspace == null)
                return;

            ApplySerializedObject();
            int removedIndex = m_SelectedWorkspaceIndex;
            m_Workspaces.RemoveAt(m_SelectedWorkspaceIndex);
            m_SelectedWorkspaceIndex = Mathf.Min(removedIndex, m_Workspaces.Count - 1);
            ResetCatalogEditorState();
            SaveState();
        }

        private void SelectWorkspace(int index)
        {
            index = Mathf.Clamp(index, -1, m_Workspaces.Count - 1);
            if (m_SelectedWorkspaceIndex == index)
                return;

            ApplySerializedObject();
            m_SelectedWorkspaceIndex = index;
            ResetCatalogEditorState();
            SaveState();
        }

        private void SetActiveShader(Shader shader)
        {
            ShaderWorkspace workspace = ActiveWorkspace;
            if (workspace == null || workspace.Shader == shader)
                return;

            workspace.Shader = shader;
            if (workspace.Catalog != null && workspace.Catalog.Shader != null && workspace.Catalog.Shader != shader)
                workspace.Catalog = null;

            ResetCatalogEditorState();
            SaveState();
        }

        private void SetActiveCatalog(ShaderPropertyCatalog catalog)
        {
            ShaderWorkspace workspace = ActiveWorkspace;
            if (workspace == null || workspace.Catalog == catalog)
                return;

            ApplySerializedObject();
            workspace.Catalog = catalog;
            if (catalog?.Shader != null)
                workspace.Shader = catalog.Shader;

            ResetCatalogEditorState();
            SaveState();
        }

        private bool HasSyncableWorkspace()
        {
            for (int i = 0; i < m_Workspaces.Count; i++)
            {
                ShaderWorkspace workspace = m_Workspaces[i];
                if (workspace?.Shader != null && workspace.Catalog != null)
                    return true;
            }

            return false;
        }

        private void SyncActiveWorkspace()
        {
            if (!SyncWorkspace(ActiveWorkspace))
                return;

            ResetCatalogEditorState();
            AssetDatabase.SaveAssets();
        }

        private void SyncAllWorkspaces()
        {
            ApplySerializedObject();
            bool changed = false;
            for (int i = 0; i < m_Workspaces.Count; i++)
                changed |= SyncWorkspace(m_Workspaces[i]);

            if (!changed)
                return;

            ResetCatalogEditorState();
            AssetDatabase.SaveAssets();
        }

        private bool SyncWorkspace(ShaderWorkspace workspace)
        {
            if (workspace?.Catalog == null || workspace.Shader == null)
                return false;

            ShaderPropertyCatalogBuilder.SyncCatalog(
                workspace.Catalog,
                workspace.Shader,
                m_SemanticKeyProfile);
            EditorUtility.SetDirty(workspace.Catalog);
            return true;
        }

        private void ExportActiveConfig()
        {
            if (ActiveCatalog == null)
                return;

            MaterialParamConfig config = ScriptableObject.CreateInstance<MaterialParamConfig>();
            config.SetDefaultShaderName(GetActiveCatalogShaderName());
            config.SetParameters(BuildMaterialParameters(ActiveCatalog));

            string path = BuildUniqueConfigAssetPath(BuildDefaultConfigAssetName());
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(config);
        }

        private static List<MaterialParameter> BuildMaterialParameters(ShaderPropertyCatalog catalog)
        {
            List<MaterialParameter> parameters = new List<MaterialParameter>();
            if (catalog?.Properties == null)
                return parameters;

            foreach (CatalogProperty catalogProperty in catalog.Properties)
            {
                if (catalogProperty == null || catalogProperty.Status != CatalogPropertyStatus.Ok)
                    continue;
                if (catalogProperty.PropertyInfo == null)
                    continue;

                parameters.Add(new MaterialParameter(
                    catalogProperty.SuggestedSemanticKey,
                    catalogProperty.PropertyInfo.ValueType,
                    catalogProperty.PropertyInfo.DefaultValue));
            }

            return parameters;
        }

        private void ClearMissingProperties(SerializedProperty list)
        {
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                SerializedProperty status = element.FindPropertyRelative("Status");
                if (status == null || status.enumValueIndex != (int)CatalogPropertyStatus.Missing)
                    continue;

                list.DeleteArrayElementAtIndex(i);
                RemovePropertySelectionIndex(i);
            }

            ApplySerializedObject();
        }

        private void RefreshSerializedObject()
        {
            ShaderPropertyCatalog catalog = ActiveCatalog;
            if (catalog == null)
            {
                m_CatalogObject = null;
                return;
            }

            if (m_CatalogObject == null || m_CatalogObject.targetObject != catalog)
                m_CatalogObject = new SerializedObject(catalog);

            m_CatalogObject.Update();
        }

        private void ApplySerializedObject()
        {
            if (m_CatalogObject != null && m_CatalogObject.ApplyModifiedProperties())
                EditorUtility.SetDirty(m_CatalogObject.targetObject);
        }

        private void ResetCatalogEditorState()
        {
            m_CatalogObject = null;
            m_SelectedPropertyIndices.Clear();
            m_SelectedPropertyAnchorIndex = -1;
            m_PropertyScroll = Vector2.zero;
            m_DetailScroll = Vector2.zero;
        }

        private void EnsureWorkspaceState()
        {
            if (m_Workspaces == null)
                m_Workspaces = new List<ShaderWorkspace>();

            for (int i = m_Workspaces.Count - 1; i >= 0; i--)
            {
                if (m_Workspaces[i] == null)
                    m_Workspaces[i] = new ShaderWorkspace();
            }

            m_SelectedWorkspaceIndex = Mathf.Clamp(
                m_SelectedWorkspaceIndex,
                -1,
                m_Workspaces.Count - 1);
        }

        private void RestoreState()
        {
            string json = EditorPrefs.GetString(GetStatePreferenceKey(), string.Empty);
            if (string.IsNullOrEmpty(json))
                return;

            PersistedState state;
            try
            {
                state = JsonUtility.FromJson<PersistedState>(json);
            }
            catch (ArgumentException)
            {
                return;
            }

            if (state == null)
                return;

            List<ShaderWorkspace> restoredWorkspaces = new List<ShaderWorkspace>();
            if (state.Workspaces != null)
            {
                for (int i = 0; i < state.Workspaces.Count; i++)
                {
                    PersistedWorkspace persistedWorkspace = state.Workspaces[i];
                    ShaderPropertyCatalog catalog =
                        ResolveObject<ShaderPropertyCatalog>(persistedWorkspace?.CatalogId);
                    Shader shader = ResolveObject<Shader>(persistedWorkspace?.ShaderId);
                    restoredWorkspaces.Add(new ShaderWorkspace
                    {
                        Shader = shader != null ? shader : catalog?.Shader,
                        Catalog = catalog
                    });
                }
            }

            m_Workspaces = restoredWorkspaces;
            m_SelectedWorkspaceIndex = state.SelectedWorkspaceIndex;
            m_SemanticKeyProfile = ResolveObject<MaterialSemanticKeyProfile>(state.SemanticKeyProfileId);
            EnsureWorkspaceState();
            ResetCatalogEditorState();
        }

        private void SaveState()
        {
            EnsureWorkspaceState();
            PersistedState state = new PersistedState
            {
                SelectedWorkspaceIndex = m_SelectedWorkspaceIndex,
                SemanticKeyProfileId = GetObjectId(m_SemanticKeyProfile)
            };

            for (int i = 0; i < m_Workspaces.Count; i++)
            {
                ShaderWorkspace workspace = m_Workspaces[i];
                state.Workspaces.Add(new PersistedWorkspace
                {
                    ShaderId = GetObjectId(workspace?.Shader),
                    CatalogId = GetObjectId(workspace?.Catalog)
                });
            }

            EditorPrefs.SetString(GetStatePreferenceKey(), JsonUtility.ToJson(state));
        }

        private static string GetStatePreferenceKey()
        {
            string projectPath = Application.dataPath.Replace('\\', '/').ToLowerInvariant();
            return StatePreferenceKeyPrefix + Hash128.Compute(projectPath);
        }

        private static string GetObjectId(UnityEngine.Object target)
        {
            return target != null
                ? GlobalObjectId.GetGlobalObjectIdSlow(target).ToString()
                : string.Empty;
        }

        private static T ResolveObject<T>(string objectId)
            where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(objectId) ||
                !GlobalObjectId.TryParse(objectId, out GlobalObjectId globalObjectId))
            {
                return null;
            }

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId) as T;
        }

        private void AssignCreatedCatalog()
        {
            ShaderWorkspace workspace = ActiveWorkspace;
            if (workspace == null)
                return;

            ShaderPropertyCatalog createdCatalog = CreateCatalog(GetSelectedShaderName());
            if (createdCatalog == null)
                return;

            workspace.Catalog = createdCatalog;
            if (workspace.Shader != null)
                createdCatalog.SetShader(workspace.Shader);
            EditorUtility.SetDirty(createdCatalog);
            AssetDatabase.SaveAssets();
            ResetCatalogEditorState();
            SaveState();
            EditorGUIUtility.PingObject(createdCatalog);
        }

        private void AssignCreatedSemanticProfile()
        {
            MaterialSemanticKeyProfile createdProfile = CreateSemanticProfile();
            if (createdProfile == null)
                return;

            m_SemanticKeyProfile = createdProfile;
            SaveState();
            EditorGUIUtility.PingObject(createdProfile);
        }

        private ShaderPropertyCatalog CreateCatalog(string shaderName)
        {
            string defaultAssetName = BuildDefaultCatalogAssetName(shaderName);
            ShaderPropertyCatalog catalog = ScriptableObject.CreateInstance<ShaderPropertyCatalog>();
            string path = BuildConfigAssetPath(defaultAssetName);
            catalog = CreateOrReplaceAsset(catalog, path);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private MaterialSemanticKeyProfile CreateSemanticProfile()
        {
            MaterialSemanticKeyProfile profile = ScriptableObject.CreateInstance<MaterialSemanticKeyProfile>();
            string path = BuildConfigAssetPath(BuildDefaultProfileAssetName());
            profile = CreateOrReplaceAsset(profile, path);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private string BuildConfigAssetPath(string assetName)
        {
            string folderPath = EnsureDefaultConfigFolder();
            string safeAssetName = SanitizeAssetFileName(assetName);
            if (string.IsNullOrEmpty(safeAssetName))
                safeAssetName = "MatDataTransferAsset";

            return folderPath + "/" + safeAssetName + ".asset";
        }

        private string BuildUniqueConfigAssetPath(string assetName)
        {
            string folderPath = EnsureDefaultConfigFolder();
            string safeAssetName = SanitizeAssetFileName(assetName);
            if (string.IsNullOrEmpty(safeAssetName))
                safeAssetName = "MatDataTransferAsset";

            string path = folderPath + "/" + safeAssetName + ".asset";
            return AssetDatabase.GenerateUniqueAssetPath(path);
        }

        private static T CreateOrReplaceAsset<T>(T asset, string path)
            where T : ScriptableObject
        {
            if (asset == null || string.IsNullOrEmpty(path))
                return asset;

            T existingAsset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existingAsset != null)
            {
                EditorUtility.CopySerialized(asset, existingAsset);
                EditorUtility.SetDirty(existingAsset);
                UnityEngine.Object.DestroyImmediate(asset);
                return existingAsset;
            }

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                AssetDatabase.DeleteAsset(path);

            AssetDatabase.CreateAsset(asset, path);
            return asset;
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

            return string.Equals(currentFolder, folderName, StringComparison.Ordinal);
        }

        private static string BuildDefaultCatalogAssetName(string shaderName)
        {
            string shaderFileName = SanitizeAssetFileName(shaderName);
            return string.IsNullOrEmpty(shaderFileName)
                ? "ShaderPropertyCatalog"
                : shaderFileName + "_ShaderPropertyCatalog";
        }

        private static string BuildDefaultProfileAssetName()
        {
            return "MaterialSemanticKeyProfile";
        }

        private string BuildDefaultConfigAssetName()
        {
            string shaderFileName = SanitizeAssetFileName(GetSelectedShaderName());
            return string.IsNullOrEmpty(shaderFileName)
                ? "MaterialParamConfig"
                : shaderFileName + "_MaterialParamConfig";
        }

        private string GetSelectedShaderName()
        {
            if (ActiveShader != null)
                return ActiveShader.name;

            return GetActiveCatalogShaderName();
        }

        private string GetActiveCatalogShaderName()
        {
            ShaderPropertyCatalog catalog = ActiveCatalog;
            if (catalog == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(catalog.ShaderName))
                return catalog.ShaderName;

            return catalog.Shader != null ? catalog.Shader.name : string.Empty;
        }

        private void DrawSemanticProfilePreview(SerializedProperty propertyInfo)
        {
            if (m_SemanticKeyProfile == null || propertyInfo == null)
                return;

            ShaderPropertyInfo info = new ShaderPropertyInfo
            {
                PropertyName = GetString(propertyInfo, "PropertyName"),
                InspectorDisplayName = GetString(propertyInfo, "InspectorDisplayName"),
                ValueType = GetParamValueType(propertyInfo)
            };

            List<string> warnings = new List<string>();
            bool matched = m_SemanticKeyProfile.TryResolveSemanticKey(
                GetSelectedShaderName(),
                info,
                out string semanticKey,
                warnings);

            EditorGUILayout.Space(8f);
            InspectorStyleLibrary.DrawParameterValue(
                "Profile Match",
                matched ? semanticKey : "<none>");

            for (int i = 0; i < warnings.Count; i++)
                EditorGUILayout.HelpBox(warnings[i], MessageType.Warning);
        }

        private static void DrawReadOnlyEnum(SerializedProperty property, string relativeName, string label)
        {
            SerializedProperty child = property?.FindPropertyRelative(relativeName);
            string value = child != null &&
                           child.enumValueIndex >= 0 &&
                           child.enumValueIndex < child.enumDisplayNames.Length
                ? child.enumDisplayNames[child.enumValueIndex]
                : string.Empty;
            InspectorStyleLibrary.DrawParameterValue(label, value);
        }

        private static string GetString(SerializedProperty property, string name)
        {
            SerializedProperty child = property?.FindPropertyRelative(name);
            return child != null ? child.stringValue : string.Empty;
        }

        private static ParamValueType GetParamValueType(SerializedProperty propertyInfo)
        {
            SerializedProperty valueType = propertyInfo.FindPropertyRelative("ValueType");
            return valueType != null
                ? (ParamValueType)valueType.enumValueIndex
                : ParamValueType.Float;
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
