using System;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public enum ParamWriteLayer
    {
        Gameplay = 200,
        VFX = 300,
        MaterialDefault = 0
    }

    public static class ParamWriteLayers
    {
        public static int GetStrength(ParamWriteLayer layer)
        {
            switch (layer)
            {
                case ParamWriteLayer.MaterialDefault:
                    return 300;
                case ParamWriteLayer.VFX:
                    return 200;
                case ParamWriteLayer.Gameplay:
                    return 100;
                default:
                    return 0;
            }
        }
    }

    public enum ParamWriteMethod
    {
        None,
        MaterialPropertyBlock,
        MaterialInstance,
        SharedMaterial
    }

    public enum ParamValueType
    {
        Float,
        Color,
        Vector,
        Texture
    }

    [Serializable]
    public struct ParamValue
    {
        public ParamValueType Type;
        public float FloatValue;
        public Vector4 VectorValue;
        public Color ColorValue;
        public Texture TextureValue;

        public static ParamValue Float(float value)
        {
            return new ParamValue
            {
                Type = ParamValueType.Float,
                FloatValue = value
            };
        }

        public static ParamValue Vector(Vector4 value)
        {
            return new ParamValue
            {
                Type = ParamValueType.Vector,
                VectorValue = value
            };
        }

        public static ParamValue Color(Color value)
        {
            return new ParamValue
            {
                Type = ParamValueType.Color,
                ColorValue = value
            };
        }

        public static ParamValue Texture(Texture value)
        {
            return new ParamValue
            {
                Type = ParamValueType.Texture,
                TextureValue = value
            };
        }

        public string ToPreview()
        {
            switch (Type)
            {
                case ParamValueType.Float:
                    return FloatValue.ToString("0.###");
                case ParamValueType.Color:
                    return FormatVector(ColorValue);
                case ParamValueType.Vector:
                    return FormatVector(VectorValue);
                case ParamValueType.Texture:
                    return TextureValue != null ? TextureValue.name : "null";
                default:
                    return string.Empty;
            }
        }

        private static string FormatVector(Vector4 value)
        {
            return string.Format(
                "({0:0.###},{1:0.###},{2:0.###},{3:0.###})",
                value.x,
                value.y,
                value.z,
                value.w);
        }
    }
}
