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
        private readonly List<ValidatedParamRequest> m_Requests = new List<ValidatedParamRequest>();

        private GameObject m_Root;
        private Material m_Material;
        private ShaderPropertyCatalog m_Catalog;
        private MaterialParameterResolver m_Resolver;
        private MaterialParameterWriter m_Writer;
        private ResolutionStats m_ResolutionStats;

        [SetUp]
        public void SetUp()
        {
            m_Requests.Clear();
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

            m_Requests.Clear();
        }

        [Test, Performance]
        public void Writer_EmptyCommandList_Baseline()
        {
            IReadOnlyList<ParamWriteCommand> commands = new List<ParamWriteCommand>();

            Measure.Method(() => m_Writer.Apply(commands, null))
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
            ResolveSingleWinningCommand();

            Measure.Method(ResolveRequests)
                .WarmupCount(10)
                .MeasurementCount(100)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void Writer_SingleCommand_Baseline()
        {
            FillPayloads(1);
            IReadOnlyList<ParamWriteCommand> commands = ResolveSingleWinningCommand();

            Measure.Method(() => m_Writer.Apply(commands, null))
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
            Assert.That(m_Catalog.Properties, Has.Count.EqualTo(1));
            m_Catalog.Properties[0].Status = CatalogPropertyStatus.Ok;
            m_Resolver = new MaterialParameterResolver();
            m_Writer = new MaterialParameterWriter();
        }

        private IReadOnlyList<ParamWriteCommand> ResolveSingleWinningCommand()
        {
            IReadOnlyList<ParamWriteCommand> commands = m_Resolver.ResolveWithoutDiagnostics(
                m_Requests,
                ref m_ResolutionStats);
            Assert.That(
                commands,
                Has.Count.EqualTo(1),
                "Performance fixture must resolve exactly one winning write command.");
            return commands;
        }

        private void ResolveRequests()
        {
            m_Resolver.ResolveWithoutDiagnostics(
                m_Requests,
                ref m_ResolutionStats);
        }

        private void FillPayloads(int count)
        {
            m_Requests.Clear();
            MatDataTransferInstance instance = m_Root.GetComponent<MatDataTransferInstance>();
            Renderer renderer = m_Root.GetComponent<Renderer>();
            RendererMaterialBinding binding = instance.QueryBinding(renderer, 0);
            Assert.That(binding, Is.Not.Null);

            int instanceId = instance.GetInstanceID();
            int rendererId = renderer.GetInstanceID();
            int propertyId = Shader.PropertyToID(m_Catalog.Properties[0].PropertyInfo.PropertyName);
            ConflictKey conflictKey = new ConflictKey(instanceId, rendererId, 0, propertyId);
            ParamWriteTarget writeTarget = new ParamWriteTarget(renderer, 0, propertyId);

            for (int i = 0; i < count; i++)
            {
                m_Requests.Add(new ValidatedParamRequest(
                    instanceId,
                    conflictKey,
                    writeTarget,
                    ParamValue.Color(Color.white),
                    new RequestStrength(
                        ParamWriteLayers.GetStrength(ParamWriteLayer.Gameplay),
                        i,
                        1,
                        i),
                    i));
            }
        }
    }
}
