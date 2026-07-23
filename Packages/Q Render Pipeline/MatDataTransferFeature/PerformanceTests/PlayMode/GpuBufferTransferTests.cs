using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Rendering.MatDataTransfer.Runtime.GpuBuffer;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rendering.MatDataTransfer.PerformanceTests
{
    [TestFixture]
    internal sealed class GpuBufferTransferTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct TestData
        {
            internal Vector4 Value;
        }

        private sealed class TestSource : IGpuBufferDataSource<TestData>
        {
            internal Vector4 Value;
            internal int WriteCount;

            public bool TryWriteGpuBufferData(GpuBufferWriteContext<TestData> context)
            {
                WriteCount++;
                context.Set(0, new TestData { Value = Value });
                return true;
            }
        }

        private sealed class TestPass : IGpuBufferPass
        {
            internal int PrepareCount;
            internal int ExecuteCount;
            internal int BindCount;

            public string Name => "Test GPU Buffer Pass";
            public bool HasWork => true;

            public void PrepareFrame(int frameIndex)
            {
                PrepareCount++;
            }

            public void Execute(CommandBuffer commandBuffer)
            {
                ExecuteCount++;
            }

            public void Bind(CommandBuffer commandBuffer)
            {
                BindCount++;
            }

            public void Dispose()
            {
            }
        }

        [Test]
        public void Register_AssignsStableContiguousHandles()
        {
            using var provider = CreateProvider();

            GpuBufferHandle first = provider.Register(new TestSource());
            GpuBufferHandle second = provider.Register(new TestSource());

            Assert.That(first.Index, Is.EqualTo(0));
            Assert.That(second.Index, Is.EqualTo(1));
            Assert.That(first.Version, Is.EqualTo(1));
        }

        [Test]
        public void Register_SameSourceReturnsExistingHandle()
        {
            using var provider = CreateProvider();
            var source = new TestSource();

            GpuBufferHandle first = provider.Register(source);
            GpuBufferHandle second = provider.Register(source);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(provider.SourceCount, Is.EqualTo(1));
        }

        [Test]
        public void Unregister_InvalidatesOldHandleAndReusesSlotWithNewVersion()
        {
            using var provider = CreateProvider();
            var firstSource = new TestSource();
            GpuBufferHandle first = provider.Register(firstSource);

            Assert.That(provider.Unregister(firstSource), Is.True);
            Assert.That(provider.TryGetSlice(first, out _), Is.False);

            GpuBufferHandle replacement = provider.Register(new TestSource());
            Assert.That(replacement.Index, Is.EqualTo(first.Index));
            Assert.That(replacement.Version, Is.GreaterThan(first.Version));
        }

        [Test]
        public void Slice_UsesConfiguredElementsPerSource()
        {
            using var provider = CreateProvider(3);
            GpuBufferHandle first = provider.Register(new TestSource());
            GpuBufferHandle second = provider.Register(new TestSource());

            Assert.That(provider.TryGetSlice(first, out GpuBufferSlice firstSlice), Is.True);
            Assert.That(provider.TryGetSlice(second, out GpuBufferSlice secondSlice), Is.True);
            Assert.That(firstSlice.Offset, Is.EqualTo(0));
            Assert.That(firstSlice.Count, Is.EqualTo(3));
            Assert.That(secondSlice.Offset, Is.EqualTo(3));
        }

        [Test]
        public void Slice_ScalePreservesInstanceRange()
        {
            using var provider = CreateProvider(elementsPerSource: 1);
            GpuBufferHandle first = provider.Register(new TestSource());
            GpuBufferHandle second = provider.Register(new TestSource());

            Assert.That(provider.TryGetSlice(first, out GpuBufferSlice firstSlice), Is.True);
            Assert.That(provider.TryGetSlice(second, out GpuBufferSlice secondSlice), Is.True);

            Assert.That(firstSlice.Scale(6), Is.EqualTo(new GpuBufferSlice(0, 6)));
            Assert.That(secondSlice.Scale(6), Is.EqualTo(new GpuBufferSlice(6, 6)));
        }

        [Test]
        public void CollectFrameData_WritesSourceDataAndMarksBufferForUpload()
        {
            using var provider = CreateProvider();
            var source = new TestSource { Value = new Vector4(1f, 2f, 3f, 4f) };
            provider.Register(source);

            provider.CollectFrameData();
            provider.Upload();

            Assert.That(provider.Buffer, Is.Not.Null);
            Assert.That(provider.Buffer.count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void WriteContext_RejectsOutOfRangeElement()
        {
            var data = new TestData[1];
            var context = new GpuBufferWriteContext<TestData>(data, 0, 1);
            TestData value = default;

            Assert.Throws<ArgumentOutOfRangeException>(() => context.Set(1, value));
        }

        [Test]
        public void Runtime_SameFrameExecutesOnceAndBindsEveryRecord()
        {
            var pass = new TestPass();
            var commandBuffer = new CommandBuffer();
            GpuBufferRuntime.Register(pass);
            try
            {
                GpuBufferRuntime.Record(commandBuffer, 100);
                GpuBufferRuntime.Record(commandBuffer, 100);

                Assert.That(pass.PrepareCount, Is.EqualTo(1));
                Assert.That(pass.ExecuteCount, Is.EqualTo(1));
                Assert.That(pass.BindCount, Is.EqualTo(2));
            }
            finally
            {
                GpuBufferRuntime.Unregister(pass);
                commandBuffer.Release();
            }
        }

        [Test]
        public void Runtime_ExternalDriverIsExclusiveAndCanBeReleased()
        {
            var firstOwner = new object();
            var secondOwner = new object();
            try
            {
                Assert.That(GpuBufferRuntime.TryAcquireExternalDriver(firstOwner), Is.True);
                Assert.That(GpuBufferRuntime.TryAcquireExternalDriver(secondOwner), Is.False);

                GpuBufferRuntime.ReleaseExternalDriver(firstOwner);
                Assert.That(GpuBufferRuntime.TryAcquireExternalDriver(secondOwner), Is.True);
            }
            finally
            {
                GpuBufferRuntime.ReleaseExternalDriver(firstOwner);
                GpuBufferRuntime.ReleaseExternalDriver(secondOwner);
            }
        }

        [Test]
        public void RendererIndexRegistry_MultipleOwnersShareIndexUntilLastRelease()
        {
            var gameObject = new GameObject("Shared Renderer Index Test");
            var renderer = gameObject.AddComponent<MeshRenderer>();
            var firstOwner = new object();
            var secondOwner = new object();
            var original = new Vector4(2f, 3f, 4f, 5f);
            renderer.realtimeLightmapScaleOffset = original;

            try
            {
                RendererGpuBufferIndexRegistry.Sync(firstOwner, renderer);
                Assert.That(
                    RendererGpuBufferIndexRegistry.TryGetIndex(renderer, out int firstIndex),
                    Is.True);

                RendererGpuBufferIndexRegistry.Sync(secondOwner, renderer);
                Assert.That(
                    RendererGpuBufferIndexRegistry.TryGetIndex(renderer, out int secondIndex),
                    Is.True);
                Assert.That(secondIndex, Is.EqualTo(firstIndex));

                RendererGpuBufferIndexRegistry.Release(firstOwner);
                Assert.That(
                    RendererGpuBufferIndexRegistry.TryGetIndex(renderer, out int retainedIndex),
                    Is.True);
                Assert.That(retainedIndex, Is.EqualTo(firstIndex));

                RendererGpuBufferIndexRegistry.Release(secondOwner);
                Assert.That(
                    RendererGpuBufferIndexRegistry.TryGetIndex(renderer, out _),
                    Is.False);
                Assert.That(renderer.realtimeLightmapScaleOffset, Is.EqualTo(original));
            }
            finally
            {
                RendererGpuBufferIndexRegistry.Release(firstOwner);
                RendererGpuBufferIndexRegistry.Release(secondOwner);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RendererIndexRegistry_DifferentRenderersReceiveDifferentIndices()
        {
            var firstObject = new GameObject("First Renderer Index Test");
            var secondObject = new GameObject("Second Renderer Index Test");
            var firstRenderer = firstObject.AddComponent<MeshRenderer>();
            var secondRenderer = secondObject.AddComponent<MeshRenderer>();
            var owner = new object();

            try
            {
                RendererGpuBufferIndexRegistry.Sync(
                    owner,
                    new Renderer[] { firstRenderer, secondRenderer });

                Assert.That(
                    RendererGpuBufferIndexRegistry.TryGetIndex(firstRenderer, out int firstIndex),
                    Is.True);
                Assert.That(
                    RendererGpuBufferIndexRegistry.TryGetIndex(secondRenderer, out int secondIndex),
                    Is.True);
                Assert.That(secondIndex, Is.Not.EqualTo(firstIndex));
            }
            finally
            {
                RendererGpuBufferIndexRegistry.Release(owner);
                UnityEngine.Object.DestroyImmediate(firstObject);
                UnityEngine.Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void RendererIndexRegistry_SliceUsesSharedRendererIndex()
        {
            var gameObject = new GameObject("Renderer Slice Test");
            var renderer = gameObject.AddComponent<MeshRenderer>();
            var owner = new object();

            try
            {
                RendererGpuBufferIndexRegistry.Sync(owner, renderer);
                Assert.That(
                    RendererGpuBufferIndexRegistry.TryGetIndex(renderer, out int index),
                    Is.True);
                Assert.That(
                    RendererGpuBufferIndexRegistry.TryGetSlice(renderer, 6, out GpuBufferSlice slice),
                    Is.True);
                Assert.That(slice.Offset, Is.EqualTo(index * 6));
                Assert.That(slice.Count, Is.EqualTo(6));
            }
            finally
            {
                RendererGpuBufferIndexRegistry.Release(owner);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RendererIndexRegistry_SliceRejectsInvalidElementCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RendererGpuBufferIndexRegistry.TryGetSlice(null, 0, out _));
        }

        [Test]
        public void RendererIndexedProvider_WritesSourceOnceForMultipleRenderers()
        {
            var firstObject = new GameObject("First Renderer Provider Test");
            var secondObject = new GameObject("Second Renderer Provider Test");
            var firstRenderer = firstObject.AddComponent<MeshRenderer>();
            var secondRenderer = secondObject.AddComponent<MeshRenderer>();
            var owner = new object();
            var source = new TestSource();

            try
            {
                RendererGpuBufferIndexRegistry.Sync(
                    owner,
                    new Renderer[] { firstRenderer, secondRenderer });
                using var provider = new RendererIndexedGpuBufferProvider<TestData>(
                    "Renderer Indexed Provider Test",
                    Shader.PropertyToID("_RendererIndexedProviderTest"));
                provider.BeginFrame();

                Assert.That(
                    provider.SetData(
                        new Renderer[] { firstRenderer, secondRenderer },
                        source),
                    Is.True);
                Assert.That(source.WriteCount, Is.EqualTo(1));
            }
            finally
            {
                RendererGpuBufferIndexRegistry.Release(owner);
                UnityEngine.Object.DestroyImmediate(firstObject);
                UnityEngine.Object.DestroyImmediate(secondObject);
            }
        }

        private static GpuBufferProvider<TestData> CreateProvider(int elementsPerSource = 1)
        {
            return new GpuBufferProvider<TestData>(
                "Gpu Buffer Transfer Tests",
                Shader.PropertyToID("_GpuBufferTransferTests"),
                elementsPerSource);
        }
    }
}
