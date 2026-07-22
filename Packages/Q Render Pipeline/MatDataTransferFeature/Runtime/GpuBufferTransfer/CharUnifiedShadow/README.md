# CharUnifiedShadow GPU Comparison

给角色根节点挂载 `CharUnifiedShadowGpuSource`。组件会自动收集子级 Renderer、查找默认锚点并注册到共享 GPU 系统，不需要额外挂 Manager 或 Provider。

## Scene 调试

选中角色并开启 `Draw Debug Samples`：

- 黄色方块：从原 `CharUnifiedShadow` 移植的 CPU 参考采样点。
- 青色球体：异步回读得到的 GPU Compute 实际采样点。
- 紫色连线：相同采样槽的 CPU/GPU 位置误差。
- 黄色矩形：`BoxProjectionHalton` 的投影区域。
- 绿色线框：合并后的 Renderer Bounds，需要开启 `Draw Debug Bounds`。

当 CPU 和 GPU 结果一致时，紫色连线应接近不可见。

GPU 回读只在 Unity Editor 中且 `Draw Debug Samples` 开启时执行，不进入 Player 构建。

## Editor 多视图

GPU Buffer Runtime 在 Play Mode 使用 `Time.frameCount` 去重。在 Edit Mode，`GpuBufferEditorFrameClock` 每次 `EditorApplication.update` 只推进一次帧号，因此 GameView、SceneView 和多个渲染 Context 共享同一个 GPU 数据帧。

Preview、Reflection 等非 Game/SceneView 编辑器相机不会启动 GPU Buffer 流水线。

## 当前边界

系统会生成并绑定：

- `_MdtCharUnifiedShadowRanges`
- `_MdtCharUnifiedShadowSamples`
- `_MdtCharUnifiedShadowInstanceCapacity`

`CharUnifiedShadowGpuData.hlsl` 提供读取函数，但当前没有修改或接入任何现有 QRP Shader。因此现阶段用于验证采样数据与 Compute 结果，不会改变最终阴影画面。
