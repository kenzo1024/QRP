# MatDataTransferFeature 重构总结

## 重构策略

**激进式重构**：直接删除旧实现，不保留向后兼容。新架构完全替代旧系统。

## 核心改动

### 1. 数据结构重构

#### 旧架构（已删除）
- **ShaderPropertyCatalog**: Shader 属性目录
- **MatDataTransferBindProfile**: 绑定配置（需要手动 confirm）
- **MaterialParamConfig**: 材质参数配置

#### 新架构（直接替换）
- **ShaderPropertyCatalog**: 合并 Catalog 和 Profile，包含状态管理（Ok/New/Missing），自动同步无需 confirm
- **MaterialParamConfig**: 简化的参数配置，仅包含 SemanticKey、ValueType、Value

### 2. 新增基础类型

#### ShaderPropertyInfo
从 Shader 提取的原始属性信息：
- PropertyName、InspectorDisplayName
- ValueType
- DefaultValue（shader 默认值）

#### CatalogProperty
Catalog 中的属性条目：
- ShaderPropertyInfo（原始信息）
- SuggestedSemanticKey（唯一标识）
- Status（Ok/New/Missing 状态）

#### MaterialParameter
材质参数配置项：
- SemanticKey（唯一标识）
- ValueType（数据类型）
- Value（应用值）

### 3. Instance 简化

#### MatDataTransferInstance
职责：**仅维护 renderer/material/shader 映射关系**

移除功能：
- 不再持有 config 文件引用
- 不再持有 catalog 文件引用
- 不再管理参数配置

新增功能：
- `QueryBindings()`: 查询绑定信息
- `GetBindingsByShader()`: 按 shader 分组获取绑定

### 4. Editor 工具简化

#### ShaderPropertyCatalogBuilder
- 直接从 Shader 提取属性生成 ShaderPropertyInfo
- 自动更新 Catalog 状态（Ok/New/Missing）
- 无需手动 confirm 流程

#### MatDataTransferBindingEditor
- 单一界面管理 Catalog
- 自动状态标记（绿色=Ok，黄色=New，红色=Missing）
- 一键导出到 MaterialParamConfig

### 5. 业务使用流程

#### 旧流程（已废弃）
```csharp
// 继承 MatDataTransferBusinessSource
// 配置 instances 和 parameters
// 调用 SubmitParametersToInstances()
```

#### 新流程
```csharp
// 1. 获取 MatDataTransferInstance
MatDataTransferInstance instance = GetComponent<MatDataTransferInstance>();

// 2. 指定业务实际操作的 Renderer 和材质槽
Renderer targetRenderer = GetComponentInChildren<Renderer>();
int materialSlot = 0;

// 3. 直接提交参数
MatDataTransferAPI.ForMaterial(
    instance,
    "base_color",
    ParamValue.Color(Color.white),
    targetRenderer,
    materialSlot,
    MatDataTransferSubmitSource.From(this, "Gameplay"),
    ParamWriteLayer.Gameplay,
    priority: 0);
```

### 6. 测试脚本

#### MatDataTransferTest
演示新 API 用法：
- 查询 Instance 绑定信息
- 查询 Catalog 属性信息
- 动态提交参数值
- Inspector 显示回执信息（未来添加）

## 工作流程对比

### 旧工作流程（已废弃）
1. Sync Catalog from Shader
2. 手动 Confirm 每个属性
3. Build Profile from Catalog
4. Export to MaterialParamConfig
5. 在 Instance 或 BusinessSource 中配置使用

### 新工作流程
1. **Sync Catalog from Shader**（自动标记状态）
2. **Export to MaterialParamConfig**（仅导出 Ok 状态）
3. 业务代码直接使用 Instance 查询和提交

## Logger 打点

### 保留内容
- Logger 基础框架（MatDataTransferLogger）
- 日志接口（IMatDataTransferLogging）
- 日志设置（MatDataTransferLoggingSettings）

### 移除内容
- 所有具体的打点调用（timelineRecords、receipts）
- 在 ApplyWriteCommands、Resolver、Request 等方法中的打点逻辑
- 等系统稳定后，根据需要重新添加打点

## 已删除文件

### Runtime
- `MatDataTransferInstance.cs`（旧版）
- `MatDataTransferInstanceParameter.cs`
- `IMatDataTransferBusinessSource.cs`
- `MatDataTransferBusinessSource.cs`
- `ShaderPropertyCatalog.cs`（旧版）
- `MatDataTransferBindProfile.cs`
- `MaterialParamConfig.cs`（旧版）

### Editor
- `MatDataTransferInstanceEditor.cs`
- `MatDataTransferBusinessSourceEditor.cs`
- `MatDataTransferBindingEditor.cs`（旧版）
- `MatDataTransferBindProfileEditor.cs`
- `MaterialParamConfigEditor.cs`
- `ShaderPropertyCatalogBuilder.cs`（旧版）
- `ShaderPropertyCatalogEditor.cs`
- `MatDataTransferInstanceParameterDrawer.cs`

## 新文件清单

### Runtime 核心
- `ShaderPropertyInfo.cs` - Shader 原始属性
- `CatalogProperty.cs` - Catalog 属性条目
- `ShaderPropertyCatalog.cs` - 合并的 Catalog（新）
- `MaterialParamConfig.cs` - 简化的参数配置（新）
- `MatDataTransferInstance.cs` - 简化的 Instance（新）

### Runtime 测试
- `MatDataTransferTest.cs` - 测试脚本

### Editor
- `ShaderPropertyCatalogBuilder.cs` - 新 Builder
- `MatDataTransferBindingEditor.cs` - 新 Editor

## 优势总结

1. **更简洁**: 删除冗余文件和流程
2. **无歧义**: 不再有新旧共存的困惑
3. **更易维护**: 代码更清晰，职责更明确
4. **更高效**: 自动状态管理，减少人工操作
5. **更直接**: 业务代码更简单，无需中间层

## 迁移注意事项

- **不兼容旧系统**：所有旧的 Profile、BusinessSource 相关代码需要重写
- **资产需要重新生成**：旧的 Catalog、Profile、Config 文件无法直接使用
- **打点暂时移除**：Logger 框架保留，但具体打点逻辑已移除，后续根据需要添加

## 后续工作

1. 验证编译和基本功能
2. 测试新工作流程（Sync → Export → 业务使用）
3. 根据实际使用情况，逐步恢复必要的打点和监控功能
4. 更新相关文档和示例
