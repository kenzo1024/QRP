using System;
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

        public int ObjectCount => m_Objects != null ? m_Objects.Length : 0;
        public int PropertyCount => m_Properties != null ? m_Properties.Length : 0;

        internal void Build(MatDataTransferBatchScenario scenario)
        {
            m_Scenario = scenario;
            ClearGeneratedObjects();
            EnsureGeneratedRoot();
            EnsureMesh();
            EnsureMaterial();
            CacheProperties(scenario.PropertyCount);
            CacheSources(scenario.SourceCount);
            CreateObjects(scenario.ObjectCount);
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

        public void AssertMaterialValues(int logicalFrame, int sampleCount)
        {
            if (m_Scenario.SourceCount <= 0)
                return;

            int count = Mathf.Min(sampleCount, m_Objects.Length);
            int winningSource = m_Scenario.SourceCount - 1;
            for (int i = 0; i < count; i++)
            {
                int objectIndex = SelectSampleIndex(i, count, m_Objects.Length);
                Material material = m_Objects[objectIndex].Renderer.material;
                if (material == null)
                    throw new InvalidOperationException("Batch test material is missing.");

                for (int propertyIndex = 0; propertyIndex < m_Properties.Length; propertyIndex++)
                {
                    MatDataTransferProperty property = m_Properties[propertyIndex];
                    ParamValue expected = BuildValue(property.ValueType, logicalFrame, objectIndex, propertyIndex, winningSource);
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

                MatDataTransferInstance instance = item.AddComponent<MatDataTransferInstance>();
                instance.RefreshBindings();

                m_Objects[i] = new MatDataTransferObject(instance, renderer);
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

        private readonly struct MatDataTransferObject
        {
            public readonly MatDataTransferInstance Instance;
            public readonly MeshRenderer Renderer;

            public MatDataTransferObject(MatDataTransferInstance instance, MeshRenderer renderer)
            {
                Instance = instance;
                Renderer = renderer;
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
