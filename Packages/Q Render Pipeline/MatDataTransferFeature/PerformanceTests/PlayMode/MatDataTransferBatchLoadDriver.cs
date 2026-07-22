using System;
using System.Collections.Generic;
using Rendering.MatDataTransfer.Runtime;
using UnityEngine;

namespace Rendering.MatDataTransfer.PerformanceTests
{
    [DisallowMultipleComponent]
    public sealed class MatDataTransferBatchLoadDriver : MonoBehaviour
    {
        private const string ShaderName = "QRP/Unlit";
        private const string GeneratedRootName = "GeneratedObjects";

        private readonly string[] m_PreferredPropertyOrder =
        {
            "_BaseColor",
            "_TestFloat",
            "_TestFloat0",
            "_TestFloat1",
            "_TestFloat2",
            "_TestColor0",
            "_TestColor1",
            "_TestColor2",
            "_TestColor3",
            "_TestVector0",
            "_TestVector1",
            "_TestVector2",
            "_TestVector3"
        };

        private MatDataTransferBatchScenario m_Scenario;
        private Mesh m_Mesh;
        private Material m_SharedMaterial;
        private Transform m_GeneratedRoot;
        private MatDataTransferObject[] m_Objects;
        private MatDataTransferProperty[] m_Properties;
        private MatDataTransferSubmitSource[] m_Sources;
        private MatDataTransferSubmitOperation[] m_CachedSubmitOperations;
        private MatDataTransferBatchSubmitOperation[] m_CachedBatchSubmitOperations;
        private ParamBatchWrite[] m_BatchWriteBuffer;
        private Material[] m_DirectMaterials;
        private MaterialPropertyBlock[] m_DirectPropertyBlocks;
        private ParamValue[] m_DirectValues;

        public int ObjectCount => m_Objects != null ? m_Objects.Length : 0;
        public int PropertyCount => m_Properties != null ? m_Properties.Length : 0;

        internal void Prepare(MatDataTransferBatchScenario scenario)
        {
            m_Scenario = scenario;
            ClearGeneratedObjects();
            EnsureGeneratedRoot();
            EnsureMesh();
            EnsureMaterial();
            CacheProperties(scenario.PropertyCount);
            CacheSources(scenario.SourceCount);
            m_Objects = null;
            m_CachedSubmitOperations = null;
            m_CachedBatchSubmitOperations = null;
            m_BatchWriteBuffer = new ParamBatchWrite[scenario.PropertyCount];
            m_DirectMaterials = null;
            m_DirectPropertyBlocks = null;
            m_DirectValues = null;
        }

        internal void CreateRenderObjects()
        {
            if (m_Objects != null)
                return;

            CreateObjects(m_Scenario.ObjectCount);
        }

        internal void AttachInstances()
        {
            if (m_Objects == null)
                throw new InvalidOperationException("Render objects must be created before instances are attached.");

            for (int i = 0; i < m_Objects.Length; i++)
            {
                MatDataTransferObject target = m_Objects[i];
                if (target.Instance != null)
                    continue;

                target.Instance = target.GameObject.AddComponent<MatDataTransferInstance>();
                target.Instance.RefreshBindings();
            }
        }

        public void Clear()
        {
            ClearGeneratedObjects();

            if (m_Mesh != null)
                DestroyImmediateSafe(m_Mesh);
            if (m_SharedMaterial != null)
                DestroyImmediateSafe(m_SharedMaterial);

            m_Mesh = null;
            m_SharedMaterial = null;
            m_Objects = null;
            m_Properties = null;
            m_Sources = null;
            m_CachedSubmitOperations = null;
            m_CachedBatchSubmitOperations = null;
            m_BatchWriteBuffer = null;
            m_DirectMaterials = null;
            m_DirectPropertyBlocks = null;
            m_DirectValues = null;
        }

        public bool AreAllInstancesReady()
        {
            if (m_Objects == null)
                return false;

            for (int i = 0; i < m_Objects.Length; i++)
            {
                if (m_Objects[i].Instance == null || !m_Objects[i].Instance.IsReady)
                    return false;
            }

            return true;
        }

        public void SubmitFrame(int logicalFrame)
        {
            if (m_Scenario.ApiMode == MatDataTransferBatchApiMode.None || m_Scenario.SourceCount <= 0)
                return;

            if (m_Scenario.ApiMode == MatDataTransferBatchApiMode.ForMaterialBatch)
            {
                SubmitBatchFrame(logicalFrame);
                return;
            }

            for (int sourceIndex = 0; sourceIndex < m_Sources.Length; sourceIndex++)
            {
                MatDataTransferSubmitSource source = m_Sources[sourceIndex];
                int priority = sourceIndex * 10;
                for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
                {
                    MatDataTransferObject target = m_Objects[objectIndex];
                    for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                    {
                        MatDataTransferProperty property = m_Properties[propertyIndex];
                        ParamValue value = BuildValue(property.ValueType, logicalFrame, objectIndex, propertyIndex, sourceIndex);
                        Submit(target, property.SemanticKey, value, source, priority);
                    }
                }
            }
        }

        internal void PrepareDirectWriteTargets()
        {
            if (m_Objects == null)
                throw new InvalidOperationException("Render objects must be created before direct writes are prepared.");

            m_DirectMaterials = new Material[m_Objects.Length];
            m_DirectPropertyBlocks = new MaterialPropertyBlock[m_Objects.Length];
            m_DirectValues = new ParamValue[m_Objects.Length * m_Properties.Length];
            for (int i = 0; i < m_Objects.Length; i++)
            {
                m_DirectMaterials[i] = m_Objects[i].Renderer.material;
                m_DirectPropertyBlocks[i] = new MaterialPropertyBlock();
            }
        }

        internal void PrepareDirectValues(int logicalFrame)
        {
            EnsureDirectWriteTargetsReady();
            int sourceIndex = Math.Max(0, m_Scenario.SourceCount - 1);
            for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
            {
                int valueOffset = objectIndex * m_Properties.Length;
                for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                {
                    MatDataTransferProperty property = m_Properties[propertyIndex];
                    m_DirectValues[valueOffset + propertyIndex] = BuildValue(
                        property.ValueType,
                        logicalFrame,
                        objectIndex,
                        propertyIndex,
                        sourceIndex);
                }
            }
        }

        internal int SetAllMaterialProperties()
        {
            EnsureDirectWriteTargetsReady();
            int writeCount = 0;
            for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
            {
                Material material = m_DirectMaterials[objectIndex];
                int valueOffset = objectIndex * m_Properties.Length;
                for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                {
                    MatDataTransferProperty property = m_Properties[propertyIndex];
                    SetMaterialValue(
                        material,
                        property,
                        m_DirectValues[valueOffset + propertyIndex]);
                    writeCount++;
                }
            }

            return writeCount;
        }

        internal int SetAllPropertyBlockProperties()
        {
            EnsureDirectWriteTargetsReady();
            int writeCount = 0;
            for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
            {
                MaterialPropertyBlock block = m_DirectPropertyBlocks[objectIndex];
                block.Clear();
                int valueOffset = objectIndex * m_Properties.Length;
                for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                {
                    MatDataTransferProperty property = m_Properties[propertyIndex];
                    SetPropertyBlockValue(
                        block,
                        property,
                        m_DirectValues[valueOffset + propertyIndex]);
                    writeCount++;
                }

                m_Objects[objectIndex].Renderer.SetPropertyBlock(block, 0);
            }

            return writeCount;
        }

        internal void ClearDirectPropertyBlocks()
        {
            if (m_Objects == null)
                return;

            for (int i = 0; i < m_Objects.Length; i++)
                m_Objects[i].Renderer.SetPropertyBlock(null, 0);
        }

        internal void AssertDirectMaterialValues(int logicalFrame, int sampleCount)
        {
            AssertMaterialValues(logicalFrame, sampleCount, Math.Max(0, m_Scenario.SourceCount - 1));
        }

        internal void AssertDirectPropertyBlockValues(int logicalFrame, int sampleCount)
        {
            AssertPropertyBlockValues(
                logicalFrame,
                sampleCount,
                Math.Max(0, m_Scenario.SourceCount - 1));
        }

        internal void CacheSubmitOperations(int logicalFrame)
        {
            int operationCount = m_Objects.Length * m_Properties.Length * m_Sources.Length;
            m_CachedSubmitOperations = new MatDataTransferSubmitOperation[operationCount];
            int operationIndex = 0;
            for (int sourceIndex = 0; sourceIndex < m_Sources.Length; sourceIndex++)
            {
                MatDataTransferSubmitSource source = m_Sources[sourceIndex];
                int priority = sourceIndex * 10;
                for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
                {
                    MatDataTransferObject target = m_Objects[objectIndex];
                    for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                    {
                        MatDataTransferProperty property = m_Properties[propertyIndex];
                        ParamValue value = BuildValue(
                            property.ValueType,
                            logicalFrame,
                            objectIndex,
                            propertyIndex,
                            sourceIndex);
                        m_CachedSubmitOperations[operationIndex++] = new MatDataTransferSubmitOperation(
                            target,
                            property.SemanticKey,
                            value,
                            source,
                            priority);
                    }
                }
            }
        }

        internal int SubmitCachedOperations()
        {
            if (m_CachedSubmitOperations == null)
                throw new InvalidOperationException("Submit operations have not been cached.");

            for (int i = 0; i < m_CachedSubmitOperations.Length; i++)
            {
                MatDataTransferSubmitOperation operation = m_CachedSubmitOperations[i];
                Submit(
                    operation.Target,
                    operation.SemanticKey,
                    operation.Value,
                    operation.Source,
                    operation.Priority);
            }

            return m_CachedSubmitOperations.Length;
        }

        internal void CacheBatchSubmitOperations(int logicalFrame)
        {
            int batchCount = m_Objects.Length * m_Sources.Length;
            m_CachedBatchSubmitOperations = new MatDataTransferBatchSubmitOperation[batchCount];
            int batchIndex = 0;
            for (int sourceIndex = 0; sourceIndex < m_Sources.Length; sourceIndex++)
            {
                MatDataTransferSubmitSource source = m_Sources[sourceIndex];
                int priority = sourceIndex * 10;
                for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
                {
                    ParamBatchWrite[] writes = new ParamBatchWrite[m_Properties.Length];
                    for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                    {
                        MatDataTransferProperty property = m_Properties[propertyIndex];
                        writes[propertyIndex] = new ParamBatchWrite(
                            property.SemanticKey,
                            BuildValue(
                                property.ValueType,
                                logicalFrame,
                                objectIndex,
                                propertyIndex,
                                sourceIndex));
                    }

                    m_CachedBatchSubmitOperations[batchIndex++] = new MatDataTransferBatchSubmitOperation(
                        m_Objects[objectIndex],
                        writes,
                        source,
                        priority);
                }
            }
        }

        internal int SubmitCachedBatches()
        {
            if (m_CachedBatchSubmitOperations == null)
                throw new InvalidOperationException("Batch submit operations have not been cached.");

            int submitted = 0;
            for (int i = 0; i < m_CachedBatchSubmitOperations.Length; i++)
            {
                MatDataTransferBatchSubmitOperation operation = m_CachedBatchSubmitOperations[i];
                ParamBatchSubmitResult result = MatDataTransferAPI.ForMaterialBatch(
                    operation.Target.Instance,
                    operation.Target.Renderer,
                    0,
                    operation.Writes,
                    operation.Source,
                    ParamWriteLayer.Gameplay,
                    operation.Priority);
                submitted += result.AcceptedCount;
            }

            return submitted;
        }

        internal void BuildWriteCommands(List<ParamWriteCommand> commands, int logicalFrame)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            commands.Clear();
            int requestId = 0;
            for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
            {
                MatDataTransferObject target = m_Objects[objectIndex];
                for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                {
                    MatDataTransferProperty property = m_Properties[propertyIndex];
                    commands.Add(new ParamWriteCommand(
                        new ParamWriteTarget(target.Renderer, 0, property.PropertyId),
                        BuildValue(property.ValueType, logicalFrame, objectIndex, propertyIndex, 0),
                        requestId++));
                }
            }
        }

        internal void BuildValidatedRequests(
            List<ValidatedParamRequest> requests,
            int logicalFrame)
        {
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));

            requests.Clear();
            int requestId = 0;
            for (int sourceIndex = 0; sourceIndex < m_Sources.Length; sourceIndex++)
            {
                int priority = sourceIndex * 10;
                for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
                {
                    MatDataTransferObject target = m_Objects[objectIndex];
                    int instanceId = target.Instance.InstanceId;
                    int rendererId = target.Renderer.GetInstanceID();
                    for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                    {
                        MatDataTransferProperty property = m_Properties[propertyIndex];
                        ParamWriteTarget writeTarget = new ParamWriteTarget(
                            target.Renderer,
                            0,
                            property.PropertyId);
                        requests.Add(new ValidatedParamRequest(
                            instanceId,
                            new ConflictKey(instanceId, rendererId, 0, property.PropertyId),
                            writeTarget,
                            BuildValue(
                                property.ValueType,
                                logicalFrame,
                                objectIndex,
                                propertyIndex,
                                sourceIndex),
                            new RequestStrength(
                                ParamWriteLayers.GetStrength(ParamWriteLayer.Gameplay),
                                priority,
                                logicalFrame,
                                requestId),
                            requestId++));
                    }
                }
            }
        }

        internal void BuildInterleavedWriteCommands(List<ParamWriteCommand> commands, int logicalFrame)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            commands.Clear();
            int requestId = 0;
            for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
            {
                MatDataTransferProperty property = m_Properties[propertyIndex];
                for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
                {
                    MatDataTransferObject target = m_Objects[objectIndex];
                    commands.Add(new ParamWriteCommand(
                        new ParamWriteTarget(target.Renderer, 0, property.PropertyId),
                        BuildValue(property.ValueType, logicalFrame, objectIndex, propertyIndex, 0),
                        requestId++));
                }
            }
        }

        internal void BuildWriteDiagnostics(
            List<RequestDiagnosticContext> diagnostics,
            int logicalFrame)
        {
            if (diagnostics == null)
                throw new ArgumentNullException(nameof(diagnostics));

            diagnostics.Clear();
            MatDataTransferSubmitSource source = m_Sources[0];
            for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
            {
                MatDataTransferObject target = m_Objects[objectIndex];
                RendererMaterialBinding binding = target.Instance.QueryBinding(target.Renderer, 0);
                if (binding == null)
                    throw new InvalidOperationException("Writer diagnostic binding is missing.");

                for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                {
                    MatDataTransferProperty property = m_Properties[propertyIndex];
                    ParamValue value = BuildValue(
                        property.ValueType,
                        logicalFrame,
                        objectIndex,
                        propertyIndex,
                        0);
                    ParamTransferPayload payload = new ParamTransferPayload(
                        new ParamRequestIdentity(target.Instance, source, property.SemanticKey, value, binding),
                        new ParamWriteConfig(ParamWriteLayer.Gameplay));
                    ParamBindingResolution resolution = new ParamBindingResolution(
                        property.SemanticKey,
                        binding.ShaderName,
                        "StageWriter",
                        property.PropertyName,
                        property.PropertyId);
                    diagnostics.Add(new RequestDiagnosticContext(payload, resolution));
                }
            }
        }

        internal void WarmMaterialInstances()
        {
            for (int i = 0; i < m_Objects.Length; i++)
                _ = m_Objects[i].Renderer.material;
        }

        internal void AssertWriterValues(int logicalFrame, int sampleCount)
        {
            AssertMaterialValues(logicalFrame, sampleCount);
        }

        internal void AssertPropertyBlockValues(int logicalFrame, int sampleCount)
        {
            AssertPropertyBlockValues(logicalFrame, sampleCount, 0);
        }

        private void AssertPropertyBlockValues(int logicalFrame, int sampleCount, int sourceIndex)
        {
            int count = Mathf.Min(sampleCount, m_Objects.Length);
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            for (int i = 0; i < count; i++)
            {
                int objectIndex = SelectSampleIndex(i, count, m_Objects.Length);
                m_Objects[objectIndex].Renderer.GetPropertyBlock(block, 0);
                for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                {
                    MatDataTransferProperty property = m_Properties[propertyIndex];
                    ParamValue expected = BuildValue(
                        property.ValueType,
                        logicalFrame,
                        objectIndex,
                        propertyIndex,
                        sourceIndex);
                    switch (property.ValueType)
                    {
                        case ParamValueType.Color:
                            AssertClose(block.GetColor(property.PropertyId), expected.ColorValue, property.PropertyName);
                            break;
                        case ParamValueType.Vector:
                            AssertClose(block.GetVector(property.PropertyId), expected.VectorValue, property.PropertyName);
                            break;
                        case ParamValueType.Float:
                            AssertClose(block.GetFloat(property.PropertyId), expected.FloatValue, property.PropertyName);
                            break;
                    }
                }
            }
        }

        public void AssertMaterialValues(int logicalFrame, int sampleCount)
        {
            if (m_Scenario.SourceCount <= 0)
                return;

            AssertMaterialValues(logicalFrame, sampleCount, m_Scenario.SourceCount - 1);
        }

        private void AssertMaterialValues(int logicalFrame, int sampleCount, int sourceIndex)
        {
            int count = Mathf.Min(sampleCount, m_Objects.Length);
            for (int i = 0; i < count; i++)
            {
                int objectIndex = SelectSampleIndex(i, count, m_Objects.Length);
                Material material = m_Objects[objectIndex].Renderer.material;
                if (material == null)
                    throw new InvalidOperationException("Batch test material is missing.");

                for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                {
                    MatDataTransferProperty property = m_Properties[propertyIndex];
                    ParamValue expected = BuildValue(property.ValueType, logicalFrame, objectIndex, propertyIndex, sourceIndex);
                    AssertMaterialValue(material, property, expected);
                }
            }
        }

        private void Submit(
            MatDataTransferObject target,
            string semanticKey,
            ParamValue value,
            MatDataTransferSubmitSource source,
            int priority)
        {
            switch (m_Scenario.ApiMode)
            {
                case MatDataTransferBatchApiMode.ForMaterial:
                    MatDataTransferAPI.ForMaterial(
                        target.Instance,
                        semanticKey,
                        value,
                        target.Renderer,
                        0,
                        source,
                        ParamWriteLayer.Gameplay,
                        priority);
                    break;
                case MatDataTransferBatchApiMode.ForInstance:
                    MatDataTransferAPI.ForInstance(
                        target.Instance,
                        semanticKey,
                        value,
                        source,
                        ParamWriteLayer.Gameplay,
                        priority);
                    break;
            }
        }

        private void SubmitBatchFrame(int logicalFrame)
        {
            for (int sourceIndex = 0; sourceIndex < m_Sources.Length; sourceIndex++)
            {
                MatDataTransferSubmitSource source = m_Sources[sourceIndex];
                int priority = sourceIndex * 10;
                for (int objectIndex = 0; objectIndex < m_Objects.Length; objectIndex++)
                {
                    MatDataTransferObject target = m_Objects[objectIndex];
                    for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                    {
                        MatDataTransferProperty property = m_Properties[propertyIndex];
                        m_BatchWriteBuffer[propertyIndex] = new ParamBatchWrite(
                            property.SemanticKey,
                            BuildValue(
                                property.ValueType,
                                logicalFrame,
                                objectIndex,
                                propertyIndex,
                                sourceIndex));
                    }

                    MatDataTransferAPI.ForMaterialBatch(
                        target.Instance,
                        target.Renderer,
                        0,
                        m_BatchWriteBuffer,
                        source,
                        ParamWriteLayer.Gameplay,
                        priority);
                }
            }
        }

        private void EnsureGeneratedRoot()
        {
            Transform existing = transform.Find(GeneratedRootName);
            if (existing != null)
            {
                m_GeneratedRoot = existing;
                return;
            }

            GameObject root = new GameObject(GeneratedRootName);
            root.transform.SetParent(transform, false);
            m_GeneratedRoot = root.transform;
        }

        private void EnsureMesh()
        {
            if (m_Mesh != null)
                return;

            m_Mesh = new Mesh { name = "MDT_Batch_QuadMesh" };
            m_Mesh.vertices = new[]
            {
                new Vector3(-0.45f, -0.45f, 0f),
                new Vector3(0.45f, -0.45f, 0f),
                new Vector3(-0.45f, 0.45f, 0f),
                new Vector3(0.45f, 0.45f, 0f)
            };
            m_Mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m_Mesh.RecalculateBounds();
        }

        private void EnsureMaterial()
        {
            if (m_SharedMaterial != null)
                return;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException("Shader not found: " + ShaderName);

            m_SharedMaterial = new Material(shader)
            {
                name = "MDT_Batch_Shared_Unlit"
            };
        }

        private void CacheProperties(int propertyCount)
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            if (feature == null)
                throw new InvalidOperationException("MatDataTransferFeature.Instance is missing.");
            if (!feature.TryGetCatalogForShader(ShaderName, out ShaderPropertyCatalog catalog) || catalog == null)
                throw new InvalidOperationException("Catalog is missing for shader: " + ShaderName);

            MatDataTransferProperty[] candidates = new MatDataTransferProperty[m_PreferredPropertyOrder.Length];
            int candidateCount = 0;
            for (int i = 0; i < m_PreferredPropertyOrder.Length; i++)
            {
                if (!TryBuildProperty(catalog, m_PreferredPropertyOrder[i], out MatDataTransferProperty property))
                    continue;

                candidates[candidateCount++] = property;
            }

            if (candidateCount < propertyCount)
                throw new InvalidOperationException(
                    "QRP/Unlit catalog has "
                    + candidateCount
                    + " usable properties, but scenario needs "
                    + propertyCount
                    + ".");

            m_Properties = new MatDataTransferProperty[propertyCount];
            Array.Copy(candidates, m_Properties, propertyCount);
        }

        private bool TryBuildProperty(
            ShaderPropertyCatalog catalog,
            string propertyName,
            out MatDataTransferProperty property)
        {
            property = default;
            for (int i = 0; i < catalog.Properties.Count; i++)
            {
                CatalogProperty catalogProperty = catalog.Properties[i];
                if (catalogProperty == null || catalogProperty.PropertyInfo == null)
                    continue;
                if (catalogProperty.Status != CatalogPropertyStatus.Ok)
                    continue;
                if (!string.Equals(catalogProperty.PropertyInfo.PropertyName, propertyName, StringComparison.Ordinal))
                    continue;

                property = new MatDataTransferProperty(
                    propertyName,
                    catalogProperty.SuggestedSemanticKey,
                    catalogProperty.PropertyInfo.ValueType,
                    Shader.PropertyToID(propertyName));
                return true;
            }

            return false;
        }

        private void CacheSources(int sourceCount)
        {
            m_Sources = new MatDataTransferSubmitSource[sourceCount];
            for (int i = 0; i < sourceCount; i++)
            {
                m_Sources[i] = new MatDataTransferSubmitSource
                {
                    Id = "MDT.Batch.Source." + i,
                    Owner = this
                };
            }
        }

        private void CreateObjects(int objectCount)
        {
            m_Objects = new MatDataTransferObject[objectCount];
            int columns = Mathf.CeilToInt(Mathf.Sqrt(objectCount));
            for (int i = 0; i < objectCount; i++)
            {
                GameObject item = new GameObject("MDT_Batch_Object_" + i.ToString("0000"));
                item.transform.SetParent(m_GeneratedRoot, false);
                item.transform.localPosition = new Vector3(i % columns, 0f, i / columns);

                MeshFilter filter = item.AddComponent<MeshFilter>();
                filter.sharedMesh = m_Mesh;

                MeshRenderer renderer = item.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = m_SharedMaterial;

                m_Objects[i] = new MatDataTransferObject(item, renderer);
            }
        }

        private void ClearGeneratedObjects()
        {
            Transform root = m_GeneratedRoot != null ? m_GeneratedRoot : transform.Find(GeneratedRootName);
            if (root == null)
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
                DestroyImmediateSafe(root.GetChild(i).gameObject);
        }

        private static ParamValue BuildValue(
            ParamValueType type,
            int logicalFrame,
            int objectIndex,
            int propertyIndex,
            int sourceIndex)
        {
            float frameOffset = (logicalFrame % 5) * 0.001f;
            float seed = frameOffset + objectIndex * 0.1f + propertyIndex * 0.02f + sourceIndex;
            switch (type)
            {
                case ParamValueType.Color:
                    return ParamValue.Color(new Color(
                        Mathf.Repeat(seed * 0.13f, 1f),
                        Mathf.Repeat(seed * 0.17f + 0.25f, 1f),
                        Mathf.Repeat(seed * 0.19f + 0.5f, 1f),
                        1f));
                case ParamValueType.Vector:
                    return ParamValue.Vector(new Vector4(seed, seed + 1f, seed + 2f, 1f));
                case ParamValueType.Float:
                default:
                    return ParamValue.Float(seed);
            }
        }

        private void EnsureDirectWriteTargetsReady()
        {
            if (m_DirectMaterials == null || m_DirectPropertyBlocks == null || m_DirectValues == null)
                throw new InvalidOperationException("Direct write targets have not been prepared.");
        }

        private static void SetMaterialValue(
            Material material,
            MatDataTransferProperty property,
            ParamValue value)
        {
            switch (property.ValueType)
            {
                case ParamValueType.Color:
                    material.SetColor(property.PropertyId, value.ColorValue);
                    break;
                case ParamValueType.Vector:
                    material.SetVector(property.PropertyId, value.VectorValue);
                    break;
                case ParamValueType.Float:
                    material.SetFloat(property.PropertyId, value.FloatValue);
                    break;
            }
        }

        private static void SetPropertyBlockValue(
            MaterialPropertyBlock block,
            MatDataTransferProperty property,
            ParamValue value)
        {
            switch (property.ValueType)
            {
                case ParamValueType.Color:
                    block.SetColor(property.PropertyId, value.ColorValue);
                    break;
                case ParamValueType.Vector:
                    block.SetVector(property.PropertyId, value.VectorValue);
                    break;
                case ParamValueType.Float:
                    block.SetFloat(property.PropertyId, value.FloatValue);
                    break;
            }
        }

        private static void AssertMaterialValue(
            Material material,
            MatDataTransferProperty property,
            ParamValue expected)
        {
            switch (property.ValueType)
            {
                case ParamValueType.Color:
                    AssertClose(material.GetColor(property.PropertyId), expected.ColorValue, property.PropertyName);
                    break;
                case ParamValueType.Vector:
                    AssertClose(material.GetVector(property.PropertyId), expected.VectorValue, property.PropertyName);
                    break;
                case ParamValueType.Float:
                    AssertClose(material.GetFloat(property.PropertyId), expected.FloatValue, property.PropertyName);
                    break;
            }
        }

        private static int SelectSampleIndex(int sampleIndex, int sampleCount, int objectCount)
        {
            if (sampleCount <= 1)
                return 0;

            return Mathf.Clamp(
                Mathf.RoundToInt((objectCount - 1) * (sampleIndex / (float)(sampleCount - 1))),
                0,
                objectCount - 1);
        }

        private static void AssertClose(Color actual, Color expected, string propertyName)
        {
            AssertClose((Vector4)actual, (Vector4)expected, propertyName);
        }

        private static void AssertClose(Vector4 actual, Vector4 expected, string propertyName)
        {
            const float tolerance = 0.006f;
            if (Mathf.Abs(actual.x - expected.x) > tolerance
                || Mathf.Abs(actual.y - expected.y) > tolerance
                || Mathf.Abs(actual.z - expected.z) > tolerance
                || Mathf.Abs(actual.w - expected.w) > tolerance)
            {
                throw new InvalidOperationException(
                    propertyName
                    + " expected "
                    + expected
                    + ", got "
                    + actual
                    + ".");
            }
        }

        private static void AssertClose(float actual, float expected, string propertyName)
        {
            if (Mathf.Abs(actual - expected) > 0.006f)
            {
                throw new InvalidOperationException(
                    propertyName
                    + " expected "
                    + expected
                    + ", got "
                    + actual
                    + ".");
            }
        }

        private static void DestroyImmediateSafe(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        private sealed class MatDataTransferObject
        {
            public readonly GameObject GameObject;
            public readonly MeshRenderer Renderer;
            public MatDataTransferInstance Instance;

            public MatDataTransferObject(GameObject gameObject, MeshRenderer renderer)
            {
                GameObject = gameObject;
                Renderer = renderer;
                Instance = null;
            }
        }

        private readonly struct MatDataTransferSubmitOperation
        {
            public readonly MatDataTransferObject Target;
            public readonly string SemanticKey;
            public readonly ParamValue Value;
            public readonly MatDataTransferSubmitSource Source;
            public readonly int Priority;

            public MatDataTransferSubmitOperation(
                MatDataTransferObject target,
                string semanticKey,
                ParamValue value,
                MatDataTransferSubmitSource source,
                int priority)
            {
                Target = target;
                SemanticKey = semanticKey;
                Value = value;
                Source = source;
                Priority = priority;
            }
        }

        private readonly struct MatDataTransferBatchSubmitOperation
        {
            public readonly MatDataTransferObject Target;
            public readonly ParamBatchWrite[] Writes;
            public readonly MatDataTransferSubmitSource Source;
            public readonly int Priority;

            public MatDataTransferBatchSubmitOperation(
                MatDataTransferObject target,
                ParamBatchWrite[] writes,
                MatDataTransferSubmitSource source,
                int priority)
            {
                Target = target;
                Writes = writes;
                Source = source;
                Priority = priority;
            }
        }

        private readonly struct MatDataTransferProperty
        {
            public readonly string PropertyName;
            public readonly string SemanticKey;
            public readonly ParamValueType ValueType;
            public readonly int PropertyId;

            public MatDataTransferProperty(
                string propertyName,
                string semanticKey,
                ParamValueType valueType,
                int propertyId)
            {
                PropertyName = propertyName;
                SemanticKey = semanticKey;
                ValueType = valueType;
                PropertyId = propertyId;
            }
        }
    }
}
