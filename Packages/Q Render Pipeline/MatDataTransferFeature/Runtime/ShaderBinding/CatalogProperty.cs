using System;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public enum CatalogPropertyStatus
    {
        Ok,
        New,
        Missing
    }

    /// <summary>
    /// Catalog 中的属性条目，包含 ShaderPropertyInfo + 唯一标识 + 状态
    /// </summary>
    [Serializable]
    public sealed class CatalogProperty
    {
        public ShaderPropertyInfo PropertyInfo;
        public string SuggestedSemanticKey;
        public CatalogPropertyStatus Status = CatalogPropertyStatus.New;

        public CatalogProperty()
        {
        }

        public CatalogProperty(ShaderPropertyInfo propertyInfo, string suggestedSemanticKey)
        {
            PropertyInfo = propertyInfo;
            SuggestedSemanticKey = suggestedSemanticKey;
            Status = CatalogPropertyStatus.Ok;
        }

        public CatalogProperty Clone()
        {
            return new CatalogProperty
            {
                PropertyInfo = PropertyInfo?.Clone(),
                SuggestedSemanticKey = SuggestedSemanticKey,
                Status = Status
            };
        }
    }
}
