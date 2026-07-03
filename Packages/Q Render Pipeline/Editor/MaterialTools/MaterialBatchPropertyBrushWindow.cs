using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor
{
    public class MaterialBatchPropertyBrushWindow : EditorWindow
    {
        private const float PROPERTY_AREA_MIN_HEIGHT = 90f;
        private const float PROPERTY_AREA_DEFAULT_HEIGHT = 160f;
        private const float PROPERTY_AREA_MAX_PADDING = 230f;
        private const float SPLITTER_HEIGHT = 5f;

        private bool m_CleanUnusedSavedProperties = true;
        private bool m_CleanUnusedKeywords = true;
        private bool m_SkipNonMatAssets = true;
        private bool m_IsDraggingPropertySplitter;
        private float m_PropertyAreaHeight = PROPERTY_AREA_DEFAULT_HEIGHT;
        private Vector2 m_WindowScrollPosition;
        private Vector2 m_MaterialScrollPosition;
        private Vector2 m_PropertyScrollPosition;
        private string m_ResultMessage = string.Empty;
        private ScanConditionLogic m_ScanConditionLogic = ScanConditionLogic.And;
        private ShaderPropertyBrowserPopup m_ShaderPropertyBrowserPopup;
        private MaterialPropertyBrowserPopup m_MaterialPropertyBrowserPopup;

        private readonly List<string> m_SearchFolders = new List<string>();
        private readonly List<Shader> m_TargetShaders = new List<Shader>();
        private readonly List<MaterialScanPropertyCondition> m_ScanPropertyConditions =
            new List<MaterialScanPropertyCondition>();
        private readonly List<MaterialPropertyOverride> m_PropertyOverrides = new List<MaterialPropertyOverride>();
        private readonly List<MaterialRecord> m_MaterialRecords = new List<MaterialRecord>();
        private readonly List<Material> m_DirectMaterials = new List<Material>();
        private readonly HashSet<string> m_FoldoutFolders = new HashSet<string>();

        private static MethodInfo s_GetShaderGlobalKeywordsMethod;
        private static MethodInfo s_GetShaderLocalKeywordsMethod;
        private static bool s_KeywordReflectionInitialized;

        [MenuItem("Q Render Pipeline/Material Tools/材质属性批量刷写工具")]
        public static void ShowWindow()
        {
            MaterialBatchPropertyBrushWindow window =
                GetWindow<MaterialBatchPropertyBrushWindow>("材质属性批量刷写");
            window.minSize = new Vector2(720, 520);
            window.Show();
        }

        private void OnGUI()
        {
            m_WindowScrollPosition = EditorGUILayout.BeginScrollView(m_WindowScrollPosition);
            DrawSearchSettings();
            EditorGUILayout.Space(4);
            DrawPropertyOverrides();
            DrawPropertyActionSplitter();
            DrawActionButtons();
            EditorGUILayout.Space(4);
            DrawScanResult();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSearchSettings()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("扫描条件", EditorStyles.boldLabel);

            DrawSearchFolders();
            EditorGUILayout.Space(2);
            DrawTargetShaders();
            EditorGUILayout.Space(2);
            DrawScanPropertyNames();
            EditorGUILayout.Space(2);
            DrawDirectMaterials();
            EditorGUILayout.Space(2);
            m_SkipNonMatAssets = EditorGUILayout.Toggle("只处理.mat文件", m_SkipNonMatAssets);
            m_CleanUnusedSavedProperties = EditorGUILayout.Toggle("清理历史无效属性", m_CleanUnusedSavedProperties);
            m_CleanUnusedKeywords = EditorGUILayout.Toggle("清理无效关键字", m_CleanUnusedKeywords);
            EditorGUILayout.EndVertical();
        }

        private void DrawSearchFolders()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("目标文件夹", EditorStyles.boldLabel);
            if (GUILayout.Button("添加选中", GUILayout.Width(88)))
            {
                AddSelectionFolders();
            }

            if (GUILayout.Button("浏览添加", GUILayout.Width(88)))
            {
                BrowseFolder();
            }

            if (GUILayout.Button("清空", GUILayout.Width(52)))
            {
                m_SearchFolders.Clear();
            }

            EditorGUILayout.EndHorizontal();

            if (m_SearchFolders.Count == 0)
            {
                EditorGUILayout.HelpBox("未添加目标文件夹时不能扫描，请添加至少一个文件夹。", MessageType.Warning);
                return;
            }

            for (int i = 0; i < m_SearchFolders.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                m_SearchFolders[i] = EditorGUILayout.TextField($"文件夹 {i + 1}", m_SearchFolders[i]);
                if (GUILayout.Button("移除", GUILayout.Width(52)))
                {
                    m_SearchFolders.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTargetShaders()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("目标Shader", EditorStyles.boldLabel);
            if (GUILayout.Button("添加", GUILayout.Width(52)))
            {
                m_TargetShaders.Add(null);
            }

            if (GUILayout.Button("拾取选中", GUILayout.Width(88)))
            {
                AddSelectionShaders();
            }

            if (GUILayout.Button("清空", GUILayout.Width(52)))
            {
                m_TargetShaders.Clear();
            }

            EditorGUILayout.EndHorizontal();

            if (m_TargetShaders.Count == 0)
            {
                EditorGUILayout.HelpBox("未添加目标Shader时，会扫描所有Shader的材质。", MessageType.None);
                return;
            }

            for (int i = 0; i < m_TargetShaders.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                m_TargetShaders[i] =
                    (Shader)EditorGUILayout.ObjectField($"Shader {i + 1}", m_TargetShaders[i], typeof(Shader), false);
                EditorGUI.BeginDisabledGroup(m_TargetShaders[i] == null);
                if (GUILayout.Button("属性", GUILayout.Width(52)))
                {
                    OpenShaderPropertyBrowser(m_TargetShaders[i]);
                }

                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("移除", GUILayout.Width(52)))
                {
                    m_TargetShaders.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawScanPropertyNames()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("扫描属性条件", EditorStyles.boldLabel);
            if (GUILayout.Button("添加", GUILayout.Width(52)))
            {
                m_ScanPropertyConditions.Add(new MaterialScanPropertyCondition());
            }

            if (GUILayout.Button("从刷写属性填充", GUILayout.Width(112)))
            {
                FillScanPropertiesFromOverrides();
            }

            if (GUILayout.Button("清空", GUILayout.Width(52)))
            {
                m_ScanPropertyConditions.Clear();
            }

            EditorGUILayout.EndHorizontal();

            if (m_ScanPropertyConditions.Count == 0)
            {
                EditorGUILayout.HelpBox("未添加扫描属性时，不按属性过滤。", MessageType.None);
                return;
            }

            m_ScanConditionLogic = DrawScanConditionLogicPopup(m_ScanConditionLogic);

            for (int i = 0; i < m_ScanPropertyConditions.Count; i++)
            {
                if (DrawScanPropertyCondition(i))
                {
                    m_ScanPropertyConditions.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private bool DrawScanPropertyCondition(int index)
        {
            MaterialScanPropertyCondition condition = m_ScanPropertyConditions[index];
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            condition.propertyName = EditorGUILayout.TextField($"属性条件 {index + 1}", condition.propertyName);
            if (GUILayout.Button("添加值条件", GUILayout.Width(88)))
            {
                condition.valueConditions.Add(new MaterialScanValueCondition());
            }

            bool shouldRemove = GUILayout.Button("移除", GUILayout.Width(52));
            EditorGUILayout.EndHorizontal();

            if (condition.valueConditions.Count == 0)
            {
                EditorGUILayout.HelpBox("未添加值条件时，只判断材质是否包含这个属性。", MessageType.None);
            }
            else
            {
                condition.valueConditionLogic = DrawScanConditionLogicPopup("值条件联合", condition.valueConditionLogic);
                for (int i = 0; i < condition.valueConditions.Count; i++)
                {
                    if (DrawScanValueCondition(condition.valueConditions[i], i))
                    {
                        condition.valueConditions.RemoveAt(i);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            EditorGUILayout.EndVertical();
            return shouldRemove;
        }

        private static bool DrawScanValueCondition(MaterialScanValueCondition condition, int index)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"值条件 {index + 1}", GUILayout.Width(72));
            condition.valueType =
                (MaterialValueType)EditorGUILayout.EnumPopup(condition.valueType, GUILayout.Width(96));
            if (ShouldUseEqualityCompareOnly(condition.valueType) &&
                !IsTextureCompareMode(condition.compareMode))
            {
                condition.compareMode = PropertyCompareMode.Equal;
            }

            condition.compareMode = DrawCompareModePopup(condition.compareMode, condition.valueType);
            bool shouldRemove = GUILayout.Button("移除", GUILayout.Width(52));
            EditorGUILayout.EndHorizontal();

            DrawScanConditionValue(condition);
            EditorGUILayout.EndVertical();
            return shouldRemove;
        }

        private static void DrawScanConditionValue(MaterialScanValueCondition condition)
        {
            switch (condition.valueType)
            {
                case MaterialValueType.Float:
                    condition.floatValue = EditorGUILayout.FloatField("目标值", condition.floatValue);
                    break;
                case MaterialValueType.Int:
                    condition.intValue = EditorGUILayout.IntField("目标值", condition.intValue);
                    break;
                case MaterialValueType.Color:
                    condition.colorValue = EditorGUILayout.ColorField("目标值", condition.colorValue);
                    break;
                case MaterialValueType.Vector:
                    condition.vectorValue = EditorGUILayout.Vector4Field("目标值", condition.vectorValue);
                    break;
                case MaterialValueType.Texture:
                    condition.textureValue =
                        (Texture)EditorGUILayout.ObjectField("目标贴图", condition.textureValue, typeof(Texture), false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static PropertyCompareMode DrawCompareModePopup(PropertyCompareMode compareMode, MaterialValueType valueType)
        {
            PropertyCompareMode[] options = GetCompareModeOptions(valueType);
            string[] displayNames = options.Select(GetCompareModeDisplayName).ToArray();
            int selectedIndex = Mathf.Max(0, Array.IndexOf(options, compareMode));
            int nextIndex = EditorGUILayout.Popup(selectedIndex, displayNames, GUILayout.Width(96));
            return options[Mathf.Clamp(nextIndex, 0, options.Length - 1)];
        }

        private static PropertyCompareMode[] GetCompareModeOptions(MaterialValueType valueType)
        {
            if (ShouldUseEqualityCompareOnly(valueType))
            {
                return new[]
                {
                    PropertyCompareMode.Equal,
                    PropertyCompareMode.NotEqual
                };
            }

            return new[]
            {
                PropertyCompareMode.Equal,
                PropertyCompareMode.NotEqual,
                PropertyCompareMode.Greater,
                PropertyCompareMode.GreaterOrEqual,
                PropertyCompareMode.Less,
                PropertyCompareMode.LessOrEqual
            };
        }

        private static ScanConditionLogic DrawScanConditionLogicPopup(ScanConditionLogic logic)
        {
            return DrawScanConditionLogicPopup("条件联合", logic);
        }

        private static ScanConditionLogic DrawScanConditionLogicPopup(string label, ScanConditionLogic logic)
        {
            ScanConditionLogic[] options =
            {
                ScanConditionLogic.And,
                ScanConditionLogic.Or
            };
            string[] displayNames =
            {
                "全部满足",
                "任一满足"
            };
            int selectedIndex = Mathf.Max(0, Array.IndexOf(options, logic));
            int nextIndex = EditorGUILayout.Popup(label, selectedIndex, displayNames);
            return options[Mathf.Clamp(nextIndex, 0, options.Length - 1)];
        }

        private static string GetCompareModeDisplayName(PropertyCompareMode compareMode)
        {
            switch (compareMode)
            {
                case PropertyCompareMode.Equal:
                    return "等于";
                case PropertyCompareMode.NotEqual:
                    return "不等于";
                case PropertyCompareMode.Greater:
                    return "大于";
                case PropertyCompareMode.GreaterOrEqual:
                    return "大于等于";
                case PropertyCompareMode.Less:
                    return "小于";
                case PropertyCompareMode.LessOrEqual:
                    return "小于等于";
                default:
                    throw new ArgumentOutOfRangeException(nameof(compareMode), compareMode, null);
            }
        }

        private static bool ShouldUseEqualityCompareOnly(MaterialValueType valueType)
        {
            return valueType == MaterialValueType.Color ||
                   valueType == MaterialValueType.Vector ||
                   valueType == MaterialValueType.Texture;
        }

        private static bool IsTextureCompareMode(PropertyCompareMode compareMode)
        {
            return compareMode == PropertyCompareMode.Equal || compareMode == PropertyCompareMode.NotEqual;
        }

        private void DrawDirectMaterials()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("指定材质", EditorStyles.boldLabel);
            if (GUILayout.Button("添加", GUILayout.Width(52)))
            {
                m_DirectMaterials.Add(null);
            }

            if (GUILayout.Button("添加选中", GUILayout.Width(88)))
            {
                AddSelectionMaterials();
            }

            if (GUILayout.Button("清空", GUILayout.Width(52)))
            {
                m_DirectMaterials.Clear();
            }

            EditorGUILayout.EndHorizontal();

            if (m_DirectMaterials.Count == 0)
            {
                EditorGUILayout.HelpBox("指定材质会直接加入处理列表，不受文件夹、Shader、扫描属性条件限制。", MessageType.None);
                return;
            }

            for (int i = 0; i < m_DirectMaterials.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                m_DirectMaterials[i] =
                    (Material)EditorGUILayout.ObjectField($"材质 {i + 1}", m_DirectMaterials[i], typeof(Material), false);
                EditorGUI.BeginDisabledGroup(m_DirectMaterials[i] == null);
                if (GUILayout.Button("属性", GUILayout.Width(52)))
                {
                    OpenMaterialPropertyBrowser(m_DirectMaterials[i]);
                }

                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("移除", GUILayout.Width(52)))
                {
                    m_DirectMaterials.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawPropertyOverrides()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("统一刷写属性", EditorStyles.boldLabel);
            if (GUILayout.Button("添加属性", GUILayout.Width(80)))
            {
                m_PropertyOverrides.Add(new MaterialPropertyOverride());
            }

            EditorGUILayout.EndHorizontal();

            m_PropertyScrollPosition = EditorGUILayout.BeginScrollView(m_PropertyScrollPosition, GUILayout.Height(m_PropertyAreaHeight));
            for (int i = 0; i < m_PropertyOverrides.Count; i++)
            {
                if (DrawPropertyOverride(i))
                {
                    m_PropertyOverrides.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPropertyActionSplitter()
        {
            Rect splitterRect = GUILayoutUtility.GetRect(1f, SPLITTER_HEIGHT, GUILayout.ExpandWidth(true));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(splitterRect.x, splitterRect.center.y - 1f, splitterRect.width, 1f),
                    new Color(0.35f, 0.35f, 0.35f, 1f));
            }

            HandlePropertyActionSplitter(splitterRect);
        }

        private void HandlePropertyActionSplitter(Rect splitterRect)
        {
            Event currentEvent = Event.current;
            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (splitterRect.Contains(currentEvent.mousePosition) && currentEvent.button == 0)
                    {
                        m_IsDraggingPropertySplitter = true;
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (m_IsDraggingPropertySplitter)
                    {
                        float maxHeight = Mathf.Max(PROPERTY_AREA_MIN_HEIGHT,
                            position.height - PROPERTY_AREA_MAX_PADDING);
                        m_PropertyAreaHeight = Mathf.Clamp(
                            m_PropertyAreaHeight + currentEvent.delta.y,
                            PROPERTY_AREA_MIN_HEIGHT,
                            maxHeight);
                        Repaint();
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (m_IsDraggingPropertySplitter && currentEvent.button == 0)
                    {
                        m_IsDraggingPropertySplitter = false;
                        currentEvent.Use();
                    }
                    break;
            }
        }

        private bool DrawPropertyOverride(int index)
        {
            MaterialPropertyOverride propertyOverride = m_PropertyOverrides[index];
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            propertyOverride.propertyName = EditorGUILayout.TextField("属性名", propertyOverride.propertyName);
            propertyOverride.valueType =
                (MaterialValueType)EditorGUILayout.EnumPopup(propertyOverride.valueType, GUILayout.Width(96));
            bool shouldRemove = GUILayout.Button("移除", GUILayout.Width(52));
            EditorGUILayout.EndHorizontal();

            DrawPropertyValue(propertyOverride);
            EditorGUILayout.EndVertical();

            return shouldRemove;
        }

        private static void DrawPropertyValue(MaterialPropertyOverride propertyOverride)
        {
            switch (propertyOverride.valueType)
            {
                case MaterialValueType.Float:
                    propertyOverride.floatValue = EditorGUILayout.FloatField("值", propertyOverride.floatValue);
                    break;
                case MaterialValueType.Int:
                    propertyOverride.intValue = EditorGUILayout.IntField("值", propertyOverride.intValue);
                    break;
                case MaterialValueType.Color:
                    propertyOverride.colorValue = EditorGUILayout.ColorField("值", propertyOverride.colorValue);
                    break;
                case MaterialValueType.Vector:
                    propertyOverride.vectorValue = EditorGUILayout.Vector4Field("值", propertyOverride.vectorValue);
                    break;
                case MaterialValueType.Texture:
                    propertyOverride.textureValue =
                        (Texture)EditorGUILayout.ObjectField("贴图", propertyOverride.textureValue, typeof(Texture), false);
                    propertyOverride.setTextureScale = EditorGUILayout.Toggle("修改Tiling", propertyOverride.setTextureScale);
                    if (propertyOverride.setTextureScale)
                    {
                        propertyOverride.textureScale =
                            EditorGUILayout.Vector2Field("Tiling", propertyOverride.textureScale);
                    }

                    propertyOverride.setTextureOffset = EditorGUILayout.Toggle("修改Offset", propertyOverride.setTextureOffset);
                    if (propertyOverride.setTextureOffset)
                    {
                        propertyOverride.textureOffset =
                            EditorGUILayout.Vector2Field("Offset", propertyOverride.textureOffset);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("扫描材质", GUILayout.Height(30)))
            {
                ScanMaterials();
            }

            if (GUILayout.Button("刷写扫描结果", GUILayout.Height(30)))
            {
                ApplyToScannedMaterials();
            }

            if (GUILayout.Button("清空结果", GUILayout.Height(30), GUILayout.Width(92)))
            {
                m_MaterialRecords.Clear();
                m_FoldoutFolders.Clear();
                m_ResultMessage = string.Empty;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawScanResult()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("扫描结果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"命中材质: {m_MaterialRecords.Count}");

            if (!string.IsNullOrEmpty(m_ResultMessage))
            {
                EditorGUILayout.HelpBox(m_ResultMessage, MessageType.Info);
            }

            m_MaterialScrollPosition =
                EditorGUILayout.BeginScrollView(m_MaterialScrollPosition, GUILayout.ExpandHeight(true));
            DrawGroupedMaterialRecords();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawGroupedMaterialRecords()
        {
            foreach (IGrouping<string, MaterialRecord> group in m_MaterialRecords.GroupBy(record => record.folderPath))
            {
                DrawMaterialFolderGroup(group.Key, group.ToList());
            }
        }

        private void DrawMaterialFolderGroup(string folderPath, List<MaterialRecord> records)
        {
            string title = $"{folderPath} ({records.Count})";
            bool isOpen = m_FoldoutFolders.Contains(folderPath);
            bool newOpen = EditorGUILayout.Foldout(isOpen, title, true);
            if (newOpen != isOpen)
            {
                if (newOpen)
                {
                    m_FoldoutFolders.Add(folderPath);
                }
                else
                {
                    m_FoldoutFolders.Remove(folderPath);
                }
            }

            if (!newOpen)
            {
                return;
            }

            EditorGUI.indentLevel++;
            foreach (MaterialRecord record in records)
            {
                DrawMaterialRecord(record);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawMaterialRecord(MaterialRecord record)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.ObjectField(record.material, typeof(Material), false);
            EditorGUILayout.LabelField(record.shaderName, GUILayout.Width(220));
            if (GUILayout.Button("属性", GUILayout.Width(52)))
            {
                OpenMaterialPropertyBrowser(record.material);
            }

            if (GUILayout.Button("删除", GUILayout.Width(52)))
            {
                m_MaterialRecords.Remove(record);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(record.path, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(record.lastResult))
            {
                EditorGUILayout.LabelField(record.lastResult, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void AddSelectionFolders()
        {
            int addedCount = 0;
            foreach (UnityEngine.Object selectedObject in Selection.objects)
            {
                string selectedPath = AssetDatabase.GetAssetPath(selectedObject);
                if (!AssetDatabase.IsValidFolder(selectedPath))
                {
                    continue;
                }

                AddSearchFolder(selectedPath);
                addedCount++;
            }

            if (addedCount == 0)
            {
                EditorUtility.DisplayDialog("提示", "请在Project面板选中一个或多个文件夹。", "确定");
            }
        }

        private void AddSelectionShaders()
        {
            int addedCount = 0;
            foreach (UnityEngine.Object selectedObject in Selection.objects)
            {
                Shader shader = GetShaderFromObject(selectedObject);
                if (shader == null)
                {
                    continue;
                }

                AddTargetShader(shader);
                addedCount++;
            }

            if (addedCount == 0)
            {
                EditorUtility.DisplayDialog("提示", "请在Project面板选中一个或多个Shader或Material资源。", "确定");
            }
        }

        private void AddSelectionMaterials()
        {
            int addedCount = 0;
            foreach (UnityEngine.Object selectedObject in Selection.objects)
            {
                Material material = GetMaterialFromObject(selectedObject);
                if (material == null)
                {
                    continue;
                }

                AddDirectMaterial(material);
                addedCount++;
            }

            if (addedCount == 0)
            {
                EditorUtility.DisplayDialog("提示", "请在Project面板选中一个或多个Material资源。", "确定");
            }
        }

        private static Shader GetShaderFromObject(UnityEngine.Object selectedObject)
        {
            if (selectedObject is Shader shader)
            {
                return shader;
            }

            if (selectedObject is Material material)
            {
                return material.shader;
            }

            string selectedPath = AssetDatabase.GetAssetPath(selectedObject);
            if (string.IsNullOrEmpty(selectedPath))
            {
                return null;
            }

            Material selectedMaterial = AssetDatabase.LoadAssetAtPath<Material>(selectedPath);
            if (selectedMaterial != null)
            {
                return selectedMaterial.shader;
            }

            return AssetDatabase.LoadAssetAtPath<Shader>(selectedPath);
        }

        private static Material GetMaterialFromObject(UnityEngine.Object selectedObject)
        {
            if (selectedObject is Material material)
            {
                return material;
            }

            string selectedPath = AssetDatabase.GetAssetPath(selectedObject);
            if (string.IsNullOrEmpty(selectedPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<Material>(selectedPath);
        }

        private void BrowseFolder()
        {
            string absolutePath = EditorUtility.OpenFolderPanel("选择材质文件夹", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(absolutePath))
            {
                return;
            }

            if (TryConvertAbsolutePathToAssetPath(absolutePath, out string assetPath))
            {
                AddSearchFolder(assetPath);
                return;
            }

            EditorUtility.DisplayDialog("路径无效", "请选择当前Unity工程内的文件夹。", "确定");
        }

        private void AddSearchFolder(string folderPath)
        {
            string normalizedFolder = NormalizeAssetPath(folderPath);
            if (!m_SearchFolders.Contains(normalizedFolder))
            {
                m_SearchFolders.Add(normalizedFolder);
            }
        }

        private void AddTargetShader(Shader shader)
        {
            if (shader != null && !m_TargetShaders.Contains(shader))
            {
                m_TargetShaders.Add(shader);
            }
        }

        private void AddDirectMaterial(Material material)
        {
            if (material != null && !m_DirectMaterials.Contains(material))
            {
                m_DirectMaterials.Add(material);
            }
        }

        private void OpenShaderPropertyBrowser(Shader shader)
        {
            if (shader == null)
            {
                return;
            }

            m_ShaderPropertyBrowserPopup?.Close();
            m_ShaderPropertyBrowserPopup = CreateInstance<ShaderPropertyBrowserPopup>();
            m_ShaderPropertyBrowserPopup.Initialize(shader);
            m_ShaderPropertyBrowserPopup.ShowUtility();
        }

        private void OpenMaterialPropertyBrowser(Material material)
        {
            if (material == null)
            {
                return;
            }

            m_MaterialPropertyBrowserPopup?.Close();
            m_MaterialPropertyBrowserPopup = CreateInstance<MaterialPropertyBrowserPopup>();
            m_MaterialPropertyBrowserPopup.Initialize(material);
            m_MaterialPropertyBrowserPopup.ShowUtility();
        }

        private void FillScanPropertiesFromOverrides()
        {
            foreach (MaterialPropertyOverride propertyOverride in GetValidPropertyOverrides())
            {
                if (!ContainsScanPropertyName(propertyOverride.propertyName))
                {
                    m_ScanPropertyConditions.Add(new MaterialScanPropertyCondition
                    {
                        propertyName = propertyOverride.propertyName
                    });
                }
            }
        }

        private void ScanMaterials()
        {
            m_MaterialRecords.Clear();
            m_ResultMessage = string.Empty;

            string[] searchFolders = Array.Empty<string>();
            bool hasSearchFolders = TryGetValidSearchFolders(out searchFolders, HasValidDirectMaterials());
            if (!hasSearchFolders && !HasValidDirectMaterials())
            {
                return;
            }

            var materialPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDirectMaterialRecords(materialPaths);

            if (hasSearchFolders)
            {
                string[] guids = AssetDatabase.FindAssets("t:Material", searchFolders).Distinct().ToArray();
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EditorUtility.DisplayProgressBar("扫描材质", path, guids.Length == 0 ? 1f : (float)i / guids.Length);

                    if (m_SkipNonMatAssets && !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!IsTargetMaterial(material))
                    {
                        continue;
                    }

                    AddMaterialRecord(material, path, materialPaths);
                }
            }

            EditorUtility.ClearProgressBar();
            m_MaterialRecords.Sort((left, right) => string.Compare(left.path, right.path, StringComparison.OrdinalIgnoreCase));
            RefreshFolderFoldouts();
            m_ResultMessage =
                $"扫描完成：找到 {m_MaterialRecords.Count} 个可处理材质。";
            Repaint();
        }

        private void AddDirectMaterialRecords(HashSet<string> materialPaths)
        {
            foreach (Material material in m_DirectMaterials.Where(material => material != null))
            {
                string path = AssetDatabase.GetAssetPath(material);
                if (m_SkipNonMatAssets && !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddMaterialRecord(material, path, materialPaths);
            }
        }

        private void AddMaterialRecord(Material material, string path, HashSet<string> materialPaths)
        {
            if (material == null || string.IsNullOrEmpty(path) || materialPaths.Contains(path))
            {
                return;
            }

            materialPaths.Add(path);
            m_MaterialRecords.Add(new MaterialRecord(material, path));
        }

        private void RefreshFolderFoldouts()
        {
            m_FoldoutFolders.Clear();
            foreach (string folderPath in m_MaterialRecords.Select(record => record.folderPath).Distinct())
            {
                m_FoldoutFolders.Add(folderPath);
            }
        }

        private void ApplyToScannedMaterials()
        {
            if (m_MaterialRecords.Count == 0)
            {
                ScanMaterials();
            }

            if (m_MaterialRecords.Count == 0)
            {
                EditorUtility.DisplayDialog("没有材质", "当前扫描条件下没有找到可处理的材质。", "确定");
                return;
            }

            if (!HasAnyWork())
            {
                EditorUtility.DisplayDialog("没有任务", "请至少添加一个属性，或开启一种清理选项。", "确定");
                return;
            }

            if (!HasShaderFilter() &&
                !EditorUtility.DisplayDialog("确认范围",
                    "当前没有设置目标Shader，会处理目标文件夹下所有材质。是否继续？", "继续", "取消"))
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("确认刷写",
                    $"即将处理 {m_MaterialRecords.Count} 个材质。\n建议确认版本库已保存当前工作。是否继续？", "执行", "取消"))
            {
                return;
            }

            ApplyMaterialChanges();
        }

        private void ApplyMaterialChanges()
        {
            int changedMaterialCount = 0;
            int changedPropertyCount = 0;
            int missingPropertyCount = 0;
            int removedSavedPropertyCount = 0;
            int removedKeywordCount = 0;

            try
            {
                for (int i = 0; i < m_MaterialRecords.Count; i++)
                {
                    MaterialRecord record = m_MaterialRecords[i];
                    Material material = record.material;
                    EditorUtility.DisplayProgressBar("刷写材质", record.path,
                        m_MaterialRecords.Count == 0 ? 1f : (float)i / m_MaterialRecords.Count);

                    MaterialChangeResult result = ApplyMaterialChange(material);
                    changedPropertyCount += result.changedPropertyCount;
                    missingPropertyCount += result.missingPropertyCount;
                    removedSavedPropertyCount += result.removedSavedPropertyCount;
                    removedKeywordCount += result.removedKeywordCount;
                    record.lastResult = result.ToDisplayText();

                    if (!result.changed)
                    {
                        continue;
                    }

                    changedMaterialCount++;
                    EditorUtility.SetDirty(material);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            m_ResultMessage =
                $"刷写完成：修改 {changedMaterialCount} 个材质，刷写属性 {changedPropertyCount} 次，缺失属性 {missingPropertyCount} 次，" +
                $"清理历史属性 {removedSavedPropertyCount} 条，清理关键字 {removedKeywordCount} 条。";
            Debug.Log(m_ResultMessage);
            Repaint();
        }

        private MaterialChangeResult ApplyMaterialChange(Material material)
        {
            var result = new MaterialChangeResult();
            if (material == null)
            {
                return result;
            }

            if (m_CleanUnusedSavedProperties)
            {
                result.removedSavedPropertyCount = CleanUnusedSavedProperties(material);
            }

            if (m_CleanUnusedKeywords)
            {
                result.removedKeywordCount = CleanUnusedKeywords(material);
            }

            List<MaterialPropertyOverride> propertyOverrides = GetValidPropertyOverrides();
            foreach (MaterialPropertyOverride propertyOverride in propertyOverrides)
            {
                if (!propertyOverride.Apply(material))
                {
                    result.missingPropertyCount++;
                    continue;
                }

                if (propertyOverride.wasChanged)
                {
                    result.changedPropertyCount++;
                }
            }

            result.changed = result.removedSavedPropertyCount > 0 ||
                             result.removedKeywordCount > 0 ||
                             result.changedPropertyCount > 0;
            return result;
        }

        private List<MaterialPropertyOverride> GetValidPropertyOverrides()
        {
            return m_PropertyOverrides
                .Where(propertyOverride => !string.IsNullOrWhiteSpace(propertyOverride.propertyName))
                .ToList();
        }

        private bool HasAnyWork()
        {
            return m_CleanUnusedSavedProperties ||
                   m_CleanUnusedKeywords ||
                   GetValidPropertyOverrides().Any();
        }

        private bool HasShaderFilter()
        {
            return GetValidTargetShaders().Count > 0;
        }

        private bool TryGetValidSearchFolders(out string[] searchFolders, bool allowEmpty)
        {
            var validFolders = new List<string>();
            for (int i = 0; i < m_SearchFolders.Count; i++)
            {
                string searchFolder = NormalizeAssetPath(m_SearchFolders[i]);
                if (!string.IsNullOrWhiteSpace(searchFolder) &&
                    !AssetDatabase.IsValidFolder(searchFolder) &&
                    TryConvertAbsolutePathToAssetPath(m_SearchFolders[i], out string assetPath))
                {
                    searchFolder = NormalizeAssetPath(assetPath);
                    m_SearchFolders[i] = searchFolder;
                }

                if (AssetDatabase.IsValidFolder(searchFolder))
                {
                    AddUniqueFolder(validFolders, searchFolder);
                }
            }

            searchFolders = validFolders.ToArray();
            if (searchFolders.Length > 0)
            {
                return true;
            }

            if (allowEmpty)
            {
                return false;
            }

            EditorUtility.DisplayDialog("文件夹无效", "请添加至少一个有效的目标文件夹。", "确定");
            return false;
        }

        private bool HasValidDirectMaterials()
        {
            return m_DirectMaterials.Any(material => material != null);
        }

        private bool IsTargetMaterial(Material material)
        {
            if (material == null || material.shader == null)
            {
                return false;
            }

            List<Shader> targetShaders = GetValidTargetShaders();
            if (targetShaders.Count > 0 && !targetShaders.Contains(material.shader))
            {
                return false;
            }

            return MatchScanPropertyConditions(material);
        }

        private List<Shader> GetValidTargetShaders()
        {
            return m_TargetShaders
                .Where(shader => shader != null)
                .Distinct()
                .ToList();
        }

        private bool MatchScanPropertyConditions(Material material)
        {
            List<MaterialScanPropertyCondition> conditions = GetValidScanPropertyConditions();
            if (conditions.Count == 0)
            {
                return true;
            }

            return m_ScanConditionLogic == ScanConditionLogic.And
                ? conditions.All(condition => condition.Match(material))
                : conditions.Any(condition => condition.Match(material));
        }

        private List<MaterialScanPropertyCondition> GetValidScanPropertyConditions()
        {
            return m_ScanPropertyConditions
                .Where(condition => condition != null && !string.IsNullOrWhiteSpace(condition.propertyName))
                .ToList();
        }

        private static void AddUniqueFolder(List<string> folders, string folder)
        {
            if (!folders.Contains(folder))
            {
                folders.Add(folder);
            }
        }

        private bool ContainsScanPropertyName(string propertyName)
        {
            return m_ScanPropertyConditions.Any(condition =>
                string.Equals(condition.propertyName, propertyName, StringComparison.Ordinal));
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static bool TryConvertAbsolutePathToAssetPath(string absolutePath, out string assetPath)
        {
            assetPath = string.Empty;
            string normalizedAbsolutePath = Path.GetFullPath(absolutePath).Replace('\\', '/').TrimEnd('/');
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(projectRoot) ||
                !normalizedAbsolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            assetPath = normalizedAbsolutePath.Substring(projectRoot.Length).TrimStart('/');
            return !string.IsNullOrEmpty(assetPath);
        }

        private static int CleanUnusedSavedProperties(Material material)
        {
            if (material == null || material.shader == null)
            {
                return 0;
            }

            HashSet<string> validProperties = GetShaderPropertyNames(material.shader);
            if (validProperties.Count == 0)
            {
                return 0;
            }

            SerializedObject serializedMaterial = new SerializedObject(material);
            SerializedProperty savedProperties = serializedMaterial.FindProperty("m_SavedProperties");
            if (savedProperties == null)
            {
                return 0;
            }

            int removedCount = 0;
            removedCount += CleanPropertyList(savedProperties.FindPropertyRelative("m_Floats"), validProperties);
            removedCount += CleanPropertyList(savedProperties.FindPropertyRelative("m_Colors"), validProperties);
            removedCount += CleanPropertyList(savedProperties.FindPropertyRelative("m_TexEnvs"), validProperties);
            removedCount += CleanPropertyList(savedProperties.FindPropertyRelative("m_Ints"), validProperties);

            if (removedCount > 0)
            {
                serializedMaterial.ApplyModifiedPropertiesWithoutUndo();
            }

            return removedCount;
        }

        private static HashSet<string> GetShaderPropertyNames(Shader shader)
        {
            var propertyNames = new HashSet<string>();
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                propertyNames.Add(ShaderUtil.GetPropertyName(shader, i));
            }

            return propertyNames;
        }

        private static int CleanPropertyList(SerializedProperty list, HashSet<string> validProperties)
        {
            if (list == null || !list.isArray)
            {
                return 0;
            }

            int removedCount = 0;
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty property = list.GetArrayElementAtIndex(i);
                string propertyName = GetSavedPropertyName(property);
                if (string.IsNullOrEmpty(propertyName) || validProperties.Contains(propertyName))
                {
                    continue;
                }

                list.DeleteArrayElementAtIndex(i);
                removedCount++;
            }

            return removedCount;
        }

        private static string GetSavedPropertyName(SerializedProperty property)
        {
            SerializedProperty nameProperty = property.FindPropertyRelative("first");
            if (nameProperty != null && !string.IsNullOrEmpty(nameProperty.stringValue))
            {
                return nameProperty.stringValue;
            }

            return property.displayName;
        }

        private static int CleanUnusedKeywords(Material material)
        {
            if (material == null || material.shader == null)
            {
                return 0;
            }

            if (!TryGetValidShaderKeywords(material.shader, out HashSet<string> validKeywords))
            {
                return 0;
            }

            SerializedObject serializedMaterial = new SerializedObject(material);
            SerializedProperty keywordsProperty = serializedMaterial.FindProperty("m_ShaderKeywords");
            if (keywordsProperty == null)
            {
                return 0;
            }

            string oldValue = keywordsProperty.stringValue;
            if (string.IsNullOrWhiteSpace(oldValue))
            {
                return 0;
            }

            string[] oldKeywords = oldValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] newKeywords = oldKeywords.Where(validKeywords.Contains).Distinct().ToArray();
            if (oldKeywords.Length == newKeywords.Length)
            {
                return 0;
            }

            keywordsProperty.stringValue = string.Join(" ", newKeywords);
            serializedMaterial.ApplyModifiedPropertiesWithoutUndo();
            return oldKeywords.Length - newKeywords.Length;
        }

        private static bool TryGetValidShaderKeywords(Shader shader, out HashSet<string> validKeywords)
        {
            validKeywords = new HashSet<string>();
            InitializeKeywordReflection();

            string[] globalKeywords = InvokeKeywordMethod(s_GetShaderGlobalKeywordsMethod, shader);
            string[] localKeywords = InvokeKeywordMethod(s_GetShaderLocalKeywordsMethod, shader);
            if (globalKeywords == null && localKeywords == null)
            {
                return false;
            }

            AddKeywords(validKeywords, globalKeywords);
            AddKeywords(validKeywords, localKeywords);
            return true;
        }

        private static void InitializeKeywordReflection()
        {
            if (s_KeywordReflectionInitialized)
            {
                return;
            }

            s_GetShaderGlobalKeywordsMethod = typeof(ShaderUtil).GetMethod("GetShaderGlobalKeywords",
                BindingFlags.NonPublic | BindingFlags.Static);
            s_GetShaderLocalKeywordsMethod = typeof(ShaderUtil).GetMethod("GetShaderLocalKeywords",
                BindingFlags.NonPublic | BindingFlags.Static);
            s_KeywordReflectionInitialized = true;
        }

        private static string[] InvokeKeywordMethod(MethodInfo method, Shader shader)
        {
            if (method == null)
            {
                return null;
            }

            return method.Invoke(null, new object[] { shader }) as string[];
        }

        private static void AddKeywords(HashSet<string> target, IEnumerable<string> keywords)
        {
            if (keywords == null)
            {
                return;
            }

            foreach (string keyword in keywords)
            {
                if (!string.IsNullOrEmpty(keyword))
                {
                    target.Add(keyword);
                }
            }
        }

        private enum MaterialValueType
        {
            Float,
            Int,
            Color,
            Vector,
            Texture
        }

        private enum ScanConditionLogic
        {
            And,
            Or
        }

        private enum PropertyCompareMode
        {
            Equal,
            NotEqual,
            Greater,
            GreaterOrEqual,
            Less,
            LessOrEqual
        }

        private sealed class MaterialRecord
        {
            public readonly Material material;
            public readonly string path;
            public readonly string folderPath;
            public readonly string shaderName;
            public string lastResult;

            public MaterialRecord(Material material, string path)
            {
                this.material = material;
                this.path = path;
                folderPath = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
                shaderName = material.shader != null ? material.shader.name : "Missing Shader";
            }
        }

        [Serializable]
        private sealed class MaterialScanValueCondition
        {
            public MaterialValueType valueType = MaterialValueType.Float;
            public PropertyCompareMode compareMode = PropertyCompareMode.Equal;
            public float floatValue;
            public int intValue;
            public Color colorValue = Color.white;
            public Vector4 vectorValue;
            public Texture textureValue;

            public bool Match(Material material, string propertyName)
            {
                if (material == null || string.IsNullOrWhiteSpace(propertyName))
                {
                    return false;
                }

                return MatchValue(material, propertyName);
            }

            private bool MatchValue(Material material, string propertyName)
            {
                switch (valueType)
                {
                    case MaterialValueType.Float:
                        return CompareFloat(material.GetFloat(propertyName), floatValue, compareMode);
                    case MaterialValueType.Int:
                        return CompareInt(material.GetInteger(propertyName), intValue, compareMode);
                    case MaterialValueType.Color:
                        return CompareColor(material.GetColor(propertyName), colorValue, compareMode);
                    case MaterialValueType.Vector:
                        return CompareVector(material.GetVector(propertyName), vectorValue, compareMode);
                    case MaterialValueType.Texture:
                        return CompareObject(material.GetTexture(propertyName), textureValue, compareMode);
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            private static bool CompareFloat(float currentValue, float targetValue, PropertyCompareMode compareMode)
            {
                switch (compareMode)
                {
                    case PropertyCompareMode.Equal:
                        return Mathf.Approximately(currentValue, targetValue);
                    case PropertyCompareMode.NotEqual:
                        return !Mathf.Approximately(currentValue, targetValue);
                    case PropertyCompareMode.Greater:
                        return currentValue > targetValue;
                    case PropertyCompareMode.GreaterOrEqual:
                        return currentValue > targetValue || Mathf.Approximately(currentValue, targetValue);
                    case PropertyCompareMode.Less:
                        return currentValue < targetValue;
                    case PropertyCompareMode.LessOrEqual:
                        return currentValue < targetValue || Mathf.Approximately(currentValue, targetValue);
                    default:
                        throw new ArgumentOutOfRangeException(nameof(compareMode), compareMode, null);
                }
            }

            private static bool CompareInt(int currentValue, int targetValue, PropertyCompareMode compareMode)
            {
                switch (compareMode)
                {
                    case PropertyCompareMode.Equal:
                        return currentValue == targetValue;
                    case PropertyCompareMode.NotEqual:
                        return currentValue != targetValue;
                    case PropertyCompareMode.Greater:
                        return currentValue > targetValue;
                    case PropertyCompareMode.GreaterOrEqual:
                        return currentValue >= targetValue;
                    case PropertyCompareMode.Less:
                        return currentValue < targetValue;
                    case PropertyCompareMode.LessOrEqual:
                        return currentValue <= targetValue;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(compareMode), compareMode, null);
                }
            }

            private static bool CompareObject(UnityEngine.Object currentValue, UnityEngine.Object targetValue,
                PropertyCompareMode compareMode)
            {
                bool isEqual = currentValue == targetValue;
                return compareMode == PropertyCompareMode.NotEqual ? !isEqual : isEqual;
            }

            private static bool CompareColor(Color currentValue, Color targetValue, PropertyCompareMode compareMode)
            {
                bool isEqual = Mathf.Approximately(currentValue.r, targetValue.r) &&
                               Mathf.Approximately(currentValue.g, targetValue.g) &&
                               Mathf.Approximately(currentValue.b, targetValue.b) &&
                               Mathf.Approximately(currentValue.a, targetValue.a);
                return compareMode == PropertyCompareMode.NotEqual ? !isEqual : isEqual;
            }

            private static bool CompareVector(Vector4 currentValue, Vector4 targetValue, PropertyCompareMode compareMode)
            {
                bool isEqual = Mathf.Approximately(currentValue.x, targetValue.x) &&
                               Mathf.Approximately(currentValue.y, targetValue.y) &&
                               Mathf.Approximately(currentValue.z, targetValue.z) &&
                               Mathf.Approximately(currentValue.w, targetValue.w);
                return compareMode == PropertyCompareMode.NotEqual ? !isEqual : isEqual;
            }
        }

        [Serializable]
        private sealed class MaterialScanPropertyCondition
        {
            public string propertyName = string.Empty;
            public ScanConditionLogic valueConditionLogic = ScanConditionLogic.And;
            public List<MaterialScanValueCondition> valueConditions =
                new List<MaterialScanValueCondition>();

            public bool Match(Material material)
            {
                if (material == null || string.IsNullOrWhiteSpace(propertyName) || !material.HasProperty(propertyName))
                {
                    return false;
                }

                if (valueConditions == null || valueConditions.Count == 0)
                {
                    return true;
                }

                return valueConditionLogic == ScanConditionLogic.And
                    ? valueConditions.All(condition => condition.Match(material, propertyName))
                    : valueConditions.Any(condition => condition.Match(material, propertyName));
            }
        }

        [Serializable]
        private sealed class MaterialPropertyOverride
        {
            public string propertyName = string.Empty;
            public MaterialValueType valueType = MaterialValueType.Float;
            public float floatValue;
            public int intValue;
            public Color colorValue = Color.white;
            public Vector4 vectorValue;
            public Texture textureValue;
            public bool setTextureScale;
            public bool setTextureOffset;
            public Vector2 textureScale = Vector2.one;
            public Vector2 textureOffset;
            public bool wasChanged;

            public bool Apply(Material material)
            {
                wasChanged = false;
                if (material == null || string.IsNullOrWhiteSpace(propertyName) || !material.HasProperty(propertyName))
                {
                    return false;
                }

                switch (valueType)
                {
                    case MaterialValueType.Float:
                        wasChanged = !Mathf.Approximately(material.GetFloat(propertyName), floatValue);
                        if (wasChanged)
                        {
                            material.SetFloat(propertyName, floatValue);
                        }
                        break;
                    case MaterialValueType.Int:
                        wasChanged = material.GetInteger(propertyName) != intValue;
                        if (wasChanged)
                        {
                            material.SetInteger(propertyName, intValue);
                        }
                        break;
                    case MaterialValueType.Color:
                        wasChanged = material.GetColor(propertyName) != colorValue;
                        if (wasChanged)
                        {
                            material.SetColor(propertyName, colorValue);
                        }
                        break;
                    case MaterialValueType.Vector:
                        wasChanged = material.GetVector(propertyName) != vectorValue;
                        if (wasChanged)
                        {
                            material.SetVector(propertyName, vectorValue);
                        }
                        break;
                    case MaterialValueType.Texture:
                        ApplyTexture(material);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return true;
            }

            private void ApplyTexture(Material material)
            {
                bool textureChanged = material.GetTexture(propertyName) != textureValue;
                if (textureChanged)
                {
                    material.SetTexture(propertyName, textureValue);
                }

                wasChanged = textureChanged;
                if (setTextureScale)
                {
                    bool scaleChanged = material.GetTextureScale(propertyName) != textureScale;
                    if (scaleChanged)
                    {
                        material.SetTextureScale(propertyName, textureScale);
                    }

                    wasChanged |= scaleChanged;
                }

                if (setTextureOffset)
                {
                    bool offsetChanged = material.GetTextureOffset(propertyName) != textureOffset;
                    if (offsetChanged)
                    {
                        material.SetTextureOffset(propertyName, textureOffset);
                    }

                    wasChanged |= offsetChanged;
                }
            }
        }

        private struct MaterialChangeResult
        {
            public bool changed;
            public int changedPropertyCount;
            public int missingPropertyCount;
            public int removedSavedPropertyCount;
            public int removedKeywordCount;

            public string ToDisplayText()
            {
                return $"清理属性:{removedSavedPropertyCount} 清理关键字:{removedKeywordCount} 缺失属性:{missingPropertyCount}";
            }
        }

        private sealed class ShaderPropertyBrowserPopup : EditorWindow
        {
            private Shader m_Shader;
            private string m_SearchText = string.Empty;
            private Vector2 m_ScrollPosition;

            public void Initialize(Shader shader)
            {
                m_Shader = shader;
                titleContent = new GUIContent("Shader属性");
                minSize = new Vector2(520, 360);
            }

            private void OnGUI()
            {
                if (m_Shader == null)
                {
                    EditorGUILayout.HelpBox("Shader无效。", MessageType.Warning);
                    return;
                }

                EditorGUILayout.ObjectField("Shader", m_Shader, typeof(Shader), false);
                m_SearchText = EditorGUILayout.TextField("查找", m_SearchText);

                EditorGUILayout.BeginHorizontal(EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Inspector显示名", GUILayout.MinWidth(180));
                EditorGUILayout.LabelField("Properties属性名", GUILayout.MinWidth(180));
                GUILayout.Space(62);
                EditorGUILayout.EndHorizontal();

                m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
                foreach (ShaderPropertyInfo propertyInfo in GetFilteredShaderProperties())
                {
                    DrawShaderPropertyInfo(propertyInfo);
                }

                EditorGUILayout.EndScrollView();
            }

            private IEnumerable<ShaderPropertyInfo> GetFilteredShaderProperties()
            {
                string searchText = m_SearchText?.Trim();
                int propertyCount = ShaderUtil.GetPropertyCount(m_Shader);
                for (int i = 0; i < propertyCount; i++)
                {
                    var propertyInfo = new ShaderPropertyInfo(
                        ShaderUtil.GetPropertyDescription(m_Shader, i),
                        ShaderUtil.GetPropertyName(m_Shader, i));
                    if (string.IsNullOrWhiteSpace(searchText) || propertyInfo.Contains(searchText))
                    {
                        yield return propertyInfo;
                    }
                }
            }

            private static void DrawShaderPropertyInfo(ShaderPropertyInfo propertyInfo)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.SelectableLabel(propertyInfo.displayName, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.SelectableLabel(propertyInfo.propertyName, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("复制", GUILayout.Width(52)))
                {
                    EditorGUIUtility.systemCopyBuffer = propertyInfo.propertyName;
                }

                EditorGUILayout.EndHorizontal();
            }

            private readonly struct ShaderPropertyInfo
            {
                public readonly string displayName;
                public readonly string propertyName;

                public ShaderPropertyInfo(string displayName, string propertyName)
                {
                    this.displayName = string.IsNullOrEmpty(displayName) ? propertyName : displayName;
                    this.propertyName = propertyName;
                }

                public bool Contains(string searchText)
                {
                    return displayName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           propertyName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
        }

        private sealed class MaterialPropertyBrowserPopup : EditorWindow
        {
            private Material m_Material;
            private string m_SearchText = string.Empty;
            private Vector2 m_ScrollPosition;

            public void Initialize(Material material)
            {
                m_Material = material;
                titleContent = new GUIContent("材质属性");
                minSize = new Vector2(720, 420);
            }

            private void OnGUI()
            {
                if (m_Material == null)
                {
                    EditorGUILayout.HelpBox("Material无效。", MessageType.Warning);
                    return;
                }

                EditorGUILayout.ObjectField("Material", m_Material, typeof(Material), false);
                EditorGUILayout.ObjectField("Shader", m_Material.shader, typeof(Shader), false);
                m_SearchText = EditorGUILayout.TextField("查找", m_SearchText);

                EditorGUILayout.BeginHorizontal(EditorStyles.boldLabel);
                EditorGUILayout.LabelField("状态", GUILayout.Width(92));
                EditorGUILayout.LabelField("来源", GUILayout.Width(104));
                EditorGUILayout.LabelField("Inspector显示名", GUILayout.MinWidth(160));
                EditorGUILayout.LabelField("属性名", GUILayout.MinWidth(160));
                EditorGUILayout.LabelField("类型/保存类型", GUILayout.Width(96));
                GUILayout.Space(62);
                EditorGUILayout.EndHorizontal();

                m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
                foreach (MaterialPropertyInfo propertyInfo in GetFilteredMaterialProperties())
                {
                    DrawMaterialPropertyInfo(propertyInfo);
                }

                EditorGUILayout.EndScrollView();
            }

            private IEnumerable<MaterialPropertyInfo> GetFilteredMaterialProperties()
            {
                string searchText = m_SearchText?.Trim();
                foreach (MaterialPropertyInfo propertyInfo in BuildMaterialProperties())
                {
                    if (string.IsNullOrWhiteSpace(searchText) || propertyInfo.Contains(searchText))
                    {
                        yield return propertyInfo;
                    }
                }
            }

            private IEnumerable<MaterialPropertyInfo> BuildMaterialProperties()
            {
                Dictionary<string, string> savedProperties = GetSavedMaterialProperties(m_Material)
                    .GroupBy(property => property.propertyName)
                    .ToDictionary(group => group.Key, group => string.Join(",", group.Select(property => property.propertyType).Distinct()));
                HashSet<string> emittedProperties = new HashSet<string>(StringComparer.Ordinal);
                if (m_Material.shader != null)
                {
                    int propertyCount = ShaderUtil.GetPropertyCount(m_Material.shader);
                    for (int i = 0; i < propertyCount; i++)
                    {
                        string propertyName = ShaderUtil.GetPropertyName(m_Material.shader, i);
                        emittedProperties.Add(propertyName);
                        bool hasSavedValue = savedProperties.TryGetValue(propertyName, out string savedType);
                        string shaderType = ShaderUtil.GetPropertyType(m_Material.shader, i).ToString();
                        yield return new MaterialPropertyInfo(
                            "Valid",
                            hasSavedValue ? "Shader+Saved" : "Shader",
                            ShaderUtil.GetPropertyDescription(m_Material.shader, i),
                            propertyName,
                            hasSavedValue ? $"{shaderType}/{savedType}" : shaderType);
                    }
                }

                foreach (SavedMaterialPropertyInfo savedProperty in savedProperties.Select(pair =>
                             new SavedMaterialPropertyInfo(pair.Key, pair.Value)))
                {
                    if (emittedProperties.Contains(savedProperty.propertyName))
                    {
                        continue;
                    }

                    yield return new MaterialPropertyInfo(
                        "Invalid",
                        "Saved",
                        string.Empty,
                        savedProperty.propertyName,
                        savedProperty.propertyType);
                }
            }

            private static IEnumerable<SavedMaterialPropertyInfo> GetSavedMaterialProperties(Material material)
            {
                SerializedObject serializedMaterial = new SerializedObject(material);
                SerializedProperty savedProperties = serializedMaterial.FindProperty("m_SavedProperties");
                if (savedProperties == null)
                {
                    yield break;
                }

                foreach (SavedMaterialPropertyInfo propertyInfo in GetSavedPropertyList(
                             savedProperties.FindPropertyRelative("m_Floats"), "Float"))
                {
                    yield return propertyInfo;
                }

                foreach (SavedMaterialPropertyInfo propertyInfo in GetSavedPropertyList(
                             savedProperties.FindPropertyRelative("m_Colors"), "Color"))
                {
                    yield return propertyInfo;
                }

                foreach (SavedMaterialPropertyInfo propertyInfo in GetSavedPropertyList(
                             savedProperties.FindPropertyRelative("m_TexEnvs"), "Texture"))
                {
                    yield return propertyInfo;
                }

                foreach (SavedMaterialPropertyInfo propertyInfo in GetSavedPropertyList(
                             savedProperties.FindPropertyRelative("m_Ints"), "Int"))
                {
                    yield return propertyInfo;
                }
            }

            private static IEnumerable<SavedMaterialPropertyInfo> GetSavedPropertyList(
                SerializedProperty list,
                string propertyType)
            {
                if (list == null || !list.isArray)
                {
                    yield break;
                }

                for (int i = 0; i < list.arraySize; i++)
                {
                    string propertyName = GetSavedPropertyName(list.GetArrayElementAtIndex(i));
                    if (!string.IsNullOrEmpty(propertyName))
                    {
                        yield return new SavedMaterialPropertyInfo(propertyName, propertyType);
                    }
                }
            }

            private static void DrawMaterialPropertyInfo(MaterialPropertyInfo propertyInfo)
            {
                bool isInvalid = propertyInfo.status == "Invalid";
                GUIStyle statusStyle = isInvalid ? GetInvalidPropertyLabelStyle() : EditorStyles.label;
                Color oldBackgroundColor = GUI.backgroundColor;
                if (isInvalid)
                {
                    GUI.backgroundColor = new Color(1f, 0.35f, 0.35f, 1f);
                }

                EditorGUILayout.BeginHorizontal("box");
                GUI.backgroundColor = oldBackgroundColor;
                EditorGUILayout.LabelField(propertyInfo.status, statusStyle, GUILayout.Width(92));
                EditorGUILayout.LabelField(propertyInfo.source, statusStyle, GUILayout.Width(104));
                EditorGUILayout.SelectableLabel(propertyInfo.displayName, statusStyle,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.SelectableLabel(propertyInfo.propertyName, statusStyle,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField(propertyInfo.propertyType, statusStyle, GUILayout.Width(96));
                if (GUILayout.Button("复制", GUILayout.Width(52)))
                {
                    EditorGUIUtility.systemCopyBuffer = propertyInfo.propertyName;
                }

                EditorGUILayout.EndHorizontal();
            }

            private static GUIStyle GetInvalidPropertyLabelStyle()
            {
                return new GUIStyle(EditorStyles.label)
                {
                    normal =
                    {
                        textColor = new Color(1f, 0.35f, 0.35f, 1f)
                    }
                };
            }

            private readonly struct MaterialPropertyInfo
            {
                public readonly string status;
                public readonly string source;
                public readonly string displayName;
                public readonly string propertyName;
                public readonly string propertyType;

                public MaterialPropertyInfo(
                    string status,
                    string source,
                    string displayName,
                    string propertyName,
                    string propertyType)
                {
                    this.status = status;
                    this.source = source;
                    this.displayName = string.IsNullOrEmpty(displayName) ? propertyName : displayName;
                    this.propertyName = propertyName;
                    this.propertyType = propertyType;
                }

                public bool Contains(string searchText)
                {
                    return status.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           source.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           displayName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           propertyName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           propertyType.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }

            private readonly struct SavedMaterialPropertyInfo
            {
                public readonly string propertyName;
                public readonly string propertyType;

                public SavedMaterialPropertyInfo(string propertyName, string propertyType)
                {
                    this.propertyName = propertyName;
                    this.propertyType = propertyType;
                }
            }
        }
    }
}
