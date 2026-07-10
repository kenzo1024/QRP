using System;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    /// <summary>
    /// 解析后的属性绑定信息（用于内部数据传递和回执）
    /// </summary>
    [Serializable]
    public struct ParamBindingResolution
    {
        public string MatchedSemanticKey;
        public string ShaderName;
        public string CatalogName;
        public string PropertyName;
        public int PropertyId;

        public ParamBindingResolution(
            string matchedSemanticKey,
            string shaderName,
            string catalogName,
            string propertyName,
            int propertyId)
        {
            MatchedSemanticKey = matchedSemanticKey;
            ShaderName = shaderName;
            CatalogName = catalogName;
            PropertyName = propertyName;
            PropertyId = propertyId;
        }

        public static ParamBindingResolution FromCatalog(
            CatalogProperty property,
            string matchedSemanticKey,
            string shaderName,
            string catalogName)
        {
            if (property?.PropertyInfo == null)
            {
                return new ParamBindingResolution(
                    matchedSemanticKey,
                    shaderName,
                    catalogName,
                    string.Empty,
                    0);
            }

            return new ParamBindingResolution(
                matchedSemanticKey,
                shaderName,
                catalogName,
                property.PropertyInfo.PropertyName,
                ResolvePropertyId(property.PropertyInfo.PropertyName));
        }

        public static int ResolvePropertyId(string propertyName)
        {
            return string.IsNullOrEmpty(propertyName)
                ? 0
                : Shader.PropertyToID(propertyName);
        }
    }
}
