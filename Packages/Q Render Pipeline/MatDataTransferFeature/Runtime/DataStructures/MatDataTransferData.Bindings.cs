using System;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    /// <summary>
    /// 解析后的属性绑定信息（用于内部数据传递和回执）
    /// </summary>
    [Serializable]
    public struct ResolvedMaterialBinding
    {
        public string MatchedSemanticKey;
        public string ShaderName;
        public string CatalogName;
        public string PropertyName;
        public int PropertyId;

        public ResolvedMaterialBinding(
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

        public static ResolvedMaterialBinding FromCatalog(
            CatalogProperty property,
            string matchedSemanticKey,
            string shaderName,
            string catalogName)
        {
            if (property?.PropertyInfo == null)
            {
                return new ResolvedMaterialBinding(
                    matchedSemanticKey,
                    shaderName,
                    catalogName,
                    string.Empty,
                    0);
            }

            return new ResolvedMaterialBinding(
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
