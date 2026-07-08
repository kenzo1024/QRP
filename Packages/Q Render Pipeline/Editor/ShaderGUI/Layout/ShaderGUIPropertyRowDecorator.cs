using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public enum ShaderGUIControlSlot
    {
        Left,
        Right
    }

    public readonly struct ShaderGUIControlRow
    {
        public const float SlotSize = 14f;
        public const float SlotGap = 4f;
        public const float ReservedSideWidth = SlotSize + SlotGap;

        private const float TallRowThreshold = 40f;
        private const float TallRowTopInset = 8f;

        public Rect ContentRect { get; }
        public Rect LeftSlot { get; }
        public Rect RightSlot { get; }

        private ShaderGUIControlRow(Rect contentRect)
        {
            ContentRect = contentRect;

            var size = Mathf.Min(SlotSize, Mathf.Max(0f, contentRect.height));
            var slotY = GetSlotY(contentRect, size);
            LeftSlot = new Rect(
                contentRect.x - SlotGap - size,
                slotY,
                size,
                size);
            RightSlot = new Rect(
                contentRect.xMax + SlotGap,
                slotY,
                size,
                size);
        }

        public static ShaderGUIControlRow FromContentRect(Rect contentRect)
        {
            return new ShaderGUIControlRow(contentRect);
        }

        public Rect GetSlot(ShaderGUIControlSlot slot)
        {
            return slot == ShaderGUIControlSlot.Right ? RightSlot : LeftSlot;
        }

        private static float GetSlotY(Rect contentRect, float size)
        {
            if (contentRect.height > TallRowThreshold)
                return contentRect.y + TallRowTopInset;

            return contentRect.y + (contentRect.height - size) * 0.5f;
        }
    }

    public static class ShaderGUIControlRowDecorator
    {
        private const string MatDataTransferContractEditorMenuPath =
            "TA/角色模型工具/材质传输系统/MatDataTransfer Binding Editor";
        private const string MatDataTransferPendingShaderPathKey = "Rendering.MatDataTransfer.PendingShaderPath";

        public static void Draw(ShaderGUIControlRow row, PropertyStaticData staticData, Shader shader)
        {
            if (staticData == null)
                return;

            if (staticData.SupportsMatDataTransfer)
                DrawMatDataTransferButton(row.GetSlot(ShaderGUIControlSlot.Left), "Open MatDataTransfer Contract Editor.", shader);
        }

        private static void DrawMatDataTransferButton(Rect rect, string tooltip, Shader shader)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            DrawMatDataTransferBadge(rect);
            DrawMatDataTransferButtonHitArea(rect, tooltip, shader);
        }

        private static void DrawMatDataTransferBadge(Rect rect)
        {
            var accent = EditorGUIUtility.isProSkin
                ? new Color(0.62f, 0.74f, 0.9f, 0.88f)
                : new Color(0.18f, 0.34f, 0.58f, 0.9f);
            var stroke = EditorGUIUtility.isProSkin
                ? new Color(0.78f, 0.84f, 0.92f, 0.75f)
                : new Color(0.12f, 0.16f, 0.22f, 0.72f);
            var shadow = EditorGUIUtility.isProSkin
                ? new Color(0.05f, 0.06f, 0.07f, 0.18f)
                : new Color(1f, 1f, 1f, 0.28f);

            var markerRect = new Rect(rect.x + 1f, rect.y + 2f, 2f, Mathf.Max(2f, rect.height - 4f));
            EditorGUI.DrawRect(markerRect, accent);

            var centerY = rect.y + rect.height * 0.5f;
            EditorGUI.DrawRect(new Rect(rect.x + 6f, centerY - 4f, 5f, 1f), stroke);
            EditorGUI.DrawRect(new Rect(rect.x + 6f, centerY, 7f, 1f), stroke);
            EditorGUI.DrawRect(new Rect(rect.x + 6f, centerY + 4f, 4f, 1f), stroke);
            EditorGUI.DrawRect(new Rect(rect.x + 11f, centerY - 2f, 1f, 5f), stroke);
            EditorGUI.DrawRect(new Rect(rect.x + 12f, centerY - 1f, 1f, 3f), stroke);
            EditorGUI.DrawRect(new Rect(rect.x + 4f, rect.y + rect.height - 1f, Mathf.Max(0f, rect.width - 5f), 1f), shadow);
        }

        private static void DrawMatDataTransferButtonHitArea(Rect rect, string tooltip, Shader shader)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            var wasEnabled = GUI.enabled;
            GUI.enabled = true;

            if (GUI.Button(rect, new GUIContent(string.Empty, tooltip), GUIStyle.none))
            {
                StorePendingShader(shader);
                EditorApplication.ExecuteMenuItem(MatDataTransferContractEditorMenuPath);
            }

            GUI.enabled = wasEnabled;
        }

        private static void StorePendingShader(Shader shader)
        {
            string shaderPath = shader != null ? AssetDatabase.GetAssetPath(shader) : string.Empty;
            if (string.IsNullOrEmpty(shaderPath))
                EditorPrefs.DeleteKey(MatDataTransferPendingShaderPathKey);
            else
                EditorPrefs.SetString(MatDataTransferPendingShaderPathKey, shaderPath);
        }
    }

    public static class ShaderGUIPropertyRowDecorator
    {
        public static void Draw(Rect rowRect, PropertyStaticData staticData, Shader shader)
        {
            ShaderGUIControlRowDecorator.Draw(
                ShaderGUIControlRow.FromContentRect(rowRect),
                staticData,
                shader);
        }
    }
}
