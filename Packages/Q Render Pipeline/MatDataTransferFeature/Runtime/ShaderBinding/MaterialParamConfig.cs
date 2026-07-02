using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    /// <summary>
    /// 材质参数配置 - 仅包含业务需要的参数值
    /// </summary>
    [Serializable]
    public sealed class MaterialParameter
    {
        public string SemanticKey;
        public ParamValueType ValueType = ParamValueType.Float;
        public ParamValue Value;

        public MaterialParameter()
        {
        }

        public MaterialParameter(string semanticKey, ParamValueType valueType, ParamValue value)
        {
            SemanticKey = semanticKey;
            ValueType = valueType;
            Value = value;
        }

        public MaterialParameter Clone()
        {
            return new MaterialParameter
            {
                SemanticKey = SemanticKey,
                ValueType = ValueType,
                Value = Value
            };
        }
    }

    /// <summary>
    /// 材质参数配置文件
    /// </summary>
    public sealed class MaterialParamConfig : ScriptableObject
    {
        [SerializeField] private string defaultShaderName;
        [SerializeField] private List<MaterialParameter> parameters = new List<MaterialParameter>();

        public string DefaultShaderName => defaultShaderName;
        public IReadOnlyList<MaterialParameter> Parameters => parameters;

        public void SetDefaultShaderName(string shaderName)
        {
            defaultShaderName = string.IsNullOrWhiteSpace(shaderName)
                ? string.Empty
                : shaderName.Trim();

            NotifyChanged();
        }

        public void SetParameters(IList<MaterialParameter> sourceParameters)
        {
            parameters.Clear();
            if (sourceParameters == null)
            {
                NotifyChanged();
                return;
            }

            for (int i = 0; i < sourceParameters.Count; i++)
            {
                MaterialParameter parameter = sourceParameters[i];
                if (parameter != null)
                    parameters.Add(parameter.Clone());
            }

            NotifyChanged();
        }

        public void AddOrUpdateParameter(string semanticKey, ParamValueType valueType, ParamValue value)
        {
            MaterialParameter existing = FindParameter(semanticKey);
            if (existing != null)
            {
                existing.ValueType = valueType;
                existing.Value = value;
            }
            else
            {
                parameters.Add(new MaterialParameter(semanticKey, valueType, value));
            }

            NotifyChanged();
        }

        public bool RemoveParameter(string semanticKey)
        {
            for (int i = parameters.Count - 1; i >= 0; i--)
            {
                if (parameters[i] != null &&
                    string.Equals(parameters[i].SemanticKey, semanticKey, StringComparison.Ordinal))
                {
                    parameters.RemoveAt(i);
                    NotifyChanged();
                    return true;
                }
            }

            return false;
        }

        public bool TryGetParameter(string semanticKey, out MaterialParameter parameter)
        {
            parameter = FindParameter(semanticKey);
            return parameter != null;
        }

        private MaterialParameter FindParameter(string semanticKey)
        {
            if (string.IsNullOrWhiteSpace(semanticKey) || parameters == null)
                return null;

            for (int i = 0; i < parameters.Count; i++)
            {
                MaterialParameter param = parameters[i];
                if (param != null &&
                    string.Equals(param.SemanticKey, semanticKey, StringComparison.Ordinal))
                    return param;
            }

            return null;
        }

        public List<MaterialParameter> GetParameterSnapshot()
        {
            List<MaterialParameter> snapshot = new List<MaterialParameter>();
            CopyParametersTo(snapshot);
            return snapshot;
        }

        public void CopyParametersTo(List<MaterialParameter> results)
        {
            if (results == null)
                return;

            results.Clear();
            for (int i = 0; i < parameters.Count; i++)
            {
                MaterialParameter parameter = parameters[i];
                if (parameter != null)
                    results.Add(parameter.Clone());
            }
        }

        private void OnValidate()
        {
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            MatDataTransferRuntime.RequestEditorUpdate();
        }
    }
}
