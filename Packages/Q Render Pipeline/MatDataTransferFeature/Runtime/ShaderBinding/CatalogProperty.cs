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

    public enum CatalogSemanticKeySource
    {
        Generated,
        Profile
    }

    /// <summary>
    /// Catalog 中的属性条目，包含 ShaderPropertyInfo + 唯一标识 + 状态
    /// </summary>
    [Serializable]
    public sealed class CatalogProperty
    {
        public ShaderPropertyInfo PropertyInfo;
        public string SuggestedSemanticKey;
        public CatalogSemanticKeySource SemanticKeySource = CatalogSemanticKeySource.Generated;
        public CatalogPropertyStatus Status = CatalogPropertyStatus.New;

        public CatalogProperty()
        {
        }

        public CatalogProperty(ShaderPropertyInfo propertyInfo, string suggestedSemanticKey)
            : this(propertyInfo, suggestedSemanticKey, CatalogSemanticKeySource.Generated)
        {
        }

        public CatalogProperty(
            ShaderPropertyInfo propertyInfo,
            string suggestedSemanticKey,
            CatalogSemanticKeySource semanticKeySource)
        {
            PropertyInfo = propertyInfo;
            SuggestedSemanticKey = suggestedSemanticKey;
            SemanticKeySource = semanticKeySource;
            Status = CatalogPropertyStatus.Ok;
        }

        public CatalogProperty Clone()
        {
            return new CatalogProperty
            {
                PropertyInfo = PropertyInfo?.Clone(),
                SuggestedSemanticKey = SuggestedSemanticKey,
                SemanticKeySource = SemanticKeySource,
                Status = Status
            };
        }
    }
}
