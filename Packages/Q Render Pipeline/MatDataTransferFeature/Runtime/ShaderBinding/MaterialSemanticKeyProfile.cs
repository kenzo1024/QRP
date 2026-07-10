using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public enum MaterialSemanticShaderMatchMode
    {
        Exact,
        Prefix
    }

    [Serializable]
    public sealed class MaterialSemanticKeyRule
    {
        public string SemanticKey;
        public List<string> PropertyNames = new List<string>();
        public ParamValueType ValueType = ParamValueType.Float;
        public List<string> IncludeShaders = new List<string>();
        public List<string> ExcludeShaders = new List<string>();
        public MaterialSemanticShaderMatchMode ShaderMatchMode = MaterialSemanticShaderMatchMode.Exact;
        public int Priority;
        public bool Enabled = true;
        [TextArea] public string Description;
    }

    [CreateAssetMenu(menuName = "Rendering/MatDataTransfer/Material Semantic Key Profile")]
    public sealed class MaterialSemanticKeyProfile : ScriptableObject
    {
        [SerializeField] private List<MaterialSemanticKeyRule> rules =
            new List<MaterialSemanticKeyRule>();

        public IReadOnlyList<MaterialSemanticKeyRule> Rules => rules;

        public bool TryResolveSemanticKey(
            string shaderName,
            ShaderPropertyInfo propertyInfo,
            out string semanticKey,
            List<string> warnings = null)
        {
            semanticKey = string.Empty;
            if (propertyInfo == null || rules == null || rules.Count == 0)
                return false;

            MaterialSemanticKeyRule bestRule = null;
            int bestIndex = -1;
            for (int i = 0; i < rules.Count; i++)
            {
                MaterialSemanticKeyRule rule = rules[i];
                if (!IsRuleCandidate(rule, shaderName, propertyInfo, warnings))
                    continue;

                if (bestRule == null || rule.Priority > bestRule.Priority)
                {
                    bestRule = rule;
                    bestIndex = i;
                    continue;
                }

                if (rule.Priority == bestRule.Priority)
                {
                    AddWarning(
                        warnings,
                        $"Semantic key profile '{name}' has equal priority rules for '{propertyInfo.PropertyName}'. Rule {bestIndex} is used before rule {i}.");
                }
            }

            if (bestRule == null)
                return false;

            semanticKey = NormalizeSemanticKey(bestRule.SemanticKey);
            return !string.IsNullOrEmpty(semanticKey);
        }

        private static bool IsRuleCandidate(
            MaterialSemanticKeyRule rule,
            string shaderName,
            ShaderPropertyInfo propertyInfo,
            List<string> warnings)
        {
            if (rule == null || !rule.Enabled)
                return false;

            if (string.IsNullOrWhiteSpace(rule.SemanticKey))
                return false;

            if (!ContainsName(rule.PropertyNames, propertyInfo.PropertyName))
                return false;

            if (!ShaderIncluded(shaderName, rule))
                return false;

            if (ShaderExcluded(shaderName, rule))
                return false;

            if (rule.ValueType != propertyInfo.ValueType)
            {
                AddWarning(
                    warnings,
                    $"Semantic rule '{rule.SemanticKey}' matches property '{propertyInfo.PropertyName}' but expects {rule.ValueType}, got {propertyInfo.ValueType}.");
                return false;
            }

            return true;
        }

        private static bool ShaderIncluded(string shaderName, MaterialSemanticKeyRule rule)
        {
            return IsNullOrEmpty(rule.IncludeShaders)
                || ContainsShader(rule.IncludeShaders, shaderName, rule.ShaderMatchMode);
        }

        private static bool ShaderExcluded(string shaderName, MaterialSemanticKeyRule rule)
        {
            return ContainsShader(rule.ExcludeShaders, shaderName, rule.ShaderMatchMode);
        }

        private static bool ContainsName(List<string> values, string expected)
        {
            if (values == null || string.IsNullOrWhiteSpace(expected))
                return false;

            string normalizedExpected = expected.Trim();
            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (string.Equals(value.Trim(), normalizedExpected, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool ContainsShader(
            List<string> values,
            string shaderName,
            MaterialSemanticShaderMatchMode matchMode)
        {
            if (values == null || string.IsNullOrWhiteSpace(shaderName))
                return false;

            string normalizedShaderName = shaderName.Trim();
            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (ShaderMatches(normalizedShaderName, value.Trim(), matchMode))
                    return true;
            }

            return false;
        }

        private static bool ShaderMatches(
            string shaderName,
            string pattern,
            MaterialSemanticShaderMatchMode matchMode)
        {
            if (string.IsNullOrEmpty(shaderName) || string.IsNullOrEmpty(pattern))
                return false;

            switch (matchMode)
            {
                case MaterialSemanticShaderMatchMode.Prefix:
                    return shaderName.StartsWith(pattern, StringComparison.Ordinal);
                case MaterialSemanticShaderMatchMode.Exact:
                default:
                    return string.Equals(shaderName, pattern, StringComparison.Ordinal);
            }
        }

        private static bool IsNullOrEmpty(List<string> values)
        {
            if (values == null || values.Count == 0)
                return true;

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return false;
            }

            return true;
        }

        private static string NormalizeSemanticKey(string semanticKey)
        {
            return string.IsNullOrWhiteSpace(semanticKey)
                ? string.Empty
                : semanticKey.Trim();
        }

        private static void AddWarning(List<string> warnings, string message)
        {
            if (warnings != null && !string.IsNullOrEmpty(message))
                warnings.Add(message);
        }
    }
}
