using System.Collections.Generic;
using NUnit.Framework;
using Rendering.MatDataTransfer.Runtime;
using UnityEngine;

namespace Rendering.MatDataTransfer.PerformanceTests
{
    [TestFixture]
    internal sealed class MatDataTransferInstanceRegistrationTests
    {
        private readonly List<GameObject> m_GameObjects = new List<GameObject>();
        private MatDataTransferInstanceRegister m_Registry;
        private MatDataTransferFeature m_OwnedFeature;

        [SetUp]
        public void SetUp()
        {
            m_Registry = new MatDataTransferInstanceRegister(4);
            if (MatDataTransferFeature.Instance == null)
            {
                m_OwnedFeature = ScriptableObject.CreateInstance<MatDataTransferFeature>();
                m_OwnedFeature.Create();
            }
        }

        [TearDown]
        public void TearDown()
        {
            m_Registry.Clear();
            for (int i = m_GameObjects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(m_GameObjects[i]);

            m_GameObjects.Clear();
            if (m_OwnedFeature != null)
            {
                m_OwnedFeature.DisposeForTests();
                Object.DestroyImmediate(m_OwnedFeature);
                m_OwnedFeature = null;
            }
        }

        [Test]
        public void Registry_ReusesReleasedLowestId()
        {
            MatDataTransferInstance first = CreateInactiveInstance("First");
            MatDataTransferInstance second = CreateInactiveInstance("Second");
            MatDataTransferInstance replacement = CreateInactiveInstance("Replacement");

            Assert.That(m_Registry.TryRegister(first, out int firstId), Is.True);
            Assert.That(m_Registry.TryRegister(second, out int secondId), Is.True);
            Assert.That(firstId, Is.EqualTo(0));
            Assert.That(secondId, Is.EqualTo(1));
            Assert.That(m_Registry.Release(first, out int releasedId), Is.True);
            Assert.That(releasedId, Is.EqualTo(firstId));

            Assert.That(m_Registry.TryRegister(replacement, out int replacementId), Is.True);
            Assert.That(replacementId, Is.EqualTo(firstId));
        }

        [Test]
        public void Registry_CapacityShrink_CompactsIds()
        {
            MatDataTransferInstance first = CreateInactiveInstance("First");
            MatDataTransferInstance second = CreateInactiveInstance("Second");
            MatDataTransferInstance third = CreateInactiveInstance("Third");

            Assert.That(m_Registry.TryRegister(first, out _), Is.True);
            Assert.That(m_Registry.TryRegister(second, out _), Is.True);
            Assert.That(m_Registry.TryRegister(third, out _), Is.True);
            Assert.That(m_Registry.Release(second, out _), Is.True);

            Assert.That(m_Registry.TrySetCapacity(2, out bool remapped), Is.True);
            Assert.That(remapped, Is.True);
            Assert.That(first.InstanceId, Is.EqualTo(0));
            Assert.That(third.InstanceId, Is.EqualTo(1));
        }

        [Test]
        public void Registry_TryGet_RemovesDestroyedInstance()
        {
            MatDataTransferInstance instance = CreateInactiveInstance("Destroyed");
            Assert.That(m_Registry.TryRegister(instance, out int id), Is.True);
            Object.DestroyImmediate(instance.gameObject);

            Assert.That(m_Registry.TryGet(id, out MatDataTransferInstance resolved), Is.False);
            Assert.That(resolved, Is.Null);
            Assert.That(m_Registry.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Instance_EnableDisable_PublishesLifecycleEvents()
        {
            MatDataTransferInstance enabledInstance = null;
            MatDataTransferInstance disabledInstance = null;
            int enabledCount = 0;
            int disabledCount = 0;

            void OnEnabled(MatDataTransferInstance instance)
            {
                enabledInstance = instance;
                enabledCount++;
            }

            void OnDisabled(MatDataTransferInstance instance)
            {
                disabledInstance = instance;
                disabledCount++;
            }

            GameObject target = CreateGameObject("Lifecycle", false);
            MatDataTransferInstance instance = target.AddComponent<MatDataTransferInstance>();
            MatDataTransferInstance.LiveInstanceEnabled += OnEnabled;
            MatDataTransferInstance.LiveInstanceDisabled += OnDisabled;
            try
            {
                target.SetActive(true);
                target.SetActive(false);
            }
            finally
            {
                MatDataTransferInstance.LiveInstanceEnabled -= OnEnabled;
                MatDataTransferInstance.LiveInstanceDisabled -= OnDisabled;
            }

            Assert.That(enabledCount, Is.EqualTo(1));
            Assert.That(disabledCount, Is.EqualTo(1));
            Assert.That(enabledInstance, Is.SameAs(instance));
            Assert.That(disabledInstance, Is.SameAs(instance));
        }

        [Test]
        public void Instance_EnabledBeforeFeatureSnapshot_RemainsDiscoverable()
        {
            MatDataTransferInstance instance = CreateInactiveInstance("Bootstrap");
            instance.gameObject.SetActive(true);
            List<MatDataTransferInstance> liveInstances = new List<MatDataTransferInstance>();

            MatDataTransferInstance.CopyLiveInstancesTo(liveInstances);

            Assert.That(liveInstances, Does.Contain(instance));
        }

        [Test]
        public void Feature_ReleasingSlot_RegistersPendingInstance()
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            Assert.That(feature, Is.Not.Null);
            int originalCapacity = feature.MaxInstanceCount;
            int requestedCapacity = feature.ActiveInstanceCount + 2;

            Assert.That(feature.TrySetMaxInstanceCount(requestedCapacity), Is.True);
            MatDataTransferInstance first = CreateEnabledInstance("First");
            MatDataTransferInstance second = CreateEnabledInstance("Second");
            MatDataTransferInstance pending = CreateEnabledInstance("Pending");
            try
            {
                Assert.That(first.IsReady, Is.True);
                Assert.That(second.IsReady, Is.True);
                Assert.That(pending.IsReady, Is.False);

                first.gameObject.SetActive(false);

                Assert.That(pending.IsReady, Is.True);
            }
            finally
            {
                first.gameObject.SetActive(false);
                second.gameObject.SetActive(false);
                pending.gameObject.SetActive(false);
                feature.TrySetMaxInstanceCount(originalCapacity);
            }
        }

        [Test]
        public void Feature_IncreasingCapacity_RegistersWaitingLiveInstance()
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            Assert.That(feature, Is.Not.Null);
            int originalCapacity = feature.MaxInstanceCount;
            int initialCapacity = feature.ActiveInstanceCount + 1;

            Assert.That(feature.TrySetMaxInstanceCount(initialCapacity), Is.True);
            MatDataTransferInstance first = CreateEnabledInstance("First");
            MatDataTransferInstance waiting = CreateEnabledInstance("Waiting");
            try
            {
                Assert.That(first.IsReady, Is.True);
                Assert.That(waiting.IsReady, Is.False);

                Assert.That(feature.TrySetMaxInstanceCount(initialCapacity + 1), Is.True);

                Assert.That(waiting.IsReady, Is.True);
            }
            finally
            {
                first.gameObject.SetActive(false);
                waiting.gameObject.SetActive(false);
                feature.TrySetMaxInstanceCount(originalCapacity);
            }
        }

        [Test]
        public void Feature_SettingCurrentCapacity_DoesNotRegisterWaitingLiveInstance()
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            Assert.That(feature, Is.Not.Null);
            int originalCapacity = feature.MaxInstanceCount;
            int initialCapacity = feature.ActiveInstanceCount + 1;

            Assert.That(feature.TrySetMaxInstanceCount(initialCapacity), Is.True);
            MatDataTransferInstance first = CreateEnabledInstance("First");
            MatDataTransferInstance waiting = CreateEnabledInstance("Waiting");
            try
            {
                Assert.That(first.IsReady, Is.True);
                Assert.That(waiting.IsReady, Is.False);

                Assert.That(feature.TrySetMaxInstanceCount(initialCapacity), Is.True);

                Assert.That(feature.MaxInstanceCount, Is.EqualTo(initialCapacity));
                Assert.That(waiting.IsReady, Is.False);
            }
            finally
            {
                first.gameObject.SetActive(false);
                waiting.gameObject.SetActive(false);
                feature.TrySetMaxInstanceCount(originalCapacity);
            }
        }

        [Test]
        public void Feature_Recreate_BootstrapsAlreadyLiveInstance()
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            Assert.That(feature, Is.Not.Null);
            MatDataTransferInstance instance = CreateEnabledInstance("FeatureRecreate");
            Assert.That(instance.IsReady, Is.True);

            feature.Create();

            Assert.That(instance.IsReady, Is.True);
            Assert.That(feature.ActiveInstanceCount, Is.GreaterThanOrEqualTo(1));
        }

        private MatDataTransferInstance CreateInactiveInstance(string name)
        {
            GameObject target = CreateGameObject(name, false);
            return target.AddComponent<MatDataTransferInstance>();
        }

        private MatDataTransferInstance CreateEnabledInstance(string name)
        {
            MatDataTransferInstance instance = CreateInactiveInstance(name);
            instance.gameObject.SetActive(true);
            return instance;
        }

        private GameObject CreateGameObject(string name, bool active)
        {
            GameObject target = new GameObject(name);
            target.SetActive(active);
            m_GameObjects.Add(target);
            return target;
        }
    }
}
