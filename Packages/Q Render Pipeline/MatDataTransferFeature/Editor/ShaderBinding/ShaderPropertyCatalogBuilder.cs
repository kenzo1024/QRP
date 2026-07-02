using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Rendering.MatDataTransfer.Runtime;

namespace Rendering.MatDataTransfer.Editor
{
    public static class ShaderPropertyCatalogBuilder
    {
        private static readonly string[] BindingMarkerAttributes =
        {
            "MatDataTransfer"
        };

        public static void SyncCatalog(ShaderPropertyCatalog catalog, Shader shader)
        {
            if (catalog == null || shader == null)
                return;

            List<ShaderPropertyInfo> shaderProperties = ExtractShaderProperties(shader);
            catalog.SetShader(shader);
            catalog.UpdateFromShader(shaderProperties);
            EditorUtility.SetDirty(catalog);
        }

        public static List<ShaderPropertyInfo> ExtractShaderProperties(Shader shader)
        {
            List<ShaderPropertyInfo> properties = new List<ShaderPropertyInfo>();
            if (shader == null)
                return properties;

            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string[] attributes = shader.GetPropertyAttributes(i);
                if (!TryReadBindingMarker(attributes, out string markedSemanticKey))
                    continue;

                ShaderPropertyInfo info = BuildShaderPropertyInfo(shader, i, attributes);
                properties.Add(info);
            }

            return properties;
        }

        private static ShaderPropertyInfo BuildShaderPropertyInfo(
            Shader shader,
            int propertyIndex,
            string[] attributes)
        {
            string propertyName = shader.GetPropertyName(propertyIndex);
            ShaderPropertyInfo info = new ShaderPropertyInfo
            {
                PropertyName = propertyName,
                InspectorDisplayName = ShaderUtil.GetPropertyDescription(shader, propertyIndex),
                ValueType = ToValueType(shader.GetPropertyType(propertyIndex)),
                Attributes = attributes ?? Array.Empty<string>()
            };

            info.DefaultValue = ExtractDefaultValue(shader, propertyIndex, info.ValueType);
            return info;
        }

        private static ParamValue ExtractDefaultValue(Shader shader, int propertyIndex, ParamValueType valueType)
        {
            switch (valueType)
            {
                case ParamValueType.Color:
                    return ParamValue.Color(shader.GetPropertyDefaultVectorValue(propertyIndex));
                case ParamValueType.Vector:
                    return ParamValue.Vector(shader.GetPropertyDefaultVectorValue(propertyIndex));
                case ParamValueType.Texture:
                    return ParamValue.Texture(null);
                case ParamValueType.Float:
                default:
                    return ParamValue.Float(shader.GetPropertyDefaultFloatValue(propertyIndex));
            }
        }

        private static ParamValueType ToValueType(UnityEngine.Rendering.ShaderPropertyType type)
        {
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    return ParamValueType.Color;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    return ParamValueType.Vector;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    return ParamValueType.Texture;
                default:
                    return ParamValueType.Float;
            }
        }

        private static bool TryReadBindingMarker(string[] attributes, out string semanticKey)
        {
            semanticKey = string.Empty;
            if (attributes == null)
                return false;

            for (int i = 0; i < attributes.Length; i++)
            {
                string attribute = attributes[i];
                for (int markerIndex = 0; markerIndex < BindingMarkerAttributes.Length; markerIndex++)
                {
                    if (!AttributeNameEquals(attribute, BindingMarkerAttributes[markerIndex]))
                        continue;

                    semanticKey = ReadAttributeArgument(attribute);
                    return true;
                }
            }

            return false;
        }

        private static bool AttributeNameEquals(string attribute, string name)
        {
            if (string.IsNullOrEmpty(attribute))
                return false;

            string trimmed = attribute.Trim();
            if (string.Equals(trimmed, name, StringComparison.Ordinal))
                return true;

            return trimmed.StartsWith(name + "(", StringComparison.Ordinal)
                && trimmed.EndsWith(")", StringComparison.Ordinal);
        }

        private static string ReadAttributeArgument(string attribute)
        {
            if (string.IsNullOrEmpty(attribute))
                return string.Empty;

            int start = attribute.IndexOf('(');
            int end = attribute.LastIndexOf(')');
            if (start < 0 || end <= start)
                return string.Empty;

            return attribute.Substring(start + 1, end - start - 1).Trim();
        }
    }
}
