using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /* QRP Drawer 基类：所有 QRP 自有的 MaterialPropertyDrawer 都应该继承这个类 */
    public abstract class MaterialPropertyDrawerBase : MaterialPropertyDrawer, IShaderGUIDrawer
    {
        /* 构建静态元数据：默认空实现，子类可以重写 */
        public virtual void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
        {
        }

        /// <summary>
        /// 检查 Drawer 是否支持指定的属性类型：默认接受所有类型。
        /// 当返回 false 时，OnGUI 会自动降级到 DefaultShaderProperty 绘制。
        /// </summary>
        public virtual bool IsMatchPropertyType(ShaderPropertyType propertyType)
        {
            return true;
        }
        
        public override void OnGUI(Rect position, MaterialProperty property, GUIContent label, MaterialEditor editor)
        {
            // QRP drawers should always have a safe default path while their custom UI is being filled in.
            if (IsMatchPropertyType(ShaderGUIUtility.GetPropertyType(property)))
                DrawProperty(position, property, label, editor);
            else
                editor.DefaultShaderProperty(position, property, label.text);
        }
        
        public virtual void DrawProperty(Rect position, MaterialProperty property, GUIContent label, MaterialEditor editor)
        {
            editor.DefaultShaderProperty(position, property, label.text);
        }

        /// <summary>
        /// 应用副作用：默认空实现，子类可以重写以同步 Keyword 等。
        /// </summary>
        public override void Apply(MaterialProperty property)
        {
        }
    }
}
