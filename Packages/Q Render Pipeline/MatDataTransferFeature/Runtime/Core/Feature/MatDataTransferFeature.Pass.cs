using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private InternalPass m_Pass;

        private void InitializeRenderPass()
        {
            m_Pass = new InternalPass(this)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses
            };
        }

        private void AddRenderPassTo(ScriptableRenderer renderer)
        {
            if (renderer == null || !IsPrimaryInstance())
                return;

            if (GetActiveInstanceCount() == 0 || m_Pass == null)
                return;

            renderer.EnqueuePass(m_Pass);
        }

        private void DisposeRenderPass()
        {
            m_Pass = null;
        }

        private sealed class InternalPass : ScriptableRenderPass
        {
            private const string ProfilerTag = "MatDataTransferFeature.RenderPass";
            private readonly MatDataTransferFeature m_Feature;
            private int m_LastUpdatedFrame = -1;

            private sealed class PassData
            {
                public MatDataTransferFeature feature;
            }

            public InternalPass(MatDataTransferFeature feature)
            {
                m_Feature = feature;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!m_Feature.IsPrimaryInstance())
                    return;

                int frame = MatDataTransferRuntime.BeginRenderPass();
                if (m_LastUpdatedFrame == frame)
                    return;
                m_LastUpdatedFrame = frame;

                using (var builder = renderGraph.AddUnsafePass<PassData>(ProfilerTag, out PassData passData))
                {
                    passData.feature = m_Feature;
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (PassData data, UnsafeGraphContext ctx) =>
                    {
                        using (MatDataTransferProfiling.PassPipeline.Auto())
                            data.feature.ExecuteTransferPipeline();
                    });
                }
            }
        }
    }
}
