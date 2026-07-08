using System.Collections.Generic;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public static class ShaderGUIRoundedRectUtility
    {
        private const int TextureSize = 24;
        private static readonly Dictionary<string, Texture2D> Textures = new();

        public static Texture2D GetTexture(Color fillColor, Color borderColor, float radius, float borderWidth)
        {
            var key = $"{ColorUtility.ToHtmlStringRGBA(fillColor)}:{ColorUtility.ToHtmlStringRGBA(borderColor)}:{radius:F1}:{borderWidth:F1}";
            if (Textures.TryGetValue(key, out var texture))
                return texture;

            texture = CreateTexture(fillColor, borderColor, radius, borderWidth);
            Textures.Add(key, texture);
            return texture;
        }

        private static Texture2D CreateTexture(Color fillColor, Color borderColor, float radius, float borderWidth)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[TextureSize * TextureSize];
            var rect = new Rect(0, 0, TextureSize - 1, TextureSize - 1);
            for (var y = 0; y < TextureSize; y++)
            {
                for (var x = 0; x < TextureSize; x++)
                {
                    var distance = SignedDistanceToRoundedRect(x + 0.5f, y + 0.5f, rect, radius);
                    var fillAlpha = Mathf.Clamp01(0.5f - distance);
                    var borderAlpha = borderWidth <= 0f || borderColor.a <= 0f
                        ? 0f
                        : Mathf.Clamp01(borderWidth + 0.5f - Mathf.Abs(distance));
                    var color = borderAlpha > 0f ? Color.Lerp(fillColor, borderColor, borderAlpha) : fillColor;
                    color.a *= Mathf.Max(fillAlpha, borderAlpha);
                    pixels[y * TextureSize + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static float SignedDistanceToRoundedRect(float x, float y, Rect rect, float radius)
        {
            var center = rect.center;
            var halfSize = new Vector2(rect.width * 0.5f, rect.height * 0.5f);
            var point = new Vector2(Mathf.Abs(x - center.x), Mathf.Abs(y - center.y));
            var inner = halfSize - Vector2.one * radius;
            var delta = point - inner;
            var outside = new Vector2(Mathf.Max(delta.x, 0f), Mathf.Max(delta.y, 0f));
            var inside = Mathf.Min(Mathf.Max(delta.x, delta.y), 0f);
            return outside.magnitude + inside - radius;
        }
    }
}
