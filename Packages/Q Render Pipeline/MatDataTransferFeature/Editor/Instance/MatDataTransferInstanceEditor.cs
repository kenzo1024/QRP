using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Rendering.MatDataTransfer.Runtime;

namespace Rendering.MatDataTransfer.Editor
{
    [CustomEditor(typeof(MatDataTransferInstance))]
    public sealed class MatDataTransferInstanceEditor : UnityEditor.Editor
    {
        private const float LabelWidth = 96f;
        private const float SlotLabelWidth = 112f;
        private const float CountLabelWidth = 150f;
        private const float RefreshButtonWidth = 112f;
        private const float RowGap = 6f;
        private const float BindingRowGap = 2f;
        private const float BindingRowIndent = 18f;
        private const float InnerFoldoutLeftPadding = 12f;
        private const int CatalogPropertyPanelContentLeftPadding = 6;
        private const float BindingTitleWidth = 70f;
        private const float BindingIdTitleWidth = 72f;
        private const float BindingIdValueWidth = 74f;
        private const float BindingSlotTitleWidth = 48f;
        private const float BindingSlotValueWidth = 32f;
        private const float KeyTypeWidth = 70f;
        private const float KeyStatusWidth = 70f;
        private const float KeyPropertyWidth = 130f;

        private static bool s_ShowInstanceInfo = true;
        private static bool s_ShowBindings = true;
        private static bool s_ShowCatalogKeys = true;
        private static bool s_ShowDebugInfo = true;
        private static readonly Dictionary<string, bool> s_ShaderBindingFoldouts =
            new Dictionary<string, bool>();
        private static readonly Dictionary<string, bool> s_CatalogKeyPropertyFoldouts =
            new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            MatDataTransferInstance instance = (MatDataTransferInstance)target;

            DrawInstanceInfo(instance);
            EditorGUILayout.Space(8);
            DrawCatalogKeys(instance);
            EditorGUILayout.Space(8);
            DrawDebugInfo(instance);
            EditorGUILayout.Space(8);
            DrawBindings(instance);
        }

        private static void RefreshBindings(MatDataTransferInstance instance)
        {
            if (instance == null)
                return;

            instance.RefreshBindings();
            EditorUtility.SetDirty(instance);
        }

        private void DrawInstanceInfo(MatDataTransferInstance instance)
        {
            s_ShowInstanceInfo = InspectorStyleLibrary.DrawFoldoutLayout(
                s_ShowInstanceInfo,
                "Instance Info",
                false);
            if (!s_ShowInstanceInfo)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                InspectorStyleLibrary.DrawCopyableReadOnlyTailText("Source ID", instance.SourceId, LabelWidth);
                InspectorStyleLibrary.DrawCopyableParameterValue("Instance ID", instance.InstanceId.ToString(), LabelWidth);
                InspectorStyleLibrary.DrawParameterValue("Ready", instance.IsReady ? "Yes" : "No", LabelWidth);
            }
        }

        private void DrawBindings(MatDataTransferInstance instance)
        {
            s_ShowBindings = DrawRendererBindingsHeader(instance);
            if (!s_ShowBindings)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (instance.Bindings == null || instance.Bindings.Count == 0)
                {
                    InspectorStyleLibrary.DrawDescription("No bindings found.");
                }
                else
                {
                    Dictionary<string, List<RendererMaterialBinding>> grouped = GetBindingsByShaderSafe(instance);
                    List<string> shaderNames = GetSortedShaderNames(grouped);
                    for (int i = 0; i < shaderNames.Count; i++)
                        DrawShaderGroup(shaderNames[i], grouped[shaderNames[i]]);
                }

                EditorGUILayout.Space(4);
            }
        }

        private bool DrawRendererBindingsHeader(MatDataTransferInstance instance)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect rect = EditorGUI.IndentedRect(row);
            float buttonWidth = Mathf.Min(RefreshButtonWidth, rect.width);
            Rect buttonRect = new Rect(
                rect.xMax - buttonWidth,
                rect.y,
                buttonWidth,
                rect.height);
            Rect foldoutRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, rect.width - buttonWidth - RowGap),
                rect.height);

            bool expanded = InspectorStyleLibrary.DrawFoldout(
                foldoutRect,
                s_ShowBindings,
                "Renderer Bindings",
                false);

            if (GUI.Button(buttonRect, "Refresh Bindings", EditorStyles.miniButton))
                RefreshBindings(instance);

            return expanded;
        }

        private void DrawCatalogKeys(MatDataTransferInstance instance)
        {
            s_ShowCatalogKeys = InspectorStyleLibrary.DrawFoldoutLayout(
                s_ShowCatalogKeys,
                "Catalog Keys",
                false);
            if (!s_ShowCatalogKeys)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MatDataTransferFeature feature = MatDataTransferFeature.Instance;
                if (feature == null)
                {
                    InspectorStyleLibrary.DrawDescription("No active MatDataTransferFeature found.");
                    return;
                }

                Dictionary<string, List<RendererMaterialBinding>> grouped = GetBindingsByShaderSafe(instance);
                if (grouped.Count == 0)
                {
                    InspectorStyleLibrary.DrawDescription("No shader bindings found.");
                    return;
                }

                List<string> shaderNames = GetSortedShaderNames(grouped);
                for (int i = 0; i < shaderNames.Count; i++)
                    DrawShaderCatalogKeys(feature, shaderNames[i], grouped[shaderNames[i]].Count);
            }
        }

        private void DrawShaderCatalogKeys(
            MatDataTransferFeature feature,
            string shaderName,
            int bindingCount)
        {
            if (!feature.TryGetCatalogForShader(shaderName, out ShaderPropertyCatalog catalog))
            {
                DrawMissingCatalog(shaderName, bindingCount);
                EditorGUILayout.Space(2);
                return;
            }

            DrawCatalogHeader(shaderName, catalog, bindingCount);
            DrawCatalogFileInfo(catalog);
            DrawCatalogPropertyRows(catalog);
            EditorGUILayout.Space(2);
        }

        private void DrawMissingCatalog(string shaderName, int bindingCount)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect rect = EditorGUI.IndentedRect(row);
            Rect countRect = new Rect(
                rect.xMax - CountLabelWidth,
                rect.y,
                CountLabelWidth,
                rect.height);
            Rect shaderRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, rect.width - CountLabelWidth - RowGap),
                rect.height);

            InspectorStyleLibrary.DrawTailLabel(shaderRect, shaderName, InspectorStyleLibrary.Title, true);
            GUI.Label(countRect, bindingCount + " slot(s), catalog missing", InspectorStyleLibrary.RightAlignedDescription);

            EditorGUI.indentLevel++;
            InspectorStyleLibrary.DrawDescription("No catalog in current feature for this shader.");
            EditorGUI.indentLevel--;
        }

        private void DrawCatalogHeader(
            string shaderName,
            ShaderPropertyCatalog catalog,
            int bindingCount)
        {
            int keyCount = catalog != null && catalog.Properties != null ? catalog.Properties.Count : 0;
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect rect = EditorGUI.IndentedRect(row);
            Rect countRect = new Rect(
                rect.xMax - CountLabelWidth,
                rect.y,
                CountLabelWidth,
                rect.height);
            Rect shaderRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, rect.width - CountLabelWidth - RowGap),
                rect.height);

            InspectorStyleLibrary.DrawTailLabel(shaderRect, shaderName, InspectorStyleLibrary.Title, true);
            GUI.Label(countRect, bindingCount + " slot(s), " + keyCount + " key(s)", InspectorStyleLibrary.RightAlignedDescription);
        }

        private void DrawCatalogFileInfo(ShaderPropertyCatalog catalog)
        {
            EditorGUI.indentLevel++;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Catalog", catalog, typeof(ShaderPropertyCatalog), false);

            InspectorStyleLibrary.DrawCopyableReadOnlyTailText("Path", AssetDatabase.GetAssetPath(catalog), LabelWidth);
            InspectorStyleLibrary.DrawCopyableParameterValue("Version", catalog.CatalogVersion, LabelWidth);
            InspectorStyleLibrary.DrawCopyableParameterValue("Synced", catalog.SyncTime, LabelWidth);
            EditorGUI.indentLevel--;
        }

        private void DrawCatalogPropertyRows(ShaderPropertyCatalog catalog)
        {
            if (catalog == null || catalog.Properties == null || catalog.Properties.Count == 0)
            {
                EditorGUI.indentLevel++;
                InspectorStyleLibrary.DrawDescription("Catalog has no keys.");
                EditorGUI.indentLevel--;
                return;
            }

            string foldoutKey = BuildCatalogPropertyFoldoutKey(catalog);
            bool expanded = GetFoldoutState(s_CatalogKeyPropertyFoldouts, foldoutKey, true);
            expanded = InspectorStyleLibrary.DrawFoldoutLayout(
                expanded,
                "Key Properties",
                catalog.Properties.Count + " key(s)",
                false,
                InspectorStyleLibrary.FoldoutPanelLeftPadding);
            SetFoldoutState(s_CatalogKeyPropertyFoldouts, foldoutKey, expanded);
            if (!expanded)
                return;

            using (InspectorStyleLibrary.BeginIndentedPanelLayout(
                GetCatalogPropertyPanelLeftPadding(),
                CatalogPropertyPanelContentLeftPadding))
            {
                DrawCatalogPropertyHeader();
                for (int i = 0; i < catalog.Properties.Count; i++)
                    DrawCatalogPropertyRow(catalog.Properties[i]);
            }
        }

        private void DrawCatalogPropertyHeader()
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect rect = EditorGUI.IndentedRect(row);
            Rect keyRect;
            Rect propertyRect;
            Rect typeRect;
            Rect statusRect;
            SplitCatalogPropertyRow(rect, out keyRect, out propertyRect, out typeRect, out statusRect);

            GUI.Label(keyRect, "Key", InspectorStyleLibrary.Description);
            GUI.Label(propertyRect, "Property", InspectorStyleLibrary.Description);
            GUI.Label(typeRect, "Type", InspectorStyleLibrary.Description);
            GUI.Label(statusRect, "Status", InspectorStyleLibrary.Description);
        }

        private void DrawCatalogPropertyRow(CatalogProperty property)
        {
            if (property == null || property.PropertyInfo == null)
                return;

            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect rect = EditorGUI.IndentedRect(row);
            Rect keyRect;
            Rect propertyRect;
            Rect typeRect;
            Rect statusRect;
            SplitCatalogPropertyRow(rect, out keyRect, out propertyRect, out typeRect, out statusRect);

            InspectorStyleLibrary.DrawCopyableTailLabel(keyRect, property.SuggestedSemanticKey, EditorStyles.label, true);
            InspectorStyleLibrary.DrawCopyableTailLabel(propertyRect, property.PropertyInfo.PropertyName, InspectorStyleLibrary.ParameterName, false);
            GUI.Label(typeRect, property.PropertyInfo.ValueType.ToString(), InspectorStyleLibrary.ParameterName);
            GUI.Label(statusRect, property.Status.ToString(), InspectorStyleLibrary.Description);
        }

        private static void SplitCatalogPropertyRow(
            Rect rect,
            out Rect keyRect,
            out Rect propertyRect,
            out Rect typeRect,
            out Rect statusRect)
        {
            statusRect = new Rect(
                rect.xMax - KeyStatusWidth,
                rect.y,
                KeyStatusWidth,
                rect.height);
            typeRect = new Rect(
                statusRect.x - RowGap - KeyTypeWidth,
                rect.y,
                KeyTypeWidth,
                rect.height);
            propertyRect = new Rect(
                typeRect.x - RowGap - KeyPropertyWidth,
                rect.y,
                KeyPropertyWidth,
                rect.height);
            keyRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, propertyRect.x - RowGap - rect.x),
                rect.height);
        }

        private static float GetCatalogPropertyPanelLeftPadding()
        {
            return Mathf.Max(0f, InspectorStyleLibrary.FoldoutPanelLeftPadding - CatalogPropertyPanelContentLeftPadding);
        }

        private void DrawShaderGroup(string shaderName, List<RendererMaterialBinding> bindings)
        {
            int rendererCount = CountUniqueRenderers(bindings);
            int slotCount = bindings != null ? bindings.Count : 0;
            if (!DrawShaderGroupHeader(shaderName, rendererCount, slotCount))
            {
                EditorGUILayout.Space(2);
                return;
            }

            if (bindings != null)
            {
                bindings.Sort(CompareBindings);
                for (int i = 0; i < bindings.Count; i++)
                    DrawBindingRow(bindings[i]);
            }

            EditorGUILayout.Space(2);
        }

        private bool DrawShaderGroupHeader(string shaderName, int rendererCount, int slotCount)
        {
            bool expanded = GetFoldoutState(s_ShaderBindingFoldouts, shaderName, true);
            expanded = InspectorStyleLibrary.DrawFoldoutLayout(
                expanded,
                shaderName,
                BuildShaderGroupSummary(rendererCount, slotCount),
                true,
                InnerFoldoutLeftPadding);
            SetFoldoutState(s_ShaderBindingFoldouts, shaderName, expanded);
            return expanded;
        }

        private void DrawBindingRow(RendererMaterialBinding binding)
        {
            if (binding == null || binding.Renderer == null)
            {
                InspectorStyleLibrary.DrawDescription("Missing renderer binding.");
                return;
            }

            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect rect = EditorGUI.IndentedRect(row);
            Rect rendererRect = GetIndentedBindingRect(rect);

            using (new EditorGUI.DisabledScope(true))
                EditorGUI.ObjectField(rendererRect, GUIContent.none, binding.Renderer, typeof(Renderer), true);

            DrawBindingInfoRow(binding);
            DrawBindingIdRow(binding);
            DrawBindingTraceRow(binding);
        }

        private static string BuildMaterialLabel(RendererMaterialBinding binding)
        {
            return string.IsNullOrEmpty(binding.MaterialName)
                ? "<none>"
                : binding.MaterialName;
        }

        private void DrawBindingInfoRow(RendererMaterialBinding binding)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect rect = GetIndentedBindingRect(EditorGUI.IndentedRect(row), 18f);
            DrawBindingTitleValue(rect, "Material", BuildMaterialLabel(binding), BindingTitleWidth, true);
        }

        private void DrawBindingIdRow(RendererMaterialBinding binding)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect rect = GetIndentedBindingRect(EditorGUI.IndentedRect(row), 18f);
            Rect remaining = rect;

            DrawBindingFixedTitleValue(ref remaining, "Renderer Id", binding.RendererId.ToString());
            DrawBindingFixedTitleValue(ref remaining, "Slot Id", binding.MaterialSlot.ToString(), BindingSlotTitleWidth, BindingSlotValueWidth);
            DrawBindingFixedTitleValue(ref remaining, "Material Id", binding.MaterialId.ToString());
        }

        private void DrawBindingTraceRow(RendererMaterialBinding binding)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect rect = GetIndentedBindingRect(EditorGUI.IndentedRect(row), 18f);
            Rect titleRect = new Rect(rect.x, rect.y, BindingTitleWidth, rect.height);
            Rect valueRect = new Rect(
                titleRect.xMax + BindingRowGap,
                rect.y,
                Mathf.Max(0f, rect.xMax - titleRect.xMax - BindingRowGap),
                rect.height);

            GUI.Label(titleRect, "Trace Id", InspectorStyleLibrary.ParameterName);
            InspectorStyleLibrary.DrawCopyableTailLabel(valueRect, GetTraceId(binding), InspectorStyleLibrary.Description, true);
        }

        private static Rect GetIndentedBindingRect(Rect rect, float extraIndent = 0f)
        {
            rect.x += BindingRowIndent + extraIndent;
            rect.width = Mathf.Max(0f, rect.width - BindingRowIndent - extraIndent);
            return rect;
        }

        private static string GetTraceId(RendererMaterialBinding binding)
        {
            return string.IsNullOrEmpty(binding.MaterialTraceId)
                ? "<none>"
                : binding.MaterialTraceId;
        }

        private static void DrawBindingTitleValue(
            Rect rect,
            string title,
            string value,
            float titleWidth,
            bool preferPathSegments)
        {
            Rect titleRect = new Rect(rect.x, rect.y, titleWidth, rect.height);
            Rect valueRect = new Rect(
                titleRect.xMax + BindingRowGap,
                rect.y,
                Mathf.Max(0f, rect.xMax - titleRect.xMax - BindingRowGap),
                rect.height);

            GUI.Label(titleRect, title, InspectorStyleLibrary.ParameterName);
            InspectorStyleLibrary.DrawCopyableTailLabel(valueRect, value ?? string.Empty, InspectorStyleLibrary.Description, preferPathSegments);
        }

        private static void DrawBindingFixedTitleValue(
            ref Rect remaining,
            string title,
            string value,
            float titleWidth = BindingIdTitleWidth,
            float fixedValueWidth = BindingIdValueWidth)
        {
            if (remaining.width <= 0f)
                return;

            Rect titleRect = TakeRect(ref remaining, titleWidth);
            Rect valueRect = TakeRect(ref remaining, fixedValueWidth);

            GUI.Label(titleRect, title, InspectorStyleLibrary.ParameterName);
            InspectorStyleLibrary.DrawCopyableTailLabel(valueRect, value ?? string.Empty, InspectorStyleLibrary.Description, false);
        }

        private static Rect TakeRect(ref Rect remaining, float width)
        {
            width = Mathf.Min(width, Mathf.Max(0f, remaining.width));
            Rect rect = new Rect(remaining.x, remaining.y, width, remaining.height);
            remaining.x += width + BindingRowGap;
            remaining.width = Mathf.Max(0f, remaining.width - width - BindingRowGap);
            return rect;
        }

        private void DrawDebugInfo(MatDataTransferInstance instance)
        {
            s_ShowDebugInfo = InspectorStyleLibrary.DrawFoldoutLayout(
                s_ShowDebugInfo,
                "Debug Info",
                false);
            if (!s_ShowDebugInfo)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int totalBindings = instance.Bindings != null ? instance.Bindings.Count : 0;
                InspectorStyleLibrary.DrawParameterValue(
                    "Total Bindings",
                    totalBindings.ToString(),
                    LabelWidth);

                Dictionary<string, List<RendererMaterialBinding>> grouped = GetBindingsByShaderSafe(instance);
                InspectorStyleLibrary.DrawParameterValue(
                    "Unique Shaders",
                    grouped.Count.ToString(),
                    LabelWidth);

                EditorGUILayout.Space(4);
                if (grouped.Count > 0)
                {
                    InspectorStyleLibrary.DrawTitle("Shader Breakdown:");
                    EditorGUI.indentLevel++;
                    List<string> shaderNames = GetSortedShaderNames(grouped);
                    for (int i = 0; i < shaderNames.Count; i++)
                        DrawShaderBreakdownRow(shaderNames[i], grouped[shaderNames[i]].Count);

                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawShaderBreakdownRow(string shaderName, int bindingCount)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect rect = EditorGUI.IndentedRect(row);
            Rect countRect = new Rect(
                rect.xMax - CountLabelWidth,
                rect.y,
                CountLabelWidth,
                rect.height);
            Rect shaderRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, rect.width - CountLabelWidth - RowGap),
                rect.height);

            InspectorStyleLibrary.DrawTailLabel(shaderRect, shaderName, EditorStyles.label, true);
            GUI.Label(countRect, bindingCount + " binding(s)", InspectorStyleLibrary.RightAlignedDescription);
        }

        private static List<string> GetSortedShaderNames(Dictionary<string, List<RendererMaterialBinding>> grouped)
        {
            List<string> shaderNames = new List<string>();
            if (grouped == null)
                return shaderNames;

            foreach (string shaderName in grouped.Keys)
                shaderNames.Add(shaderName);

            shaderNames.Sort(System.StringComparer.Ordinal);
            return shaderNames;
        }

        private static Dictionary<string, List<RendererMaterialBinding>> GetBindingsByShaderSafe(
            MatDataTransferInstance instance)
        {
            if (instance == null || instance.Bindings == null)
                return new Dictionary<string, List<RendererMaterialBinding>>();

            return instance.GetBindingsByShader();
        }

        private static int CountUniqueRenderers(List<RendererMaterialBinding> bindings)
        {
            if (bindings == null)
                return 0;

            HashSet<Renderer> renderers = new HashSet<Renderer>();
            for (int i = 0; i < bindings.Count; i++)
            {
                RendererMaterialBinding binding = bindings[i];
                if (binding == null || binding.Renderer == null)
                    continue;

                renderers.Add(binding.Renderer);
            }

            return renderers.Count;
        }

        private static int CompareBindings(RendererMaterialBinding left, RendererMaterialBinding right)
        {
            string leftName = left?.Renderer != null ? left.Renderer.name : string.Empty;
            string rightName = right?.Renderer != null ? right.Renderer.name : string.Empty;
            int rendererName = System.StringComparer.Ordinal.Compare(leftName, rightName);
            if (rendererName != 0)
                return rendererName;

            int leftSlot = left != null ? left.MaterialSlot : -1;
            int rightSlot = right != null ? right.MaterialSlot : -1;
            return leftSlot.CompareTo(rightSlot);
        }

        private static string BuildShaderGroupSummary(int rendererCount, int slotCount)
        {
            return rendererCount + " renderer(s), " + slotCount + " slot(s)";
        }

        private static bool GetFoldoutState(
            Dictionary<string, bool> foldouts,
            string key,
            bool defaultValue)
        {
            if (foldouts == null || string.IsNullOrEmpty(key))
                return defaultValue;

            bool expanded;
            return foldouts.TryGetValue(key, out expanded) ? expanded : defaultValue;
        }

        private static void SetFoldoutState(
            Dictionary<string, bool> foldouts,
            string key,
            bool expanded)
        {
            if (foldouts == null || string.IsNullOrEmpty(key))
                return;

            foldouts[key] = expanded;
        }

        private static string BuildCatalogPropertyFoldoutKey(ShaderPropertyCatalog catalog)
        {
            if (catalog == null)
                return string.Empty;

            string path = AssetDatabase.GetAssetPath(catalog);
            if (!string.IsNullOrEmpty(path))
                return path;

            return catalog.GetInstanceID().ToString();
        }

    }
}
