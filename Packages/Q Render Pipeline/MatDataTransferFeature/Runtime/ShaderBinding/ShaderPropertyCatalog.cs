using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    /// <summary>
    /// Shader 属性同步文件
    /// </summary>
    public sealed class ShaderPropertyCatalog : ScriptableObject
    {
        [SerializeField] private string catalogVersion = "2.0";
        [SerializeField] private string syncTime;
        [SerializeField] private string shaderGuid;
        [SerializeField] private string shaderName;
        [SerializeField] private Shader shader;
        [SerializeField] private List<CatalogProperty> properties = new List<CatalogProperty>();

        private Dictionary<string, CatalogProperty> m_PropertyMap;

        public string CatalogVersion => catalogVersion;
        public string SyncTime => syncTime;
        public string ShaderGuid => shaderGuid;
        public string ShaderName => shaderName;
        public Shader Shader => shader;
        public IReadOnlyList<CatalogProperty> Properties => properties;

        public bool TryGetProperty(string semanticKey, out CatalogProperty property)
        {
            EnsurePropertyMap();
            string normalizedKey = NormalizeSemanticKey(semanticKey);
            return m_PropertyMap.TryGetValue(normalizedKey, out property);
        }

        public void SetShader(Shader targetShader)
        {
            shader = targetShader;
            if (shader != null)
            {
                shaderName = shader.name;
#if UNITY_EDITOR
                shaderGuid = UnityEditor.AssetDatabase.AssetPathToGUID(
                    UnityEditor.AssetDatabase.GetAssetPath(shader));
#endif
            }
            else
            {
                shaderName = string.Empty;
                shaderGuid = string.Empty;
            }
        }

        public void UpdateFromShader(List<ShaderPropertyInfo> shaderProperties)
        {
            if (shaderProperties == null)
                return;

            syncTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            HashSet<string> incomingPropertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var info in shaderProperties)
            {
                if (info == null || string.IsNullOrWhiteSpace(info.PropertyName))
                    continue;

                string propertyName = info.PropertyName;
                string semanticKey = GenerateSemanticKey(info, shaderName);
                incomingPropertyNames.Add(propertyName);

                CatalogProperty existing = FindByPropertyName(propertyName);

                if (existing != null)
                {
                    existing.PropertyInfo = info;
                    existing.SuggestedSemanticKey = semanticKey;
                    existing.Status = CatalogPropertyStatus.Ok;
                }
                else
                {
                    properties.Add(new CatalogProperty(info, semanticKey)
                    {
                        Status = CatalogPropertyStatus.New
                    });
                }
            }

            foreach (var prop in properties)
            {
                string propertyName = prop?.PropertyInfo?.PropertyName;
                if (prop != null &&
                    (string.IsNullOrWhiteSpace(propertyName) || !incomingPropertyNames.Contains(propertyName)))
                {
                    prop.Status = CatalogPropertyStatus.Missing;
                }
            }

            RebuildPropertyMap();
        }

        private CatalogProperty FindByPropertyName(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName) || properties == null)
                return null;

            for (int i = 0; i < properties.Count; i++)
            {
                CatalogProperty prop = properties[i];
                if (prop?.PropertyInfo == null)
                    continue;

                if (string.Equals(prop.PropertyInfo.PropertyName, propertyName, StringComparison.Ordinal))
                    return prop;
            }

            return null;
        }

        public void RebuildPropertyMap()
        {
            m_PropertyMap = new Dictionary<string, CatalogProperty>();
            if (properties == null)
                return;

            foreach (var prop in properties)
            {
                if (prop == null || string.IsNullOrWhiteSpace(prop.SuggestedSemanticKey))
                    continue;

                string normalizedKey = NormalizeSemanticKey(prop.SuggestedSemanticKey);
                m_PropertyMap[normalizedKey] = prop;
            }
        }

        public bool ValidateForRuntime(List<string> errors = null)
        {
            bool valid = true;
            HashSet<string> semanticKeys = new HashSet<string>();

            if (properties == null)
                return true;

            for (int i = 0; i < properties.Count; i++)
            {
                CatalogProperty prop = properties[i];
                if (prop == null)
                    continue;

                if (string.IsNullOrWhiteSpace(prop.SuggestedSemanticKey))
                    valid &= AddError(errors, i, "SemanticKey is empty.");
                else if (!semanticKeys.Add(NormalizeSemanticKey(prop.SuggestedSemanticKey)))
                    valid &= AddError(errors, i, "SemanticKey is duplicated: " + prop.SuggestedSemanticKey);
            }

            return valid;
        }

        private void EnsurePropertyMap()
        {
            if (m_PropertyMap == null)
                RebuildPropertyMap();
        }

        private static string NormalizeSemanticKey(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

        private static string GenerateSemanticKey(ShaderPropertyInfo info, string shaderName)
        {
            if (info == null || string.IsNullOrWhiteSpace(shaderName))
                return string.Empty;

            string shaderPart = SanitizeSemanticKeyPart(shaderName);
            string propertyPart = SanitizeSemanticKeyPart(info.PropertyName);
            if (string.IsNullOrEmpty(shaderPart) || string.IsNullOrEmpty(propertyPart))
                return string.Empty;

            return shaderPart + "." + propertyPart;
        }

        private static string SanitizeSemanticKeyPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim()
                .Replace(" ", "_")
                .Replace("/", ".")
                .Replace("\\", ".")
                .ToLowerInvariant();
        }

        private static bool AddError(List<string> errors, int index, string message)
        {
            errors?.Add($"Property[{index}] {message}");
            return false;
        }

        private void OnValidate()
        {
            RebuildPropertyMap();
            MatDataTransferRuntime.RequestEditorUpdate();
        }
    }
}

