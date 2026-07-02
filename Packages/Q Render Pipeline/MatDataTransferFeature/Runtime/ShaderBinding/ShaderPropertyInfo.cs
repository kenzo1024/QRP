using System;

namespace Rendering.MatDataTransfer.Runtime
{
    /// <summary>
    /// 从 Shader 中提取的原始属性信息
    /// </summary>
    [Serializable]
    public sealed class ShaderPropertyInfo
    {
        public string PropertyName;
        public string InspectorDisplayName;
        public ParamValueType ValueType;
        public string[] Attributes = Array.Empty<string>();

        public ParamValue DefaultValue;

        public ShaderPropertyInfo()
        {
        }

        public ShaderPropertyInfo(
            string propertyName,
            string inspectorDisplayName,
            ParamValueType valueType)
        {
            PropertyName = propertyName;
            InspectorDisplayName = inspectorDisplayName;
            ValueType = valueType;
        }

        public ShaderPropertyInfo Clone()
        {
            return new ShaderPropertyInfo
            {
                PropertyName = PropertyName,
                InspectorDisplayName = InspectorDisplayName,
                ValueType = ValueType,
                Attributes = Attributes != null ? (string[])Attributes.Clone() : Array.Empty<string>(),
                DefaultValue = DefaultValue
            };
        }
    }
}
