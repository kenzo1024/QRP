using System;
using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    /// <summary>
    /// 在 Catalog 列表中通过 shader 名称和 semantic key 查找属性
    /// </summary>
    public static class MaterialBindingResolver
    {
        public static bool TryGetProperty(
            IReadOnlyList<ShaderPropertyCatalog> catalogs,
            string shaderName,
            string semanticKey,
            out ShaderPropertyCatalog matchedCatalog,
            out CatalogProperty matchedProperty)
        {
            matchedCatalog = null;
            matchedProperty = null;

            if (catalogs == null || string.IsNullOrEmpty(shaderName) || string.IsNullOrWhiteSpace(semanticKey))
                return false;

            for (int i = 0; i < catalogs.Count; i++)
            {
                ShaderPropertyCatalog catalog = catalogs[i];
                if (!CatalogMatchesShader(catalog, shaderName))
                    continue;

                if (catalog.TryGetProperty(semanticKey, out CatalogProperty property))
                {
                    if (property == null || property.Status != CatalogPropertyStatus.Ok)
                        continue;

                    matchedCatalog = catalog;
                    matchedProperty = property;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetCatalogForShader(
            IReadOnlyList<ShaderPropertyCatalog> catalogs,
            string shaderName,
            out ShaderPropertyCatalog matchedCatalog)
        {
            matchedCatalog = null;
            if (catalogs == null || string.IsNullOrEmpty(shaderName))
                return false;

            for (int i = 0; i < catalogs.Count; i++)
            {
                ShaderPropertyCatalog catalog = catalogs[i];
                if (CatalogMatchesShader(catalog, shaderName))
                {
                    matchedCatalog = catalog;
                    return true;
                }
            }

            return false;
        }

        private static bool CatalogMatchesShader(ShaderPropertyCatalog catalog, string shaderName)
        {
            if (catalog == null)
                return false;

            return !string.IsNullOrEmpty(catalog.ShaderName)
                && string.Equals(catalog.ShaderName, shaderName, StringComparison.Ordinal);
        }
    }
}
