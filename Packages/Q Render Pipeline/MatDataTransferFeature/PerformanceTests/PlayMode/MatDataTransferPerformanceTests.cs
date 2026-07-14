using System.Collections.Generic;
using NUnit.Framework;
using Rendering.MatDataTransfer.Runtime;
using Unity.PerformanceTesting;
using UnityEngine;

namespace Rendering.MatDataTransfer.PerformanceTests
{
    [TestFixture]
    internal sealed class MatDataTransferPerformanceTests
    {
        private readonly List<ShaderPropertyCatalog> m_Catalogs = new List<ShaderPropertyCatalog>();
        private readonly List<ParamTransferPayload> m_Payloads = new List<ParamTransferPayload>();

        private GameObject m_Root;
        private Material m_Material;
        private ShaderPropertyCatalog m_Catalog;
        private MatDataTransferInstanceRegister m_Registry;
        private MaterialParameterResolver m_Resolver;
        private MaterialParameterWriter m_Writer;

        [SetUp]
        public void SetUp()
        {
            MatDataTransferLogging.Instance.ApplySettings(new MatDataTransferLoggingSettings());
            CreateFixedFixture();
        }

        [TearDown]
        public void TearDown()
        {
            m_Writer?.ClearWrittenState();
            MatDataTransferLogging.Instance.ClearTimelineRecords();

            if (m_Catalog != null)
                Object.DestroyImmediate(m_Catalog);
            if (m_Material != null)
                Object.DestroyImmediate(m_Material);
            if (m_Root != null)
                Object.DestroyImmediate(m_Root);
        }

        [Test, Performance]
        public void Writer_EmptyCommandList_Baseline()
        {
            IReadOnlyList<ParamWriteCommand> commands = new List<ParamWriteCommand>();

            Measure.Method(() => m_Writer.Apply(commands))
                .WarmupCount(10)
                .MeasurementCount(100)
                .GC()
                .Run();
        }

        [Test, Performance]
        [TestCase(1)]
        [TestCase(100)]
        public void Resolver_CompetingRequests_Baseline(int requestCount)
        {
            FillPayloads(requestCount);

            Measure.Method(() => m_Resolver.Resolve(m_Catalogs, m_Registry, m_Payloads))
                .WarmupCount(10)
                .MeasurementCount(100)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void Writer_SingleCommand_Baseline()
        {
            FillPayloads(1);
            IReadOnlyList<ParamWriteCommand> commands = m_Resolver.Resolve(
                m_Catalogs,
                m_Registry,
                m_Payloads);

            Measure.Method(() => m_Writer.Apply(commands))
                .WarmupCount(10)
                .MeasurementCount(100)
                .GC()
                .Run();
        }

        private void CreateFixedFixture()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            Assert.That(shader, Is.Not.Null, "Performance fixture needs a supported unlit shader.");

            m_Root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Root.name = "MDT_PerformanceFixture";
            Renderer renderer = m_Root.GetComponent<Renderer>();
            m_Material = new Material(shader) { name = "MDT_PerformanceMaterial" };
            renderer.sharedMaterial = m_Material;

            MatDataTransferInstance instance = m_Root.AddComponent<MatDataTransferInstance>();
            instance.RefreshBindings();

            string propertyName = m_Material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            m_Catalog = ScriptableObject.CreateInstance<ShaderPropertyCatalog>();
            m_Catalog.SetShader(shader);
            m_Catalog.UpdateFromShader(new List<ShaderPropertyInfo>
            {
                new ShaderPropertyInfo(propertyName, "Base Color", ParamValueType.Color)
            });
            m_Catalogs.Add(m_Catalog);

            m_Registry = new MatDataTransferInstanceRegister(1);
            Assert.That(m_Registry.TryRegister(instance, out _), Is.True);
            m_Resolver = new MaterialParameterResolver();
            m_Writer = new MaterialParameterWriter();
        }

        private void FillPayloads(int count)
        {
            m_Payloads.Clear();
            MatDataTransferInstance instance = m_Root.GetComponent<MatDataTransferInstance>();
            Renderer renderer = m_Root.GetComponent<Renderer>();
            RendererMaterialBinding binding = instance.QueryBinding(renderer, 0);
            Assert.That(binding, Is.Not.Null);

            string semanticKey = m_Catalog.Properties[0].SuggestedSemanticKey;
            MatDataTransferSubmitSource source = new MatDataTransferSubmitSource
            {
                Id = "MDTS.Performance",
                Owner = null
            };

            for (int i = 0; i < count; i++)
            {
                ParamRequestIdentity identity = new ParamRequestIdentity(
                    instance,
                    source,
                    semanticKey,
                    ParamValue.Color(Color.white),
                    binding);
                ParamTransferPayload payload = new ParamTransferPayload(
                    identity,
                    new ParamWriteConfig(ParamWriteLayer.Gameplay, i));
                payload.Sequence = i;
                payload.SubmitFrameIndex = 1;
                m_Payloads.Add(payload);
            }
        }
    }
}
